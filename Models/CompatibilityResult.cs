using System.Collections.Generic;

namespace Optimarr.Models
{
    public class CompatibilityResult
    {
        public string OverallScore { get; set; } = "Unknown"; // Optimal, Good, Poor
        public int CompatibilityRating { get; set; } = 0; // 0-100 rating scale (based on media properties)
        public Dictionary<string, ClientCompatibility> ClientResults { get; set; } = new(); // Deprecated - kept for backward compatibility
        public List<string> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class ClientCompatibility
    {
        public string Status { get; set; } = "Unknown"; // Direct Play, Remux, Transcode, Unsupported
        public string Reason { get; set; } = string.Empty;
        public List<string> Warnings { get; set; } = new();
    }
}

