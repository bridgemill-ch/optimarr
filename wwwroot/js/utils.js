// Utility Functions
export function escapeHtml(text) {
    if (text == null) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

export function formatDuration(seconds) {
    if (!seconds || seconds <= 0) return '-';
    
    let durationSeconds = seconds;
    
    // Detect if value is likely in milliseconds instead of seconds
    // The backend should send duration in seconds, but some values might be in milliseconds
    
    // If value is extremely large (more than 100 days in seconds = 8,640,000),
    // it's definitely in milliseconds - convert it
    if (seconds > 8640000) {
        durationSeconds = seconds / 1000.0;
    }
    // If value is >= 1000, check if it makes more sense as milliseconds
    // Heuristic: if dividing by 1000 gives a reasonable video duration
    // (1 second to 10 hours), assume it's in milliseconds
    else if (seconds >= 1000) {
        const asSecondsFromMs = seconds / 1000.0;
        // Convert if the value as milliseconds gives a reasonable duration (1s to 10 hours = 36000s)
        // This catches cases like:
        // - 120000 → 120 seconds (2 min) ✓
        // - 7200000 → 7200 seconds (2 hours) ✓
        // - 3600000 → 3600 seconds (1 hour) ✓
        // But won't convert:
        // - 7200 → 7.2 seconds (7200 seconds = 2 hours is more reasonable)
        if (asSecondsFromMs >= 1 && asSecondsFromMs <= 36000) {
            // Additional check: if original value as seconds would be > 1 hour,
            // it's very likely milliseconds (most videos aren't > 1 hour when measured in seconds incorrectly)
            if (seconds > 3600 || (asSecondsFromMs >= 60 && seconds >= 60000)) {
                durationSeconds = asSecondsFromMs;
            }
        }
    }
    
    const hours = Math.floor(durationSeconds / 3600);
    const minutes = Math.floor((durationSeconds % 3600) / 60);
    const secs = Math.floor(durationSeconds % 60);
    
    if (hours > 0) {
        return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
    }
    return `${minutes}:${secs.toString().padStart(2, '0')}`;
}

export function formatFileSize(bytes) {
    if (bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
}

export function formatTimeSpan(timeSpan) {
    if (!timeSpan) return '-';
    
    let totalSeconds = 0;
    
    // Handle TimeSpan object from C# (has totalSeconds property)
    if (typeof timeSpan === 'object' && timeSpan.totalSeconds !== undefined) {
        totalSeconds = timeSpan.totalSeconds;
    }
    // Handle number (seconds)
    else if (typeof timeSpan === 'number') {
        totalSeconds = timeSpan;
    }
    // Handle string format
    else if (typeof timeSpan === 'string') {
        // Try to parse as TimeSpan string (e.g., "1.02:30:45" or "00:05:30")
        const parts = timeSpan.split(':');
        if (parts.length === 3) {
            const days = parts[0].split('.');
            const dayPart = days.length > 1 ? parseInt(days[0]) || 0 : 0;
            const hourPart = parseInt(days[days.length - 1]) || 0;
            const minPart = parseInt(parts[1]) || 0;
            const secPart = parseInt(parts[2]) || 0;
            totalSeconds = dayPart * 86400 + hourPart * 3600 + minPart * 60 + secPart;
        } else {
            return timeSpan;
        }
    } else {
        return String(timeSpan);
    }
    
    // Format as duration
    return formatDuration(totalSeconds);
}

export function getCategoryIcon(category) {
    if (!category) return '📁';
    const catLower = category.toLowerCase().trim();
    
    // Exact matches for form categories (check these first)
    if (catLower === 'movies' || catLower === 'movie') return '🎬';
    if (catLower === 'tv shows' || catLower === 'tv' || catLower === 'shows' || catLower === 'show') return '📺';
    if (catLower === 'misc' || catLower === 'miscellaneous') return '📁';
    
    // Partial matches for flexibility (check after exact matches)
    if (catLower.includes('movie')) return '🎬';
    if (catLower.includes('tv') || catLower.includes('show') || catLower.includes('series')) return '📺';
    if (catLower.includes('music') || catLower.includes('audio')) return '🎵';
    if (catLower.includes('photo') || catLower.includes('image') || catLower.includes('picture')) return '📷';
    if (catLower.includes('document') || catLower.includes('book')) return '📄';
    if (catLower.includes('game')) return '🎮';
    if (catLower.includes('anime')) return '🎌';
    if (catLower.includes('sport')) return '⚽';
    if (catLower.includes('kid') || catLower.includes('children')) return '🧸';
    
    // Default
    return '📁';
}

// Helper function to determine rating category based on rating value (0-100 scale)
// Uses default thresholds: Optimal ≥80, Good ≥60, Poor <60
export function getRatingCategory(rating) {
    if (rating >= 80) return 'optimal';
    if (rating >= 60) return 'good';
    return 'poor';
}

// Helper function to extract title from filename (without extension)
export function getTitleFromFileName(fileName) {
    if (!fileName || fileName === 'Unknown') return 'Unknown';
    try {
        // Get filename without extension
        const lastDotIndex = fileName.lastIndexOf('.');
        if (lastDotIndex === -1 || lastDotIndex === 0) {
            // No extension found, or filename starts with dot (hidden file)
            return fileName;
        }
        const title = fileName.substring(0, lastDotIndex);
        return title || fileName; // Return original if title is empty
    } catch (e) {
        console.warn('Error extracting title from filename:', fileName, e);
        return fileName; // Fallback to original if parsing fails
    }
}

