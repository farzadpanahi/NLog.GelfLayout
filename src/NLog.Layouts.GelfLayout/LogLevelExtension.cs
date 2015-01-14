using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NLog
{
    public static class LogLevelExtension
    {
        /// <summary>
        /// Values from SyslogSeverity enum here: http://marc.info/?l=log4net-dev&m=109519564630799
        /// </summary>
        /// <param name="logLevel"></param>
        /// <returns>Ordinal representation of LogLevel</returns>
        public static int GetOrdinal(this LogLevel logLevel)
        {
            if (logLevel == LogLevel.Trace) return 7;
            if (logLevel == LogLevel.Debug) return 7;
            if (logLevel == LogLevel.Info)  return 6;
            if (logLevel == LogLevel.Warn)  return 4;
            if (logLevel == LogLevel.Fatal) return 2;

            return 3; // Log.LevelError or anything else
        }
    }
}
