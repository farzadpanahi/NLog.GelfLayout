using Newtonsoft.Json;
using NLog.Config;
using NLog.LayoutRenderers;
using NLog.Layouts.GelfLayout.Features.Masking;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace NLog.Layouts.GelfLayout
{
    [LayoutRenderer("gelf")]
    [ThreadAgnostic]
    [ThreadSafe]
    public class GelfLayoutRenderer : LayoutRenderer, IGelfConverterOptions
    {
        private readonly GelfConverter _converter;

        internal static readonly Layout _disableThreadAgnostic = "${threadid:cached=true}";

        public GelfLayoutRenderer()
        {

            IncludeEventProperties = true;
            try
            {
                var masking = ResolveService<IMaskingService>(); // artık provider bağlı
                _converter = masking != null
                    ? new GelfConverter(masking)
                    : new GelfConverter();
            }
            catch (Exception)
            {
                _converter = new GelfConverter();
            }

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
                if (!_includeLegacyFields.HasValue && value != null &&
                    !(value is SimpleLayout simpleLayout &&
                    simpleLayout.IsFixedText &&
                    string.IsNullOrEmpty(simpleLayout.FixedText)))
                {
                    IncludeLegacyFields = true;
                }
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

        public ISet<string> ExcludeProperties { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                using (StringWriter sw = new(builder, CultureInfo.InvariantCulture))
                {
                    JsonTextWriter jw = new JsonTextWriter(sw)
                    {
                        Formatting = Formatting.None
                    };
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