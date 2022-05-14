using System;
using System.Collections.Generic;
using System.Text;
using NLog.Config;

namespace NLog.Layouts.GelfLayout
{
    [Layout("GelfLayout")]
    [ThreadSafe]
    [ThreadAgnostic]
    public class GelfLayout : Layout, IGelfConverterOptions
    {
        private readonly GelfLayoutRenderer _renderer = new GelfLayoutRenderer();

        public GelfLayout()
        {
            _renderer.ExtraFields = new List<GelfField>();
        }

        /// <summary>
        /// Performance hack for NLog, that allows the Layout to dynamically disable <see cref="ThreadAgnosticAttribute"/> to ensure correct async context capture
        /// </summary>
        public Layout DisableThreadAgnostic => _renderer.DisableThreadAgnostic;

        /// <inheritdoc/>
        [ArrayParameter(typeof(GelfField), "field")]
        public IList<GelfField> ExtraFields { get => _renderer.ExtraFields; set => _renderer.ExtraFields = value; }

        /// <inheritdoc/>
        public bool IncludeEventProperties { get => _renderer.IncludeEventProperties; set => _renderer.IncludeEventProperties = value; }

        /// <inheritdoc/>
        public bool IncludeScopeProperties { get => _renderer.IncludeScopeProperties; set => _renderer.IncludeScopeProperties = value; }

        /// <inheritdoc/>
        public ISet<string> ExcludeProperties { get => _renderer.ExcludeProperties; set => _renderer.ExcludeProperties = value; }

        /// <inheritdoc/>
        [Obsolete("Replaced by IncludeEventProperties")]
        public bool IncludeAllProperties { get => IncludeEventProperties; set => IncludeEventProperties = value; }

        /// <inheritdoc/>
        [Obsolete("Replaced by IncludeScopeProperties")]
        public bool IncludeMdlc { get => IncludeScopeProperties; set => IncludeScopeProperties = value; }

        /// <inheritdoc/>
        public bool IncludeLegacyFields { get => _renderer.IncludeLegacyFields; set => _renderer.IncludeLegacyFields = value; }

        /// <inheritdoc/>
        public Layout Facility { get => _renderer.Facility; set => _renderer.Facility = value; }

        /// <inheritdoc/>
        protected override void RenderFormattedMessage(LogEventInfo logEvent, StringBuilder target)
        {
            _renderer.RenderAppend(logEvent, target);
        }

        /// <inheritdoc/>
        protected override string GetFormattedMessage(LogEventInfo logEvent)
        {
            return _renderer.Render(logEvent);
        }
    }
}
