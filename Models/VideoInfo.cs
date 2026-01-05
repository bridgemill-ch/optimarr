using System.Collections.Generic;

namespace Optimarr.Models
{
    public class VideoInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public string VideoCodec { get; set; } = string.Empty;
        public string VideoCodecTag { get; set; } = string.Empty; // CodecID tag (e.g., HVC1, AVC1, HEVC)
        public bool IsCodecTagCorrect { get; set; } = true; // Whether the codec tag is correct for the codec
        public int BitDepth { get; set; } = 8;
        public string VideoProfile { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public bool IsHDR { get; set; }
        public string HDRType { get; set; } = string.Empty; // HDR10, Dolby Vision, HLG
        public List<AudioTrack> AudioTracks { get; set; } = new();
        public List<SubtitleTrack> SubtitleTracks { get; set; } = new();
        public long FileSize { get; set; }
        public double Duration { get; set; }
        public bool IsFastStart { get; set; } // MP4 fast start optimization (moov atom at beginning)
    }

    public class AudioTrack
    {
        public string Codec { get; set; } = string.Empty;
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public string Language { get; set; } = string.Empty;
        public int Bitrate { get; set; }
    }

    public class SubtitleTrack
    {
        public string Format { get; set; } = string.Empty; // SRT, VTT, ASS, etc.
        public string Language { get; set; } = string.Empty;
        public bool IsEmbedded { get; set; }
        public string FilePath { get; set; } = string.Empty; // For external subtitles
    }
}

