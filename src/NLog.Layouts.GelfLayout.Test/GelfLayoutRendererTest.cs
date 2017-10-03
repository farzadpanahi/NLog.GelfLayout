using System;
using System.Globalization;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NLog.Layouts.GelfLayout.Test
{
    [TestClass]
    public class GelfLayoutRendererTest
    {
        
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
            var expectedGelf = string.Format("{{\"facility\":\"{0}\",\"file\":\"\",\"full_message\":\"{1}\",\"host\":\"{2}\",\"level\":{3},\"line\":0,\"short_message\":\"{4}\",\"timestamp\":{5},\"version\":\"1.1\",\"_LoggerName\":\"{6}\"}}", facility, message, hostname, logLevel.GetOrdinal(), message, expectedDateTime, loggerName);

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
            const string exceptionPath = "c:\\\\GitHub\\\\NLog.GelfLayout\\\\src\\\\NLog.Layouts.GelfLayout.Test\\\\FakeException.cs";
            const string expectedException = "\"_ExceptionSource\":\"NLog.Layouts.GelfLayout.Test\",\"_ExceptionMessage\":\"funny exception :D\",\"_StackTrace\":\"System.Exception: funny exception :D ---> System.Exception: very funny exception ::D\\r\\n   --- End of inner exception stack trace ---\\r\\n   at NLog.Layouts.GelfLayout.Test.FakeException.Throw() in "+ exceptionPath + ":line 9\"";
            var expectedGelf = string.Format("{{\"facility\":\"{0}\",\"file\":\"\",\"full_message\":\"{1}\",\"host\":\"{2}\",\"level\":{3},\"line\":0,\"short_message\":\"{4}\",\"timestamp\":{5},\"version\":\"1.1\",{6},\"_LoggerName\":\"{7}\"}}", facility, message, hostname, logLevel.GetOrdinal(), message, expectedDateTime, expectedException, loggerName);

            Assert.AreEqual(expectedGelf, renderedGelf);
        }
    }
}
