using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Optimarr.Models
{
    public class PlaybackHistory
    {
        public int Id { get; set; }

        [Required]
        public string JellyfinItemId { get; set; } = string.Empty;

        public string? JellyfinUserId { get; set; }
        public string? JellyfinUserName { get; set; }
        
        [Required]
        public string ItemName { get; set; } = string.Empty;
        
        public string? ItemType { get; set; } // Movie, Episode, etc.
        public string? MediaType { get; set; } // Video, Audio
        
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        public DateTime PlaybackStartTime { get; set; }
        public DateTime? PlaybackStopTime { get; set; }
        public TimeSpan? PlaybackDuration { get; set; }
        
        public string? ClientName { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceId { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string PlayMethod { get; set; } = string.Empty; // DirectPlay, DirectStream, Transcode
        
        public bool IsDirectPlay { get; set; }
        public bool IsDirectStream { get; set; }
        public bool IsTranscode { get; set; }
        
        public string? TranscodeReason { get; set; }
        public string? TranscodeVideoCodec { get; set; }
        public string? TranscodeAudioCodec { get; set; }
        public string? TranscodeContainer { get; set; }
        
        public string? HardwareAccelerationType { get; set; } // None, NVENC, QSV, VAAPI, etc.
        public string? TranscodeHardwareDecoder { get; set; }
        public string? TranscodeHardwareEncoder { get; set; }
        
        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;
        
        // Link to local video analysis if matched
        public int? VideoAnalysisId { get; set; }
        public virtual VideoAnalysis? VideoAnalysis { get; set; }
        
        // Link to library path if matched
        public int? LibraryPathId { get; set; }
        public virtual LibraryPath? LibraryPath { get; set; }
    }
}

