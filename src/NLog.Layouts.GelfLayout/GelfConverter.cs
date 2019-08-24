using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog.Common;

namespace NLog.Layouts.GelfLayout
{
    public class GelfConverter
    {
        private const int ShortMessageMaxLength = 250;
        private const int FullMessageMaxLength = 16383; // Truncate due to: https://github.com/Graylog2/graylog2-server/issues/873
        private const string GelfVersion11 = "1.1";
        private static DateTime UnixDateStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private long _previousUtcTimestampTicks;
        private long _artificialUtcTimestampTicks;

        private static readonly JsonConverter[] _emptyJsonConverters = new JsonConverter[0];
        private JsonSerializer JsonSerializer => _jsonSerializer ?? (_jsonSerializer = JsonSerializer.CreateDefault());
        private JsonSerializer _jsonSerializer;

        private static readonly HashSet<string> ExcludePropertyKeys = new HashSet<string>(new string[] { "LoggerName", "_LoggerName", "ExceptionSource", "_ExceptionSource", "ExceptionMessage", "_ExceptionMessage", "ExceptionType", "_ExceptionType", "StackTrace", "_StackTrace" }, StringComparer.OrdinalIgnoreCase);

        private string _hostName;

        public void ConvertToGelfMessage(JsonWriter jsonWriter, LogEventInfo logEventInfo, IGelfConverterOptions converterOptions)
        {
            //Retrieve the formatted message from LogEventInfo
            var logEventMessage = logEventInfo.FormattedMessage ?? string.Empty;
            if (logEventMessage.Length > FullMessageMaxLength)
            {
                logEventMessage = logEventMessage.Substring(0, FullMessageMaxLength);
            }

            //Figure out the short message
            var shortMessage = logEventMessage;
            if (shortMessage.Length > ShortMessageMaxLength)
            {
                shortMessage = shortMessage.Substring(0, ShortMessageMaxLength);
            }

            // Attempt to produce unique timestamps even when low time precision (Improve sorting in Grafana)
            var logEventUtcTimeStamp = logEventInfo.TimeStamp.ToUniversalTime();
            if (converterOptions.FixDuplicateTimestamp)
            {
                var artificialUtcTimestampTicks = _artificialUtcTimestampTicks;
                if (System.Threading.Interlocked.Read(ref _previousUtcTimestampTicks) == logEventUtcTimeStamp.Ticks)
                {
                    logEventUtcTimeStamp = MakeUniqueUnixTimeStamp(logEventUtcTimeStamp, artificialUtcTimestampTicks);
                }
                else
                {
                    _artificialUtcTimestampTicks = _previousUtcTimestampTicks = logEventUtcTimeStamp.Ticks;
                }
            }

            //Construct the instance of GelfMessage
            jsonWriter.WriteStartObject();

            if (converterOptions.IncludeLegacyFields)
            {
                var facility = converterOptions.Facility?.Render(logEventInfo);
                if (string.IsNullOrEmpty(facility))
                {
                    facility = "GELF";   //Spec says: facility must be set by the client to "GELF" if empty
                }
                WriteGelfVersionLegacy(jsonWriter, logEventInfo, logEventUtcTimeStamp, logEventMessage, shortMessage, facility);
            }
            else
            {
                WriteGelfVersion11(jsonWriter, logEventInfo, logEventUtcTimeStamp, logEventMessage, shortMessage);
            }

            //We will persist them "Additional Fields" according to Gelf spec
            bool hasProperties = converterOptions.IncludeAllProperties && logEventInfo.HasProperties;
            if (hasProperties)
            {
                foreach (var property in logEventInfo.Properties)
                {
                    string key = property.Key as string;
                    if (key == null || ExcludePropertyKeys.Contains(key))
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
            if (converterOptions.IncludeMdlc)
            {
                mdlcKeys = MappedDiagnosticsLogicalContext.GetNames();
                bool foundMdlcItem = false;
                foreach (string key in mdlcKeys)
                {
                    if (string.IsNullOrEmpty(key) || ExcludePropertyKeys.Contains(key))
                        continue;

                    if (hasProperties && logEventInfo.Properties.ContainsKey(key))
                        continue;

                    foundMdlcItem = true;
                    object propertyValue = MappedDiagnosticsLogicalContext.GetObject(key);
                    AddAdditionalField(jsonWriter, key, propertyValue);
                }
                if (!foundMdlcItem)
                    mdlcKeys = null;
            }

            if (converterOptions.ExtraFields?.Count > 0)
            {
                AddLayoutGelfFields(jsonWriter, logEventInfo, converterOptions.ExtraFields, hasProperties, mdlcKeys);
            }

            jsonWriter.WriteEndObject();
        }

        /// <summary>
        /// See http://docs.graylog.org/en/2.0/pages/gelf.html#gelf-payload-specification "Specification (version 1.0)"
        /// </summary>
        private void WriteGelfVersionLegacy(JsonWriter jsonWriter, LogEventInfo logEventInfo, DateTime logEventTimeStamp, string logEventMessage, string shortMessage, string facility)
        {
            jsonWriter.WritePropertyName("facility");
            jsonWriter.WriteValue((string.IsNullOrEmpty(facility) ? "GELF" : facility));
            string callSiteFileName = logEventInfo.CallerFilePath;
            if (string.IsNullOrEmpty(callSiteFileName))
                callSiteFileName = logEventInfo.LoggerName ?? string.Empty;
            jsonWriter.WritePropertyName("file");
            jsonWriter.WriteValue(callSiteFileName);
            jsonWriter.WritePropertyName("full_message");
            jsonWriter.WriteValue(logEventMessage);
            jsonWriter.WritePropertyName("host");
            jsonWriter.WriteValue((_hostName ?? (_hostName = GetHostName())) ?? "UnknownHost");
            jsonWriter.WritePropertyName("level");
            jsonWriter.WriteValue(logEventInfo.Level.GetOrdinal());
            jsonWriter.WritePropertyName("line");
            jsonWriter.WriteValue(logEventInfo.CallerLineNumber);
            jsonWriter.WritePropertyName("short_message");
            jsonWriter.WriteValue(shortMessage);
            jsonWriter.WritePropertyName("timestamp");
            jsonWriter.WriteValue(ToUnixTimeStamp(logEventTimeStamp));
            jsonWriter.WritePropertyName("version");
            jsonWriter.WriteValue(GelfVersion11);
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
        private void WriteGelfVersion11(JsonWriter jsonWriter, LogEventInfo logEventInfo, DateTime logEventTimeStamp, string logEventMessage, string shortMessage)
        {
            jsonWriter.WritePropertyName("version");
            jsonWriter.WriteValue(GelfVersion11);
            jsonWriter.WritePropertyName("host");
            jsonWriter.WriteValue((_hostName ?? (_hostName = GetHostName())) ?? "UnknownHost");
            jsonWriter.WritePropertyName("short_message");
            jsonWriter.WriteValue(shortMessage);
            jsonWriter.WritePropertyName("full_message");
            jsonWriter.WriteValue(logEventMessage);
            jsonWriter.WritePropertyName("timestamp");
            jsonWriter.WriteValue(ToUnixTimeStamp(logEventTimeStamp));
            jsonWriter.WritePropertyName("level");
            jsonWriter.WriteValue(logEventInfo.Level.GetOrdinal());
        }

        private void AddLayoutGelfFields(JsonWriter jsonWriter, LogEventInfo logEventInfo, IList<GelfField> gelfFields, bool hasProperties, ICollection<string> mdlcKeys)
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
                            using (var reader = new JsonTextReader(new StringReader(fieldValue)))
                            {
                                var jsonSerializer = JsonSerializer;
                                lock (jsonSerializer)
                                {
                                    var fieldOject = jsonSerializer.Deserialize(reader, typeof(ExpandoObject));
                                    AddAdditionalField(jsonWriter, gelfField.FieldName, fieldOject);
                                }
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
                        _jsonSerializer = null; // Maybe become broken
                        InternalLogger.Error(ex, "GelfConverter: Error while formatting field: {0} (Type={1})", gelfField.CleanName, gelfField.PropertyType.FullName);
                        if (LogManager.ThrowExceptions)
                            throw;
                    }
                }
            }
        }

        private DateTime MakeUniqueUnixTimeStamp(DateTime logEventUtcTimeStamp, long artificialUtcTimestampTicks)
        {
            do
            {
                if (System.Threading.Interlocked.CompareExchange(ref _artificialUtcTimestampTicks, artificialUtcTimestampTicks + TimeSpan.TicksPerMillisecond / 10, artificialUtcTimestampTicks) == artificialUtcTimestampTicks)
                {
                    artificialUtcTimestampTicks += TimeSpan.TicksPerMillisecond / 10;
                    break;
                }
                artificialUtcTimestampTicks = System.Threading.Interlocked.Read(ref _artificialUtcTimestampTicks);
            }
            while (System.Threading.Interlocked.Read(ref _previousUtcTimestampTicks) == logEventUtcTimeStamp.Ticks);

            if (artificialUtcTimestampTicks > logEventUtcTimeStamp.Ticks && (logEventUtcTimeStamp.Ticks + TimeSpan.TicksPerMillisecond) > artificialUtcTimestampTicks)
            {
                return new DateTime(artificialUtcTimestampTicks, DateTimeKind.Utc);
            }
            else
            {
                return new DateTime(logEventUtcTimeStamp.Ticks + TimeSpan.TicksPerMillisecond - TimeSpan.TicksPerMillisecond / 10);
            }
        }

        public static decimal ToUnixTimeStamp(DateTime timeStamp)
        {
            return Convert.ToDecimal(timeStamp.ToUniversalTime().Subtract(UnixDateStart).TotalSeconds);
        }

        private static string GetHostName()
        {
            try
            {
                return Dns.GetHostName();
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"GELF HostName Lookup Failed: {ex}");
                if (LogManager.ThrowExceptions)
                    throw;

                return null;
            }
        }

        private static void AddAdditionalField(JsonWriter jsonWriter, string key, object propertyValue)
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
                    jsonWriter.WriteNull();
                    return;
                case TypeCode.Boolean:
                    jsonWriter.WriteValue(Convert.ToBoolean(propertyValue));
                    return;
                case TypeCode.DateTime:
                    jsonWriter.WriteValue(ToUnixTimeStamp(Convert.ToDateTime(propertyValue)));
                    return;
                case TypeCode.Char:
                case TypeCode.String:
                    var propertyString = propertyValue.ToString();
                    if (propertyString.Length > FullMessageMaxLength)
                    {
                        propertyString = propertyString.Substring(0, FullMessageMaxLength);
                    }
                    jsonWriter.WriteValue(propertyString);
                    return;
                case TypeCode.Decimal:
                    jsonWriter.WriteValue(Convert.ToDecimal(propertyValue));
                    return;
                case TypeCode.Double:
                    jsonWriter.WriteValue(Convert.ToDouble(propertyValue));
                    return;
                case TypeCode.Single:
                    jsonWriter.WriteValue(Convert.ToSingle(propertyValue));
                    return;
            }

            if (typeCode != TypeCode.Object)
            {
                if (propertyValue is Enum)
                {
                    jsonWriter.WriteValue(propertyValue.ToString());
                    return;
                }
                else
                {
                    switch (typeCode)
                    {
                        case TypeCode.Byte:
                            jsonWriter.WriteValue(Convert.ToByte(propertyValue));
                            return;
                        case TypeCode.SByte:
                            jsonWriter.WriteValue(Convert.ToSByte(propertyValue));
                            return;
                        case TypeCode.Int16:
                            jsonWriter.WriteValue(Convert.ToInt16(propertyValue));
                            return;
                        case TypeCode.UInt16:
                            jsonWriter.WriteValue(Convert.ToUInt16(propertyValue));
                            return;
                        case TypeCode.Int32:
                            jsonWriter.WriteValue(Convert.ToInt32(propertyValue));
                            return;
                        case TypeCode.UInt32:
                            jsonWriter.WriteValue(Convert.ToUInt32(propertyValue));
                            return;
                        case TypeCode.Int64:
                            jsonWriter.WriteValue(Convert.ToInt64(propertyValue));
                            return;
                        case TypeCode.UInt64:
                            jsonWriter.WriteValue(Convert.ToUInt64(propertyValue));
                            return;
                    }
                }
            }

            try
            {
                var jtoken = JToken.FromObject(propertyValue);
                jtoken.WriteTo(jsonWriter, _emptyJsonConverters);
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "GelfConverter: Error while adding field: {0} (Type={1})", key, propertyValue.GetType().FullName);
                if (LogManager.ThrowExceptions)
                    throw;
            }
        }
    }
}
