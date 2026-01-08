using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Optimarr.Models
{
    public class VideoAnalysis
    {
        public int Id { get; set; }
        
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public double Duration { get; set; }
        
        public string Container { get; set; } = string.Empty;
        public string VideoCodec { get; set; } = string.Empty;
        public string VideoCodecTag { get; set; } = string.Empty; // CodecID tag (e.g., HVC1, AVC1)
        public bool IsCodecTagCorrect { get; set; } = true; // Whether the codec tag is correct
        public int BitDepth { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public bool IsHDR { get; set; }
        public string HDRType { get; set; } = string.Empty;
        public bool IsFastStart { get; set; } // MP4 fast start optimization (moov atom at beginning)
        
        public string AudioCodecs { get; set; } = string.Empty; // Comma-separated
        public int AudioTrackCount { get; set; }
        public string AudioTracksJson { get; set; } = string.Empty; // JSON array of AudioTrack objects
        public string SubtitleFormats { get; set; } = string.Empty; // Comma-separated
        public int SubtitleTrackCount { get; set; }
        public string SubtitleTracksJson { get; set; } = string.Empty; // JSON array of SubtitleTrack objects
        
        public CompatibilityScore OverallScore { get; set; }
        public int CompatibilityRating { get; set; } // 0-11 rating scale (count of Direct Play clients)
        public int DirectPlayClients { get; set; }
        public int RemuxClients { get; set; }
        public int TranscodeClients { get; set; }
        
        public string Issues { get; set; } = string.Empty; // JSON array
        public string Recommendations { get; set; } = string.Empty; // JSON array
        public string ClientResults { get; set; } = string.Empty; // JSON object with client compatibility details
        
        public string FullReport { get; set; } = string.Empty; // Full analysis report
        
        public bool IsBroken { get; set; } = false; // True if media information cannot be read or file is corrupted
        public string? BrokenReason { get; set; } // Reason why the file is marked as broken
        
        public DateTime AnalyzedAt { get; set; }
        
        public int? LibraryScanId { get; set; }
        public virtual LibraryScan? LibraryScan { get; set; }
        
        // Sonarr/Radarr Integration
        public string? ServarrType { get; set; } // "Sonarr" or "Radarr"
        public int? SonarrSeriesId { get; set; }
        public string? SonarrSeriesTitle { get; set; }
        public int? SonarrEpisodeId { get; set; }
        public int? SonarrEpisodeNumber { get; set; }
        public int? SonarrSeasonNumber { get; set; }
        public int? RadarrMovieId { get; set; }
        public string? RadarrMovieTitle { get; set; }
        public int? RadarrYear { get; set; }
        public DateTime? ServarrMatchedAt { get; set; } // When the match was made
    }

    public enum CompatibilityScore
    {
        Unknown,
        Optimal,
        Good,
        Poor
    }
}

