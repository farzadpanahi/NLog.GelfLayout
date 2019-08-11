using System;
using System.ComponentModel;
using NLog.Config;

namespace NLog.Layouts.GelfLayout
{
    /// <summary>
    /// Additional Gelf field for <see cref="GelfLayout"/> 
    /// </summary>
    [NLogConfigurationItem]
    [ThreadAgnostic]
    public class GelfField
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GelfField" /> class.
        /// </summary>
        public GelfField() : this(null, null) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="GelfField" /> class.
        /// </summary>
        /// <param name="name">The name of the attribute.</param>
        /// <param name="layout">The layout of the attribute's value.</param>
        public GelfField(string name, Layout layout)
        {
            Name = name;
            Layout = layout;
        }

        /// <summary>
        /// Gets or sets the name of the attribute.
        /// </summary>
        /// <docgen category='Property Options' order='10' />
        [RequiredParameter]
        public string Name
        {
            get { return CleanName; }
            set
            {
                var name = (value ?? string.Empty).Trim();
                CleanName = name.TrimStart(new[] { '_' });
                if (!name.StartsWith("_", StringComparison.OrdinalIgnoreCase))
                    name = "_" + name;
                if (name.IndexOf("-", StringComparison.OrdinalIgnoreCase) >= 0)
                    name = name.Replace("-", "_");
                FieldName = name;
            }
        }

        /// <summary>
        /// Gets or sets the layout that will be rendered as the attribute's value.
        /// </summary>
        /// <docgen category='Property Options' order='10' />
        [RequiredParameter]
        public Layout Layout { get; set; }

        /// <summary>
        /// Gets or sets when an empty value should cause the property to be included
        /// </summary>
        [DefaultValue(true)]
        public bool IncludeEmptyValue { get; set; } = true;

        /// <summary>
        /// Gets or sets the type of the property.
        /// </summary>
        [DefaultValue(typeof(string))]
        public Type PropertyType { get; set; } = typeof(string);

        internal string CleanName { get; private set; }
        internal string FieldName { get; private set; }
    }
}
