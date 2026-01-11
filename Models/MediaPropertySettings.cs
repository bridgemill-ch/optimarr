using System.Collections.Generic;

namespace Optimarr.Models
{
    /// <summary>
    /// Settings for media property compatibility
    /// </summary>
    public class MediaPropertySettings
    {
        /// <summary>
        /// Video codecs and their support status
        /// Key: Codec name (e.g., "H.264", "H.265", "AV1")
        /// Value: true if supported, false if unsupported
        /// </summary>
        public Dictionary<string, bool> VideoCodecs { get; set; } = new();

        /// <summary>
        /// Audio codecs and their support status
        /// Key: Codec name (e.g., "AAC", "AC3", "EAC3")
        /// Value: true if supported, false if unsupported
        /// </summary>
        public Dictionary<string, bool> AudioCodecs { get; set; } = new();

        /// <summary>
        /// Containers and their support status
        /// Key: Container name (e.g., "MP4", "MKV", "WebM")
        /// Value: true if supported, false if unsupported
        /// </summary>
        public Dictionary<string, bool> Containers { get; set; } = new();

        /// <summary>
        /// Subtitle formats and their support status
        /// Key: Format name (e.g., "SRT", "VTT", "ASS")
        /// Value: true if supported, false if unsupported
        /// </summary>
        public Dictionary<string, bool> SubtitleFormats { get; set; } = new();

        /// <summary>
        /// Bit depths and their support status
        /// Key: Bit depth as string (e.g., "8", "10", "12")
        /// Value: true if supported, false if unsupported
        /// </summary>
        public Dictionary<string, bool> BitDepths { get; set; } = new();
    }

    /// <summary>
    /// Rating impact weights for additional factors
    /// </summary>
    public class RatingWeights
    {
        /// <summary>
        /// Weight for stereo sound (channels <= 2)
        /// Default: 3 (reduces rating by 3 points if present)
        /// Stereo sound may have compatibility limitations compared to surround sound
        /// </summary>
        public int SurroundSound { get; set; } = 3;

        /// <summary>
        /// Weight for SDR content (no HDR)
        /// Default: 8 (reduces rating by 8 points if present)
        /// SDR content may have reduced visual quality compared to HDR
        /// </summary>
        public int HDR { get; set; } = 8;

        /// <summary>
        /// Weight for high bitrate (above threshold)
        /// Default: 5 (reduces rating by 5 points if bitrate is too high)
        /// High bitrate can cause buffering issues on slower connections
        /// </summary>
        public int HighBitrate { get; set; } = 5;

        /// <summary>
        /// Weight for incorrect codec tag
        /// Default: 12 (reduces rating by 12 points if codec tag is incorrect)
        /// Incorrect codec tags can cause playback issues and force transcoding
        /// </summary>
        public int IncorrectCodecTag { get; set; } = 12;

        /// <summary>
        /// Weight for unsupported video codec
        /// Default: 35 (reduces rating by 35 points if video codec is unsupported)
        /// Video codec is the most critical factor - unsupported codecs require transcoding
        /// </summary>
        public int UnsupportedVideoCodec { get; set; } = 35;

        /// <summary>
        /// Weight for unsupported audio codec
        /// Default: 25 (reduces rating by 25 points if audio codec is unsupported)
        /// Audio codec is important - unsupported codecs require transcoding
        /// </summary>
        public int UnsupportedAudioCodec { get; set; } = 25;

        /// <summary>
        /// Weight for unsupported container
        /// Default: 30 (reduces rating by 30 points if container is unsupported)
        /// Container format is critical - unsupported containers may not play at all
        /// </summary>
        public int UnsupportedContainer { get; set; } = 30;

        /// <summary>
        /// Weight for unsupported subtitle format
        /// Default: 8 (reduces rating by 8 points if subtitle format is unsupported)
        /// Subtitle format is less critical - unsupported formats simply won't display
        /// </summary>
        public int UnsupportedSubtitleFormat { get; set; } = 8;

        /// <summary>
        /// Weight for unsupported bit depth
        /// Default: 18 (reduces rating by 18 points if bit depth is unsupported)
        /// Bit depth affects compatibility - 10-bit can cause issues on some devices
        /// </summary>
        public int UnsupportedBitDepth { get; set; } = 18;

        /// <summary>
        /// Weight for missing fast start optimization (MP4 only)
        /// Default: 5 (reduces rating by 5 points if MP4 file lacks fast start)
        /// Fast start (moov atom at beginning) enables better streaming performance
        /// </summary>
        public int FastStart { get; set; } = 5;

        /// <summary>
        /// High bitrate threshold in Mbps
        /// Default: 40 Mbps
        /// Videos above this bitrate may cause buffering on slower connections
        /// </summary>
        public double HighBitrateThresholdMbps { get; set; } = 40.0;
    }

    /// <summary>
    /// Rating thresholds for Optimal/Good/Poor classification
    /// </summary>
    public class RatingThresholds
    {
        /// <summary>
        /// Minimum rating for "Optimal" classification
        /// Default: 80
        /// </summary>
        public int Optimal { get; set; } = 80;

        /// <summary>
        /// Minimum rating for "Good" classification
        /// Default: 60
        /// </summary>
        public int Good { get; set; } = 60;

        /// <summary>
        /// Ratings below this are "Poor"
        /// </summary>
    }
}
