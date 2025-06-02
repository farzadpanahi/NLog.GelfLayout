using NLog.Common;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
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
        private static readonly JsonSerializerOptions _jsonSerializerOptions = CreateJsonSerializerOptions();
        private static readonly JsonWriterOptions _jsonWriterOptions = new JsonWriterOptions
        {
            Indented = false,
            SkipValidation = false
        };

        private static readonly HashSet<string> ExcludePropertyKeys = new HashSet<string>(new string[] { "LoggerName", "_LoggerName", "ExceptionSource", "_ExceptionSource", "ExceptionMessage", "_ExceptionMessage", "ExceptionType", "_ExceptionType", "StackTrace", "_StackTrace" }, StringComparer.OrdinalIgnoreCase);

        private string _hostName;

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
                mdlcKeys = MappedDiagnosticsLogicalContext.GetNames();
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
                    object propertyValue = MappedDiagnosticsLogicalContext.GetObject(key);
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

                if(gdcKeys != null)
                {
                    if(gdcKeys.Contains(gelfField.FieldName) || gdcKeys.Contains(gelfField.CleanName))
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
                        if (gelfField.PropertyType == typeof(object))
                        {
                            try
                            {
                                var fieldObject = JsonSerializer.Deserialize<ExpandoObject>(fieldValue, _jsonSerializerOptions);
                                AddAdditionalField(jsonWriter, gelfField.FieldName, fieldObject);
                            }
                            catch (JsonException ex)
                            {
                                InternalLogger.Error(ex, "GelfConverter: Error while deserializing field: {0}", gelfField.CleanName);
                                if (LogManager.ThrowExceptions)
                                    throw;
                            }
                        }
                        else
                        {
                            var fieldValueType = Convert.ChangeType(fieldValue, gelfField.PropertyType);
                            AddAdditionalField(jsonWriter, gelfField.FieldName, fieldValueType);
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

        public static decimal ToUnixTimeStamp(DateTime timeStamp)
        {
            return Convert.ToDecimal(timeStamp.ToUniversalTime().Subtract(UnixDateStart).TotalSeconds);
        }

        private void AddAdditionalField(Utf8JsonWriter jsonWriter, string key, object propertyValue)
        {
            if (key.IndexOf("-", StringComparison.OrdinalIgnoreCase) >= 0)
                key = key.Replace('-', '_');

            //According to the GELF spec, additional field keys should start with '_' to avoid collision
            if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                key = "_" + key;

            //According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
            //Server MUST skip the field because it could override the MongoDB _key field
            //id field overriten here as _idx to get around the issue id_ not a valid field and will be ignored by graylog
            if (key.Equals("_id", StringComparison.OrdinalIgnoreCase))
                key = "_idx";

            jsonWriter.WritePropertyName(key);
            var typeCode = Convert.GetTypeCode(propertyValue);
            switch (typeCode)
            {
                case TypeCode.Empty:
#if !NETSTANDARD1_3
                case TypeCode.DBNull:
#endif
                    jsonWriter.WriteNullValue();
                    return;
                case TypeCode.Boolean:
                    jsonWriter.WriteBooleanValue(Convert.ToBoolean(propertyValue));
                    return;
                case TypeCode.DateTime:
                    jsonWriter.WriteNumberValue(ToUnixTimeStamp(Convert.ToDateTime(propertyValue)));
                    return;
                case TypeCode.Char:
                case TypeCode.String:
                    var propertyString = propertyValue.ToString();
                    if (propertyString.Length > FullMessageMaxLength)
                    {
                        propertyString = propertyString.Substring(0, FullMessageMaxLength);
                    }
                    jsonWriter.WriteStringValue(propertyString);
                    return;
                case TypeCode.Decimal:
                    jsonWriter.WriteNumberValue(Convert.ToDecimal(propertyValue));
                    return;
                case TypeCode.Double:
                    jsonWriter.WriteNumberValue(Convert.ToDouble(propertyValue));
                    return;
                case TypeCode.Single:
                    jsonWriter.WriteNumberValue(Convert.ToSingle(propertyValue));
                    return;
            }

            if (typeCode != TypeCode.Object)
            {
                if (propertyValue is Enum)
                {
                    jsonWriter.WriteStringValue(propertyValue.ToString());
                    return;
                }
                else
                {
                    switch (typeCode)
                    {
                        case TypeCode.Byte:
                            jsonWriter.WriteNumberValue(Convert.ToByte(propertyValue));
                            return;
                        case TypeCode.SByte:
                            jsonWriter.WriteNumberValue(Convert.ToSByte(propertyValue));
                            return;
                        case TypeCode.Int16:
                            jsonWriter.WriteNumberValue(Convert.ToInt16(propertyValue));
                            return;
                        case TypeCode.UInt16:
                            jsonWriter.WriteNumberValue(Convert.ToUInt16(propertyValue));
                            return;
                        case TypeCode.Int32:
                            jsonWriter.WriteNumberValue(Convert.ToInt32(propertyValue));
                            return;
                        case TypeCode.UInt32:
                            jsonWriter.WriteNumberValue(Convert.ToUInt32(propertyValue));
                            return;
                        case TypeCode.Int64:
                            jsonWriter.WriteNumberValue(Convert.ToInt64(propertyValue));
                            return;
                        case TypeCode.UInt64:
                            jsonWriter.WriteNumberValue(Convert.ToUInt64(propertyValue));
                            return;
                    }
                }
            }

            try
            {
                // For complex objects, we serialize them to JSON and write the raw value
                using (var memoryStream = new MemoryStream())
                {
                    using (var tempWriter = new Utf8JsonWriter(memoryStream, _jsonWriterOptions))
                    {
                        JsonSerializer.Serialize(tempWriter, propertyValue, propertyValue.GetType(), _jsonSerializerOptions);
                        tempWriter.Flush();

                        // copy json without parsing
                        var json = Encoding.UTF8.GetString(memoryStream.ToArray());
                        jsonWriter.WriteRawValue(json);
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "GelfConverter: Error while adding field: {0} (Type={1})", key, propertyValue.GetType().FullName);
                if (LogManager.ThrowExceptions)
                    throw;
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
