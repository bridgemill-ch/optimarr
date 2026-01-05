using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Optimarr.Models
{
    public class LibraryScan
    {
        public int Id { get; set; }
        
        [Required]
        public string LibraryPath { get; set; } = string.Empty;
        
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        
        public ScanStatus Status { get; set; } = ScanStatus.Pending;
        
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int FailedFiles { get; set; }
        
        public string? ErrorMessage { get; set; }
        
        public virtual ICollection<VideoAnalysis> VideoAnalyses { get; set; } = new List<VideoAnalysis>();
        public virtual ICollection<FailedFile> FailedFileRecords { get; set; } = new List<FailedFile>();
    }

    public enum ScanStatus
    {
        Pending,
        Running,
        Completed,
        Failed,
        Cancelled
    }
}

