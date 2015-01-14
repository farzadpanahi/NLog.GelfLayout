using System;
using System.Globalization;
using System.Net;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NLog.Layouts.GelfLayout.Test
{
    [TestClass]
    public class GelfLayoutRendererTest
    {
        //Newtonsoft.Json uses ISO 8601 by default to convert date. GelfConverted is using this converter.
        const string ISO8601DateTimeFormat = "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK";

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
            var expectedDateTime = dateTime.ToString(ISO8601DateTimeFormat, CultureInfo.CurrentCulture);
            var expectedGelf = string.Format("{{\"facility\":\"{0}\",\"file\":\"\",\"full_message\":\"{1}\",\"host\":\"{2}\",\"level\":{3},\"line\":\"\",\"short_message\":\"{4}\",\"timestamp\":\"{5}\",\"version\":\"1.0\",\"_LoggerName\":\"{6}\"}}", facility, message, hostname, logLevel.GetOrdinal(), message, expectedDateTime, loggerName);

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
            var expectedDateTime = dateTime.ToString(ISO8601DateTimeFormat, CultureInfo.CurrentCulture);
            const string expectedException = "\"_ExceptionSource\":\"NLog.Layouts.GelfLayout.Test\",\"_ExceptionMessage\":\"funny exception :D\",\"_StackTrace\":\"   at NLog.Layouts.GelfLayout.Test.FakeException.Throw() in c:\\\\GitHub\\\\NLog.GelfLayout\\\\src\\\\NLog.Layouts.GelfLayout.Test\\\\FakeException.cs:line 9\"";
            var expectedGelf = string.Format("{{\"facility\":\"{0}\",\"file\":\"\",\"full_message\":\"{1}\",\"host\":\"{2}\",\"level\":{3},\"line\":\"\",\"short_message\":\"{4}\",\"timestamp\":\"{5}\",\"version\":\"1.0\",{6},\"_LoggerName\":\"{7}\"}}", facility, message, hostname, logLevel.GetOrdinal(), message, expectedDateTime, expectedException, loggerName);

            Assert.AreEqual(expectedGelf, renderedGelf);
        }
    }
}
