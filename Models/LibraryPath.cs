using System;
using System.ComponentModel.DataAnnotations;

namespace Optimarr.Models
{
    public class LibraryPath
    {
        public int Id { get; set; }
        
        [Required]
        public string Path { get; set; } = string.Empty;
        
        public string Name { get; set; } = string.Empty;
        
        public string Category { get; set; } = "Misc"; // Movie, TV Shows, Misc
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastScannedAt { get; set; }
        
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
    }
}

