namespace NLog.Layouts.GelfLayout.Features.Masking
{
    public sealed class MaskingFieldRule
    {
        public string Field { get; set; } = default!;
        public int Prefix { get; set; }
        public int Suffix { get; set; }
        public bool Exclude { get; set; }
    }
}