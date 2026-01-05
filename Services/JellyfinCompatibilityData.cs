using System.Collections.Generic;

namespace Optimarr.Services
{
    /// <summary>
    /// Hardcoded Jellyfin compatibility data as of December 2025
    /// Based on latest Jellyfin documentation and client support
    /// 
    /// Client Version Requirements:
    /// - Chrome: Version 107+ for HEVC on Windows 8+, macOS Big Sur+, ChromeOS, Android 6+
    /// - Edge: Based on Chromium, similar to Chrome
    /// - Firefox: Version 134+ for HEVC on Windows
    /// - Safari: Version 12+ on macOS High Sierra (10.13)+, iOS 11+ for HEVC
    /// - iOS: iPhone 7+ for HEVC, iPhone X+ for HDR (requires MP4/M4V/MOV container, hvc1 codec tag)
    /// - Android: Android 5.0+ for HEVC (hardware dependent), Android 7.0+ for HDR
    /// - AndroidTV: Similar to Android but optimized for TV hardware
    /// - SwiftFin: iOS native client with similar requirements to iOS
    /// - Roku: Varies by model, generally supports HEVC on newer devices
    /// - Kodi: Full codec support via addons and hardware acceleration
    /// - Desktop: Jellyfin Media Player with full codec support
    /// </summary>
    public static class JellyfinCompatibilityData
    {
        public enum SupportLevel
        {
            Supported,
            Partial,
            Unsupported
        }

        public static readonly Dictionary<string, Dictionary<string, SupportLevel>> VideoCodecSupport = new()
        {
            // MPEG-4 Part 2/SP
            ["MPEG-4 SP"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Unsupported,
                ["Edge"] = SupportLevel.Unsupported,
                ["Firefox"] = SupportLevel.Unsupported,
                ["Safari"] = SupportLevel.Unsupported,
                ["Android"] = SupportLevel.Unsupported,
                ["AndroidTV"] = SupportLevel.Unsupported,
                ["iOS"] = SupportLevel.Unsupported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            // MPEG-4 Part 2/ASP
            ["MPEG-4 ASP"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Unsupported,
                ["Edge"] = SupportLevel.Unsupported,
                ["Firefox"] = SupportLevel.Unsupported,
                ["Safari"] = SupportLevel.Unsupported,
                ["Android"] = SupportLevel.Unsupported,
                ["AndroidTV"] = SupportLevel.Unsupported,
                ["iOS"] = SupportLevel.Unsupported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Unsupported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            // H.264 8-bit
            ["H.264 8-bit"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported,
                ["Edge"] = SupportLevel.Supported,
                ["Firefox"] = SupportLevel.Supported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Supported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            // H.264 10-bit
            ["H.264 10-bit"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome 107+ on Windows 8+, macOS Big Sur+, ChromeOS, Android 6+
                ["Edge"] = SupportLevel.Supported, // Edge 107+ (Chromium-based)
                ["Firefox"] = SupportLevel.Unsupported, // Firefox does not support 10-bit H.264
                ["Safari"] = SupportLevel.Partial, // Safari 12+ on macOS High Sierra+, limited support
                ["Android"] = SupportLevel.Supported, // Android 5.0+ (hardware dependent)
                ["AndroidTV"] = SupportLevel.Supported, // Android 5.0+ (hardware dependent)
                ["iOS"] = SupportLevel.Unsupported, // iOS does not support 10-bit H.264
                ["SwiftFin"] = SupportLevel.Supported, // Native iOS client with hardware acceleration
                ["Roku"] = SupportLevel.Unsupported, // Roku does not support 10-bit H.264
                ["Kodi"] = SupportLevel.Supported, // Full support via hardware acceleration
                ["Desktop"] = SupportLevel.Supported // Jellyfin Media Player with full codec support
            },
            // H.265 8-bit
            ["H.265 8-bit"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Partial, // Chrome 107+ on Windows 8+, macOS Big Sur+, ChromeOS, Android 6+ (requires hardware support)
                ["Edge"] = SupportLevel.Supported, // Edge 107+ (Chromium-based, hardware acceleration)
                ["Firefox"] = SupportLevel.Supported, // Firefox 134+ on Windows (hardware acceleration required)
                ["Safari"] = SupportLevel.Partial, // Safari 12+ on macOS High Sierra (10.13)+, iOS 11+ (hardware dependent)
                ["Android"] = SupportLevel.Partial, // Android 5.0+ (hardware dependent, can be buggy on some devices)
                ["AndroidTV"] = SupportLevel.Supported, // Android 5.0+ (TV hardware typically has better support)
                ["iOS"] = SupportLevel.Supported, // iOS 11+ / iPhone 7+ (requires MP4/M4V/MOV container, hvc1 codec tag preferred)
                ["SwiftFin"] = SupportLevel.Unsupported, // SwiftFin does not support HEVC currently
                ["Roku"] = SupportLevel.Partial, // Roku Ultra and newer models (varies by device)
                ["Kodi"] = SupportLevel.Supported, // Full support via hardware acceleration
                ["Desktop"] = SupportLevel.Supported // Jellyfin Media Player with full codec support
            },
            // H.265 10-bit
            ["H.265 10-bit"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Partial, // Chrome 107+ on Windows 8+, macOS Big Sur+, ChromeOS, Android 6+ (hardware dependent)
                ["Edge"] = SupportLevel.Supported, // Edge 107+ (Chromium-based, hardware acceleration)
                ["Firefox"] = SupportLevel.Supported, // Firefox 134+ on Windows (hardware acceleration required)
                ["Safari"] = SupportLevel.Partial, // Safari 12+ on macOS High Sierra (10.13)+, iOS 11+ (hardware dependent)
                ["Android"] = SupportLevel.Partial, // Android 7.0+ for HDR (hardware dependent, varies by device)
                ["AndroidTV"] = SupportLevel.Partial, // Android 7.0+ for HDR (TV hardware typically better)
                ["iOS"] = SupportLevel.Supported, // iOS 11+ / iPhone 7+ for HEVC, iPhone X+ for HDR (requires MP4/M4V/MOV, hvc1 tag)
                ["SwiftFin"] = SupportLevel.Partial, // Limited support, may require transcoding
                ["Roku"] = SupportLevel.Partial, // Roku Ultra and newer (varies by device, HDR support limited)
                ["Kodi"] = SupportLevel.Supported, // Full support via hardware acceleration
                ["Desktop"] = SupportLevel.Supported // Jellyfin Media Player with full codec support
            },
            // VP9
            ["VP9"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome 29+ (full support)
                ["Edge"] = SupportLevel.Supported, // Edge 79+ (Chromium-based)
                ["Firefox"] = SupportLevel.Supported, // Firefox 28+ (full support)
                ["Safari"] = SupportLevel.Supported, // Safari 14+ on macOS Big Sur (11.0)+, iOS 14+
                ["Android"] = SupportLevel.Partial, // Android 5.0+ (hardware dependent, can be buggy)
                ["AndroidTV"] = SupportLevel.Partial, // Android 5.0+ (TV hardware typically better)
                ["iOS"] = SupportLevel.Unsupported, // iOS does not support VP9
                ["SwiftFin"] = SupportLevel.Supported, // Native iOS client with VP9 support
                ["Roku"] = SupportLevel.Supported, // Roku Ultra and newer models
                ["Kodi"] = SupportLevel.Supported, // Full support via hardware acceleration
                ["Desktop"] = SupportLevel.Supported // Jellyfin Media Player with full codec support
            },
            // AV1
            ["AV1"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome 120+ (hardware acceleration on supported devices)
                ["Edge"] = SupportLevel.Supported, // Edge 120+ (Chromium-based, hardware acceleration)
                ["Firefox"] = SupportLevel.Supported, // Firefox 117+ (software decode), hardware decode varies
                ["Safari"] = SupportLevel.Partial, // Safari 17+ on macOS Sonoma (14.0)+, iOS 17+ (requires A17 Pro/M3+ chips for hardware decode)
                ["Android"] = SupportLevel.Partial, // Android 12+ (hardware dependent, varies by device)
                ["AndroidTV"] = SupportLevel.Partial, // Android 12+ (TV hardware typically better, varies by device)
                ["iOS"] = SupportLevel.Unsupported, // iOS does not support AV1 (except via Safari 17+ on A17 Pro/M3+)
                ["SwiftFin"] = SupportLevel.Unsupported, // Disabled by default, no AV1 support
                ["Roku"] = SupportLevel.Unsupported, // Roku does not support AV1 currently
                ["Kodi"] = SupportLevel.Supported, // Full support via hardware acceleration (varies by platform)
                ["Desktop"] = SupportLevel.Supported // Jellyfin Media Player with full codec support
            }
        };

        public static readonly Dictionary<string, Dictionary<string, SupportLevel>> AudioCodecSupport = new()
        {
            ["FLAC"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported,
                ["Edge"] = SupportLevel.Supported,
                ["Firefox"] = SupportLevel.Supported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Supported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            ["MP3"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Partial, // Chrome (mono may transcode, stereo fully supported)
                ["Edge"] = SupportLevel.Supported, // Edge (full support)
                ["Firefox"] = SupportLevel.Partial, // Firefox (mono may transcode, stereo fully supported)
                ["Safari"] = SupportLevel.Supported, // Safari (full support)
                ["Android"] = SupportLevel.Supported, // Android (full support)
                ["AndroidTV"] = SupportLevel.Supported, // AndroidTV (full support)
                ["iOS"] = SupportLevel.Supported, // iOS (full support)
                ["SwiftFin"] = SupportLevel.Supported, // SwiftFin (full support)
                ["Roku"] = SupportLevel.Supported, // Roku (full support)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["AAC"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported,
                ["Edge"] = SupportLevel.Supported,
                ["Firefox"] = SupportLevel.Supported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Supported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            ["AC3"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome (full support)
                ["Edge"] = SupportLevel.Supported, // Edge (full support)
                ["Firefox"] = SupportLevel.Unsupported, // Firefox does not support AC3
                ["Safari"] = SupportLevel.Supported, // Safari (full support)
                ["Android"] = SupportLevel.Supported, // Android (full support)
                ["AndroidTV"] = SupportLevel.Supported, // AndroidTV (full support)
                ["iOS"] = SupportLevel.Supported, // iOS (full support)
                ["SwiftFin"] = SupportLevel.Supported, // SwiftFin (full support)
                ["Roku"] = SupportLevel.Unsupported, // Roku does not support AC3 (EAC3 supported)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["EAC3"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported,
                ["Edge"] = SupportLevel.Supported,
                ["Firefox"] = SupportLevel.Supported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Supported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Unsupported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            ["Vorbis"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome (full support in WebM/OGG)
                ["Edge"] = SupportLevel.Supported, // Edge (full support in WebM/OGG)
                ["Firefox"] = SupportLevel.Supported, // Firefox (full support in WebM/OGG)
                ["Safari"] = SupportLevel.Partial, // Safari 17.4+ on macOS Sequoia (15.4)+, iOS 18.4+ (OGG container only)
                ["Android"] = SupportLevel.Supported, // Android (full support in WebM/OGG)
                ["AndroidTV"] = SupportLevel.Unsupported, // AndroidTV does not support Vorbis
                ["iOS"] = SupportLevel.Partial, // iOS 18.4+ (OGG container only, WebM not supported)
                ["SwiftFin"] = SupportLevel.Supported, // SwiftFin (full support)
                ["Roku"] = SupportLevel.Supported, // Roku (full support)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["DTS"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Unsupported,
                ["Edge"] = SupportLevel.Unsupported,
                ["Firefox"] = SupportLevel.Unsupported,
                ["Safari"] = SupportLevel.Unsupported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Unsupported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported, // Passthrough
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            ["Opus"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome (full support in WebM/OGG)
                ["Edge"] = SupportLevel.Supported, // Edge (full support in WebM/OGG)
                ["Firefox"] = SupportLevel.Supported, // Firefox (full support in WebM/OGG)
                ["Safari"] = SupportLevel.Supported, // Safari (in .caf container, iOS 17+ stereo in MP4)
                ["Android"] = SupportLevel.Supported, // Android (full support in WebM/OGG)
                ["AndroidTV"] = SupportLevel.Supported, // AndroidTV (full support)
                ["iOS"] = SupportLevel.Partial, // iOS 17+ (stereo in MP4 only, WebM/OGG not supported)
                ["SwiftFin"] = SupportLevel.Supported, // SwiftFin (full support)
                ["Roku"] = SupportLevel.Supported, // Roku (full support)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["ALAC"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Unsupported,
                ["Edge"] = SupportLevel.Unsupported,
                ["Firefox"] = SupportLevel.Unsupported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Unsupported,
                ["AndroidTV"] = SupportLevel.Unsupported,
                ["iOS"] = SupportLevel.Unsupported,
                ["SwiftFin"] = SupportLevel.Unsupported,
                ["Roku"] = SupportLevel.Unsupported,
                ["Kodi"] = SupportLevel.Unsupported,
                ["Desktop"] = SupportLevel.Supported
            }
        };

        public static readonly Dictionary<string, Dictionary<string, SupportLevel>> ContainerSupport = new()
        {
            ["MP4"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported,
                ["Edge"] = SupportLevel.Supported,
                ["Firefox"] = SupportLevel.Supported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Supported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            ["MKV"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Unsupported, // Chrome does not support MKV natively
                ["Edge"] = SupportLevel.Supported, // Edge 79+ (Chromium-based, via MSE)
                ["Firefox"] = SupportLevel.Unsupported, // Firefox does not support MKV natively
                ["Safari"] = SupportLevel.Unsupported, // Safari does not support MKV
                ["Android"] = SupportLevel.Supported, // Android (full support via native players)
                ["AndroidTV"] = SupportLevel.Supported, // AndroidTV (full support)
                ["iOS"] = SupportLevel.Unsupported, // iOS does not support MKV (requires MP4/M4V/MOV)
                ["SwiftFin"] = SupportLevel.Unsupported, // SwiftFin does not support MKV
                ["Roku"] = SupportLevel.Supported, // Roku (full support)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["WebM"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome 6+ (full support)
                ["Edge"] = SupportLevel.Supported, // Edge 79+ (Chromium-based)
                ["Firefox"] = SupportLevel.Supported, // Firefox 4+ (full support)
                ["Safari"] = SupportLevel.Supported, // Safari 14+ on macOS Big Sur (11.0)+, iOS 14+
                ["Android"] = SupportLevel.Supported, // Android 2.3+ (full support)
                ["AndroidTV"] = SupportLevel.Supported, // AndroidTV (full support)
                ["iOS"] = SupportLevel.Supported, // iOS 14+ (full support)
                ["SwiftFin"] = SupportLevel.Supported, // SwiftFin (full support)
                ["Roku"] = SupportLevel.Supported, // Roku (full support)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["TS"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported,
                ["Edge"] = SupportLevel.Supported,
                ["Firefox"] = SupportLevel.Supported,
                ["Safari"] = SupportLevel.Supported,
                ["Android"] = SupportLevel.Supported,
                ["AndroidTV"] = SupportLevel.Supported,
                ["iOS"] = SupportLevel.Supported,
                ["SwiftFin"] = SupportLevel.Supported,
                ["Roku"] = SupportLevel.Supported,
                ["Kodi"] = SupportLevel.Supported,
                ["Desktop"] = SupportLevel.Supported
            },
            ["OGG"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Supported, // Chrome (full support)
                ["Edge"] = SupportLevel.Supported, // Edge (full support)
                ["Firefox"] = SupportLevel.Supported, // Firefox (full support)
                ["Safari"] = SupportLevel.Partial, // Safari 17.4+ on macOS Sequoia (15.4)+, iOS 18.4+ (limited support)
                ["Android"] = SupportLevel.Supported, // Android (full support)
                ["AndroidTV"] = SupportLevel.Supported, // AndroidTV (full support)
                ["iOS"] = SupportLevel.Partial, // iOS 18.4+ (limited support, audio only)
                ["SwiftFin"] = SupportLevel.Supported, // SwiftFin (full support)
                ["Roku"] = SupportLevel.Supported, // Roku (full support)
                ["Kodi"] = SupportLevel.Supported, // Kodi (full support)
                ["Desktop"] = SupportLevel.Supported // Desktop (full support)
            },
            ["AVI"] = new Dictionary<string, SupportLevel>
            {
                ["Chrome"] = SupportLevel.Partial,
                ["Edge"] = SupportLevel.Partial,
                ["Firefox"] = SupportLevel.Partial,
                ["Safari"] = SupportLevel.Partial,
                ["Android"] = SupportLevel.Partial,
                ["AndroidTV"] = SupportLevel.Partial,
                ["iOS"] = SupportLevel.Partial,
                ["SwiftFin"] = SupportLevel.Partial,
                ["Roku"] = SupportLevel.Partial,
                ["Kodi"] = SupportLevel.Partial,
                ["Desktop"] = SupportLevel.Partial
            }
        };

        // Subtitle format support by container
        public static readonly Dictionary<string, Dictionary<string, SupportLevel>> SubtitleSupport = new()
        {
            ["SRT"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Unsupported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Partial,
                ["WebM"] = SupportLevel.Supported,
                ["OGG"] = SupportLevel.Supported
            },
            ["VTT"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Unsupported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Supported,
                ["OGG"] = SupportLevel.Supported,
                ["HLS"] = SupportLevel.Supported
            },
            ["ASS"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Unsupported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["SSA"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Unsupported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["VobSub"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Supported,
                ["TS"] = SupportLevel.Supported,
                ["AVI"] = SupportLevel.Partial,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["MP4TT"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Unsupported,
                ["MP4"] = SupportLevel.Supported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["TXTT"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Unsupported,
                ["MP4"] = SupportLevel.Supported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["PGSSUB"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Unsupported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["EIA-608"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Supported,
                ["TS"] = SupportLevel.Supported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["EIA-708"] = new Dictionary<string, SupportLevel>
            {
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Supported,
                ["TS"] = SupportLevel.Supported,
                ["AVI"] = SupportLevel.Unsupported,
                ["WebM"] = SupportLevel.Unsupported,
                ["OGG"] = SupportLevel.Unsupported
            },
            ["UTF-8"] = new Dictionary<string, SupportLevel>
            {
                // UTF-8 is an encoding, not a format, but SRT files with UTF-8 encoding
                // have the same support as SRT format
                ["MKV"] = SupportLevel.Supported,
                ["MP4"] = SupportLevel.Unsupported,
                ["TS"] = SupportLevel.Unsupported,
                ["AVI"] = SupportLevel.Partial,
                ["WebM"] = SupportLevel.Supported,
                ["OGG"] = SupportLevel.Supported,
                ["External"] = SupportLevel.Supported // External UTF-8 encoded subtitles are generally supported
            }
        };

        public static readonly string[] AllClients = { "Chrome", "Edge", "Firefox", "Safari", "Android", "AndroidTV", "iOS", "SwiftFin", "Roku", "Kodi", "Desktop" };
    }
}

