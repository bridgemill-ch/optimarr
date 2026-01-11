using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Xml.Linq;
using Optimarr.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Optimarr.Services
{
    public class VideoAnalyzerService
    {
        private readonly ReportGenerator _reportGenerator;
        private readonly ILogger<VideoAnalyzerService>? _logger;
        private readonly IConfiguration? _configuration;
        private readonly MediaPropertyRatingService? _mediaPropertyRatingService;

        public VideoAnalyzerService(IConfiguration? configuration = null, ILogger<VideoAnalyzerService>? logger = null)
        {
            _reportGenerator = new ReportGenerator();
            _logger = logger;
            _configuration = configuration;
            _mediaPropertyRatingService = new MediaPropertyRatingService(configuration, logger);
        }


        public string AnalyzeVideo(string videoPath, string? subtitlePath = null)
        {
            // Support both single subtitle path (legacy) and list of paths
            var subtitlePaths = subtitlePath != null ? new List<string> { subtitlePath } : new List<string>();
            var (videoInfo, compatibilityResult) = AnalyzeVideoInternal(videoPath, subtitlePaths);
            return _reportGenerator.GenerateReport(videoInfo, compatibilityResult);
        }

        public (VideoInfo VideoInfo, CompatibilityResult CompatibilityResult) AnalyzeVideoStructured(string videoPath, string? subtitlePath = null)
        {
            // Support both single subtitle path (legacy) and list of paths
            var subtitlePaths = subtitlePath != null ? new List<string> { subtitlePath } : new List<string>();
            return AnalyzeVideoStructured(videoPath, subtitlePaths);
        }

        public (VideoInfo VideoInfo, CompatibilityResult CompatibilityResult) AnalyzeVideoStructured(string videoPath, List<string> subtitlePaths)
        {
            return AnalyzeVideoInternal(videoPath, subtitlePaths);
        }

        // Public method to recalculate compatibility from existing VideoInfo (without re-analyzing the file)
        public CompatibilityResult RecalculateCompatibility(VideoInfo videoInfo)
        {
            return AnalyzeCompatibility(videoInfo);
        }

        private (VideoInfo VideoInfo, CompatibilityResult CompatibilityResult) AnalyzeVideoInternal(string videoPath, List<string>? subtitlePaths = null)
        {
            _logger?.LogInformation(">>> AnalyzeVideoInternal START: {VideoPath}", videoPath);
            
            if (!File.Exists(videoPath))
            {
                _logger?.LogError("Video file not found: {VideoPath}", videoPath);
                throw new FileNotFoundException("Video file not found", videoPath);
            }

            var fileInfo = new FileInfo(videoPath);
            _logger?.LogInformation("File info - Size: {Size} bytes ({SizeMB} MB), Extension: {Extension}, Last modified: {Modified}", 
                fileInfo.Length, Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2), fileInfo.Extension, fileInfo.LastWriteTime);

            _logger?.LogInformation(">>> Step 1: Extracting video information");
            var extractStartTime = DateTime.UtcNow;
            var videoInfo = ExtractVideoInfo(videoPath);
            var extractDuration = DateTime.UtcNow - extractStartTime;
            _logger?.LogInformation(">>> Video info extracted in {Duration}ms", extractDuration.TotalMilliseconds);
            _logger?.LogInformation("Extracted - Codec: {Codec}, Container: {Container}, Resolution: {Width}x{Height}, BitDepth: {BitDepth}, HDR: {HDR}",
                videoInfo.VideoCodec, videoInfo.Container, videoInfo.Width, videoInfo.Height, videoInfo.BitDepth, videoInfo.IsHDR);
            
            // Process all external subtitle files
            if (subtitlePaths != null && subtitlePaths.Count > 0)
            {
                _logger?.LogInformation(">>> Processing {Count} external subtitle file(s)", subtitlePaths.Count);
                foreach (var subtitlePath in subtitlePaths)
                {
                    if (!string.IsNullOrEmpty(subtitlePath) && File.Exists(subtitlePath))
                    {
                        _logger?.LogInformation(">>> Processing external subtitle: {SubtitlePath}", subtitlePath);
                        try
                        {
                            var subtitleInfo = ExtractSubtitleInfo(subtitlePath);
                            videoInfo.SubtitleTracks.Add(subtitleInfo);
                            _logger?.LogInformation(">>> Successfully added subtitle track: Format={Format}, Language={Language}, IsEmbedded={IsEmbedded}", 
                                subtitleInfo.Format, subtitleInfo.Language, subtitleInfo.IsEmbedded);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to process external subtitle file: {SubtitlePath}", subtitlePath);
                            // Continue processing other subtitles even if one fails
                        }
                    }
                    else
                    {
                        _logger?.LogWarning("Subtitle file not found or empty: {SubtitlePath}", subtitlePath);
                    }
                }
                _logger?.LogInformation(">>> Total subtitle tracks after processing: {Count}", videoInfo.SubtitleTracks.Count);
            }
            else
            {
                _logger?.LogInformation(">>> No external subtitle paths provided");
            }

            _logger?.LogInformation(">>> Step 2: Analyzing compatibility");
            _logger?.LogInformation("Audio tracks: {AudioTracks}, Subtitle tracks: {SubtitleTracks}",
                videoInfo.AudioTracks.Count, videoInfo.SubtitleTracks.Count);

            var compatStartTime = DateTime.UtcNow;
            var compatibilityResult = AnalyzeCompatibility(videoInfo);
            var compatDuration = DateTime.UtcNow - compatStartTime;
            
            _logger?.LogInformation(">>> Compatibility analyzed in {Duration}ms", compatDuration.TotalMilliseconds);
            _logger?.LogInformation(">>> Video analysis completed. Overall score: {Score}", compatibilityResult.OverallScore);
            _logger?.LogInformation(">>> AnalyzeVideoInternal COMPLETE: {VideoPath}", videoPath);
            
            return (videoInfo, compatibilityResult);
        }

        private VideoInfo ExtractVideoInfo(string filePath)
        {
            _logger?.LogDebug("Extracting video information from: {FilePath}", filePath);
            
            var videoInfo = new VideoInfo
            {
                FilePath = filePath,
                FileSize = new FileInfo(filePath).Length
            };

            // Pre-flight checks
            if (!File.Exists(filePath))
            {
                var error = $"File does not exist: {filePath}";
                _logger?.LogError(">>> {Error}", error);
                throw new FileNotFoundException(error);
            }

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                var error = $"File is empty (0 bytes): {filePath}";
                _logger?.LogError(">>> {Error}", error);
                throw new InvalidOperationException(error);
            }

            // Check file permissions
            try
            {
                using (var stream = File.OpenRead(filePath))
                {
                    // File is readable
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                var error = $"File access denied (permissions): {filePath}";
                _logger?.LogError(ex, ">>> {Error}", error);
                throw new UnauthorizedAccessException(error, ex);
            }
            catch (IOException ex)
            {
                var error = $"File I/O error: {filePath}";
                _logger?.LogError(ex, ">>> {Error}", error);
                throw new IOException(error, ex);
            }

            try
            {
                _logger?.LogInformation(">>> Calling mediainfo CLI for: {FilePath}", filePath);
                _logger?.LogInformation(">>> File size: {Size} bytes ({SizeMB} MB), Last modified: {Modified}", 
                    fileInfo.Length, Math.Round(fileInfo.Length / 1024.0 / 1024.0, 2), fileInfo.LastWriteTime);
                
                var startTime = DateTime.UtcNow;
                
                // Call mediainfo CLI with XML output
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "mediainfo",
                    Arguments = $"--Output=XML \"{filePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                string xmlOutput;
                string errorOutput;
                
                using (var process = Process.Start(processStartInfo))
                {
                    if (process == null)
                    {
                        throw new InvalidOperationException("Failed to start mediainfo process");
                    }
                    
                    xmlOutput = process.StandardOutput.ReadToEnd();
                    errorOutput = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    
                    var duration = DateTime.UtcNow - startTime;
                    _logger?.LogInformation(">>> mediainfo CLI completed in {Duration}ms, exit code: {ExitCode}", 
                        duration.TotalMilliseconds, process.ExitCode);
                    
                    if (process.ExitCode != 0)
                    {
                        var errorMessage = $"mediainfo CLI failed with exit code {process.ExitCode}";
                        if (!string.IsNullOrEmpty(errorOutput))
                        {
                            errorMessage += $": {errorOutput}";
                        }
                        _logger?.LogError(">>> FAILED to analyze file with mediainfo: {FilePath}. {Error}", filePath, errorMessage);
                        throw new InvalidOperationException($"Failed to analyze media file with mediainfo: {filePath}. {errorMessage}");
                    }
                }
                
                if (string.IsNullOrWhiteSpace(xmlOutput))
                {
                    throw new InvalidOperationException($"mediainfo returned empty output for: {filePath}");
                }
                
                _logger?.LogInformation(">>> mediainfo XML output received ({Size} bytes)", xmlOutput.Length);
                
                // Clean XML output - remove any leading/trailing whitespace and find XML start
                var xmlStart = xmlOutput.IndexOf("<?xml", StringComparison.OrdinalIgnoreCase);
                if (xmlStart < 0)
                {
                    xmlStart = xmlOutput.IndexOf("<MediaInfo", StringComparison.OrdinalIgnoreCase);
                }
                if (xmlStart < 0)
                {
                    xmlStart = xmlOutput.IndexOf("<media", StringComparison.OrdinalIgnoreCase);
                }
                
                if (xmlStart >= 0)
                {
                    xmlOutput = xmlOutput.Substring(xmlStart).Trim();
                }
                
                // Log first 500 chars of XML for debugging
                if (xmlOutput.Length > 0)
                {
                    var preview = xmlOutput.Substring(0, Math.Min(500, xmlOutput.Length));
                    _logger?.LogDebug(">>> XML preview: {Preview}", preview);
                }
                
                // Parse XML output
                XDocument doc;
                try
                {
                    doc = XDocument.Parse(xmlOutput);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, ">>> Failed to parse mediainfo XML output");
                    _logger?.LogError(">>> XML content (first 1000 chars): {Xml}", 
                        xmlOutput.Length > 1000 ? xmlOutput.Substring(0, 1000) : xmlOutput);
                    throw new InvalidOperationException($"Failed to parse mediainfo XML output: {ex.Message}", ex);
                }
                
                // Get root element
                var rootElement = doc.Root;
                if (rootElement == null)
                {
                    _logger?.LogError(">>> XML has no root element. Full XML: {Xml}", xmlOutput);
                    throw new InvalidOperationException("Invalid mediainfo XML: no root element found");
                }
                
                _logger?.LogDebug(">>> XML root element name: {Name}, Namespace: {Namespace}", 
                    rootElement.Name.LocalName, rootElement.Name.Namespace);
                
                // Handle XML namespaces - mediainfo uses xmlns="https://mediaarea.net/mediainfo"
                XNamespace ns = rootElement.GetDefaultNamespace();
                if (string.IsNullOrEmpty(ns.NamespaceName))
                {
                    // Try to get namespace from root element attributes
                    var xmlnsAttr = rootElement.Attribute(XNamespace.Xmlns + "default") 
                        ?? rootElement.Attributes().FirstOrDefault(a => a.Name.LocalName == "xmlns");
                    if (xmlnsAttr != null)
                    {
                        ns = xmlnsAttr.Value;
                    }
                }
                
                // If still no namespace, try the common mediainfo namespace
                if (string.IsNullOrEmpty(ns.NamespaceName))
                {
                    ns = "https://mediaarea.net/mediainfo";
                }
                
                _logger?.LogDebug(">>> Using XML namespace: {Namespace}", ns.NamespaceName);
                
                // mediainfo CLI XML structure: <MediaInfo xmlns="..."><media>...</media></MediaInfo>
                XElement? mediaElement2 = null;
                
                if (rootElement.Name.LocalName == "MediaInfo")
                {
                    // Structure: <MediaInfo><media>...</media></MediaInfo>
                    // Try with namespace first, then without
                    mediaElement2 = rootElement.Element(ns + "media") 
                        ?? rootElement.Element("media")
                        ?? rootElement.Descendants(ns + "media").FirstOrDefault()
                        ?? rootElement.Descendants("media").FirstOrDefault();
                }
                else if (rootElement.Name.LocalName == "media")
                {
                    // Structure: <media>...</media> (direct)
                    mediaElement2 = rootElement;
                }
                else
                {
                    // Try to find media element anywhere (with and without namespace)
                    mediaElement2 = rootElement.Descendants(ns + "media").FirstOrDefault()
                        ?? rootElement.Descendants("media").FirstOrDefault();
                }
                
                if (mediaElement2 == null)
                {
                    _logger?.LogError(">>> Could not find 'media' element. Root: {RootName}, Namespace: {Ns}, Available elements: {Elements}", 
                        rootElement.Name.LocalName, 
                        ns.NamespaceName,
                        string.Join(", ", rootElement.Elements().Select(e => e.Name.LocalName)));
                    _logger?.LogError(">>> XML content (first 1000 chars): {Xml}", 
                        xmlOutput.Length > 1000 ? xmlOutput.Substring(0, 1000) : xmlOutput);
                    throw new InvalidOperationException($"Invalid mediainfo XML: missing media element. Root is: {rootElement.Name.LocalName}");
                }
                
                _logger?.LogDebug(">>> Found media element with {Count} track elements", 
                    mediaElement2.Elements(ns + "track").Count() + mediaElement2.Elements("track").Count());
                
                // Get General track (container info) - handle namespace
                var generalTrack = mediaElement2.Elements(ns + "track").FirstOrDefault(t => t.Attribute("type")?.Value == "General")
                    ?? mediaElement2.Elements("track").FirstOrDefault(t => t.Attribute("type")?.Value == "General");
                
                if (generalTrack != null)
                {
                    // Container
                    var containerFormat = GetElementValue(generalTrack, "Format");
                    videoInfo.Container = !string.IsNullOrEmpty(containerFormat) ? containerFormat.ToUpperInvariant() : string.Empty;
                    if (string.IsNullOrEmpty(videoInfo.Container))
                    {
                        var ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                        videoInfo.Container = ext switch
                        {
                            "MP4" or "M4V" or "MOV" => "MP4",
                            "MKV" or "MKA" => "MKV",
                            "WEBM" => "WebM",
                            "TS" or "M2TS" => "TS",
                            "OGG" or "OGV" => "OGG",
                            "AVI" => "AVI",
                            _ => ext
                        };
                    }
                    
                    // Duration - try to get from Duration_String3 (formatted) first as it's more reliable,
                    // then Duration field (milliseconds) as fallback
                    var durationMsStr = GetElementValue(generalTrack, "Duration", ns) 
                        ?? GetElementValue(generalTrack, "Duration", null);
                    var durationFormattedStr = GetElementValue(generalTrack, "Duration_String3", ns)
                        ?? GetElementValue(generalTrack, "Duration_String3", null);
                    
                    // Parse both Duration_String3 and Duration field, then choose the more reasonable one
                    double parsedFromFormatted = 0;
                    double parsedFromNumeric = 0;
                    
                    if (!string.IsNullOrEmpty(durationFormattedStr))
                    {
                        parsedFromFormatted = ParseDurationString(durationFormattedStr);
                        _logger?.LogDebug("Parsed Duration_String3 '{FormattedStr}' -> {ParsedSeconds} seconds", durationFormattedStr, parsedFromFormatted);
                    }
                    
                    if (!string.IsNullOrEmpty(durationMsStr))
                    {
                        // Duration field should be in milliseconds according to mediainfo spec
                        if (double.TryParse(durationMsStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var durationValue))
                        {
                            // Standard mediainfo Duration field is in milliseconds
                            // However, some edge cases might have it in seconds, so we check
                            if (durationValue > 86400000)
                            {
                                // Definitely milliseconds (> 24 hours in ms)
                                parsedFromNumeric = durationValue / 1000.0;
                            }
                            else if (durationValue >= 1.0 && durationValue <= 86400.0)
                            {
                                // Ambiguous range: could be milliseconds (1-86400 ms = 0.001-86.4 seconds)
                                // or seconds (1-86400 seconds = 1 second to 24 hours)
                                // Since mediainfo spec says Duration is in milliseconds, we assume milliseconds
                                // But if the result seems unreasonably small for a video, it might actually be in seconds
                                var asMilliseconds = durationValue / 1000.0;
                                
                                // Heuristic: If value is >= 60 (1 minute) and treating as milliseconds gives < 10 seconds,
                                // it's likely already in seconds (most videos are at least 10 seconds, usually much longer)
                                // Also check: if value >= 3600 (1 hour) and asMilliseconds < 60, definitely seconds
                                if (durationValue >= 60.0 && asMilliseconds < 10.0)
                                {
                                    // Likely already in seconds
                                    parsedFromNumeric = durationValue;
                                    _logger?.LogDebug("Duration field value {DurationValue} in ambiguous range, treating as seconds (value >= 60 and ms conversion {AsMs} < 10s): {Duration} seconds", durationValue, asMilliseconds, parsedFromNumeric);
                                }
                                else if (durationValue >= 3600.0 && asMilliseconds < 60.0)
                                {
                                    // Definitely seconds (value >= 1 hour but ms conversion < 1 minute)
                                    parsedFromNumeric = durationValue;
                                    _logger?.LogDebug("Duration field value {DurationValue} in ambiguous range, treating as seconds (value >= 3600 and ms conversion {AsMs} < 60s): {Duration} seconds", durationValue, asMilliseconds, parsedFromNumeric);
                                }
                                else
                                {
                                    // Treat as milliseconds (standard)
                                    parsedFromNumeric = asMilliseconds;
                                    _logger?.LogDebug("Extracted duration from General track Duration field (as milliseconds): {DurationStr} -> {DurationValue} ms -> {Duration} seconds", durationMsStr, durationValue, parsedFromNumeric);
                                }
                            }
                            else
                            {
                                // Very small value (< 1), assume milliseconds
                                parsedFromNumeric = durationValue / 1000.0;
                                _logger?.LogDebug("Extracted duration from General track Duration field (as milliseconds): {DurationStr} -> {DurationValue} ms -> {Duration} seconds", durationMsStr, durationValue, parsedFromNumeric);
                            }
                        }
                        else
                        {
                            _logger?.LogWarning("Could not parse Duration field as number: {DurationStr}", durationMsStr);
                        }
                    }
                    
                    // Choose the more reasonable duration value
                    // Prefer the larger value if both are available and one seems too small
                    if (parsedFromFormatted > 0 && parsedFromNumeric > 0)
                    {
                        // Both available - use the larger one if there's a significant difference
                        // If one is < 60 seconds and the other is >= 60 seconds, prefer the larger one
                        // This handles cases where "0:03" (3 seconds) is wrong but Duration field has correct value
                        if (parsedFromFormatted < 60.0 && parsedFromNumeric >= 60.0)
                        {
                            videoInfo.Duration = parsedFromNumeric;
                            _logger?.LogDebug("Both duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using numeric (formatted < 60s but numeric >= 60s).", parsedFromFormatted, parsedFromNumeric);
                        }
                        else if (parsedFromNumeric < 60.0 && parsedFromFormatted >= 60.0)
                        {
                            videoInfo.Duration = parsedFromFormatted;
                            _logger?.LogDebug("Both duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using formatted (numeric < 60s but formatted >= 60s).", parsedFromFormatted, parsedFromNumeric);
                        }
                        // If both are < 60 seconds but one is significantly larger (2x or more), prefer the larger one
                        else if (parsedFromFormatted < 60.0 && parsedFromNumeric < 60.0)
                        {
                            if (parsedFromNumeric >= parsedFromFormatted * 2.0)
                            {
                                videoInfo.Duration = parsedFromNumeric;
                                _logger?.LogDebug("Both duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using numeric (numeric is 2x+ larger).", parsedFromFormatted, parsedFromNumeric);
                            }
                            else if (parsedFromFormatted >= parsedFromNumeric * 2.0)
                            {
                                videoInfo.Duration = parsedFromFormatted;
                                _logger?.LogDebug("Both duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using formatted (formatted is 2x+ larger).", parsedFromFormatted, parsedFromNumeric);
                            }
                            else
                            {
                                // Both are similar and both < 60s, prefer formatted but log warning
                                videoInfo.Duration = parsedFromFormatted;
                                _logger?.LogWarning("Both duration sources are suspiciously small: formatted={Formatted}s, numeric={Numeric}s. Using formatted but duration may be incorrect.", parsedFromFormatted, parsedFromNumeric);
                            }
                        }
                        // Both seem reasonable (>= 60 seconds), prefer formatted (more reliable)
                        else
                        {
                            videoInfo.Duration = parsedFromFormatted;
                            _logger?.LogDebug("Both duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using formatted (preferred).", parsedFromFormatted, parsedFromNumeric);
                        }
                    }
                    else if (parsedFromFormatted > 0)
                    {
                        // If formatted string gives a very small value (< 60 seconds), log a warning
                        // as it might be incorrect (most videos are longer than 1 minute)
                        if (parsedFromFormatted < 60.0)
                        {
                            _logger?.LogWarning("Duration from Duration_String3 seems suspiciously small: {FormattedStr} -> {Duration} seconds. Duration may be incorrect. Duration field was: {DurationField}", durationFormattedStr, parsedFromFormatted, durationMsStr ?? "N/A");
                        }
                        videoInfo.Duration = parsedFromFormatted;
                        _logger?.LogDebug("Extracted duration from General track Duration_String3: {FormattedStr} -> {Duration} seconds", durationFormattedStr, videoInfo.Duration);
                    }
                    else if (parsedFromNumeric > 0)
                    {
                        videoInfo.Duration = parsedFromNumeric;
                        _logger?.LogDebug("Extracted duration from General track Duration field: {DurationStr} -> {Duration} seconds", durationMsStr, videoInfo.Duration);
                    }
                    
                    // If duration is still 0, try to get it from video track as fallback
                    if (videoInfo.Duration <= 0)
                    {
                        var videoTracksForDuration = mediaElement2.Elements(ns + "track").Where(t => t.Attribute("type")?.Value == "Video")
                            .Concat(mediaElement2.Elements("track").Where(t => t.Attribute("type")?.Value == "Video"))
                            .ToList();
                        if (videoTracksForDuration.Count > 0)
                        {
                            var videoTrack = videoTracksForDuration[0];
                            var videoDurationMsStr = GetElementValue(videoTrack, "Duration", ns) 
                                ?? GetElementValue(videoTrack, "Duration", null);
                            var videoDurationFormattedStr = GetElementValue(videoTrack, "Duration_String3", ns)
                                ?? GetElementValue(videoTrack, "Duration_String3", null);
                            
                            // Parse both Duration_String3 and Duration field, then choose the more reasonable one
                            double videoParsedFromFormatted = 0;
                            double videoParsedFromNumeric = 0;
                            
                            if (!string.IsNullOrEmpty(videoDurationFormattedStr))
                            {
                                videoParsedFromFormatted = ParseDurationString(videoDurationFormattedStr);
                            }
                            
                            if (!string.IsNullOrEmpty(videoDurationMsStr))
                            {
                                if (double.TryParse(videoDurationMsStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var durationValue))
                                {
                                    if (durationValue > 86400000)
                                    {
                                        videoParsedFromNumeric = durationValue / 1000.0;
                                    }
                                    else if (durationValue >= 1.0 && durationValue <= 86400.0)
                                    {
                                        var asMilliseconds = durationValue / 1000.0;
                                        
                                        if (durationValue >= 60.0 && asMilliseconds < 10.0)
                                        {
                                            videoParsedFromNumeric = durationValue;
                                            _logger?.LogDebug("Video track Duration field value {DurationValue} in ambiguous range, treating as seconds (value >= 60 and ms conversion {AsMs} < 10s): {Duration} seconds", durationValue, asMilliseconds, videoParsedFromNumeric);
                                        }
                                        else if (durationValue >= 3600.0 && asMilliseconds < 60.0)
                                        {
                                            videoParsedFromNumeric = durationValue;
                                            _logger?.LogDebug("Video track Duration field value {DurationValue} in ambiguous range, treating as seconds (value >= 3600 and ms conversion {AsMs} < 60s): {Duration} seconds", durationValue, asMilliseconds, videoParsedFromNumeric);
                                        }
                                        else
                                        {
                                            videoParsedFromNumeric = asMilliseconds;
                                            _logger?.LogDebug("Extracted duration from Video track Duration field (as milliseconds): {DurationStr} -> {DurationValue} ms -> {Duration} seconds", videoDurationMsStr, durationValue, videoParsedFromNumeric);
                                        }
                                    }
                                    else
                                    {
                                        // Very small value (< 1)
                                        if (durationValue < 0.001)
                                        {
                                            videoParsedFromNumeric = durationValue;
                                            _logger?.LogDebug("Video track Duration field value {DurationValue} < 0.001, treating as seconds: {Duration} seconds", durationValue, videoParsedFromNumeric);
                                    }
                                    else
                                    {
                                        // Very small value (< 1)
                                        if (durationValue < 0.001)
                                        {
                                            videoParsedFromNumeric = durationValue;
                                            _logger?.LogDebug("Video track Duration field value {DurationValue} < 0.001, treating as seconds: {Duration} seconds", durationValue, videoParsedFromNumeric);
                                        }
                                        else
                                        {
                                            videoParsedFromNumeric = durationValue / 1000.0;
                                            _logger?.LogDebug("Extracted duration from Video track Duration field (as milliseconds): {DurationStr} -> {DurationValue} ms -> {Duration} seconds", videoDurationMsStr, durationValue, videoParsedFromNumeric);
                                        }
                                    }
                                    }
                                }
                            }
                            
                            // Choose the more reasonable duration value (same logic as General track)
                            if (videoParsedFromFormatted > 0 && videoParsedFromNumeric > 0)
                            {
                                if (videoParsedFromFormatted < 60.0 && videoParsedFromNumeric >= 60.0)
                                {
                                    videoInfo.Duration = videoParsedFromNumeric;
                                    _logger?.LogDebug("Both video track duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using numeric (formatted < 60s but numeric >= 60s).", videoParsedFromFormatted, videoParsedFromNumeric);
                                }
                                else if (videoParsedFromNumeric < 60.0 && videoParsedFromFormatted >= 60.0)
                                {
                                    videoInfo.Duration = videoParsedFromFormatted;
                                    _logger?.LogDebug("Both video track duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using formatted (numeric < 60s but formatted >= 60s).", videoParsedFromFormatted, videoParsedFromNumeric);
                                }
                                else if (videoParsedFromFormatted < 60.0 && videoParsedFromNumeric < 60.0)
                                {
                                    if (videoParsedFromNumeric >= videoParsedFromFormatted * 2.0)
                                    {
                                        videoInfo.Duration = videoParsedFromNumeric;
                                        _logger?.LogDebug("Both video track duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using numeric (numeric is 2x+ larger).", videoParsedFromFormatted, videoParsedFromNumeric);
                                    }
                                    else if (videoParsedFromFormatted >= videoParsedFromNumeric * 2.0)
                                    {
                                        videoInfo.Duration = videoParsedFromFormatted;
                                        _logger?.LogDebug("Both video track duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using formatted (formatted is 2x+ larger).", videoParsedFromFormatted, videoParsedFromNumeric);
                                    }
                                    else
                                    {
                                        videoInfo.Duration = videoParsedFromFormatted;
                                        _logger?.LogWarning("Both video track duration sources are suspiciously small: formatted={Formatted}s, numeric={Numeric}s. Using formatted but duration may be incorrect.", videoParsedFromFormatted, videoParsedFromNumeric);
                                    }
                                }
                                else
                                {
                                    videoInfo.Duration = videoParsedFromFormatted;
                                    _logger?.LogDebug("Both video track duration sources available: formatted={Formatted}s, numeric={Numeric}s. Using formatted (preferred).", videoParsedFromFormatted, videoParsedFromNumeric);
                                }
                            }
                            else if (videoParsedFromFormatted > 0)
                            {
                                if (videoParsedFromFormatted < 60.0)
                                {
                                    _logger?.LogWarning("Video track Duration from Duration_String3 seems suspiciously small: {FormattedStr} -> {Duration} seconds. Duration may be incorrect.", videoDurationFormattedStr, videoParsedFromFormatted);
                                }
                                videoInfo.Duration = videoParsedFromFormatted;
                                _logger?.LogDebug("Extracted duration from Video track Duration_String3: {FormattedStr} -> {Duration} seconds", videoDurationFormattedStr, videoInfo.Duration);
                            }
                            else if (videoParsedFromNumeric > 0)
                            {
                                videoInfo.Duration = videoParsedFromNumeric;
                                _logger?.LogDebug("Extracted duration from Video track Duration field: {DurationStr} -> {Duration} seconds", videoDurationMsStr, videoInfo.Duration);
                            }
                        }
                    }
                    
                    if (videoInfo.Duration <= 0)
                    {
                        _logger?.LogWarning("Could not extract duration from mediainfo XML for: {FilePath}", filePath);
                    }
                    
                    // Check for fast start optimization (MP4 only)
                    if (videoInfo.Container == "MP4" || videoInfo.Container == "M4V" || videoInfo.Container == "MOV")
                    {
                        videoInfo.IsFastStart = CheckFastStart(filePath);
                    }
                }
                
                // Get Video tracks - handle namespace
                var videoTracks = mediaElement2.Elements(ns + "track").Where(t => t.Attribute("type")?.Value == "Video")
                    .Concat(mediaElement2.Elements("track").Where(t => t.Attribute("type")?.Value == "Video"))
                    .ToList();
                if (videoTracks.Count > 0)
                {
                    var videoTrack = videoTracks[0]; // Use first video track
                    
                    var codec = GetElementValue(videoTrack, "Format", ns);
                    videoInfo.VideoCodec = NormalizeCodecName(codec);
                    
                    // Get CodecID tag (important for MP4 compatibility)
                    var codecId = GetElementValue(videoTrack, "CodecID", ns);
                    videoInfo.VideoCodecTag = !string.IsNullOrEmpty(codecId) ? codecId.ToUpperInvariant() : string.Empty;
                    
                    // Validate codec tag for MP4 files
                    if (videoInfo.Container == "MP4" || videoInfo.Container == "M4V" || videoInfo.Container == "MOV")
                    {
                        videoInfo.IsCodecTagCorrect = ValidateCodecTag(videoInfo.VideoCodec, videoInfo.VideoCodecTag);
                    }
                    
                    var widthStr = GetElementValue(videoTrack, "Width", ns);
                    if (int.TryParse(widthStr, out var width))
                        videoInfo.Width = width;
                    
                    var heightStr = GetElementValue(videoTrack, "Height", ns);
                    if (int.TryParse(heightStr, out var height))
                        videoInfo.Height = height;
                    
                    var frameRateStr = GetElementValue(videoTrack, "FrameRate", ns);
                    if (double.TryParse(frameRateStr, out var frameRate))
                        videoInfo.FrameRate = frameRate;
                    
                    // Bit depth
                    var bitDepthStr = GetElementValue(videoTrack, "BitDepth", ns);
                    if (!string.IsNullOrEmpty(bitDepthStr) && int.TryParse(bitDepthStr, out var bitDepth))
                    {
                        videoInfo.BitDepth = bitDepth;
                    }
                    else
                    {
                        // Try alternative method
                        var colorDepth = GetElementValue(videoTrack, "colour_primaries", ns) ?? string.Empty;
                        videoInfo.BitDepth = videoInfo.VideoCodec.Contains("10") || colorDepth.Contains("BT.2020") ? 10 : 8;
                    }
                    
                    // HDR detection
                    var colorSpace = GetElementValue(videoTrack, "colour_primaries", ns) ?? string.Empty;
                    var transfer = GetElementValue(videoTrack, "transfer_characteristics", ns) ?? string.Empty;
                    var matrix = GetElementValue(videoTrack, "matrix_coefficients", ns) ?? string.Empty;
                    
                    if (transfer.Contains("SMPTE ST 2084") || colorSpace.Contains("BT.2020"))
                    {
                        videoInfo.IsHDR = true;
                        videoInfo.HDRType = "HDR10";
                    }
                    else if (transfer.Contains("HLG") || transfer.Contains("Hybrid Log-Gamma"))
                    {
                        videoInfo.IsHDR = true;
                        videoInfo.HDRType = "HLG";
                    }
                    else if (colorSpace.Contains("Dolby") || matrix.Contains("Dolby"))
                    {
                        videoInfo.IsHDR = true;
                        videoInfo.HDRType = "Dolby Vision";
                    }
                    
                    // Profile
                    videoInfo.VideoProfile = GetElementValue(videoTrack, "Format_Profile", ns) ?? string.Empty;
                }
                
                // Get Audio tracks - handle namespace
                var audioTracks = mediaElement2.Elements(ns + "track").Where(t => t.Attribute("type")?.Value == "Audio")
                    .Concat(mediaElement2.Elements("track").Where(t => t.Attribute("type")?.Value == "Audio"))
                    .ToList();
                foreach (var audioTrackXml in audioTracks)
                {
                    var codecId = GetElementValue(audioTrackXml, "CodecID", ns) ?? GetElementValue(audioTrackXml, "Format", ns) ?? string.Empty;
                    var language = GetElementValue(audioTrackXml, "Language", ns) ?? "Unknown";
                    var channelsStr = GetElementValue(audioTrackXml, "Channels", ns);
                    var sampleRateStr = GetElementValue(audioTrackXml, "SamplingRate", ns);
                    
                    var audioTrack = new AudioTrack
                    {
                        Codec = NormalizeAudioCodec(codecId),
                        Language = language,
                        Channels = !string.IsNullOrEmpty(channelsStr) && int.TryParse(channelsStr, out var ch) ? ch : 2,
                        SampleRate = !string.IsNullOrEmpty(sampleRateStr) && int.TryParse(sampleRateStr, out var sr) ? sr : 48000
                    };
                    
                    var bitrateStr = GetElementValue(audioTrackXml, "BitRate", ns);
                    if (!string.IsNullOrEmpty(bitrateStr) && int.TryParse(bitrateStr, out var bitrate))
                    {
                        audioTrack.Bitrate = bitrate / 1000; // Convert to kbps
                    }
                    
                    videoInfo.AudioTracks.Add(audioTrack);
                }
                
                // Get Text (subtitle) tracks - handle namespace
                var textTracks = mediaElement2.Elements(ns + "track").Where(t => t.Attribute("type")?.Value == "Text")
                    .Concat(mediaElement2.Elements("track").Where(t => t.Attribute("type")?.Value == "Text"))
                    .ToList();
                foreach (var textTrackXml in textTracks)
                {
                    var format = GetElementValue(textTrackXml, "Format", ns) ?? string.Empty;
                    var language = GetElementValue(textTrackXml, "Language", ns) ?? "Unknown";
                    
                    var subtitleTrack = new SubtitleTrack
                    {
                        Format = NormalizeSubtitleFormat(format),
                        Language = language,
                        IsEmbedded = true
                    };
                    videoInfo.SubtitleTracks.Add(subtitleTrack);
                }
                
                // Check for fast start optimization (MP4 only)
                if (videoInfo.Container == "MP4" || videoInfo.Container == "M4V" || videoInfo.Container == "MOV")
                {
                    videoInfo.IsFastStart = CheckFastStart(filePath);
                }

                _logger?.LogDebug("Successfully extracted video information");
                return videoInfo;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error extracting video information from: {FilePath}", filePath);
                throw;
            }
        }

        private double ParseDurationString(string durationStr)
        {
            if (string.IsNullOrWhiteSpace(durationStr))
                return 0;

            // Try parsing as pure number (milliseconds)
            if (double.TryParse(durationStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var durationMs))
            {
                // If the number is very large (> 86400000 = 24 hours in ms), assume it's milliseconds
                // If it's smaller, it might already be in seconds
                if (durationMs > 86400000)
                {
                    return durationMs / 1000.0; // Convert milliseconds to seconds
                }
                else if (durationMs > 86400)
                {
                    // Likely already in seconds
                    return durationMs;
                }
                else
                {
                    // Very small number, might be in hours or minutes, but more likely seconds
                    return durationMs;
                }
            }

            // Try parsing as time format HH:MM:SS or HH:MM:SS.mmm first
            var timeMatch = System.Text.RegularExpressions.Regex.Match(durationStr, @"(\d+):(\d+):(\d+)(?:\.(\d+))?");
            if (timeMatch.Success)
            {
                var hours = int.Parse(timeMatch.Groups[1].Value);
                var minutes = int.Parse(timeMatch.Groups[2].Value);
                var seconds = int.Parse(timeMatch.Groups[3].Value);
                var milliseconds = timeMatch.Groups[4].Success ? int.Parse(timeMatch.Groups[4].Value) : 0;
                
                var totalSeconds = hours * 3600.0 + minutes * 60.0 + seconds + milliseconds / 1000.0;
                return totalSeconds;
            }
            
            // Try parsing as time format MM:SS (e.g., "3:45" = 3 minutes 45 seconds)
            // This handles cases where mediainfo omits hours when they're 0
            // Note: "0:03" is ambiguous - could mean 0 minutes 3 seconds (3s) OR 0 hours 3 minutes (180s)
            // For video files, "0:03" is more likely to mean "0:03:00" (3 minutes) than "00:00:03" (3 seconds)
            var mmssMatch = System.Text.RegularExpressions.Regex.Match(durationStr, @"^(\d+):(\d+)(?:\.(\d+))?$");
            if (mmssMatch.Success)
            {
                var firstPart = int.Parse(mmssMatch.Groups[1].Value);
                var secondPart = int.Parse(mmssMatch.Groups[2].Value);
                var milliseconds = mmssMatch.Groups[3].Success ? int.Parse(mmssMatch.Groups[3].Value) : 0;
                
                double totalSeconds;
                if (secondPart >= 60)
                {
                    // Invalid format - seconds can't be >= 60
                    // This might be a malformed string, try treating as H:MM instead
                    totalSeconds = firstPart * 3600.0 + secondPart * 60.0 + milliseconds / 1000.0;
                    _logger?.LogWarning("Duration string '{DurationStr}' has seconds >= 60, treating as H:MM format: {TotalSeconds} seconds", durationStr, totalSeconds);
                }
                else if (firstPart == 0 && secondPart < 60)
                {
                    // Ambiguous case: "0:03" could be:
                    // - "00:00:03" (0 hours, 0 minutes, 3 seconds) = 3 seconds
                    // - "00:03:00" (0 hours, 3 minutes, 0 seconds) = 180 seconds
                    // For video files, the latter is more common (videos are rarely exactly 3 seconds)
                    // We'll treat it as H:MM (0 hours, 3 minutes) = 180 seconds
                    // This is more reasonable for video files
                    totalSeconds = firstPart * 3600.0 + secondPart * 60.0 + milliseconds / 1000.0;
                    _logger?.LogDebug("Duration string '{DurationStr}' is ambiguous (0:MM format), treating as H:MM (0 hours, {Minutes} minutes) = {TotalSeconds} seconds", durationStr, secondPart, totalSeconds);
                }
                else
                {
                    // Standard MM:SS format (first part > 0, so definitely minutes:seconds)
                    totalSeconds = firstPart * 60.0 + secondPart + milliseconds / 1000.0;
                }
                
                return totalSeconds;
            }

            // Try parsing as formatted string like "1h 23min 45s" or "1h 23min 45.123s"
            var formattedMatch = System.Text.RegularExpressions.Regex.Match(durationStr, 
                @"(?:(\d+)\s*h(?:ours?)?)?\s*(?:(\d+)\s*min(?:utes?)?)?\s*(?:(\d+(?:\.\d+)?)\s*s(?:econds?)?)?", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (formattedMatch.Success)
            {
                var hours = formattedMatch.Groups[1].Success ? int.Parse(formattedMatch.Groups[1].Value) : 0;
                var minutes = formattedMatch.Groups[2].Success ? int.Parse(formattedMatch.Groups[2].Value) : 0;
                var seconds = formattedMatch.Groups[3].Success ? double.Parse(formattedMatch.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : 0;
                
                var totalSeconds = hours * 3600.0 + minutes * 60.0 + seconds;
                return totalSeconds;
            }

            // Try extracting just numbers and assume milliseconds if large enough
            // Only do this if the string doesn't contain colons (which indicate time format)
            // If it contains colons but didn't match our patterns, it's likely malformed
            if (!durationStr.Contains(":"))
            {
                var numbersOnly = System.Text.RegularExpressions.Regex.Replace(durationStr, @"[^\d.]", "");
                if (!string.IsNullOrEmpty(numbersOnly) && double.TryParse(numbersOnly, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numValue))
                {
                    // If the number is very large, assume milliseconds
                    if (numValue > 86400000)
                    {
                        return numValue / 1000.0;
                    }
                    // Otherwise assume seconds
                    return numValue;
                }
            }

            // If we get here, the duration string couldn't be parsed
            _logger?.LogWarning("Could not parse duration string: {DurationStr}", durationStr);
            return 0;
        }

        private string GetElementValue(XElement element, string name, XNamespace? ns = null)
        {
            XElement? child = null;
            if (ns != null && !string.IsNullOrEmpty(ns.NamespaceName))
            {
                child = element.Element(ns + name);
            }
            if (child == null)
            {
                child = element.Element(name);
            }
            return child?.Value ?? string.Empty;
        }

        /// <summary>
        /// Checks if an MP4 file has fast start optimization (moov atom at the beginning).
        /// The fast start flag (-movflags +faststart) moves the moov atom to the beginning
        /// of the file, enabling streaming without downloading the entire file first.
        /// </summary>
        private bool CheckFastStart(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return false;

                // MP4 files use a box/atom structure
                // Fast start means the 'moov' atom appears before the 'mdat' atom
                // We'll check the first 32KB of the file for the moov atom
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var buffer = new byte[Math.Min(32768, fileStream.Length)];
                    var bytesRead = fileStream.Read(buffer, 0, buffer.Length);

                    if (bytesRead < 8)
                        return false;

                    // MP4 files start with a 'ftyp' box (4 bytes size + 4 bytes type)
                    // We need to parse boxes to find 'moov' and 'mdat'
                    int offset = 0;
                    bool foundMoov = false;
                    bool foundMdat = false;

                    while (offset < bytesRead - 8)
                    {
                        // Read box size (big-endian)
                        if (offset + 4 > bytesRead) break;
                        uint boxSize = (uint)((buffer[offset] << 24) | (buffer[offset + 1] << 16) | 
                                             (buffer[offset + 2] << 8) | buffer[offset + 3]);
                        
                        // Read box type (4 bytes)
                        if (offset + 8 > bytesRead) break;
                        string boxType = System.Text.Encoding.ASCII.GetString(buffer, offset + 4, 4);

                        // Check for moov atom
                        if (boxType == "moov")
                        {
                            foundMoov = true;
                            // If we find moov before mdat, it's fast start
                            if (!foundMdat)
                            {
                                return true;
                            }
                        }

                        // Check for mdat atom
                        if (boxType == "mdat")
                        {
                            foundMdat = true;
                            // If we find mdat before moov, it's NOT fast start
                            if (!foundMoov)
                            {
                                return false;
                            }
                        }

                        // Handle large boxes (size = 1 means extended size follows)
                        if (boxSize == 1)
                        {
                            // Extended size is 8 bytes, skip it
                            offset += 16;
                        }
                        else if (boxSize == 0)
                        {
                            // Size 0 means rest of file, break
                            break;
                        }
                        else
                        {
                            // Move to next box
                            offset += (int)boxSize;
                        }

                        // Safety check
                        if (offset >= bytesRead || offset < 0)
                            break;
                    }

                    // If we found moov but not mdat in the first 32KB, it's likely fast start
                    // (mdat is usually large and might be later in the file)
                    return foundMoov && !foundMdat;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Error checking fast start for file: {FilePath}", filePath);
                return false;
            }
        }

        private SubtitleTrack ExtractSubtitleInfo(string filePath)
        {
            var ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            
            // Extract language from filename (common patterns: movie.en.srt, movie.eng.srt, movie.english.srt, etc.)
            var language = ExtractLanguageFromFilename(fileName);
            
            return new SubtitleTrack
            {
                Format = ext switch
                {
                    "SRT" => "SRT",
                    "VTT" => "VTT",
                    "ASS" => "ASS",
                    "SSA" => "SSA",
                    "SUB" => "VobSub",
                    _ => ext
                },
                Language = language,
                IsEmbedded = false,
                FilePath = filePath
            };
        }

        /// <summary>
        /// Extracts language information from subtitle filename.
        /// Supports common patterns:
        /// - movie.en.srt (ISO 639-1: en, de, fr, es, etc.)
        /// - movie.eng.srt (ISO 639-2: eng, deu, fra, spa, etc.)
        /// - movie.en-US.srt (locale: en-US, en-GB, etc.)
        /// - movie.english.srt (full language name)
        /// - movie.spanish.srt (full language name)
        /// </summary>
        private string ExtractLanguageFromFilename(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Unknown";

            // Common ISO 639-1 language codes (2-letter)
            var iso6391Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", "English" },
                { "de", "German" },
                { "fr", "French" },
                { "es", "Spanish" },
                { "it", "Italian" },
                { "pt", "Portuguese" },
                { "ru", "Russian" },
                { "ja", "Japanese" },
                { "zh", "Chinese" },
                { "ko", "Korean" },
                { "ar", "Arabic" },
                { "hi", "Hindi" },
                { "nl", "Dutch" },
                { "pl", "Polish" },
                { "tr", "Turkish" },
                { "sv", "Swedish" },
                { "da", "Danish" },
                { "no", "Norwegian" },
                { "fi", "Finnish" },
                { "cs", "Czech" },
                { "hu", "Hungarian" },
                { "ro", "Romanian" },
                { "th", "Thai" },
                { "vi", "Vietnamese" },
                { "he", "Hebrew" },
                { "el", "Greek" },
                { "ms", "Malay" },
                { "nb", "Norwegian Bokmål" },
                { "id", "Indonesian" },
                { "is", "Icelandic" },
                { "hr", "Croatian" }
            };

            // Common ISO 639-2 language codes (3-letter)
            var iso6392Codes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "eng", "English" },
                { "deu", "German" },
                { "fra", "French" },
                { "spa", "Spanish" },
                { "ita", "Italian" },
                { "por", "Portuguese" },
                { "rus", "Russian" },
                { "jpn", "Japanese" },
                { "chi", "Chinese" },
                { "kor", "Korean" },
                { "ara", "Arabic" },
                { "hin", "Hindi" },
                { "nld", "Dutch" },
                { "pol", "Polish" },
                { "tur", "Turkish" },
                { "swe", "Swedish" },
                { "dan", "Danish" },
                { "nor", "Norwegian" },
                { "fin", "Finnish" },
                { "ces", "Czech" },
                { "hun", "Hungarian" },
                { "ron", "Romanian" },
                { "tha", "Thai" },
                { "vie", "Vietnamese" },
                { "heb", "Hebrew" },
                { "ell", "Greek" },
                { "may", "Malay" },
                { "msa", "Malay" },
                { "nob", "Norwegian Bokmål" },
                { "ind", "Indonesian" },
                { "ice", "Icelandic" },
                { "isl", "Icelandic" },
                { "hrv", "Croatian" }
            };

            // Common full language names
            var languageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "english", "English" },
                { "german", "German" },
                { "french", "French" },
                { "spanish", "Spanish" },
                { "italian", "Italian" },
                { "portuguese", "Portuguese" },
                { "russian", "Russian" },
                { "japanese", "Japanese" },
                { "chinese", "Chinese" },
                { "korean", "Korean" },
                { "arabic", "Arabic" },
                { "hindi", "Hindi" },
                { "dutch", "Dutch" },
                { "polish", "Polish" },
                { "turkish", "Turkish" },
                { "swedish", "Swedish" },
                { "danish", "Danish" },
                { "norwegian", "Norwegian" },
                { "finnish", "Finnish" },
                { "czech", "Czech" },
                { "hungarian", "Hungarian" },
                { "romanian", "Romanian" },
                { "thai", "Thai" },
                { "vietnamese", "Vietnamese" },
                { "hebrew", "Hebrew" },
                { "greek", "Greek" },
                { "malay", "Malay" },
                { "norwegian", "Norwegian" },
                { "norwegian bokmål", "Norwegian Bokmål" },
                { "bokmål", "Norwegian Bokmål" },
                { "indonesian", "Indonesian" },
                { "icelandic", "Icelandic" },
                { "croatian", "Croatian" }
            };

            // Split filename by dots to check for language codes
            var parts = fileName.Split('.');
            
            // Check each part for language indicators
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                var partLower = part.ToLowerInvariant();

                // Check for locale format (e.g., en-US, en-GB)
                if (part.Contains('-'))
                {
                    var localeParts = part.Split('-');
                    if (localeParts.Length >= 1 && iso6391Codes.ContainsKey(localeParts[0]))
                    {
                        var languageName = iso6391Codes[localeParts[0]];
                        // Include locale if it's a country code (2 letters)
                        if (localeParts.Length > 1 && localeParts[1].Length == 2)
                        {
                            return $"{languageName} ({part})";
                        }
                        return languageName;
                    }
                }

                // Check ISO 639-1 (2-letter codes)
                if (part.Length == 2 && iso6391Codes.ContainsKey(part))
                {
                    return iso6391Codes[part];
                }

                // Check ISO 639-2 (3-letter codes)
                if (part.Length == 3 && iso6392Codes.ContainsKey(part))
                {
                    return iso6392Codes[part];
                }

                // Check full language names
                if (languageNames.ContainsKey(partLower))
                {
                    return languageNames[partLower];
                }
            }

            // If no language found, return Unknown
            return "Unknown";
        }

        /// <summary>
        /// Validates if the codec tag (CodecID) is correct for the given codec in MP4 files.
        /// Correct tags:
        /// - H.265/HEVC: hvc1 or HVC1 (preferred over hev1 or HEVC)
        /// - H.264/AVC: avc1 or AVC1 (preferred over avc3)
        /// - VP9: vp09
        /// - AV1: av01
        /// </summary>
        private bool ValidateCodecTag(string videoCodec, string codecTag)
        {
            if (string.IsNullOrEmpty(codecTag))
                return true; // Can't validate if tag is missing

            var codecUpper = videoCodec.ToUpperInvariant();
            var tagUpper = codecTag.ToUpperInvariant();

            // H.265/HEVC should use hvc1 (not hev1)
            if (codecUpper.Contains("H.265") || codecUpper.Contains("HEVC"))
            {
                // HVC1 is preferred, but HEVC is also acceptable (some tools use it)
                return tagUpper == "HVC1" || tagUpper == "HEVC";
            }

            // H.264/AVC should use avc1 (preferred over avc3)
            if (codecUpper.Contains("H.264") || codecUpper.Contains("AVC"))
            {
                // AVC1 is preferred, but AVC3 and H264 are also acceptable
                return tagUpper == "AVC1" || tagUpper == "AVC3" || tagUpper == "H264";
            }

            // VP9 should use vp09
            if (codecUpper.Contains("VP9"))
            {
                return tagUpper == "VP09";
            }

            // AV1 should use av01
            if (codecUpper.Contains("AV1"))
            {
                return tagUpper == "AV01";
            }

            // For other codecs, any tag is acceptable
            return true;
        }

        private string NormalizeCodecName(string codec)
        {
            if (string.IsNullOrEmpty(codec))
                return "Unknown";

            var upper = codec.ToUpperInvariant();
            
            if (upper.Contains("AVC") || upper.Contains("H264") || upper.Contains("X264"))
                return "H.264";
            if (upper.Contains("HEVC") || upper.Contains("H265") || upper.Contains("X265"))
                return "H.265";
            if (upper.Contains("VP9"))
                return "VP9";
            if (upper.Contains("AV1"))
                return "AV1";
            if (upper.Contains("MPEG-4") || upper.Contains("MPEG4"))
            {
                if (upper.Contains("SP"))
                    return "MPEG-4 SP";
                if (upper.Contains("ASP"))
                    return "MPEG-4 ASP";
                return "MPEG-4";
            }

            return codec;
        }

        private string NormalizeAudioCodec(string codec)
        {
            if (string.IsNullOrEmpty(codec))
                return "Unknown";

            var upper = codec.ToUpperInvariant();
            
            if (upper.Contains("AAC") || upper.Contains("MP4A"))
                return "AAC";
            if (upper.Contains("MP3") || upper.Contains("MPEG"))
                return "MP3";
            if (upper.Contains("AC3") || upper.Contains("AC-3"))
                return "AC3";
            if (upper.Contains("EAC3") || upper.Contains("E-AC-3") || upper.Contains("DD+"))
                return "EAC3";
            if (upper.Contains("DTS"))
                return "DTS";
            if (upper.Contains("FLAC"))
                return "FLAC";
            if (upper.Contains("OPUS"))
                return "Opus";
            if (upper.Contains("VORBIS"))
                return "Vorbis";
            if (upper.Contains("ALAC"))
                return "ALAC";

            return codec;
        }

        private string NormalizeSubtitleFormat(string format)
        {
            if (string.IsNullOrEmpty(format))
                return "Unknown";

            var upper = format.ToUpperInvariant();
            
            if (upper.Contains("SRT") || upper.Contains("SUBRIP"))
                return "SRT";
            if (upper.Contains("VTT") || upper.Contains("WEBVTT"))
                return "VTT";
            if (upper.Contains("ASS"))
                return "ASS";
            if (upper.Contains("SSA"))
                return "SSA";
            if (upper.Contains("VOBSUB") || upper.Contains("SUB"))
                return "VobSub";
            if (upper.Contains("MP4TT") || upper.Contains("TTXT"))
                return "MP4TT";
            if (upper.Contains("TXTT"))
                return "TXTT";
            if (upper.Contains("PGS") || upper.Contains("PGSSUB"))
                return "PGSSUB";
            if (upper.Contains("EIA-608") || upper.Contains("608"))
                return "EIA-608";
            if (upper.Contains("EIA-708") || upper.Contains("708"))
                return "EIA-708";

            return format;
        }

        private CompatibilityResult AnalyzeCompatibility(VideoInfo videoInfo)
        {
            // Use new media property-based rating system
            if (_mediaPropertyRatingService == null)
            {
                _logger?.LogError("MediaPropertyRatingService is not available. Cannot calculate compatibility rating.");
                // Return a default result with error
                return new CompatibilityResult
                {
                    CompatibilityRating = 0,
                    OverallScore = "Poor",
                    Issues = new List<string> { "Rating service not available" },
                    Recommendations = new List<string>()
                };
            }

            return _mediaPropertyRatingService.CalculateRating(videoInfo);
        }

    }
}

