using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Net;

namespace NLog.Layouts.GelfLayout.Test
{
    [TestClass]
    public class GelfLayoutRendererTest
    {
        private enum TestMsgEnum
        {
            Enum1,
            Enum2,
        }

        [TestMethod]
        public void CanRenderGelf()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();

            gelfRenderer.Facility = facility;

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"\","
                    + "\"full_message\":\"{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "\"_LoggerName\":\"{6}\"}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                loggerName);

            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfWithNonStringJsonValues()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = $"stringVal 1 {TestMsgEnum.Enum1} {dateTime.AddMinutes(-1)}";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();
            string stringKey = "stringKey";
            string stringVal = "stringVal";
            string intKey = "intKey";
            int intVal = 1;
            string enumKey = "enumKey";
            TestMsgEnum enumVal = TestMsgEnum.Enum1;
            string dateTimeKey = "dateTimeKey";
            DateTime dateTimeVal = dateTime.AddMinutes(-1);

            gelfRenderer.Facility = facility;

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
                Properties =
                {
                    { stringKey, stringVal },
                    { intKey, intVal },
                    { enumKey, enumVal },
                    { dateTimeKey, dateTimeVal }
                }
            };

            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedProperties = String.Format(
                "\"_{0}\":\"{1}\",\"_{2}\":{3},\"_{4}\":\"{5}\",\"_{6}\":{7}",
                stringKey,
                stringVal,
                intKey,
                intVal,
                enumKey,
                enumVal,
                dateTimeKey,
                GelfConverter.ToUnixTimeStamp(dateTimeVal));
            var expectedGelf = string.Format(
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"\","
                    + "\"full_message\":\"{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "{6},"
                    + "\"_LoggerName\":\"{7}\"}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                expectedProperties,
                loggerName);

            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfException()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Fatal;
            var hostname = Dns.GetHostName();
            var exception = FakeException.Throw();
            var gelfRenderer = new GelfLayoutRenderer();

            gelfRenderer.Facility = facility;

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
                Exception = exception,
            };
            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            string executingDirectory = Directory.GetCurrentDirectory();
            string srcDirectory =
                executingDirectory.Substring(0, executingDirectory.IndexOf("bin")).Replace("\\", "\\\\");
            string exceptionPath = $"{srcDirectory}FakeException.cs";
            string expectedException =
                "\"_ExceptionSource\":\"NLog.Layouts.GelfLayout.Test\","
                    + "\"_ExceptionMessage\":\"funny exception :D\","
                    + "\"_StackTrace\":\"System.Exception: funny exception :D ---> System.Exception: very funny "
                    + "exception ::D\\r\\n   --- End of inner exception stack trace ---\\r\\n   "
                    + "at NLog.Layouts.GelfLayout.Test.FakeException.Throw() in "
                    + exceptionPath
                    + ":line 9\"";
            var expectedGelf = string.Format(
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"\","
                    + "\"full_message\":\"{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "{6},"
                    + "\"_LoggerName\":\"{7}\"}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                expectedException,
                loggerName);

            Assert.AreEqual(expectedGelf, renderedGelf);
        }
    }
}
