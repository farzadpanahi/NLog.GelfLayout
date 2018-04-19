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

        public JObject GetGelfJson(LogEventInfo logEventInfo, string facility)
        {
            //Retrieve the formatted message from LogEventInfo
            var logEventMessage = logEventInfo.FormattedMessage;
            if (logEventMessage == null) return null;

            //If we are dealing with an exception, pass exception properties to LogEventInfo properties
            if (logEventInfo.Exception != null)
            {
                logEventInfo.Properties.Add("ExceptionSource", logEventInfo.Exception.Source);
                logEventInfo.Properties.Add("ExceptionMessage", logEventInfo.Exception.Message);
                logEventInfo.Properties.Add("StackTrace", logEventInfo.Exception.ToString());
            }

            //Figure out the short message
            var shortMessage = logEventMessage;
            if (shortMessage.Length > ShortMessageMaxLength)
            {
                shortMessage = shortMessage.Substring(0, ShortMessageMaxLength);
            }

            //Construct the instance of GelfMessage
            //See https://github.com/Graylog2/graylog2-docs/wiki/GELF "Specification (version 1.0)"
            var gelfMessage = new GelfMessage
            {
                Version = GelfVersion,
                Host = Dns.GetHostName(),
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

            //Add any other interesting data to LogEventInfo properties
            logEventInfo.Properties.Add("LoggerName", logEventInfo.LoggerName);

            //We will persist them "Additional Fields" according to Gelf spec
            foreach (var property in logEventInfo.Properties)
            {
                AddAdditionalField(jsonObject, property);
            }

            return jsonObject;
        }

        public static decimal ToUnixTimeStamp(DateTime timeStamp)
        {
            return Convert.ToDecimal(timeStamp.ToUniversalTime().Subtract(UnixDateStart).TotalSeconds);
        }

        private static void AddAdditionalField(IDictionary<string, JToken> jObject, KeyValuePair<object, object> property)
        {
            var key = property.Key as string;

            if (key == null) return;

            //According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
            //Server MUST skip the field because it could override the MongoDB _key field
            //id field overriten here as _idx to get around the issue id_ not a valid field and will be ignored by graylog
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                key = "_idx";

            //According to the GELF spec, additional field keys should start with '_' to avoid collision
            if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                key = "_" + key;

            if (property.Value is Enum)
            {
                jObject.Add(key, property.Value.ToString());
            }
            else if (property.Value is DateTime)
            {
                jObject.Add(key, ToUnixTimeStamp((DateTime)property.Value));
            }
            else
            {
                jObject.Add(key, JToken.FromObject(property.Value));
            }
        }

    }
}
