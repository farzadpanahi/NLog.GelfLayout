using NLog.Common;
using NLog.Targets;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NLog.Layouts.GelfLayout
{
    public class GelfConverter
    {
        private const int ShortMessageMaxLength = 250;
        private const int FullMessageMaxLength = 16383; // Truncate due to: https://github.com/Graylog2/graylog2-server/issues/873
        private const string GelfVersion11 = "1.1";
        private static DateTime UnixDateStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly HashSet<string> ExcludePropertyKeys = new HashSet<string>(new string[] { "LoggerName", "_LoggerName", "ExceptionSource", "_ExceptionSource", "ExceptionMessage", "_ExceptionMessage", "ExceptionType", "_ExceptionType", "StackTrace", "_StackTrace" }, StringComparer.OrdinalIgnoreCase);

        private string _hostName;

        private static readonly JsonWriterOptions _jsonWriterOptions = new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        };

        public void ConvertToGelfMessage(Utf8JsonWriter jsonWriter, LogEventInfo logEventInfo, IGelfConverterOptions converterOptions)
        {
            //Retrieve the formatted message from LogEventInfo
            var fullMessage = converterOptions.FullMessage?.Render(logEventInfo) ?? string.Empty;
            if (fullMessage.Length > FullMessageMaxLength)
            {
                fullMessage = fullMessage.Substring(0, FullMessageMaxLength);
            }

            //Figure out the short message
            var shortMessage = converterOptions.ShortMessage?.Render(logEventInfo) ?? string.Empty;
            if (shortMessage.Length > ShortMessageMaxLength)
            {
                shortMessage = shortMessage.Substring(0, ShortMessageMaxLength);
            }

            //Construct the instance of GelfMessage
            jsonWriter.WriteStartObject();

            var hostname = _hostName ?? (_hostName = converterOptions.HostName?.Render(logEventInfo));
            if (string.IsNullOrEmpty(hostname))
            {
                _hostName = null;
                hostname = "UnknownHost";
            }

            if (converterOptions.IncludeLegacyFields)
            {
                var facility = converterOptions.Facility?.Render(logEventInfo);
                if (string.IsNullOrEmpty(facility))
                {
                    facility = "GELF";   //Spec says: facility must be set by the client to "GELF" if empty
                }
                WriteGelfVersionLegacy(jsonWriter, logEventInfo, fullMessage, shortMessage, hostname, facility);
            }
            else
            {
                WriteGelfVersion11(jsonWriter, logEventInfo, fullMessage, shortMessage, hostname);
            }

            //We will persist them "Additional Fields" according to Gelf spec
            bool hasProperties = converterOptions.IncludeEventProperties && logEventInfo.HasProperties;
            if (hasProperties)
            {
                bool hasExcludeProperties = converterOptions.ExcludeProperties?.Count > 0;
                foreach (var property in logEventInfo.Properties)
                {
                    string key = property.Key as string;
                    if (key == null || ExcludePropertyKeys.Contains(key))
                        continue;

                    if (hasExcludeProperties && converterOptions.ExcludeProperties.Contains(key))
                        continue;

                    AddAdditionalField(jsonWriter, key, property.Value);
                }
            }

            //If we are dealing with an exception, pass exception properties as additional fields
            if (logEventInfo.Exception != null)
            {
                AddAdditionalField(jsonWriter, "_ExceptionSource", logEventInfo.Exception.Source);
                AddAdditionalField(jsonWriter, "_ExceptionMessage", logEventInfo.Exception.Message);
                AddAdditionalField(jsonWriter, "_ExceptionType", logEventInfo.Exception.GetType().ToString());
                AddAdditionalField(jsonWriter, "_StackTrace", logEventInfo.Exception.ToString());
            }

            //Add any other interesting data as additional fields
            AddAdditionalField(jsonWriter, "_LoggerName", logEventInfo.LoggerName);

            ICollection<string> mdlcKeys = null;
            if (converterOptions.IncludeScopeProperties)
            {
                mdlcKeys = ScopeContext.GetAllProperties().Select(x => x.Key).ToList();
                bool foundMdlcItem = false;
                bool hasExcludeProperties = converterOptions.ExcludeProperties?.Count > 0;
                foreach (string key in mdlcKeys)
                {
                    if (string.IsNullOrEmpty(key) || ExcludePropertyKeys.Contains(key))
                        continue;

                    if (hasProperties && logEventInfo.Properties.ContainsKey(key))
                        continue;

                    if (hasExcludeProperties && converterOptions.ExcludeProperties.Contains(key))
                        continue;

                    foundMdlcItem = true;
                    object propertyValue;
                    ScopeContext.TryGetProperty(key, out propertyValue);
                    AddAdditionalField(jsonWriter, key, propertyValue);
                }
                if (!foundMdlcItem)
                    mdlcKeys = null;
            }

            ICollection<string> gdcKeys = null;
            if(converterOptions.IncludeGdc)
            {
                gdcKeys = GlobalDiagnosticsContext.GetNames();
                bool foundGdcItem = false;
                bool hasExcludedProperties = converterOptions.ExcludeProperties?.Count > 0;
                foreach(string key in gdcKeys)
                {
                    if (string.IsNullOrEmpty(key) || ExcludePropertyKeys.Contains(key))
                        continue;

                    if (hasProperties && logEventInfo.Properties.ContainsKey(key))
                        continue;

                    if(hasExcludedProperties && converterOptions.ExcludeProperties.Contains(key))
                        continue;

                    foundGdcItem = true;
                    object propertyValue = GlobalDiagnosticsContext.GetObject(key);
                    AddAdditionalField(jsonWriter, key, propertyValue);
                }
                if (!foundGdcItem)
                    gdcKeys = null;
            }

            if (converterOptions.ExtraFields?.Count > 0)
            {
                AddLayoutGelfFields(jsonWriter, logEventInfo, converterOptions.ExtraFields, hasProperties, mdlcKeys, gdcKeys);
            }

            jsonWriter.WriteEndObject();
        }

        /// <summary>
        /// See http://docs.graylog.org/en/2.0/pages/gelf.html#gelf-payload-specification "Specification (version 1.0)"
        /// </summary>
        private static void WriteGelfVersionLegacy(Utf8JsonWriter jsonWriter, LogEventInfo logEventInfo, string fullMessage, string shortMessage, string hostname, string facility)
        {
            jsonWriter.WritePropertyName("facility");
            jsonWriter.WriteStringValue((string.IsNullOrEmpty(facility) ? "GELF" : facility));
            string callSiteFileName = logEventInfo.CallerFilePath;
            if (string.IsNullOrEmpty(callSiteFileName))
                callSiteFileName = logEventInfo.LoggerName ?? string.Empty;
            jsonWriter.WritePropertyName("file");
            jsonWriter.WriteStringValue(callSiteFileName);
            jsonWriter.WritePropertyName("full_message");
            jsonWriter.WriteStringValue(fullMessage);
            jsonWriter.WritePropertyName("host");
            jsonWriter.WriteStringValue(hostname);
            jsonWriter.WritePropertyName("level");
            jsonWriter.WriteNumberValue(logEventInfo.Level.GetOrdinal());
            jsonWriter.WritePropertyName("line");
            jsonWriter.WriteNumberValue(logEventInfo.CallerLineNumber);
            jsonWriter.WritePropertyName("short_message");
            jsonWriter.WriteStringValue(shortMessage);
            jsonWriter.WritePropertyName("timestamp");
            jsonWriter.WriteNumberValue(ToUnixTimeStamp(logEventInfo.TimeStamp));
            jsonWriter.WritePropertyName("version");
            jsonWriter.WriteStringValue(GelfVersion11);
        }

        /// <summary>
        /// See http://docs.graylog.org/en/3.0/pages/gelf.html#gelf-payload-specification "Specification (version 1.1)"
        /// </summary>
        /// <remarks>
        /// Required Fields are reordered to match example from specification.
        /// 
        /// Excluding deprecated fields:
        ///     facility - optional, deprecated. Send as additional field instead.
        ///     file - optional, deprecated. Send as additional field instead.
        ///     line - optional, deprecated. Send as additional field instead.
        /// </remarks>
        private static void WriteGelfVersion11(Utf8JsonWriter jsonWriter, LogEventInfo logEventInfo, string fullMessage, string shortMessage, string hostname)
        {
            jsonWriter.WritePropertyName("version");
            jsonWriter.WriteStringValue(GelfVersion11);
            jsonWriter.WritePropertyName("host");
            jsonWriter.WriteStringValue(hostname);
            jsonWriter.WritePropertyName("short_message");
            jsonWriter.WriteStringValue(shortMessage);
            jsonWriter.WritePropertyName("full_message");
            jsonWriter.WriteStringValue(fullMessage);
            jsonWriter.WritePropertyName("timestamp");
            jsonWriter.WriteNumberValue(ToUnixTimeStamp(logEventInfo.TimeStamp));
            jsonWriter.WritePropertyName("level");
            jsonWriter.WriteNumberValue(logEventInfo.Level.GetOrdinal());
        }

        private void AddLayoutGelfFields(Utf8JsonWriter jsonWriter, LogEventInfo logEventInfo, IList<GelfField> gelfFields, bool hasProperties, ICollection<string> mdlcKeys, ICollection<string> gdcKeys)
        {
            for (int i = 0; i < gelfFields.Count; ++i)
            {
                var gelfField = gelfFields[i];
                if (gelfField == null || string.IsNullOrEmpty(gelfField.Name) || gelfField.Layout == null || ExcludePropertyKeys.Contains(gelfField.FieldName))
                    continue;

                string fieldValue = gelfField.Layout.Render(logEventInfo);
                if (!gelfField.IncludeEmptyValue && string.IsNullOrEmpty(fieldValue))
                    continue;

                if (hasProperties)
                {
                    if (logEventInfo.Properties.ContainsKey(gelfField.FieldName) || logEventInfo.Properties.ContainsKey(gelfField.CleanName))
                        continue;
                }

                if (mdlcKeys != null)
                {
                    if (mdlcKeys.Contains(gelfField.FieldName) || mdlcKeys.Contains(gelfField.CleanName))
                        continue;
                }

                if (gdcKeys != null)
                {
                    if (gdcKeys.Contains(gelfField.FieldName) || gdcKeys.Contains(gelfField.CleanName))
                        continue;
                }

                if (gelfField.PropertyType == typeof(string) || gelfField.PropertyType == null)
                {
                    AddAdditionalField(jsonWriter, gelfField.FieldName, fieldValue);
                }
                else if (!string.IsNullOrEmpty(fieldValue))
                {
                    try
                    {
                        if (gelfField.PropertyType == typeof(int))
                        {
                            if (int.TryParse(fieldValue, out var intValue))
                                AddAdditionalField(jsonWriter, gelfField.FieldName, intValue);
                        }
                        else if (gelfField.PropertyType == typeof(long))
                        {
                            if (long.TryParse(fieldValue, out var longValue))
                                AddAdditionalField(jsonWriter, gelfField.FieldName, longValue);
                        }
                        else if (gelfField.PropertyType == typeof(double))
                        {
                            if (double.TryParse(fieldValue, out var doubleValue))
                                AddAdditionalField(jsonWriter, gelfField.FieldName, doubleValue);
                        }
                        else if (gelfField.PropertyType == typeof(bool))
                        {
                            if (bool.TryParse(fieldValue, out var boolValue))
                                AddAdditionalField(jsonWriter, gelfField.FieldName, boolValue);
                        }
                        else if (gelfField.PropertyType == typeof(object))
                        {
                            // Use JsonDocument for JSON Objects
                            using (var doc = JsonDocument.Parse(fieldValue))
                            {
                                var dict = ParseJsonElement(doc.RootElement);
                                AddAdditionalField(jsonWriter, gelfField.FieldName, dict);
                            }
                        }
                        else
                        {
                            AddAdditionalField(jsonWriter, gelfField.FieldName, fieldValue);
                        }
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error(ex, "GelfConverter: Error while formatting field: {0} (Type={1})", gelfField.CleanName, gelfField.PropertyType.FullName);
                        if (LogManager.ThrowExceptions)
                            throw;
                    }
                }
            }
        }

        private Dictionary<string, object> ParseJsonElement(JsonElement element)
        {
            var dict = new Dictionary<string, object>();

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        dict[property.Name] = ParseJsonValue(property.Value);
                    }
                    return dict;

                default:
                    dict["value"] = ParseJsonValue(element);
                    return dict;
            }
        }

        private object ParseJsonValue(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt32(out var intValue))
                        return intValue;
                    if (element.TryGetInt64(out var longValue))
                        return longValue;
                    if (element.TryGetDouble(out var doubleValue))
                        return doubleValue;
                    return element.GetDecimal();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                        list.Add(ParseJsonValue(item));
                    return list.ToArray();
                case JsonValueKind.Object:
                    return ParseJsonElement(element);
                default:
                    return element.ToString();
            }
        }

        public static decimal ToUnixTimeStamp(DateTime timeStamp)
        {
            return Convert.ToDecimal(timeStamp.ToUniversalTime().Subtract(UnixDateStart).TotalSeconds);
        }

        // AOT compatible serialization without reflection
        private void AddAdditionalField(Utf8JsonWriter jsonWriter, string key, object propertyValue)
        {
            // Key processing
            if (key.IndexOf("-", StringComparison.OrdinalIgnoreCase) >= 0)
                key = key.Replace('-', '_');

            //According to the GELF spec, additional field keys should start with '_' to avoid collision
            if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                key = "_" + key;

            //According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
            //Server MUST skip the field because it could override the MongoDB _key field
            //id field overwritten here as _idx to get around the issue id_ not a valid field and will be ignored by Graylog
            if (key.Equals("_id", StringComparison.OrdinalIgnoreCase))
                key = "_idx";

            jsonWriter.WritePropertyName(key);

            // AOT friendly typ handling without GetTypeCode()
            switch (propertyValue)
            {
                case null:
                    jsonWriter.WriteNullValue();
                    return;

                case bool boolValue:
                    jsonWriter.WriteBooleanValue(boolValue);
                    return;

                case byte byteValue:
                    jsonWriter.WriteNumberValue(byteValue);
                    return;

                case sbyte sbyteValue:
                    jsonWriter.WriteNumberValue(sbyteValue);
                    return;

                case short shortValue:
                    jsonWriter.WriteNumberValue(shortValue);
                    return;

                case ushort ushortValue:
                    jsonWriter.WriteNumberValue(ushortValue);
                    return;

                case int intValue:
                    jsonWriter.WriteNumberValue(intValue);
                    return;

                case uint uintValue:
                    jsonWriter.WriteNumberValue(uintValue);
                    return;

                case long longValue:
                    jsonWriter.WriteNumberValue(longValue);
                    return;

                case ulong ulongValue:
                    jsonWriter.WriteNumberValue(ulongValue);
                    return;

                case float floatValue:
                    jsonWriter.WriteNumberValue(floatValue);
                    return;

                case double doubleValue:
                    jsonWriter.WriteNumberValue(doubleValue);
                    return;

                case decimal decimalValue:
                    jsonWriter.WriteNumberValue(decimalValue);
                    return;

                case DateTime dateTimeValue:
                    jsonWriter.WriteNumberValue(ToUnixTimeStamp(dateTimeValue));
                    return;

                case string stringValue:
                    if (stringValue.Length > FullMessageMaxLength)
                        stringValue = stringValue.Substring(0, FullMessageMaxLength);
                    jsonWriter.WriteStringValue(stringValue);
                    return;

                case char charValue:
                    jsonWriter.WriteStringValue(charValue.ToString());
                    return;

                case Enum enumValue:
                    jsonWriter.WriteStringValue(enumValue.ToString());
                    return;

                case Dictionary<string, object> dict:
                    // for complex objects
                    WriteObject(jsonWriter, dict);
                    return;

                default:
                    // Fallback for unknown types
                    try
                    {
                        if (JsonSerializer.IsReflectionEnabledByDefault)
                        {
                            // NOT AOT compatible!
                            try
                            {
                                using (var memoryStream = new MemoryStream())
                                {
                                    using (var tempWriter = new Utf8JsonWriter(memoryStream, _jsonWriterOptions))
                                    {
                                        // This one is NOT AOT compatible
                                        JsonSerializer.Serialize(tempWriter, propertyValue, propertyValue.GetType(), CreateJsonSerializerOptions());
                                        tempWriter.Flush();

                                        // Copy JSON without parsing
                                        var json = Encoding.UTF8.GetString(memoryStream.ToArray());
                                        jsonWriter.WriteRawValue(json);
                                    }
                                }
                            }
                            catch(Exception ex)
                                                        {
                                InternalLogger.Warn(ex, "GelfConverter: Error while serializing field: {0} (Type={1})", key, propertyValue.GetType().FullName);
                                jsonWriter.WriteStringValue("[Error serializing value]");
                            }
                        }
                        else // if AOT is enabled
                        {
                            // Fallback for unknown types (AOT compatible, but less information)
                            jsonWriter.WriteStringValue(propertyValue.ToString());
                        }

                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Warn(ex, "GelfConverter: Error while adding field: {0} (Type={1})", key, propertyValue.GetType().FullName);
                        jsonWriter.WriteStringValue("[Error converting value]");
                        if (LogManager.ThrowExceptions)
                            throw;
                    }
                    return;
            }
        }

        private void WriteObject(Utf8JsonWriter writer, Dictionary<string, object> dict)
        {
            writer.WriteStartObject();
            foreach (var kvp in dict)
            {
                writer.WritePropertyName(kvp.Key);
                WriteValue(writer, kvp.Value);
            }
            writer.WriteEndObject();
        }

        private void WriteValue(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case double d:
                    writer.WriteNumberValue(d);
                    break;
                case decimal dec:
                    writer.WriteNumberValue(dec);
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case Dictionary<string, object> dict:
                    WriteObject(writer, dict);
                    break;
                case Array array:
                    writer.WriteStartArray();
                    foreach (var item in array)
                        WriteValue(writer, item);
                    writer.WriteEndArray();
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }

        private static JsonSerializerOptions CreateJsonSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = false,
                PropertyNamingPolicy = null // Behalten Sie die originalen Property-Namen
            };

            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new SystemTextJsonToStringConverter(typeof(System.Reflection.MemberInfo)));
            options.Converters.Add(new SystemTextJsonToStringConverter(typeof(System.Reflection.Assembly)));
            options.Converters.Add(new SystemTextJsonToStringConverter(typeof(System.Reflection.Module)));
            options.Converters.Add(new SystemTextJsonToStringConverter(typeof(System.IO.Stream)));

            return options;
        }
    }
}
