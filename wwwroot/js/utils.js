// Utility Functions
export function escapeHtml(text) {
    if (text == null) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

export function formatDuration(seconds) {
    if (!seconds) return '-';
    const hours = Math.floor(seconds / 3600);
    const minutes = Math.floor((seconds % 3600) / 60);
    const secs = Math.floor(seconds % 60);
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

