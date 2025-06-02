using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NLog.Layouts.GelfLayout
{
    internal class SystemTextJsonToStringConverter : JsonConverter<object>
    {
        private readonly Type _type;

        public SystemTextJsonToStringConverter(Type type)
        {
            _type = type;
        }

        public override bool CanConvert(Type typeToConvert)
        {
            // No reflection based checks in AOT
            return typeToConvert == _type || typeToConvert.IsSubclassOf(_type);
        }

        public override object Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("Only serialization is supported");
        }

        public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStringValue(value.ToString());
            }
        }
    }
}