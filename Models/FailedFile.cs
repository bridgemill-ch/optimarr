using System;
using System.ComponentModel.DataAnnotations;

namespace Optimarr.Models
{
    public class FailedFile
    {
        public int Id { get; set; }
        
        public int LibraryScanId { get; set; }
        public virtual LibraryScan? LibraryScan { get; set; }
        
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        public string? FileName { get; set; }
        
        [Required]
        public string ErrorMessage { get; set; } = string.Empty;
        
        public string? ErrorType { get; set; }
        
        public DateTime FailedAt { get; set; } = DateTime.UtcNow;
        
        public long? FileSize { get; set; }
    }
}

