using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;

namespace NLog.Layouts.GelfLayout
{
    public class GelfConverter : IConverter
    {
        private const int ShortMessageMaxLength = 250;
        private const string GelfVersion = "1.1";
        private static DateTime UnixDateStart = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static HashSet<string> ExcludePropertyKeys = new HashSet<string>(new string[] { "LoggerName", "_LoggerName", "ExceptionSource", "_ExceptionSource", "ExceptionMessage", "_ExceptionMessage", "StackTrace", "_StackTrace" }, StringComparer.OrdinalIgnoreCase);

        private string _hostName;

        public JObject GetGelfJson(LogEventInfo logEventInfo, string facility)
        {
            //Retrieve the formatted message from LogEventInfo
            var logEventMessage = logEventInfo.FormattedMessage;
            if (logEventMessage == null) return null;

            //Figure out the short message
            var shortMessage = logEventMessage;
            if (shortMessage.Length > ShortMessageMaxLength)
            {
                shortMessage = shortMessage.Substring(0, ShortMessageMaxLength);
            }

            //Construct the instance of GelfMessage
            //See http://docs.graylog.org/en/2.0/pages/gelf.html#gelf-payload-specification "Specification (version 1.0)"
            var gelfMessage = new GelfMessage
            {
                Version = GelfVersion,
                Host = (_hostName ?? (_hostName = GetHostName())) ?? "UnknownHost",
                ShortMessage = shortMessage,
                FullMessage = logEventMessage,
                Timestamp = ToUnixTimeStamp(logEventInfo.TimeStamp),
                Level = logEventInfo.Level.GetOrdinal(),
                //Spec says: facility must be set by the client to "GELF" if empty
                Facility = (string.IsNullOrEmpty(facility) ? "GELF" : facility),
                Line = (logEventInfo.UserStackFrame != null)
                                                 ? logEventInfo.UserStackFrame.GetFileLineNumber()
                                                 : 0,
                File = (logEventInfo.UserStackFrame != null)
                                                 ? logEventInfo.UserStackFrame.GetFileName()
                                                 : string.Empty,
            };

            //Convert to JSON
            var jsonObject = JObject.FromObject(gelfMessage);

            //We will persist them "Additional Fields" according to Gelf spec
            if (logEventInfo.Properties.Count > 0)
            {
                foreach (var property in logEventInfo.Properties)
                {
                    string key = property.Key as string;
                    if (key == null || ExcludePropertyKeys.Contains(key))
                        continue;

                    AddAdditionalField(jsonObject, key, property.Value);
                }
            }

            //If we are dealing with an exception, pass exception properties as additional fields
            if (logEventInfo.Exception != null)
            {
                AddAdditionalField(jsonObject, "_ExceptionSource", logEventInfo.Exception.Source);
                AddAdditionalField(jsonObject, "_ExceptionMessage", logEventInfo.Exception.Message);
                AddAdditionalField(jsonObject, "_StackTrace", logEventInfo.Exception.ToString());
            }

            //Add any other interesting data as additional fields
            AddAdditionalField(jsonObject, "_LoggerName", logEventInfo.LoggerName);

            return jsonObject;
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
                Common.InternalLogger.Error($"GELF HostName Lookup Failed: {ex}");
                if (LogManager.ThrowExceptions)
                    throw;

                return null;
            }
        }

        private static void AddAdditionalField(IDictionary<string, JToken> jObject, string key, object propertyValue)
        {
            //According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
            //Server MUST skip the field because it could override the MongoDB _key field
            //id field overriten here as _idx to get around the issue id_ not a valid field and will be ignored by graylog
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                key = "_idx";

            //According to the GELF spec, additional field keys should start with '_' to avoid collision
            if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                key = "_" + key;

            if (propertyValue is Enum)
            {
                jObject.Add(key, propertyValue.ToString());
            }
            else if (propertyValue is DateTime)
            {
                jObject.Add(key, ToUnixTimeStamp((DateTime)propertyValue));
            }
            else
            {
                jObject.Add(key, JToken.FromObject(propertyValue));
            }
        }

    }
}
