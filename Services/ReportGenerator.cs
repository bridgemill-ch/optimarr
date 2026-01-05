using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Optimarr.Models;

namespace Optimarr.Services
{
    public class ReportGenerator
    {
        public string GenerateReport(VideoInfo videoInfo, CompatibilityResult compatibilityResult)
        {
            var report = new StringBuilder();

            // Header
            report.AppendLine("=".PadRight(80, '='));
            report.AppendLine("MEDIA OPTIMIZATION ANALYSIS REPORT");
            report.AppendLine("=".PadRight(80, '='));
            report.AppendLine();

            // File Information
            report.AppendLine("FILE INFORMATION");
            report.AppendLine("-".PadRight(80, '-'));
            report.AppendLine($"File: {System.IO.Path.GetFileName(videoInfo.FilePath)}");
            report.AppendLine($"Path: {videoInfo.FilePath}");
            report.AppendLine($"Size: {FormatFileSize(videoInfo.FileSize)}");
            report.AppendLine($"Duration: {FormatDuration(videoInfo.Duration)}");
            report.AppendLine();

            // Container
            report.AppendLine("CONTAINER");
            report.AppendLine("-".PadRight(80, '-'));
            report.AppendLine($"Format: {videoInfo.Container}");
            report.AppendLine();

            // Video Information
            report.AppendLine("VIDEO");
            report.AppendLine("-".PadRight(80, '-'));
            report.AppendLine($"Codec: {videoInfo.VideoCodec}");
            report.AppendLine($"Bit Depth: {videoInfo.BitDepth}-bit");
            report.AppendLine($"Resolution: {videoInfo.Width}x{videoInfo.Height}");
            report.AppendLine($"Frame Rate: {videoInfo.FrameRate:F2} fps");
            if (!string.IsNullOrEmpty(videoInfo.VideoProfile))
            {
                report.AppendLine($"Profile: {videoInfo.VideoProfile}");
            }
            if (videoInfo.IsHDR)
            {
                report.AppendLine($"HDR: {videoInfo.HDRType}");
            }
            report.AppendLine();

            // Audio Information
            report.AppendLine("AUDIO");
            report.AppendLine("-".PadRight(80, '-'));
            if (videoInfo.AudioTracks.Count == 0)
            {
                report.AppendLine("No audio tracks found");
            }
            else
            {
                for (int i = 0; i < videoInfo.AudioTracks.Count; i++)
                {
                    var track = videoInfo.AudioTracks[i];
                    report.AppendLine($"Track {i + 1}:");
                    report.AppendLine($"  Codec: {track.Codec}");
                    report.AppendLine($"  Channels: {track.Channels}");
                    report.AppendLine($"  Sample Rate: {track.SampleRate} Hz");
                    report.AppendLine($"  Language: {track.Language}");
                    if (track.Bitrate > 0)
                    {
                        report.AppendLine($"  Bitrate: {track.Bitrate} kbps");
                    }
                }
            }
            report.AppendLine();

            // Subtitle Information
            report.AppendLine("SUBTITLES");
            report.AppendLine("-".PadRight(80, '-'));
            if (videoInfo.SubtitleTracks.Count == 0)
            {
                report.AppendLine("No subtitle tracks found");
            }
            else
            {
                for (int i = 0; i < videoInfo.SubtitleTracks.Count; i++)
                {
                    var track = videoInfo.SubtitleTracks[i];
                    report.AppendLine($"Track {i + 1}:");
                    report.AppendLine($"  Format: {track.Format}");
                    report.AppendLine($"  Type: {(track.IsEmbedded ? "Embedded" : "External")}");
                    report.AppendLine($"  Language: {track.Language}");
                    if (!track.IsEmbedded && !string.IsNullOrEmpty(track.FilePath))
                    {
                        report.AppendLine($"  File: {System.IO.Path.GetFileName(track.FilePath)}");
                    }
                }
            }
            report.AppendLine();

            // Overall Score
            report.AppendLine("OVERALL COMPATIBILITY SCORE");
            report.AppendLine("-".PadRight(80, '-'));
            var scoreSymbol = compatibilityResult.OverallScore switch
            {
                "Optimal" => "✓",
                "Good" => "~",
                "Poor" => "✗",
                _ => "?"
            };
            report.AppendLine($"{scoreSymbol} {compatibilityResult.OverallScore}");
            report.AppendLine();
            report.AppendLine($"Direct Play: {compatibilityResult.ClientResults.Values.Count(r => r.Status == "Direct Play")} clients");
            report.AppendLine($"Remux: {compatibilityResult.ClientResults.Values.Count(r => r.Status == "Remux")} clients");
            report.AppendLine($"Transcode: {compatibilityResult.ClientResults.Values.Count(r => r.Status == "Transcode")} clients");
            report.AppendLine();

            // Per-Client Breakdown
            report.AppendLine("PER-CLIENT COMPATIBILITY");
            report.AppendLine("-".PadRight(80, '-'));
            foreach (var client in JellyfinCompatibilityData.AllClients)
            {
                if (compatibilityResult.ClientResults.TryGetValue(client, out var clientResult))
                {
                    var statusSymbol = clientResult.Status switch
                    {
                        "Direct Play" => "✓",
                        "Remux" => "~",
                        "Transcode" => "✗",
                        _ => "?"
                    };
                    report.AppendLine($"{statusSymbol} {client}: {clientResult.Status}");
                    if (!string.IsNullOrEmpty(clientResult.Reason) && clientResult.Reason != "All components supported")
                    {
                        report.AppendLine($"    Reason: {clientResult.Reason}");
                    }
                    foreach (var warning in clientResult.Warnings)
                    {
                        report.AppendLine($"    Warning: {warning}");
                    }
                }
            }
            report.AppendLine();

            // Issues
            if (compatibilityResult.Issues.Count > 0)
            {
                report.AppendLine("IDENTIFIED ISSUES");
                report.AppendLine("-".PadRight(80, '-'));
                foreach (var issue in compatibilityResult.Issues)
                {
                    report.AppendLine($"✗ {issue}");
                }
                report.AppendLine();
            }

            // Recommendations
            if (compatibilityResult.Recommendations.Count > 0)
            {
                report.AppendLine("RECOMMENDATIONS");
                report.AppendLine("-".PadRight(80, '-'));
                foreach (var recommendation in compatibilityResult.Recommendations)
                {
                    report.AppendLine($"• {recommendation}");
                }
                report.AppendLine();
            }

            // Footer
            report.AppendLine("=".PadRight(80, '='));
            report.AppendLine("Report generated by Optimarr");
            report.AppendLine("For more information, visit: https://jellyfin.org/docs/general/clients/codec-support/");
            report.AppendLine("=".PadRight(80, '='));

            return report.ToString();
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string FormatDuration(double seconds)
        {
            if (seconds < 60)
                return $"{seconds:F1} seconds";
            
            var minutes = (int)(seconds / 60);
            var secs = (int)(seconds % 60);
            
            if (minutes < 60)
                return $"{minutes}m {secs}s";
            
            var hours = minutes / 60;
            minutes = minutes % 60;
            return $"{hours}h {minutes}m {secs}s";
        }
    }
}

