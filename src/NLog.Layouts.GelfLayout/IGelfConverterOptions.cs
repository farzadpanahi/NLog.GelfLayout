using System.Collections.Generic;

namespace NLog.Layouts.GelfLayout
{
    public interface IGelfConverterOptions
    {
        /// <summary>
        /// Gets or sets a value indicating whether to include contents of the <see cref="MappedDiagnosticsLogicalContext"/> dictionary.
        /// </summary>
        bool IncludeMdlc { get; }

        /// <summary>
        /// Gets or sets the option to include all properties from the log events
        /// </summary>
        bool IncludeAllProperties { get; }

        /// <summary>
        /// Gets the array of additional custom fields to include in the Gelf message
        /// </summary>
        IList<GelfField> ExtraFields { get; }

        /// <summary>
        /// Gets the collection of property names to exclude from the LogEvent
        /// </summary>
        ISet<string> ExcludeProperties { get; }

        /// <summary>
        /// Include deprecated fields no longer part of official GelfVersion 1.1 specification
        /// </summary>
        bool IncludeLegacyFields { get; }

        /// <summary>
        /// Graylog Facility
        /// </summary>
        /// <remarks>
        /// Ignored when <see cref="IncludeLegacyFields"/> = false
        /// </remarks>
        Layout Facility { get; }
    }
}
