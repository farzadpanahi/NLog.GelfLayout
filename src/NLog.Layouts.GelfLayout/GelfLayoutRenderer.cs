using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using NLog.LayoutRenderers;
using NLog.Config;

namespace NLog.Layouts.GelfLayout
{
    [LayoutRenderer("gelf")]
    [ThreadAgnostic]
    [ThreadSafe]
    public class GelfLayoutRenderer : LayoutRenderer, IGelfConverterOptions
    {
        private readonly GelfConverter _converter = new GelfConverter();

        internal static readonly Layout _disableThreadAgnostic = "${threadid:cached=true}";

        public GelfLayoutRenderer()
        {
            IncludeAllProperties = true;
            IncludeLegacyFields = true;
        }

        /// <summary>
        /// Performance hack for NLog, that allows the Layout to dynamically disable <see cref="ThreadAgnosticAttribute"/> to ensure correct async context capture
        /// </summary>
        public Layout DisableThreadAgnostic => IncludeMdlc ? _disableThreadAgnostic : null;

        /// <inheritdoc/>
        public bool IncludeAllProperties { get; set; }

        /// <inheritdoc/>
        public bool IncludeMdlc { get; set; }

        /// <inheritdoc/>
        public bool IncludeLegacyFields { get; set; }

        /// <inheritdoc/>
        public Layout Facility { get; set; }

        IList<GelfField> IGelfConverterOptions.ExtraFields { get => ExtraFields; }

        internal IList<GelfField> ExtraFields { get; set; }

        internal void RenderAppend(LogEventInfo logEvent, StringBuilder builder)
        {
            Append(builder, logEvent);
        }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            int orgLength = builder.Length;

            try
            {
                // Write directly to StringBuilder, instead of allocating string first
                using (StringWriter sw = new StringWriter(builder, CultureInfo.InvariantCulture))
                {
                    JsonTextWriter jw = new JsonTextWriter(sw);
                    jw.Formatting = Formatting.None;
                    _converter.ConvertToGelfMessage(jw, logEvent, this);
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