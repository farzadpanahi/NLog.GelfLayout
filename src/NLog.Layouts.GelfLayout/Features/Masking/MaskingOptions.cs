using System.Collections.Generic;

namespace NLog.Layouts.GelfLayout.Features.Masking
{
    public sealed class MaskingOptions
    {
        public bool Enabled { get; set; } = true;
        public char MaskChar { get; set; } = '*';
        public bool FullExcludeAsEmpty { get; set; } = true;
        public bool CaseInsensitive { get; set; } = true;
        public List<MaskingFieldRule> Rules { get; set; } = new();
    }
}