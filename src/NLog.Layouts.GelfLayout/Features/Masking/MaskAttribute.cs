using System;

namespace NLog.Layouts.GelfLayout.Features.Masking
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public sealed class MaskAttribute : Attribute
    {
        public int Prefix { get; set; } = 0;
        public int Suffix { get; set; } = 0;
        public bool Exclude { get; set; } = false;

        /// <summary>İsteğe bağlı alan adı eşlemesi (ör. JSON/dictionary içinde farklı isimliyse)</summary>
        public string FieldName { get; set; }
    }
}