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
        private Dictionary<string, CodecThresholds>? _codecThresholdsCache;

        public VideoAnalyzerService(IConfiguration? configuration = null, ILogger<VideoAnalyzerService>? logger = null)
        {
            _reportGenerator = new ReportGenerator();
            _logger = logger;
            _configuration = configuration;
        }
        
        // Read global thresholds dynamically from configuration
        private int GetOptimalDirectPlayThreshold()
        {
            return _configuration?.GetValue<int>("CompatibilityRating:OptimalDirectPlayThreshold") ?? 8;
        }
        
        private int GetGoodDirectPlayThreshold()
        {
            return _configuration?.GetValue<int>("CompatibilityRating:GoodDirectPlayThreshold") ?? 5;
        }
        
        private int GetGoodCombinedThreshold()
        {
            return _configuration?.GetValue<int>("CompatibilityRating:GoodCombinedThreshold") ?? 8;
        }

        private List<string> GetEnabledClients()
        {
            var enabledClients = new List<string>();
            var allClients = JellyfinCompatibilityData.AllClients;
            
            // Get disabled clients from configuration
            var disabledClientsSection = _configuration?.GetSection("CompatibilityRating:DisabledClients");
            var disabledClients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (disabledClientsSection != null && disabledClientsSection.Exists())
            {
                foreach (var client in allClients)
                {
                    var isDisabled = disabledClientsSection.GetValue<bool>(client);
                    if (isDisabled)
                    {
                        disabledClients.Add(client);
                    }
                }
            }
            
            // Return all clients except disabled ones
            foreach (var client in allClients)
            {
                if (!disabledClients.Contains(client))
                {
                    enabledClients.Add(client);
                }
            }
            
            // Ensure at least one client is enabled
            if (enabledClients.Count == 0)
            {
                _logger?.LogWarning("All clients are disabled, enabling all clients by default");
                return allClients.ToList();
            }
            
            return enabledClients;
        }

        private Dictionary<string, CodecThresholds> LoadCodecThresholds()
        {
            // Return cached version if available (codec support doesn't change)
            if (_codecThresholdsCache != null)
            {
                return _codecThresholdsCache;
            }
            
            var thresholds = new Dictionary<string, CodecThresholds>();
            
            // Get enabled clients for threshold calculations
            var enabledClients = GetEnabledClients();
            
            // Calculate expected Direct Play clients for each codec based on JellyfinCompatibilityData
            foreach (var codecEntry in JellyfinCompatibilityData.VideoCodecSupport)
            {
                var codecName = codecEntry.Key;
                var support = codecEntry.Value;
                
                // Count enabled clients with Supported level (can Direct Play)
                var expectedDirectPlay = enabledClients.Count(client => 
                    support.ContainsKey(client) && 
                    support[client] == JellyfinCompatibilityData.SupportLevel.Supported);
                var expectedRemux = enabledClients.Count(client => 
                    support.ContainsKey(client) && 
                    support[client] == JellyfinCompatibilityData.SupportLevel.Partial);
                var totalClients = enabledClients.Count;
                
                // Calculate thresholds based on expected support
                // Optimal: 80% of expected Direct Play clients
                // Good: 60% of expected Direct Play clients, or 80% of (Direct Play + Remux)
                var optimalThreshold = (int)Math.Ceiling(expectedDirectPlay * 0.8);
                var goodDirectThreshold = (int)Math.Ceiling(expectedDirectPlay * 0.6);
                var goodCombinedThreshold = (int)Math.Ceiling((expectedDirectPlay + expectedRemux) * 0.8);
                
                // Ensure minimum values
                optimalThreshold = Math.Max(1, optimalThreshold);
                goodDirectThreshold = Math.Max(1, goodDirectThreshold);
                goodCombinedThreshold = Math.Max(1, goodCombinedThreshold);
                
                // Try to load from configuration, otherwise use calculated defaults
                var configPath = $"CompatibilityRating:CodecThresholds:{codecName}";
                thresholds[codecName] = new CodecThresholds
                {
                    OptimalDirectPlayThreshold = _configuration?.GetValue<int>($"{configPath}:OptimalDirectPlayThreshold") ?? optimalThreshold,
                    GoodDirectPlayThreshold = _configuration?.GetValue<int>($"{configPath}:GoodDirectPlayThreshold") ?? goodDirectThreshold,
                    GoodCombinedThreshold = _configuration?.GetValue<int>($"{configPath}:GoodCombinedThreshold") ?? goodCombinedThreshold,
                    ExpectedDirectPlay = expectedDirectPlay,
                    ExpectedRemux = expectedRemux,
                    TotalClients = totalClients
                };
            }
            
            _codecThresholdsCache = thresholds;
            return thresholds;
        }

        private CodecThresholds GetThresholdsForCodec(string videoCodec, int bitDepth)
        {
            var codecThresholds = LoadCodecThresholds();
            
            // Try with bit depth first
            var codecKey = $"{videoCodec} {bitDepth}-bit";
            if (codecThresholds.ContainsKey(codecKey))
            {
                return codecThresholds[codecKey];
            }
            
            // Try without bit depth
            if (codecThresholds.ContainsKey(videoCodec))
            {
                return codecThresholds[videoCodec];
            }
            
            // Fallback to global thresholds (read dynamically)
            var enabledClients = GetEnabledClients();
            return new CodecThresholds
            {
                OptimalDirectPlayThreshold = GetOptimalDirectPlayThreshold(),
                GoodDirectPlayThreshold = GetGoodDirectPlayThreshold(),
                GoodCombinedThreshold = GetGoodCombinedThreshold(),
                ExpectedDirectPlay = 0,
                ExpectedRemux = 0,
                TotalClients = enabledClients.Count
            };
        }

        private class CodecThresholds
        {
            public int OptimalDirectPlayThreshold { get; set; }
            public int GoodDirectPlayThreshold { get; set; }
            public int GoodCombinedThreshold { get; set; }
            public int ExpectedDirectPlay { get; set; }
            public int ExpectedRemux { get; set; }
            public int TotalClients { get; set; }
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
                    
                    // Duration - try to get from Duration field (milliseconds) first, then Duration_String3 (formatted)
                    var durationMsStr = GetElementValue(generalTrack, "Duration", ns) 
                        ?? GetElementValue(generalTrack, "Duration", null);
                    var durationFormattedStr = GetElementValue(generalTrack, "Duration_String3", ns)
                        ?? GetElementValue(generalTrack, "Duration_String3", null);
                    
                    if (!string.IsNullOrEmpty(durationMsStr))
                    {
                        // Duration field is in milliseconds
                        if (double.TryParse(durationMsStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var durationMs))
                        {
                            videoInfo.Duration = durationMs / 1000.0;
                            _logger?.LogDebug("Extracted duration from General track Duration field: {DurationStr} -> {DurationMs} ms -> {Duration} seconds", durationMsStr, durationMs, videoInfo.Duration);
                        }
                        else
                        {
                            _logger?.LogWarning("Could not parse Duration field as number: {DurationStr}", durationMsStr);
                        }
                    }
                    
                    // If duration is still 0 or invalid, try parsing Duration_String3 (formatted time)
                    if (videoInfo.Duration <= 0 && !string.IsNullOrEmpty(durationFormattedStr))
                    {
                        var parsedDuration = ParseDurationString(durationFormattedStr);
                        if (parsedDuration > 0)
                        {
                            videoInfo.Duration = parsedDuration;
                            _logger?.LogDebug("Extracted duration from General track Duration_String3: {DurationStr} -> {Duration} seconds", durationFormattedStr, videoInfo.Duration);
                        }
                        else
                        {
                            _logger?.LogWarning("Could not parse Duration_String3: {DurationStr}", durationFormattedStr);
                        }
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
                            
                            if (!string.IsNullOrEmpty(videoDurationMsStr))
                            {
                                if (double.TryParse(videoDurationMsStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var durationMs))
                                {
                                    videoInfo.Duration = durationMs / 1000.0;
                                    _logger?.LogDebug("Extracted duration from Video track Duration field: {DurationStr} -> {DurationMs} ms -> {Duration} seconds", videoDurationMsStr, durationMs, videoInfo.Duration);
                                }
                            }
                            else if (!string.IsNullOrEmpty(videoDurationFormattedStr))
                            {
                                var parsedDuration = ParseDurationString(videoDurationFormattedStr);
                                if (parsedDuration > 0)
                                {
                                    videoInfo.Duration = parsedDuration;
                                    _logger?.LogDebug("Extracted duration from Video track Duration_String3: {DurationStr} -> {Duration} seconds", videoDurationFormattedStr, videoInfo.Duration);
                                }
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

            // Try parsing as time format HH:MM:SS or HH:MM:SS.mmm
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
            var result = new CompatibilityResult();
            var clientResults = new Dictionary<string, ClientCompatibility>();

            // Get compatibility overrides from configuration
            var overrides = GetCompatibilityOverrides();

            // Determine video codec key
            var videoCodecKey = $"{videoInfo.VideoCodec} {videoInfo.BitDepth}-bit";
            if (!JellyfinCompatibilityData.VideoCodecSupport.ContainsKey(videoCodecKey))
            {
                // Try without bit depth
                videoCodecKey = videoInfo.VideoCodec;
            }

            // Get enabled clients (filter out disabled ones)
            var enabledClients = GetEnabledClients();
            
            // Analyze each enabled client
            foreach (var client in enabledClients)
            {
                var clientResult = new ClientCompatibility();
                var issues = new List<string>();
                var warnings = new List<string>();

                // Check container support (with override)
                var containerSupport = GetSupportLevelWithOverride(
                    videoInfo.Container, 
                    client, 
                    "Container",
                    JellyfinCompatibilityData.ContainerSupport
                        .GetValueOrDefault(videoInfo.Container, new Dictionary<string, JellyfinCompatibilityData.SupportLevel>())
                        .GetValueOrDefault(client, JellyfinCompatibilityData.SupportLevel.Unsupported),
                    overrides);

                if (containerSupport == JellyfinCompatibilityData.SupportLevel.Unsupported)
                {
                    clientResult.Status = "Remux";
                    issues.Add($"{videoInfo.Container} container not supported");
                }
                else if (containerSupport == JellyfinCompatibilityData.SupportLevel.Partial)
                {
                    clientResult.Status = "Remux";
                    var warningMessage = $"{videoInfo.Container} container has partial support";
                    warnings.Add(warningMessage);
                    issues.Add(warningMessage); // Also add to issues for visibility
                }

                // Check video codec support (with override)
                var videoSupport = GetSupportLevelWithOverride(
                    videoCodecKey,
                    client,
                    "Video",
                    JellyfinCompatibilityData.VideoCodecSupport
                        .GetValueOrDefault(videoCodecKey, new Dictionary<string, JellyfinCompatibilityData.SupportLevel>())
                        .GetValueOrDefault(client, JellyfinCompatibilityData.SupportLevel.Unsupported),
                    overrides);

                if (videoSupport == JellyfinCompatibilityData.SupportLevel.Unsupported)
                {
                    clientResult.Status = "Transcode";
                    issues.Add($"{videoInfo.VideoCodec} {videoInfo.BitDepth}-bit video codec not supported");
                }
                else if (videoSupport == JellyfinCompatibilityData.SupportLevel.Partial)
                {
                    if (clientResult.Status == "Direct Play")
                        clientResult.Status = "Remux";
                    var warningMessage = $"{videoInfo.VideoCodec} {videoInfo.BitDepth}-bit has partial support";
                    warnings.Add(warningMessage);
                    issues.Add(warningMessage); // Also add to issues for visibility
                }

                // Special cases
                // iOS requires MP4/M4V/MOV container for H.265/HEVC (iPhone 7+)
                // If container is correct, iOS can direct play H.265 (including HDR on iPhone X+)
                if (videoInfo.VideoCodec == "H.265" && videoInfo.Container != "MP4" && videoInfo.Container != "M4V" && videoInfo.Container != "MOV")
                {
                    if (client == "iOS" || client == "SwiftFin")
                    {
                        clientResult.Status = "Transcode";
                        issues.Add("HEVC only supported in MP4/M4V/MOV containers on iOS");
                    }
                }
                
                // iOS also requires correct codec tag (hvc1) for H.265 in MP4
                // If codec tag is incorrect, it may still work but could cause issues
                if (videoInfo.VideoCodec == "H.265" && (videoInfo.Container == "MP4" || videoInfo.Container == "M4V" || videoInfo.Container == "MOV"))
                {
                    if (client == "iOS" && !string.IsNullOrEmpty(videoInfo.VideoCodecTag) && 
                        videoInfo.VideoCodecTag.ToUpperInvariant() != "HVC1" && videoInfo.VideoCodecTag.ToUpperInvariant() != "HEVC")
                    {
                        // Incorrect codec tag may cause issues, but iOS might still play it
                        warnings.Add($"iOS prefers hvc1 codec tag for HEVC in MP4 (found: {videoInfo.VideoCodecTag})");
                    }
                }

                // HDR is supported on iOS (iPhone X and newer) - don't downgrade to transcode
                if (videoInfo.IsHDR && client == "iOS")
                {
                    // HDR is supported, just add a warning about display requirements
                    warnings.Add($"HDR content ({videoInfo.HDRType}) requires HDR-capable iPhone (X or newer) for proper display");
                }
                else if (videoInfo.IsHDR)
                {
                    warnings.Add($"HDR content ({videoInfo.HDRType}) may require tone-mapping on non-HDR displays");
                }

                // Check audio codec support (with override)
                bool hasUnsupportedAudio = false;
                foreach (var audioTrack in videoInfo.AudioTracks)
                {
                    var audioSupport = GetSupportLevelWithOverride(
                        audioTrack.Codec,
                        client,
                        "Audio",
                        JellyfinCompatibilityData.AudioCodecSupport
                            .GetValueOrDefault(audioTrack.Codec, new Dictionary<string, JellyfinCompatibilityData.SupportLevel>())
                            .GetValueOrDefault(client, JellyfinCompatibilityData.SupportLevel.Unsupported),
                        overrides);

                    if (audioSupport == JellyfinCompatibilityData.SupportLevel.Unsupported)
                    {
                        hasUnsupportedAudio = true;
                        var warningMessage = $"{audioTrack.Codec} audio codec not supported";
                        issues.Add(warningMessage);
                        warnings.Add(warningMessage); // Also add to warnings for visibility
                    }
                    else if (audioSupport == JellyfinCompatibilityData.SupportLevel.Partial)
                    {
                        warnings.Add($"{audioTrack.Codec} audio has partial support");
                    }
                }

                if (hasUnsupportedAudio && clientResult.Status == "Direct Play")
                {
                    clientResult.Status = "Remux"; // Light transcoding for audio
                }

                // Check subtitle support
                // Note: External subtitles don't affect direct play compatibility - Jellyfin serves them separately
                // Only check embedded subtitles for compatibility issues
                foreach (var subtitleTrack in videoInfo.SubtitleTracks)
                {
                    // Skip external subtitles - they don't prevent direct play
                    if (!subtitleTrack.IsEmbedded)
                    {
                        continue;
                    }

                    var subtitleSupport = JellyfinCompatibilityData.SubtitleSupport
                        .GetValueOrDefault(subtitleTrack.Format, new Dictionary<string, JellyfinCompatibilityData.SupportLevel>())
                        .GetValueOrDefault(videoInfo.Container, JellyfinCompatibilityData.SupportLevel.Unsupported);

                    if (subtitleSupport == JellyfinCompatibilityData.SupportLevel.Unsupported)
                    {
                        var warningMessage = $"{subtitleTrack.Format} subtitles in {videoInfo.Container} may require burn-in";
                        warnings.Add(warningMessage);
                        issues.Add(warningMessage); // Also add to issues for visibility in non-optimal fields
                    }
                    else if (subtitleSupport == JellyfinCompatibilityData.SupportLevel.Partial)
                    {
                        var warningMessage = $"{subtitleTrack.Format} subtitles have partial support";
                        warnings.Add(warningMessage);
                        issues.Add(warningMessage); // Also add to issues for visibility
                    }
                }

                // Set final status
                if (clientResult.Status == "Unknown" && issues.Count == 0)
                {
                    clientResult.Status = "Direct Play";
                }
                else if (clientResult.Status == "Unknown")
                {
                    clientResult.Status = "Transcode";
                }

                clientResult.Reason = issues.Count > 0 ? string.Join("; ", issues) : "All components supported";
                clientResult.Warnings = warnings;
                clientResults[client] = clientResult;
            }

            result.ClientResults = clientResults;

            // Calculate overall score using codec-specific thresholds
            var directPlayCount = clientResults.Values.Count(r => r.Status == "Direct Play");
            var remuxCount = clientResults.Values.Count(r => r.Status == "Remux");
            var transcodeCount = clientResults.Values.Count(r => r.Status == "Transcode");
            var totalClients = clientResults.Count;

            // Get codec-specific thresholds
            var thresholds = GetThresholdsForCodec(videoInfo.VideoCodec, videoInfo.BitDepth);

            // Use direct play count as the rating
            result.CompatibilityRating = directPlayCount;

            // Use codec-specific thresholds for text labels
            if (directPlayCount >= thresholds.OptimalDirectPlayThreshold)
                result.OverallScore = "Optimal";
            else if (directPlayCount >= thresholds.GoodDirectPlayThreshold || (directPlayCount + remuxCount) >= thresholds.GoodCombinedThreshold)
                result.OverallScore = "Good";
            else
                result.OverallScore = "Poor";

            // Generate issues and recommendations
            GenerateIssuesAndRecommendations(videoInfo, result);

            return result;
        }

        private void GenerateIssuesAndRecommendations(VideoInfo videoInfo, CompatibilityResult result)
        {
            var issues = new List<string>();
            var recommendations = new List<string>();

            // Container issues
            if (videoInfo.Container == "MKV")
            {
                issues.Add("MKV container may require remuxing on Chrome, Firefox, and Safari");
                recommendations.Add("Consider using MP4 container for broader compatibility");
            }

            // Video codec issues
            if (videoInfo.VideoCodec == "H.265" && videoInfo.BitDepth == 10)
            {
                issues.Add("H.265 10-bit has limited client support");
                recommendations.Add("Re-encode to H.264 8-bit for maximum compatibility");
            }
            else if (videoInfo.VideoCodec == "H.265" && videoInfo.BitDepth == 8)
            {
                issues.Add("H.265 8-bit has partial support on many clients");
                recommendations.Add("Consider H.264 8-bit for better compatibility");
            }

            if (videoInfo.VideoCodec == "AV1")
            {
                issues.Add("AV1 codec has limited support, especially on iOS and Roku");
                recommendations.Add("Use H.264 or H.265 for better compatibility");
            }

            // HDR issues
            if (videoInfo.IsHDR)
            {
                issues.Add($"HDR content ({videoInfo.HDRType}) requires client/display support or server tone-mapping");
                recommendations.Add("Consider SDR version for clients without HDR support");
            }

            // Audio issues - check for codecs that have compatibility problems
            var problematicAudio = videoInfo.AudioTracks.Where(a => 
                a.Codec == "DTS" || a.Codec == "ALAC" || a.Codec == "AC3" || a.Codec == "EAC3").ToList();
            if (problematicAudio.Any())
            {
                var codecList = string.Join(", ", problematicAudio.Select(a => a.Codec));
                var specificIssues = new List<string>();
                
                if (problematicAudio.Any(a => a.Codec == "DTS" || a.Codec == "ALAC"))
                {
                    specificIssues.Add($"{string.Join(", ", problematicAudio.Where(a => a.Codec == "DTS" || a.Codec == "ALAC").Select(a => a.Codec))} audio requires transcoding on web browsers");
                }
                
                if (problematicAudio.Any(a => a.Codec == "AC3" || a.Codec == "EAC3"))
                {
                    specificIssues.Add($"{string.Join(", ", problematicAudio.Where(a => a.Codec == "AC3" || a.Codec == "EAC3").Select(a => a.Codec))} audio codec not supported on some clients (e.g., Roku, Firefox)");
                }
                
                if (specificIssues.Any())
                {
                    issues.AddRange(specificIssues);
                }
                else
                {
                    issues.Add($"{codecList} audio may have compatibility issues");
                }
                
                recommendations.Add("Use AAC audio for universal compatibility");
            }

            // Subtitle issues
            // Only flag embedded SRT/VTT in MP4 - external SRT/VTT files work fine with MP4
            if (videoInfo.Container == "MP4" && videoInfo.SubtitleTracks.Any(s => 
                (s.Format == "SRT" || s.Format == "VTT") && s.IsEmbedded))
            {
                issues.Add("Embedded SRT/VTT subtitles are not supported in MP4 container");
                recommendations.Add("Use MP4TT/TXTT subtitles in MP4, or switch to MKV container, or use external SRT/VTT files");
            }

            if (videoInfo.SubtitleTracks.Any(s => (s.Format == "ASS" || s.Format == "SSA") && videoInfo.Container != "MKV"))
            {
                issues.Add("ASS/SSA subtitles only work in MKV container");
                recommendations.Add("Use SRT or VTT subtitles, or switch to MKV container");
            }

            // Fast start optimization check (MP4 only)
            // Note: Fast start doesn't affect direct play compatibility - it's just a streaming optimization
            // Only add as a recommendation, not an issue
            if ((videoInfo.Container == "MP4" || videoInfo.Container == "M4V" || videoInfo.Container == "MOV") && !videoInfo.IsFastStart)
            {
                recommendations.Add("MP4 file is not optimized for streaming (missing fast start). Use FFmpeg with -movflags +faststart to optimize for streaming");
            }

            // Codec tag validation (MP4 only)
            if ((videoInfo.Container == "MP4" || videoInfo.Container == "M4V" || videoInfo.Container == "MOV") && !videoInfo.IsCodecTagCorrect && !string.IsNullOrEmpty(videoInfo.VideoCodecTag))
            {
                var expectedTag = GetExpectedCodecTag(videoInfo.VideoCodec);
                if (!string.IsNullOrEmpty(expectedTag))
                {
                    issues.Add($"Incorrect codec tag '{videoInfo.VideoCodecTag}' for {videoInfo.VideoCodec} in MP4 (should be {expectedTag})");
                    recommendations.Add($"Use FFmpeg with -tag:v {expectedTag.ToLowerInvariant()} to fix the codec tag");
                }
            }

            result.Issues = issues;
            result.Recommendations = recommendations;
        }

        /// <summary>
        /// Gets the expected codec tag for a given video codec.
        /// </summary>
        private string GetExpectedCodecTag(string videoCodec)
        {
            if (string.IsNullOrEmpty(videoCodec))
                return string.Empty;

            var codecUpper = videoCodec.ToUpperInvariant();

            if (codecUpper.Contains("H.265") || codecUpper.Contains("HEVC"))
                return "hvc1";

            if (codecUpper.Contains("H.264") || codecUpper.Contains("AVC"))
                return "avc1";

            if (codecUpper.Contains("VP9"))
                return "vp09";

            if (codecUpper.Contains("AV1"))
                return "av01";

            return string.Empty;
        }

        /// <summary>
        /// Gets compatibility overrides from configuration.
        /// </summary>
        private List<CompatibilityOverride> GetCompatibilityOverrides()
        {
            if (_configuration == null)
                return new List<CompatibilityOverride>();

            try
            {
                var overrides = _configuration.GetSection("CompatibilityOverrides").Get<List<CompatibilityOverride>>();
                return overrides ?? new List<CompatibilityOverride>();
            }
            catch
            {
                return new List<CompatibilityOverride>();
            }
        }

        /// <summary>
        /// Gets support level with override applied if exists.
        /// </summary>
        private JellyfinCompatibilityData.SupportLevel GetSupportLevelWithOverride(
            string codec,
            string client,
            string category,
            JellyfinCompatibilityData.SupportLevel defaultLevel,
            List<CompatibilityOverride> overrides)
        {
            // Check for override
            var overrideItem = overrides.FirstOrDefault(o => 
                o.Codec.Equals(codec, StringComparison.OrdinalIgnoreCase) &&
                o.Client.Equals(client, StringComparison.OrdinalIgnoreCase) &&
                o.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

            if (overrideItem != null)
            {
                // Parse override support level
                if (Enum.TryParse<JellyfinCompatibilityData.SupportLevel>(overrideItem.SupportLevel, out var overrideLevel))
                {
                    return overrideLevel;
                }
            }

            return defaultLevel;
        }
    }
}

