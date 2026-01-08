using System.Collections.Concurrent;

namespace Optimarr.Services
{
    public class VideoMatchingProgress
    {
        public string Status { get; set; } = "running"; // running, completed, error
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Matched { get; set; }
        public int Errors { get; set; }
        public string? CurrentItem { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
    }

    public class VideoMatchingProgressService
    {
        private readonly ConcurrentDictionary<string, VideoMatchingProgress> _progress = new();
        private readonly ILogger<VideoMatchingProgressService> _logger;

        public VideoMatchingProgressService(ILogger<VideoMatchingProgressService> logger)
        {
            _logger = logger;
        }

        public string CreateProgress(string matchId)
        {
            var progress = new VideoMatchingProgress();
            _progress[matchId] = progress;
            _logger.LogDebug("Created progress tracker for match {MatchId}", matchId);
            return matchId;
        }

        public VideoMatchingProgress? GetProgress(string matchId)
        {
            if (_progress.TryGetValue(matchId, out var progress))
            {
                // Create a snapshot to avoid returning a reference that might be modified
                lock (progress)
                {
                    return new VideoMatchingProgress
                    {
                        Status = progress.Status,
                        Total = progress.Total,
                        Processed = progress.Processed,
                        Matched = progress.Matched,
                        Errors = progress.Errors,
                        CurrentItem = progress.CurrentItem,
                        ErrorMessage = progress.ErrorMessage,
                        StartTime = progress.StartTime,
                        EndTime = progress.EndTime
                    };
                }
            }
            return null;
        }

        public void UpdateProgress(string matchId, int processed, int total, int matched, int errors, string? currentItem = null)
        {
            if (_progress.TryGetValue(matchId, out var progress))
            {
                // Update atomically to prevent flickering
                lock (progress)
                {
                    progress.Processed = processed;
                    progress.Total = total;
                    progress.Matched = matched;
                    progress.Errors = errors;
                    progress.CurrentItem = currentItem;
                    progress.Status = "running";
                }
            }
        }

        public void CompleteProgress(string matchId, int matched, int errors)
        {
            if (_progress.TryGetValue(matchId, out var progress))
            {
                lock (progress)
                {
                    progress.Status = "completed";
                    progress.Matched = matched;
                    progress.Errors = errors;
                    progress.EndTime = DateTime.UtcNow;
                }
                _logger.LogDebug("Completed progress tracker for match {MatchId}: {Matched} matched, {Errors} errors", 
                    matchId, matched, errors);
            }
        }

        public void FailProgress(string matchId, string errorMessage)
        {
            if (_progress.TryGetValue(matchId, out var progress))
            {
                lock (progress)
                {
                    progress.Status = "error";
                    progress.ErrorMessage = errorMessage;
                    progress.EndTime = DateTime.UtcNow;
                }
                _logger.LogWarning("Failed progress tracker for match {MatchId}: {Error}", matchId, errorMessage);
            }
        }

        public void RemoveProgress(string matchId)
        {
            _progress.TryRemove(matchId, out _);
            _logger.LogDebug("Removed progress tracker for match {MatchId}", matchId);
        }

        // Get all active (running) matches
        public List<VideoMatchingProgress> GetActiveMatches()
        {
            return _progress
                .Where(kvp => kvp.Value.Status == "running")
                .Select(kvp =>
                {
                    lock (kvp.Value)
                    {
                        return new VideoMatchingProgress
                        {
                            Status = kvp.Value.Status,
                            Total = kvp.Value.Total,
                            Processed = kvp.Value.Processed,
                            Matched = kvp.Value.Matched,
                            Errors = kvp.Value.Errors,
                            CurrentItem = kvp.Value.CurrentItem,
                            ErrorMessage = kvp.Value.ErrorMessage,
                            StartTime = kvp.Value.StartTime,
                            EndTime = kvp.Value.EndTime
                        };
                    }
                })
                .ToList();
        }

        // Get match ID by status (for finding active matches)
        public string? GetActiveMatchId()
        {
            foreach (var kvp in _progress)
            {
                lock (kvp.Value)
                {
                    if (kvp.Value.Status == "running")
                    {
                        return kvp.Key;
                    }
                }
            }
            return null;
        }

        // Cleanup old completed progress (older than 1 hour)
        public void CleanupOldProgress()
        {
            var cutoff = DateTime.UtcNow.AddHours(-1);
            var toRemove = _progress
                .Where(kvp => kvp.Value.Status != "running" && 
                             (kvp.Value.EndTime ?? kvp.Value.StartTime) < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var matchId in toRemove)
            {
                RemoveProgress(matchId);
            }

            if (toRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} old progress trackers", toRemove.Count);
            }
        }
    }
}
