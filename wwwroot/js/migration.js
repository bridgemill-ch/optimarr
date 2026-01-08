// Database Migration Status Management
let migrationCheckInterval = null;

export async function checkMigrationStatus() {
    try {
        const response = await fetch('/api/system/migration');
        if (!response.ok) {
            // If endpoint doesn't exist or fails, hide banner
            hideMigrationBanner();
            return;
        }
        
        const progress = await response.json();
        
        // Don't show banner for "unknown" status (migration service hasn't started yet)
        if (progress.status === 'unknown') {
            hideMigrationBanner();
            return;
        }
        
        updateMigrationBanner(progress);
        
        // If migration is in progress, poll for updates
        if (progress.status === 'checking' || progress.status === 'creating' || progress.status === 'migrating') {
            if (!migrationCheckInterval) {
                migrationCheckInterval = setInterval(checkMigrationStatus, 1000); // Poll every second
            }
        } else {
            // Migration completed or error, stop polling
            if (migrationCheckInterval) {
                clearInterval(migrationCheckInterval);
                migrationCheckInterval = null;
            }
            
            // Hide banner after 5 seconds if completed (but not for errors)
            if (progress.status === 'completed') {
                setTimeout(() => {
                    hideMigrationBanner();
                }, 5000);
            }
            // Error status should persist - don't auto-hide
        }
    } catch (error) {
        console.error('Error checking migration status:', error);
        hideMigrationBanner();
    }
}

function updateMigrationBanner(progress) {
    const banner = document.getElementById('migrationBanner');
    const messageEl = document.getElementById('migrationMessage');
    const detailsEl = document.getElementById('migrationDetails');
    
    if (!banner || !messageEl) return;
    
    // Show banner
    banner.style.display = 'block';
    
    // Update message
    messageEl.textContent = progress.message || 'Checking database...';
    
    // Update details
    if (detailsEl) {
        let details = '';
        
        if (progress.pendingMigrations && progress.pendingMigrations.length > 0) {
            details += `Pending: ${progress.pendingMigrations.join(', ')}`;
        }
        
        if (progress.appliedMigrations && progress.appliedMigrations.length > 0) {
            if (details) details += ' | ';
            details += `Applied: ${progress.appliedMigrations.join(', ')}`;
        }
        
        // Show error details if available
        if (progress.status === 'error' && progress.error) {
            if (details) details += ' | ';
            details += `Error: ${progress.error}`;
        }
        
        if (details) {
            detailsEl.textContent = details;
            detailsEl.style.display = 'block';
        } else {
            detailsEl.style.display = 'none';
        }
    }
    
    // Update banner class based on status
    banner.className = 'migration-banner';
    if (progress.status === 'error') {
        banner.classList.add('error');
    } else if (progress.status === 'completed') {
        banner.classList.add('completed');
    }
    
    // Update icon based on status
    const iconEl = banner.querySelector('.migration-banner-icon');
    if (iconEl) {
        if (progress.status === 'completed') {
            iconEl.textContent = '✓';
            iconEl.style.animation = 'none';
        } else if (progress.status === 'error') {
            iconEl.textContent = '✗';
            iconEl.style.animation = 'none';
        } else {
            iconEl.textContent = '🔄';
            iconEl.style.animation = 'spin 2s linear infinite';
        }
    }
}

function hideMigrationBanner() {
    const banner = document.getElementById('migrationBanner');
    if (banner) {
        banner.style.display = 'none';
    }
    
    if (migrationCheckInterval) {
        clearInterval(migrationCheckInterval);
        migrationCheckInterval = null;
    }
}

// Export for potential manual refresh
export { checkMigrationStatus as refreshMigrationStatus };

