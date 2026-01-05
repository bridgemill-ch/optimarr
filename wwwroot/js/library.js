// Library Management Functions
import { escapeHtml, formatFileSize, formatTimeSpan, getCategoryIcon } from './utils.js';
import { loadBrowseMedia } from './browse.js';
import { loadDashboard } from './dashboard.js';

export async function loadKnownLibraries() {
    try {
        const response = await fetch('/api/library/paths');
        if (!response.ok) throw new Error('Failed to load libraries');
        
        const libraries = await response.json();
        const container = document.getElementById('knownLibraries');
        
        if (libraries.length === 0) {
            container.innerHTML = '<div class="empty-state"><p>No libraries added yet. Click the "+" button to add one.</p></div>';
            return;
        }
        
        container.innerHTML = libraries.map(lib => {
            const lastScanned = lib.lastScannedAt 
                ? new Date(lib.lastScannedAt).toLocaleString()
                : 'Never';
            const totalSize = formatFileSize(lib.totalSize || 0);
            const statusBadge = lib.latestScanStatus 
                ? `<span class="status-badge ${lib.latestScanStatus.toLowerCase()}">${lib.latestScanStatus}</span>`
                : '';
            const categoryIcon = getCategoryIcon(lib.category);
            const categoryBadge = lib.category 
                ? `<span class="category-badge category-${lib.category.toLowerCase().replace(/\s+/g, '-')}" title="Category: ${escapeHtml(lib.category)}">${escapeHtml(lib.category)}</span>`
                : '';
            
            return `
                <div class="library-card">
                    <div class="library-card-title-section">
                        <span class="library-category-icon" title="Category: ${escapeHtml(lib.category || 'Misc')}">${categoryIcon}</span>
                        <strong>${escapeHtml(lib.name || lib.path)}</strong>
                        ${categoryBadge}
                        ${statusBadge}
                    </div>
                    <div class="library-card-header">
                        <div class="library-card-actions">
                            <button class="btn btn-sm btn-secondary" onclick="rescanLibrary(${lib.id}, '${escapeHtml(lib.path)}')" title="Rescan">
                                <span class="icon">↻</span>
                            </button>
                            <button class="btn btn-sm btn-danger" onclick="deleteLibrary(${lib.id}, '${escapeHtml(lib.name || lib.path)}')" title="Delete">
                                <span class="icon">🗑</span>
                            </button>
                        </div>
                    </div>
                    <div class="library-card-body">
                        <div class="library-card-path">${escapeHtml(lib.path)}</div>
                        <div class="library-card-stats">
                            <div class="stat-item">
                                <span class="stat-label">Files:</span>
                                <span class="stat-value">${lib.totalFiles || 0}</span>
                            </div>
                            <div class="stat-item">
                                <span class="stat-label">Size:</span>
                                <span class="stat-value">${totalSize}</span>
                            </div>
                            <div class="stat-item">
                                <span class="stat-label">Last scanned:</span>
                                <span class="stat-value">${lastScanned}</span>
                            </div>
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    } catch (error) {
        console.error('Error loading libraries:', error);
        document.getElementById('knownLibraries').innerHTML = 
            `<div class="error-state"><p>Error loading libraries: ${escapeHtml(error.message)}</p></div>`;
    }
}

export async function rescanLibrary(libraryPathId, libraryPath) {
    try {
        const response = await fetch(`/api/library/rescan/${libraryPathId}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to start rescan');
        }
        
        const scan = await response.json();
        
        // Show scan progress
        startScanPolling(scan.id);
        
        // Reload libraries list
        loadKnownLibraries();
    } catch (error) {
        console.error('Rescan error:', error);
        alert(`Error starting rescan: ${error.message}`);
    }
}

export async function deleteLibrary(libraryPathId, libraryName) {
    const confirmed = confirm(`Are you sure you want to delete the library "${libraryName}"?\n\nThis will permanently delete:\n- The library configuration\n- All scan history\n- All video analysis data for this library\n\nThis action cannot be undone.`);
    
    if (!confirmed) {
        return;
    }
    
    try {
        const response = await fetch(`/api/library/paths/${libraryPathId}`, {
            method: 'DELETE',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to delete library');
        }
        
        // Reload libraries list
        loadKnownLibraries();
        
        // If we're on the Browse tab, reload media to reflect the deletion
        const activeTab = document.querySelector('.nav-item.active');
        if (activeTab && activeTab.getAttribute('data-tab') === 'browse') {
            loadBrowseMedia();
        }
    } catch (error) {
        console.error('Delete library error:', error);
        alert(`Error deleting library: ${error.message}`);
    }
}

export async function loadRecentScans() {
    try {
        const response = await fetch('/api/library/scans');
        if (!response.ok) throw new Error('Failed to load scans');
        
        const scans = await response.json();
        const container = document.getElementById('recentScans');
        if (!container) return;
        
        if (scans.length === 0) {
            container.innerHTML = '<div class="empty-state">No scans yet</div>';
            return;
        }
        
        container.innerHTML = scans.slice(0, 10).map(scan => {
            const statusClass = scan.status?.toLowerCase() || 'unknown';
            const startTime = scan.startedAt ? new Date(scan.startedAt).toLocaleString() : 'Unknown';
            return `
                <div class="scan-item">
                    <div class="scan-info">
                        <strong>${escapeHtml(scan.libraryPath || 'Unknown')}</strong>
                        <span class="scan-status ${statusClass}">${escapeHtml(scan.status || 'Unknown')}</span>
                    </div>
                    <div class="scan-details">
                        <span>Started: ${startTime}</span>
                        ${scan.processedFiles !== undefined ? `<span>Processed: ${scan.processedFiles}</span>` : ''}
                    </div>
                </div>
            `;
        }).join('');
    } catch (error) {
        console.error('Error loading scans:', error);
        const container = document.getElementById('recentScans');
        if (container) {
            container.innerHTML = '<div class="error-state">Error loading scans</div>';
        }
    }
}

export async function startScanPolling(scanId) {
    const progressDiv = document.getElementById('scanProgress');
    if (!progressDiv) return;
    
    progressDiv.style.display = 'block';
    
    const pollInterval = setInterval(async () => {
        try {
            const response = await fetch(`/api/library/scans/${scanId}`);
            if (!response.ok) {
                clearInterval(pollInterval);
                return;
            }
            
            const scan = await response.json();
            const statusEl = document.getElementById('scanStatus');
            const progressEl = document.getElementById('scanProgressBar');
            const currentFileEl = document.getElementById('currentFile');
            const speedEl = document.getElementById('scanSpeed');
            const elapsedEl = document.getElementById('scanElapsed');
            const remainingEl = document.getElementById('scanRemaining');
            
            if (statusEl) {
                statusEl.textContent = scan.status || 'Running';
            }
            
            if (progressEl && scan.totalFiles > 0) {
                const percent = Math.round((scan.processedFiles / scan.totalFiles) * 100);
                progressEl.style.width = percent + '%';
                progressEl.textContent = `${scan.processedFiles} / ${scan.totalFiles} (${percent}%)`;
            }
            
            // Update detail fields
            if (currentFileEl) {
                const fileName = scan.currentFile || scan.lastAnalyzedFile;
                if (fileName && fileName !== 'Initializing...') {
                    // Extract just the filename from the path if it's a full path
                    const displayName = fileName.includes('/') || fileName.includes('\\') 
                        ? fileName.split(/[/\\]/).pop() 
                        : fileName;
                    currentFileEl.textContent = displayName;
                } else {
                    currentFileEl.textContent = fileName || '-';
                }
            }
            
            if (speedEl) {
                if (scan.filesPerSecond && scan.filesPerSecond > 0) {
                    speedEl.textContent = `${scan.filesPerSecond.toFixed(2)} files/sec`;
                } else {
                    speedEl.textContent = '-';
                }
            }
            
            if (elapsedEl) {
                elapsedEl.textContent = formatTimeSpan(scan.elapsedTime);
            }
            
            if (remainingEl) {
                remainingEl.textContent = formatTimeSpan(scan.estimatedTimeRemaining);
            }
            
            if (scan.status === 'Completed' || scan.status === 'Failed') {
                clearInterval(pollInterval);
                console.log(`Scan ${scan.status.toLowerCase()}, refreshing dashboard...`);
                
                loadKnownLibraries();
                loadRecentScans();
                
                // Reload browse media if on browse tab
                const activeTab = document.querySelector('.nav-item.active');
                if (activeTab && activeTab.getAttribute('data-tab') === 'browse') {
                    loadBrowseMedia();
                }
                
                // Refresh dashboard to show updated stats
                // Add a delay to ensure database has been updated (increased to 2 seconds)
                setTimeout(() => {
                    console.log('Refreshing dashboard after scan completion...');
                    loadDashboard().catch(err => {
                        console.error('Failed to refresh dashboard:', err);
                    });
                }, 2000);
                
                // Also refresh again after a longer delay to catch any late database updates
                setTimeout(() => {
                    console.log('Secondary dashboard refresh after scan completion...');
                    loadDashboard().catch(err => {
                        console.error('Failed to refresh dashboard (secondary):', err);
                    });
                }, 5000);
                
                setTimeout(() => {
                    progressDiv.style.display = 'none';
                }, 5000);
            }
        } catch (error) {
            console.error('Error polling scan status:', error);
            clearInterval(pollInterval);
        }
    }, 2000); // Poll every 2 seconds
}

export function updateErrorsDisplay(failedFiles) {
    const errorsList = document.getElementById('errorsList');
    if (!errorsList) return;
    
    if (!failedFiles || failedFiles.length === 0) {
        errorsList.innerHTML = '<div class="empty-state">No errors</div>';
        return;
    }
    
    errorsList.innerHTML = failedFiles.map((file, index) => {
        const errorType = file.errorType || 'Unknown Error';
        const errorMsg = file.errorMessage || 'No error message';
        const fileName = file.fileName || file.filePath || 'Unknown file';
        
        return `
            <div class="error-item">
                <div class="error-header">
                    <span class="error-index">#${index + 1}</span>
                    <strong class="error-filename">${escapeHtml(fileName)}</strong>
                    <span class="error-type">${escapeHtml(errorType)}</span>
                </div>
                <div class="error-message">${escapeHtml(errorMsg)}</div>
            </div>
        `;
    }).join('');
}

// Export to window for onclick handlers
window.rescanLibrary = rescanLibrary;
window.deleteLibrary = deleteLibrary;

