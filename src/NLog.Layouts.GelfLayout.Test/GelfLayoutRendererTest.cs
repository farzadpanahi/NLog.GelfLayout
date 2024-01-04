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
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
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
        public void CanRenderGelf11()
        {
            var loggerName = "TestLogger";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();

            gelfRenderer.IncludeLegacyFields = false;

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"version\":\"1.1\","
                    + "\"host\":\"{0}\","
                    + "\"short_message\":\"{1}\","
                    + "\"full_message\":\"{2}\","
                    + "\"timestamp\":{3},"
                    + "\"level\":{4},"
                    + "\"_LoggerName\":\"{5}\"}}",
                hostname,
                message,
                message,
                expectedDateTime,
                logLevel.GetOrdinal(),
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
            var expectedProperties = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "\"_{0}\":\"{1}\",\"_{2}\":{3},\"_{4}\":\"{5}\",\"_{6}\":{7}",
                stringKey,
                stringVal,
                intKey,
                intVal,
                enumKey,
                enumVal,
                dateTimeKey,
                GelfConverter.ToUnixTimeStamp(dateTimeVal));
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
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
                    + "\"_ExceptionType\":\"System.ArgumentException\","
                    + "\"_StackTrace\":\"System.ArgumentException: funny exception :D\\r\\n ---> System.Exception: very funny "
                    + "exception ::D\\r\\n   --- End of inner exception stack trace ---\\r\\n   "
                    + "at NLog.Layouts.GelfLayout.Test.FakeException.Throw() in "
                    + exceptionPath
                    + ":line 9\"";
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
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

        [TestMethod]
        public void CanRenderGelfIncludeMdlc()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();

            gelfRenderer.Facility = facility;
            gelfRenderer.IncludeScopeProperties = true;

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            Guid requestId = Guid.NewGuid();
            using (var mdlc = NLog.MappedDiagnosticsLogicalContext.SetScoped("RequestId", requestId))
            {
                var renderedGelf = gelfRenderer.Render(logEvent);
                var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
                var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{{\"facility\":\"{0}\","
                        + "\"file\":\"TestLogger\","
                        + "\"full_message\":\"{1}\","
                        + "\"host\":\"{2}\","
                        + "\"level\":{3},"
                        + "\"line\":0,"
                        + "\"short_message\":\"{4}\","
                        + "\"timestamp\":{5},"
                        + "\"version\":\"1.1\","
                        + "\"_LoggerName\":\"{6}\","
                        + "\"_RequestId\":\"{7}\"}}",
                    facility,
                    message,
                    hostname,
                    logLevel.GetOrdinal(),
                    message,
                    expectedDateTime,
                    loggerName,
                    requestId);
                Assert.AreEqual(expectedGelf, renderedGelf);
            }
        }

        [TestMethod]
        public void CanRenderGelfAdditionalFields()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfLayout= new GelfLayout();

            gelfLayout.Facility = facility;
            gelfLayout.ExtraFields.Add(new GelfField("ThreadId", "${threadid}") { PropertyType = typeof(int) });

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var renderedGelf = gelfLayout.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "\"_LoggerName\":\"{6}\","
                    + "\"_ThreadId\":{7}}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                loggerName,
                threadId);
            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfWithBadObject()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfLayout = new GelfLayout();
            gelfLayout.Facility = facility;
            gelfLayout.IncludeEventProperties = true;

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };
            logEvent.Properties["BadBoy"] = new BadLogObject();

            var renderedGelf = gelfLayout.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "\"_BadBoy\":{{\"BadArray\":[],\"BadProperty\":\"{6}\"}},"
                    + "\"_LoggerName\":\"{7}\"}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                typeof(BadLogObject).Assembly.ToString(),
                loggerName);
            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfWithBadObjectExcluded()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfLayout = new GelfLayout();
            gelfLayout.Facility = facility;
            gelfLayout.IncludeEventProperties = true;
            gelfLayout.ExcludeProperties.Add("BadBoy");

            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };
            logEvent.Properties["BadBoy"] = new BadLogObject();

            var renderedGelf = gelfLayout.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
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
        public void CanRenderGelfCustomMessage()
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
            gelfRenderer.FullMessage = "${hostname}|${message}";
            gelfRenderer.ShortMessage = "short|${message}";
            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{2}|{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"short|{4}\","
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
        public void CanRenderGelf11CustomMessage()
        {
            var loggerName = "TestLogger";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();

            gelfRenderer.IncludeLegacyFields = false;
            gelfRenderer.FullMessage = "${hostname}|${message}";
            gelfRenderer.ShortMessage = "short|${message}";
            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"version\":\"1.1\","
                    + "\"host\":\"{0}\","
                    + "\"short_message\":\"short|{1}\","
                    + "\"full_message\":\"{0}|{2}\","
                    + "\"timestamp\":{3},"
                    + "\"level\":{4},"
                    + "\"_LoggerName\":\"{5}\"}}",
                hostname,
                message,
                message,
                expectedDateTime,
                logLevel.GetOrdinal(),
                loggerName);

            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfWithNonStringJsonValuesCustomMessage()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = $"stringVal 1 {GelfLayoutRendererTest.TestMsgEnum.Enum1} {dateTime.AddMinutes(-1)}";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();
            string stringKey = "stringKey";
            string stringVal = "stringVal";
            string intKey = "intKey";
            int intVal = 1;
            string enumKey = "enumKey";
            GelfLayoutRendererTest.TestMsgEnum enumVal = GelfLayoutRendererTest.TestMsgEnum.Enum1;
            string dateTimeKey = "dateTimeKey";
            DateTime dateTimeVal = dateTime.AddMinutes(-1);

            gelfRenderer.Facility = facility;

            gelfRenderer.FullMessage = "${hostname}|${message}";
            gelfRenderer.ShortMessage = "short|${message}";
            
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
            var expectedProperties = String.Format(System.Globalization.CultureInfo.InvariantCulture,
                "\"_{0}\":\"{1}\",\"_{2}\":{3},\"_{4}\":\"{5}\",\"_{6}\":{7}",
                stringKey,
                stringVal,
                intKey,
                intVal,
                enumKey,
                enumVal,
                dateTimeKey,
                GelfConverter.ToUnixTimeStamp(dateTimeVal));
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{2}|{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"short|{4}\","
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
        public void CanRenderGelfExceptionCustomMessage()
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

            gelfRenderer.FullMessage = "${hostname}|${message}";
            gelfRenderer.ShortMessage = "short|${message}";
            
            var renderedGelf = gelfRenderer.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            string executingDirectory = Directory.GetCurrentDirectory();
            string srcDirectory =
                executingDirectory.Substring(0, executingDirectory.IndexOf("bin")).Replace("\\", "\\\\");
            string exceptionPath = $"{srcDirectory}FakeException.cs";
            string expectedException =
                "\"_ExceptionSource\":\"NLog.Layouts.GelfLayout.Test\","
                    + "\"_ExceptionMessage\":\"funny exception :D\","
                    + "\"_ExceptionType\":\"System.ArgumentException\","
                    + "\"_StackTrace\":\"System.ArgumentException: funny exception :D\\r\\n ---> System.Exception: very funny "
                    + "exception ::D\\r\\n   --- End of inner exception stack trace ---\\r\\n   "
                    + "at NLog.Layouts.GelfLayout.Test.FakeException.Throw() in "
                    + exceptionPath
                    + ":line 9\"";
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{2}|{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"short|{4}\","
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

        [TestMethod]
        public void CanRenderGelfIncludeMdlcCustomMessage()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfRenderer = new GelfLayoutRenderer();

            gelfRenderer.Facility = facility;
            gelfRenderer.IncludeScopeProperties = true;
            gelfRenderer.FullMessage = "${hostname}|${message}";
            gelfRenderer.ShortMessage = "short|${message}";
            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            Guid requestId = Guid.NewGuid();
            using (var mdlc = NLog.MappedDiagnosticsLogicalContext.SetScoped("RequestId", requestId))
            {
                var renderedGelf = gelfRenderer.Render(logEvent);
                var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
                var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{{\"facility\":\"{0}\","
                        + "\"file\":\"TestLogger\","
                        + "\"full_message\":\"{2}|{1}\","
                        + "\"host\":\"{2}\","
                        + "\"level\":{3},"
                        + "\"line\":0,"
                        + "\"short_message\":\"short|{4}\","
                        + "\"timestamp\":{5},"
                        + "\"version\":\"1.1\","
                        + "\"_LoggerName\":\"{6}\","
                        + "\"_RequestId\":\"{7}\"}}",
                    facility,
                    message,
                    hostname,
                    logLevel.GetOrdinal(),
                    message,
                    expectedDateTime,
                    loggerName,
                    requestId);
                Assert.AreEqual(expectedGelf, renderedGelf);
            }
        }

        [TestMethod]
        public void CanRenderGelfAdditionalFieldsCustomMessage()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfLayout= new GelfLayout();

            gelfLayout.Facility = facility;
            gelfLayout.ExtraFields.Add(new GelfField("ThreadId", "${threadid}") { PropertyType = typeof(int) });

            gelfLayout.FullMessage = "${hostname}|${message}";
            gelfLayout.ShortMessage = "short|${message}";
            
            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };

            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            var renderedGelf = gelfLayout.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{2}|{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"short|{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "\"_LoggerName\":\"{6}\","
                    + "\"_ThreadId\":{7}}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                loggerName,
                threadId);
            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfWithBadObjectCustomMessage()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfLayout = new GelfLayout();
            gelfLayout.Facility = facility;
            gelfLayout.IncludeEventProperties = true;

            gelfLayout.FullMessage = "${hostname}|${message}";
            gelfLayout.ShortMessage = "short|${message}";
            
            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };
            logEvent.Properties["BadBoy"] = new GelfLayoutRendererTest.BadLogObject();

            var renderedGelf = gelfLayout.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{2}|{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"short|{4}\","
                    + "\"timestamp\":{5},"
                    + "\"version\":\"1.1\","
                    + "\"_BadBoy\":{{\"BadArray\":[],\"BadProperty\":\"{6}\"}},"
                    + "\"_LoggerName\":\"{7}\"}}",
                facility,
                message,
                hostname,
                logLevel.GetOrdinal(),
                message,
                expectedDateTime,
                typeof(GelfLayoutRendererTest.BadLogObject).Assembly.ToString(),
                loggerName);
            Assert.AreEqual(expectedGelf, renderedGelf);
        }

        [TestMethod]
        public void CanRenderGelfWithBadObjectExcludedCustomMessage()
        {
            var loggerName = "TestLogger";
            var facility = "TestFacility";
            var dateTime = DateTime.Now;
            var message = "hello, gelf :)";
            var logLevel = LogLevel.Info;
            var hostname = Dns.GetHostName();
            var gelfLayout = new GelfLayout();
            gelfLayout.Facility = facility;
            gelfLayout.IncludeEventProperties = true;
            gelfLayout.ExcludeProperties.Add("BadBoy");
            gelfLayout.FullMessage = "${hostname}|${message}";
            gelfLayout.ShortMessage = "short|${message}";
            var logEvent = new LogEventInfo
            {
                LoggerName = loggerName,
                Level = logLevel,
                Message = message,
                TimeStamp = dateTime,
            };
            logEvent.Properties["BadBoy"] = new GelfLayoutRendererTest.BadLogObject();

            var renderedGelf = gelfLayout.Render(logEvent);
            var expectedDateTime = GelfConverter.ToUnixTimeStamp(dateTime);
            var expectedGelf = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{{\"facility\":\"{0}\","
                    + "\"file\":\"TestLogger\","
                    + "\"full_message\":\"{2}|{1}\","
                    + "\"host\":\"{2}\","
                    + "\"level\":{3},"
                    + "\"line\":0,"
                    + "\"short_message\":\"short|{4}\","
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

        public class BadLogObject
        {
            public object[] BadArray { get; }
            public System.Reflection.Assembly BadProperty => typeof(BadLogObject).Assembly;

            public object ExceptionalBadProperty => throw new System.NotSupportedException();

            public BadLogObject()
            {
                BadArray = new object[] { this };
            }
        }
    }
}
