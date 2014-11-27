using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using NLog;
using Newtonsoft.Json.Linq;

namespace NLog.Layouts.GelfLayout
{
    public class GelfConverter : IConverter
    {
        private const int ShortMessageMaxLength = 250;
        private const string GelfVersion = "1.0";

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
                logEventInfo.Properties.Add("StackTrace", logEventInfo.Exception.StackTrace);
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
                                      Timestamp = logEventInfo.TimeStamp,
                                      Level = GetSeverityLevel(logEventInfo.Level),
                                      //Spec says: facility must be set by the client to "GELF" if empty
                                      Facility = (string.IsNullOrEmpty(facility) ? "GELF" : facility),
                                      Line = (logEventInfo.UserStackFrame != null)
                                                 ? logEventInfo.UserStackFrame.GetFileLineNumber().ToString(
                                                     CultureInfo.InvariantCulture)
                                                 : string.Empty,
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

        private static void AddAdditionalField(IDictionary<string, JToken> jObject, KeyValuePair<object, object> property)
        {
            var key = property.Key as string;
            var value = property.Value as string;

            if (key == null) return;

            //According to the GELF spec, libraries should NOT allow to send id as additional field (_id)
            //Server MUST skip the field because it could override the MongoDB _key field
            if (key.Equals("id", StringComparison.OrdinalIgnoreCase))
                key = "id_";

            //According to the GELF spec, additional field keys should start with '_' to avoid collision
            if (!key.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                key = "_" + key;

            jObject.Add(key, value);
        }

        /// <summary>
        /// Values from SyslogSeverity enum here: http://marc.info/?l=log4net-dev&m=109519564630799
        /// </summary>
        /// <param name="level"></param>
        /// <returns></returns>
        private static int GetSeverityLevel(LogLevel level)
        {
            if (level == LogLevel.Trace)
            {
                return 7;
            }
            if (level == LogLevel.Debug)
            {
                return 7;
            }
            if (level == LogLevel.Info)
            {
                return 6;
            }
            if (level == LogLevel.Warn)
            {
                return 4;
            }
            if (level == LogLevel.Fatal)
            {
                return 2;
            }

            return 3; //LogLevel.Error
        }
    }
}
