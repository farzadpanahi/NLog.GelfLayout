using NLog;
using Newtonsoft.Json.Linq;

namespace NLog.Layouts.GelfLayout
{
    public interface IConverter
    {
        JObject GetGelfJson(LogEventInfo logEventInfo, string facility);
    }
}
