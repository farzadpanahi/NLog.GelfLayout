using System.IO;
using System.Globalization;
using System.Text;
using NLog.LayoutRenderers;
using Newtonsoft.Json;

namespace NLog.Layouts.GelfLayout
{
    [LayoutRenderer("gelf")]
    public class GelfLayoutRenderer : LayoutRenderer
    {
        private static readonly JsonConverter[] _emptyJsonConverters = new JsonConverter[0];

        private readonly IConverter _converter;
        public GelfLayoutRenderer()
        {
            _converter = new GelfConverter();
        }

        public Layout Facility { get; set; }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var jsonObject = _converter.GetGelfJson(logEvent, Facility.Render(logEvent));
            if (jsonObject == null) return;

            int orgLength = builder.Length;

            try
            {
                // Write directly to StringBuilder, instead of allocating string first
                using (StringWriter sw = new StringWriter(builder, CultureInfo.InvariantCulture))
                {
                    JsonTextWriter jw = new JsonTextWriter(sw);
                    jw.Formatting = Formatting.None;
                    jsonObject.WriteTo(jw, _emptyJsonConverters);
                }
            }
            catch
            {
                builder.Length = orgLength; // Rewind, truncate
                throw;
            }
        }
    }
}