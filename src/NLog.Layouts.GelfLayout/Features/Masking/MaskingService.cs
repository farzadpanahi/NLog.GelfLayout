using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NLog.Layouts.GelfLayout.Features.Masking
{
    public sealed class MaskingService : IMaskingService
    {
        private readonly MaskingOptions _options;

        // Alan-adı kural index'i
        private readonly Dictionary<string, MaskingFieldRule> _fieldRuleIndex;

        // Tip -> plan cache
        private static readonly ConcurrentDictionary<Type, List<MemberPlan>> _planCache = new();

        private readonly StringComparison _cmp;

        private sealed record MemberPlan
        {
            public MemberPlan(
                Action<object, object> setter,
                Func<object, object> getter,
                Func<object, object> masker)
            {
                Setter = setter;
                Getter = getter;
                Masker = masker;
            }

            // Üyeyi nasıl maskeleyeceğimizi kapsüller
            public Action<object, object?> Setter { get; set; }
            public Func<object, object?> Getter { get; set; }
            public Func<object?, object?> Masker { get; set; }
        }

        public MaskingService(MaskingOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options))!;
            _cmp = _options.CaseInsensitive ?
                StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal;

            _fieldRuleIndex = (_options.Rules ?? new())
                .Where(r => !string.IsNullOrWhiteSpace(r.Field))
                .GroupBy(r => Normalize(r.Field))
                .ToDictionary(g => g.Key, g => g.First());
        }

        public T Mask<T>(T obj)
        {
            if (!_options.Enabled || obj is null) return obj;

            var type = obj!.GetType();

            // Basit tipler
            if (IsPrimitiveLike(type)) return obj;

            // String ise JSON da olabilir
            if (obj is string s) return (T)(object)MaskPossiblyJsonString(s);

            // Dictionary / IEnumerable / dinamik
            if (obj is IDictionary dict) return (T)MaskDictionary(dict)!;
            if (obj is IEnumerable en) return (T)MaskEnumerable(en, type)!;

            // Strongly-typed sınıf: attribute + alan adı kuralları
            ApplyPlans(obj!, type);
            return obj!;
        }

        public object? MaskDynamic(object? value)
        {
            if (!_options.Enabled || value is null) return value;

            var t = value.GetType();
            if (IsPrimitiveLike(t))
            {
                return value is string s ? MaskPossiblyJsonString(s) : value;
            }

            if (value is string str) return MaskPossiblyJsonString(str);
            if (value is IDictionary dict) return MaskDictionary(dict);
            if (value is IEnumerable en) return MaskEnumerable(en, t);

            ApplyPlans(value, t);
            return value;
        }

        public string MaskJson(string json) => MaskPossiblyJsonString(json);

        public string MaskValue(string? value, int prefix, int suffix, bool exclude)
        {
            if (!_options.Enabled) return value ?? string.Empty;
            if (string.IsNullOrEmpty(value)) return value ?? string.Empty;

            if (exclude)
            {
                return _options.FullExcludeAsEmpty ? string.Empty : new string(_options.MaskChar, value!.Length);
            }

            // Full mask isteniyorsa
            if (prefix == 0 && suffix == 0)
            {
                return new string(_options.MaskChar, value!.Length);
            }

            var len = value!.Length;

            // Prefix + Suffix uzunluğu aşarsa "uygun handle": güvenli tercih -> Full mask
            if (prefix + suffix >= len)
            {
                return new string(_options.MaskChar, len);
            }

            var keepLeft = prefix;
            var keepRight = suffix;
            var maskedCount = len - keepLeft - keepRight;

            var sb = new StringBuilder(len);
            if (keepLeft > 0) sb.Append(value, 0, keepLeft); // value.AsSpan(0, keepLeft) yerine
            if (maskedCount > 0) sb.Append(new string(_options.MaskChar, maskedCount));
            if (keepRight > 0) sb.Append(value, len - keepRight, keepRight); // value.AsSpan(len - keepRight)
            return sb.ToString();
        }

        // ---------- İç yardımcı metotlar ----------

        private void ApplyPlans(object obj, Type type)
        {
            var plans = _planCache.GetOrAdd(type, BuildPlans);
            foreach (var plan in plans)
            {
                var current = plan.Getter(obj);
                var masked = plan.Masker(current);
                plan.Setter(obj, masked);
            }
        }

        private List<MemberPlan> BuildPlans(Type type)
        {
            var plans = new List<MemberPlan>();

            foreach (var m in GetWritableMembers(type))
            {
                var memberType = GetMemberType(m);
                var getter = CompileGetter(m, type);
                var setter = CompileSetter(m, type);

                // Sınıfa tanımlı bir attribute mü?
                var attr = m.GetCustomAttribute<MaskAttribute>();
                Func<object?, object?> masker;

                if (attr is not null)
                {
                    masker = v => MaskByAttribute(v, attr, memberType);
                }
                else
                {
                    // Alan adına göre kural var mı?
                    var targetFieldName = m.Name;
                    if (_fieldRuleIndex.TryGetValue(Normalize(targetFieldName), out var rule))
                    {
                        masker = v => MaskByRule(v, rule, memberType, targetFieldName);
                    }
                    else
                    {
                        // Rekürsif gez: iç model/dizi/dictionary olabilir
                        masker = v => MaskTraverse(v, memberType);
                    }
                }

                plans.Add(new MemberPlan(setter, getter, masker));
            }
            return plans;
        }

        private object? MaskByAttribute(object? value, MaskAttribute attr, Type memberType)
        {
            return MaskCore(value, memberType, attr.Prefix, attr.Suffix, attr.Exclude, attr.FieldName);
        }

        private object? MaskByRule(object? value, MaskingFieldRule rule, Type memberType, string? fieldName)
        {
            return MaskCore(value, memberType, rule.Prefix, rule.Suffix, rule.Exclude, fieldName);
        }

        private object? MaskCore(
            object? value, Type memberType, int prefix, int suffix, bool exclude, string? fieldName)
        {
            if (value is null) return null;

            // string ise
            if (memberType == typeof(string))
            {
                return MaskValue((string?)value, prefix, suffix, exclude);
            }

            // primitive değilse: altını gez; fakat kural alan adınadır => string/primitive ise uygula,
            // değilse alt nesnede alan adına göre arama yapmanın anlamı yok, o yüzden rekürsif gez.
            return MaskTraverse(value, memberType);
        }

        private object? MaskTraverse(object? value, Type type)
        {
            if (value is null) return null;

            if (IsPrimitiveLike(type)) return value;

            if (value is string s) return MaskPossiblyJsonString(s);

            if (value is IDictionary dict) return MaskDictionary(dict);

            if (value is IEnumerable en) return MaskEnumerable(en, type);

            // Strongly typed complex type
            ApplyPlans(value, type);
            return value;
        }

        private object? MaskEnumerable(IEnumerable en, Type enumerableType)
        {
            var list = new List<object?>();
            foreach (var item in en)
            {
                var it = item;
                if (it is null)
                {
                    list.Add(null);
                    continue;
                }
                var t = it.GetType();
                list.Add(MaskDynamic(it));
            }

            // Orijinal tip IEnumerable ise koleksiyon yeni listeye kopyalanmış olur.
            return list;
        }

        private object? MaskDictionary(IDictionary dict)
        {
            // key -> value, value string/primitive ise alan adına göre maskele
            var keys = new List<object?>();
            foreach (var k in dict.Keys)
            {
                keys.Add(k);
            }

            foreach (var k in keys)
            {
                var v = dict[k!];

                if (k is string keyName)
                {
                    if (_fieldRuleIndex.TryGetValue(Normalize(keyName), out var rule))
                    {
                        if (v is string vs)
                        {
                            dict[k!] = MaskValue(vs, rule.Prefix, rule.Suffix, rule.Exclude);
                            continue;
                        }
                        // diğer tipler: rekürsif
                        dict[k!] = MaskDynamic(v);
                        continue;
                    }
                }

                // alan adı kuralı yoksa: yine de güvenli tarafta rekürsif maskele
                dict[k!] = MaskDynamic(v);
            }

            return dict;
        }

        private string MaskPossiblyJsonString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;

            // JSON gibi görünmüyorsa dokunma
            var first = s.TrimStart();
            if (first.Length == 0 || (first[0] != '{' && first[0] != '[')) return s;

            try
            {
                var node = JsonNode.Parse(s);
                if (node is null) return s;

                MaskJsonNode(node);
                return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
            }
            catch
            {
                // geçersiz json: dokunma
                return s;
            }
        }

        private void MaskJsonNode(JsonNode node, string? currentName = null)
        {
            switch (node)
            {
                case JsonObject obj:
                    foreach (var kv in obj.ToList())
                    {
                        var name = kv.Key;
                        var child = kv.Value!;
                        if (child is null) continue;

                        if (child is JsonValue val && TryGetString(val, out var str))
                        {
                            if (_fieldRuleIndex.TryGetValue(Normalize(name), out var rule))
                                obj[name] = MaskValue(str, rule.Prefix, rule.Suffix, rule.Exclude);
                            // değilse alt node değil zaten value, bırak
                        }
                        else
                        {
                            // alt düğüm: alan adı bilgisini geçir
                            MaskJsonNode(child, name);
                        }
                    }
                    break;

                case JsonArray arr:
                    foreach (var (child, idx) in arr.Select((c, i) => (c, i)))
                    {
                        if (child is null) continue;

                        if (child is JsonValue val && TryGetString(val, out var str))
                        {
                            // array içindeki "adı" yok; kurallar sadece adla çalışır -> rekürsif yok
                            arr[idx] = str; // olduğu gibi
                        }
                        else
                        {
                            MaskJsonNode(child);
                        }
                    }
                    break;

                case JsonValue valNode:
                    // yalnız değer: ad yok -> kural eşleşmesi olmayacak
                    break;
            }

            static bool TryGetString(JsonValue val, out string s)
            {
                if (val.TryGetValue<string>(out s!)) return true;
                s = string.Empty;
                return false;
            }
        }

        // ---------- Reflection yardımcı metotları ----------

        private static IEnumerable<MemberInfo> GetWritableMembers(Type t)
        {
            return t.GetMembers(BindingFlags.Instance | BindingFlags.Public)
             .Where(m =>
                (m is PropertyInfo p && p.CanRead && p.CanWrite) ||
                (m is FieldInfo f && !f.IsInitOnly && !f.IsLiteral));
        }

        private static Type GetMemberType(MemberInfo m) =>
            m switch
            {
                PropertyInfo p => p.PropertyType,
                FieldInfo f => f.FieldType,
                _ => typeof(object)
            };

        private static Func<object, object?> CompileGetter(MemberInfo m, Type declaring)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var cast = Expression.Convert(obj, declaring);

            Expression access = m switch
            {
                PropertyInfo p => Expression.Property(cast, p),
                FieldInfo f => Expression.Field(cast, f),
                _ => throw new NotSupportedException()
            };

            var box = Expression.Convert(access, typeof(object));
            return Expression.Lambda<Func<object, object?>>(box, obj).Compile();
        }

        private static Action<object, object?> CompileSetter(MemberInfo m, Type declaring)
        {
            var obj = Expression.Parameter(typeof(object), "obj");
            var value = Expression.Parameter(typeof(object), "value");
            var castObj = Expression.Convert(obj, declaring);

            switch (m)
            {
                case PropertyInfo p:
                    var valCastP = Expression.Convert(value, p.PropertyType);
                    var setP = Expression.Call(castObj, p.GetSetMethod()!, valCastP);
                    return Expression.Lambda<Action<object, object?>>(setP, obj, value).Compile();

                case FieldInfo f:
                    var valCastF = Expression.Convert(value, f.FieldType);
                    var setF = Expression.Assign(Expression.Field(castObj, f), valCastF);
                    return Expression.Lambda<Action<object, object?>>(setF, obj, value).Compile();

                default:
                    throw new NotSupportedException();
            }
        }

        private static bool IsPrimitiveLike(Type t) =>
            t.IsPrimitive || t.IsEnum ||
            t == typeof(decimal) || t == typeof(DateTime) ||
            t == typeof(Guid) || t == typeof(DateTimeOffset) ||
            t == typeof(TimeSpan);

        private string Normalize(string s) =>
            _options.CaseInsensitive ? s.Trim().ToLowerInvariant() : s.Trim();
    }
}