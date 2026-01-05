namespace Optimarr.Models
{
    public class CompatibilityOverride
    {
        public string Codec { get; set; } = string.Empty; // e.g., "H.265 8-bit", "AAC", "MP4"
        public string Client { get; set; } = string.Empty; // e.g., "iOS", "Chrome", "Android"
        public string SupportLevel { get; set; } = string.Empty; // "Supported", "Partial", "Unsupported"
        public string Category { get; set; } = string.Empty; // "Video", "Audio", "Container", "Subtitle"
    }
}

