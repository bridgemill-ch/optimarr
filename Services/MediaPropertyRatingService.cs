using System;
using System.Collections.Generic;
using System.Linq;
using Optimarr.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Optimarr.Services
{
    /// <summary>
    /// Service for calculating compatibility ratings based on media properties
    /// </summary>
    public class MediaPropertyRatingService
    {
        private readonly IConfiguration? _configuration;
        private readonly ILogger? _logger;

        public MediaPropertyRatingService(IConfiguration? configuration = null, ILogger? logger = null)
        {
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Calculate compatibility rating for a video based on its properties
        /// </summary>
        public CompatibilityResult CalculateRating(VideoInfo videoInfo)
        {
            var result = new CompatibilityResult();
            var issues = new List<string>();
            var recommendations = new List<string>();

            // Load settings
            var propertySettings = LoadMediaPropertySettings();
            var weights = LoadRatingWeights();
            var thresholds = LoadRatingThresholds();

            // Start with a perfect score
            int rating = 100;

            // Check video codec
            var videoCodecSupported = IsVideoCodecSupported(videoInfo.VideoCodec, videoInfo.BitDepth, propertySettings);
            if (!videoCodecSupported)
            {
                rating -= weights.UnsupportedVideoCodec;
                issues.Add($"{videoInfo.VideoCodec} {videoInfo.BitDepth}-bit video codec is not supported");
                recommendations.Add($"Re-encode to a supported video codec (e.g., H.264 8-bit)");
            }

            // Check container
            var containerSupported = IsContainerSupported(videoInfo.Container, propertySettings);
            if (!containerSupported)
            {
                rating -= weights.UnsupportedContainer;
                issues.Add($"{videoInfo.Container} container is not supported");
                recommendations.Add($"Use a supported container (e.g., MP4)");
            }

            // Check audio codecs
            var unsupportedAudioCodecs = videoInfo.AudioTracks
                .Where(track => !IsAudioCodecSupported(track.Codec, propertySettings))
                .Select(track => track.Codec)
                .Distinct()
                .ToList();

            if (unsupportedAudioCodecs.Any())
            {
                rating -= weights.UnsupportedAudioCodec;
                var codecList = string.Join(", ", unsupportedAudioCodecs);
                issues.Add($"{codecList} audio codec(s) are not supported");
                recommendations.Add($"Use supported audio codecs (e.g., AAC)");
            }

            // Check subtitle formats
            var unsupportedSubtitleFormats = videoInfo.SubtitleTracks
                .Where(track => !IsSubtitleFormatSupported(track.Format, propertySettings))
                .Select(track => track.Format)
                .Distinct()
                .ToList();

            if (unsupportedSubtitleFormats.Any())
            {
                rating -= weights.UnsupportedSubtitleFormat;
                var formatList = string.Join(", ", unsupportedSubtitleFormats);
                issues.Add($"{formatList} subtitle format(s) are not supported");
                recommendations.Add($"Use supported subtitle formats (e.g., SRT, VTT)");
            }

            // Check bit depth
            var bitDepthSupported = IsBitDepthSupported(videoInfo.BitDepth, propertySettings);
            if (!bitDepthSupported)
            {
                rating -= weights.UnsupportedBitDepth;
                issues.Add($"{videoInfo.BitDepth}-bit depth is not supported");
                recommendations.Add($"Use 8-bit depth for maximum compatibility");
            }

            // Check additional factors
            // Stereo sound (2 channels or less) - penalize only if ALL audio tracks are stereo
            // If any track has more than 2 channels (surround), don't penalize
            if (videoInfo.AudioTracks.Count > 0)
            {
                var hasSurroundTrack = videoInfo.AudioTracks.Any(track => track.Channels > 2);
                var allTracksAreStereo = videoInfo.AudioTracks.All(track => track.Channels <= 2);
                
                // Only penalize if all tracks are stereo (no surround tracks available)
                if (!hasSurroundTrack && allTracksAreStereo)
                {
                    rating -= weights.SurroundSound;
                    var maxChannels = videoInfo.AudioTracks.Max(track => track.Channels);
                    issues.Add($"All audio tracks are stereo ({maxChannels} channels) - may have compatibility limitations");
                }
            }

            // SDR content (no HDR) - penalize when video is SDR
            if (!videoInfo.IsHDR)
            {
                rating -= weights.HDR;
                issues.Add("SDR content may have reduced visual quality compared to HDR");
                recommendations.Add("Consider HDR version for better visual quality");
            }

            // High bitrate
            if (videoInfo.FileSize > 0 && videoInfo.Duration > 0)
            {
                // Duration should be in seconds, but check if it might be in wrong unit
                // Apply same correction logic as frontend to handle duration unit issues
                var durationSeconds = videoInfo.Duration;
                
                // Calculate initial bitrate to check if duration unit is correct
                var initialBps = (videoInfo.FileSize * 8.0) / durationSeconds;
                var initialMbps = initialBps / 1000000.0;
                
                // If bitrate is unreasonably high (> 1000 Mbps), duration is likely in wrong unit
                if (initialMbps > 1000.0)
                {
                    // Try different conversions based on duration value
                    if (durationSeconds < 0.01)
                    {
                        // Very small - might be in microseconds, multiply by 1000
                        durationSeconds = videoInfo.Duration * 1000.0;
                    }
                    else if (durationSeconds < 1.0)
                    {
                        // Less than 1 second - might be in milliseconds, divide by 1000
                        durationSeconds = videoInfo.Duration / 1000.0;
                    }
                    else if (durationSeconds < 100.0)
                    {
                        // Small duration (1-100 seconds) but huge bitrate - maybe duration was incorrectly converted
                        // Try multiplying by 1000 (reverse conversion if backend incorrectly converted milliseconds to seconds)
                        var alternativeDuration = videoInfo.Duration * 1000.0;
                        var alternativeBps = (videoInfo.FileSize * 8.0) / alternativeDuration;
                        var alternativeMbps = alternativeBps / 1000000.0;
                        
                        if (alternativeMbps < 1000.0 && alternativeMbps > 0)
                        {
                            durationSeconds = alternativeDuration;
                        }
                    }
                }
                
                // Recalculate bitrate with corrected duration
                var bitrateMbps = (videoInfo.FileSize * 8.0) / (durationSeconds * 1000000.0);
                
                // Only flag as high bitrate if it's reasonable (not corrupted data)
                if (bitrateMbps > weights.HighBitrateThresholdMbps && bitrateMbps < 1000.0)
                {
                    rating -= weights.HighBitrate;
                    issues.Add($"High bitrate ({bitrateMbps:F2} Mbps) may cause buffering on slower connections");
                    recommendations.Add("Consider reducing bitrate for better streaming performance");
                }
            }

            // Incorrect codec tag
            if (!videoInfo.IsCodecTagCorrect && !string.IsNullOrEmpty(videoInfo.VideoCodecTag))
            {
                rating -= weights.IncorrectCodecTag;
                issues.Add($"Incorrect codec tag ({videoInfo.VideoCodecTag}) - should be correct for {videoInfo.VideoCodec}");
                recommendations.Add("Fix codec tag to ensure proper playback");
            }

            // Fast start optimization (MP4 only)
            var isMp4Container = string.Equals(videoInfo.Container, "MP4", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(videoInfo.Container, "M4V", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(videoInfo.Container, "MOV", StringComparison.OrdinalIgnoreCase);
            if (isMp4Container && !videoInfo.IsFastStart)
            {
                rating -= weights.FastStart;
                issues.Add("MP4 file lacks fast start optimization (moov atom not at beginning)");
                recommendations.Add("Re-encode with fast start flag (-movflags +faststart) for better streaming performance");
            }

            // Ensure rating doesn't go below 0
            rating = Math.Max(0, rating);

            // Set compatibility rating (0-100 scale)
            result.CompatibilityRating = rating;

            // Determine overall score
            if (rating >= thresholds.Optimal)
                result.OverallScore = "Optimal";
            else if (rating >= thresholds.Good)
                result.OverallScore = "Good";
            else
                result.OverallScore = "Poor";

            result.Issues = issues;
            result.Recommendations = recommendations;

            return result;
        }

        private bool IsVideoCodecSupported(string codec, int bitDepth, MediaPropertySettings settings)
        {
            if (string.IsNullOrEmpty(codec)) return false;

            // Check with bit depth first (e.g., "H.265 10-bit")
            var codecWithBitDepth = $"{codec} {bitDepth}-bit";
            if (settings.VideoCodecs.ContainsKey(codecWithBitDepth))
            {
                return settings.VideoCodecs[codecWithBitDepth];
            }

            // Check without bit depth
            if (settings.VideoCodecs.ContainsKey(codec))
            {
                return settings.VideoCodecs[codec];
            }

            // Default to unsupported if not configured
            return false;
        }

        private bool IsAudioCodecSupported(string codec, MediaPropertySettings settings)
        {
            if (string.IsNullOrEmpty(codec)) return false;

            if (settings.AudioCodecs.ContainsKey(codec))
            {
                return settings.AudioCodecs[codec];
            }

            // Default to unsupported if not configured
            return false;
        }

        private bool IsContainerSupported(string container, MediaPropertySettings settings)
        {
            if (string.IsNullOrEmpty(container)) return false;

            if (settings.Containers.ContainsKey(container))
            {
                return settings.Containers[container];
            }

            // Default to unsupported if not configured
            return false;
        }

        private bool IsSubtitleFormatSupported(string format, MediaPropertySettings settings)
        {
            if (string.IsNullOrEmpty(format)) return true; // No subtitles is fine

            if (settings.SubtitleFormats.ContainsKey(format))
            {
                return settings.SubtitleFormats[format];
            }

            // Default to unsupported if not configured
            return false;
        }

        private bool IsBitDepthSupported(int bitDepth, MediaPropertySettings settings)
        {
            var bitDepthKey = bitDepth.ToString();
            if (settings.BitDepths.ContainsKey(bitDepthKey))
            {
                return settings.BitDepths[bitDepthKey];
            }

            // Default to unsupported if not configured
            return false;
        }

        public MediaPropertySettings LoadMediaPropertySettings()
        {
            var settings = new MediaPropertySettings();

            if (_configuration == null) return GetDefaultPropertySettings();

            // Load from configuration
            var videoCodecsSection = _configuration.GetSection("MediaPropertySettings:VideoCodecs");
            var audioCodecsSection = _configuration.GetSection("MediaPropertySettings:AudioCodecs");
            var containersSection = _configuration.GetSection("MediaPropertySettings:Containers");
            var subtitleFormatsSection = _configuration.GetSection("MediaPropertySettings:SubtitleFormats");
            var bitDepthsSection = _configuration.GetSection("MediaPropertySettings:BitDepths");

            foreach (var item in videoCodecsSection.GetChildren())
            {
                if (bool.TryParse(item.Value, out var boolValue))
                {
                    settings.VideoCodecs[item.Key] = boolValue;
                }
            }

            foreach (var item in audioCodecsSection.GetChildren())
            {
                if (bool.TryParse(item.Value, out var boolValue))
                {
                    settings.AudioCodecs[item.Key] = boolValue;
                }
            }

            foreach (var item in containersSection.GetChildren())
            {
                if (bool.TryParse(item.Value, out var boolValue))
                {
                    settings.Containers[item.Key] = boolValue;
                }
            }

            foreach (var item in subtitleFormatsSection.GetChildren())
            {
                if (bool.TryParse(item.Value, out var boolValue))
                {
                    settings.SubtitleFormats[item.Key] = boolValue;
                }
            }

            foreach (var item in bitDepthsSection.GetChildren())
            {
                if (bool.TryParse(item.Value, out var boolValue))
                {
                    settings.BitDepths[item.Key] = boolValue;
                }
            }

            // If no settings found, use defaults
            if (settings.VideoCodecs.Count == 0 && 
                settings.AudioCodecs.Count == 0 && 
                settings.Containers.Count == 0 && 
                settings.SubtitleFormats.Count == 0 && 
                settings.BitDepths.Count == 0)
            {
                return GetDefaultPropertySettings();
            }

            return settings;
        }

        public RatingWeights LoadRatingWeights()
        {
            // Start with default values
            var weights = new RatingWeights();

            if (_configuration == null)
            {
                _logger?.LogWarning("Configuration is null, returning default rating weights");
                return weights;
            }

            var weightsSection = _configuration.GetSection("MediaPropertySettings:RatingWeights");

            _logger?.LogInformation("Checking RatingWeights section - Exists: {Exists}, Path: {Path}", 
                weightsSection.Exists(), weightsSection.Path);
            
            // Also check if we can get any children to verify the section structure
            if (weightsSection.Exists())
            {
                var children = weightsSection.GetChildren().ToList();
                _logger?.LogInformation("RatingWeights section has {Count} children: {Keys}", 
                    children.Count, string.Join(", ", children.Select(c => c.Key)));
            }

            if (weightsSection.Exists())
            {
                // Load values from GetChildren - this ensures we only get values that actually exist in config
                // Don't use GetValue as it returns defaults even when keys don't exist
                var foundKeys = new List<string>();
                foreach (var child in weightsSection.GetChildren())
                {
                    var key = child.Key;
                    var valueStr = child.Value;

                    if (string.IsNullOrEmpty(valueStr)) continue;
                    
                    foundKeys.Add(key);

                    switch (key)
                    {
                        case "SurroundSound":
                            if (int.TryParse(valueStr, out var surroundSound))
                                weights.SurroundSound = surroundSound;
                            break;
                        case "HDR":
                            if (int.TryParse(valueStr, out var hdr))
                                weights.HDR = hdr;
                            break;
                        case "HighBitrate":
                            if (int.TryParse(valueStr, out var highBitrate))
                                weights.HighBitrate = highBitrate;
                            break;
                        case "IncorrectCodecTag":
                            if (int.TryParse(valueStr, out var incorrectCodecTag))
                                weights.IncorrectCodecTag = incorrectCodecTag;
                            break;
                        case "UnsupportedVideoCodec":
                            if (int.TryParse(valueStr, out var unsupportedVideoCodec))
                                weights.UnsupportedVideoCodec = unsupportedVideoCodec;
                            break;
                        case "UnsupportedAudioCodec":
                            if (int.TryParse(valueStr, out var unsupportedAudioCodec))
                                weights.UnsupportedAudioCodec = unsupportedAudioCodec;
                            break;
                        case "UnsupportedContainer":
                            if (int.TryParse(valueStr, out var unsupportedContainer))
                                weights.UnsupportedContainer = unsupportedContainer;
                            break;
                        case "UnsupportedSubtitleFormat":
                            if (int.TryParse(valueStr, out var unsupportedSubtitleFormat))
                                weights.UnsupportedSubtitleFormat = unsupportedSubtitleFormat;
                            break;
                        case "UnsupportedBitDepth":
                            if (int.TryParse(valueStr, out var unsupportedBitDepth))
                                weights.UnsupportedBitDepth = unsupportedBitDepth;
                            break;
                        case "FastStart":
                            if (int.TryParse(valueStr, out var fastStart))
                                weights.FastStart = fastStart;
                            break;
                        case "HighBitrateThresholdMbps":
                            if (double.TryParse(valueStr, out var highBitrateThreshold))
                                weights.HighBitrateThresholdMbps = highBitrateThreshold;
                            break;
                    }
                }
                
                // Log which keys were actually found in the config
                if (foundKeys.Count > 0)
                {
                    _logger?.LogDebug("Found RatingWeights keys in config: {Keys}", string.Join(", ", foundKeys));
                }
                else
                {
                    _logger?.LogWarning("RatingWeights section exists but contains no keys - using defaults");
                }

                _logger?.LogInformation("Loaded rating weights: SurroundSound={SurroundSound}, HDR={HDR}, HighBitrate={HighBitrate}, UnsupportedVideoCodec={UnsupportedVideoCodec}, UnsupportedAudioCodec={UnsupportedAudioCodec}, UnsupportedContainer={UnsupportedContainer}, HighBitrateThresholdMbps={HighBitrateThresholdMbps}",
                    weights.SurroundSound, weights.HDR, weights.HighBitrate, weights.UnsupportedVideoCodec, weights.UnsupportedAudioCodec, weights.UnsupportedContainer, weights.HighBitrateThresholdMbps);
            }
            else
            {
                _logger?.LogWarning("RatingWeights section not found in configuration, using defaults");
            }

            return weights;
        }

        public RatingThresholds LoadRatingThresholds()
        {
            if (_configuration == null) return new RatingThresholds();

            var thresholds = new RatingThresholds();
            var thresholdsSection = _configuration.GetSection("MediaPropertySettings:RatingThresholds");

            if (thresholdsSection.Exists())
            {
                thresholds.Optimal = thresholdsSection.GetValue<int>("Optimal", 80);
                thresholds.Good = thresholdsSection.GetValue<int>("Good", 60);
            }

            return thresholds;
        }

        private MediaPropertySettings GetDefaultPropertySettings()
        {
            var settings = new MediaPropertySettings();

            // Default supported video codecs
            settings.VideoCodecs["H.264"] = true;
            settings.VideoCodecs["H.264 8-bit"] = true;
            settings.VideoCodecs["H.265"] = true;
            settings.VideoCodecs["H.265 8-bit"] = true;
            settings.VideoCodecs["H.265 10-bit"] = false; // Less compatible
            settings.VideoCodecs["VP9"] = true;
            settings.VideoCodecs["AV1"] = false; // Less compatible

            // Default supported audio codecs
            settings.AudioCodecs["AAC"] = true;
            settings.AudioCodecs["MP3"] = true;
            settings.AudioCodecs["AC3"] = false; // Less compatible
            settings.AudioCodecs["EAC3"] = false; // Less compatible
            settings.AudioCodecs["DTS"] = false; // Less compatible
            settings.AudioCodecs["FLAC"] = true;
            settings.AudioCodecs["Opus"] = true;

            // Default supported containers
            settings.Containers["MP4"] = true;
            settings.Containers["M4V"] = true;
            settings.Containers["MOV"] = true;
            settings.Containers["MKV"] = false; // Less compatible
            settings.Containers["WebM"] = true;
            settings.Containers["TS"] = false; // Less compatible

            // Default supported subtitle formats
            settings.SubtitleFormats["SRT"] = true;
            settings.SubtitleFormats["VTT"] = true;
            settings.SubtitleFormats["ASS"] = false; // Less compatible
            settings.SubtitleFormats["SSA"] = false; // Less compatible

            // Default supported bit depths
            settings.BitDepths["8"] = true;
            settings.BitDepths["10"] = false; // Less compatible
            settings.BitDepths["12"] = false; // Less compatible

            return settings;
        }
    }
}
