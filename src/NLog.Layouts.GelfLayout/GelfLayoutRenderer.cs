using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using NLog.LayoutRenderers;
using NLog.Config;
using System;
using System.Text.Encodings.Web;

namespace NLog.Layouts.GelfLayout
{
    [LayoutRenderer("gelf")]
    [ThreadAgnostic]
    public class GelfLayoutRenderer : LayoutRenderer, IGelfConverterOptions
    {
        private readonly GelfConverter _converter = new GelfConverter();

        internal static readonly Layout _disableThreadAgnostic = "${threadid:cached=true}";

        public GelfLayoutRenderer()
        {
            IncludeEventProperties = true;
        }

        /// <summary>
        /// Performance hack for NLog, that allows the Layout to dynamically disable <see cref="ThreadAgnosticAttribute"/> to ensure correct async context capture
        /// </summary>
        public Layout DisableThreadAgnostic => IncludeScopeProperties ? _disableThreadAgnostic : null;

        /// <inheritdoc/>
        public bool IncludeEventProperties { get; set; }

        /// <inheritdoc/>
        public bool IncludeScopeProperties { get; set; }

        /// <inheritdoc/>
        public bool IncludeGdc { get; set; }

        /// <inheritdoc/>
        [Obsolete("Replaced by IncludeEventProperties")]
        public bool IncludeAllProperties { get => IncludeEventProperties; set => IncludeEventProperties = value; }

        /// <inheritdoc/>
        [Obsolete("Replaced by IncludeScopeProperties")]
        public bool IncludeMdlc { get => IncludeScopeProperties; set => IncludeScopeProperties = value; }

        /// <inheritdoc/>
        public bool IncludeLegacyFields { get => _includeLegacyFields ?? false; set => _includeLegacyFields = value; }
        private bool? _includeLegacyFields;

        /// <inheritdoc/>
        public Layout Facility
        {
            get => _facility;
            set
            {
                _facility = value;
                if (!_includeLegacyFields.HasValue && value != null && !(value is SimpleLayout simpleLayout && simpleLayout.IsFixedText && string.IsNullOrEmpty(simpleLayout.FixedText)))
                    IncludeLegacyFields = true;
            }
        }
        private Layout _facility;

        /// <inheritdoc/>
        public Layout HostName { get; set; } = "${hostname}";
        
        /// <inheritdoc/>
        public Layout FullMessage { get; set; } = "${message}";
        
        /// <inheritdoc/>
        public Layout ShortMessage { get; set; } = "${message}";

        IList<GelfField> IGelfConverterOptions.ExtraFields { get => ExtraFields; }

        internal IList<GelfField> ExtraFields { get; set; }

        public ISet<string> ExcludeProperties { get; set; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);



        internal void RenderAppend(LogEventInfo logEvent, StringBuilder builder)
        {
            Append(builder, logEvent);
        }

        protected override void Append(StringBuilder builder, LogEventInfo logEvent)
        {
            int orgLength = builder.Length;
            try
            {
                using (var memoryStream = new MemoryStream())
                {
                    using (var jsonWriter = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
                    {
                        _converter.ConvertToGelfMessage(jsonWriter, logEvent, this);
                        jsonWriter.Flush();
                    }

                    var json = Encoding.UTF8.GetString(memoryStream.ToArray());
                    builder.Append(json);
                }
            }
            catch
            {
                builder.Length = orgLength;
                throw;
            }
        }
    }
}