using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using NLog;
using NLog.LayoutRenderers;
using Newtonsoft.Json;

namespace NLog.Layouts.GelfLayout
{
    [LayoutRenderer("gelf")]
    public class GelfLayoutRenderer : LayoutRenderer
    {
        private readonly IConverter _converter;
        public GelfLayoutRenderer()
        {
            _converter = new GelfConverter();
        }

        public string Facility { get; set; }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            var jsonObject = _converter.GetGelfJson(logEvent, Facility);
            if (jsonObject == null) return;
            var jsonString = jsonObject.ToString(Formatting.None, null);
            builder.Append(jsonString);
        }
    }
}