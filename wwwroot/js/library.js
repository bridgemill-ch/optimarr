// Library Management Functions
import { escapeHtml, formatFileSize, formatTimeSpan, getCategoryIcon } from './utils.js';
import { loadBrowseMedia } from './browse.js';
import { loadDashboard } from './dashboard.js';

// Poll for processing count and update badge
let processingCountInterval = null;

export function startProcessingCountPolling() {
    // Clear existing interval if any
    if (processingCountInterval) {
        clearInterval(processingCountInterval);
    }
    
    // Poll immediately, then every 5 seconds
    updateProcessingCount();
    processingCountInterval = setInterval(updateProcessingCount, 5000);
}

export function stopProcessingCountPolling() {
    if (processingCountInterval) {
        clearInterval(processingCountInterval);
        processingCountInterval = null;
    }
}

async function updateProcessingCount() {
    try {
        const response = await fetch('/api/library/processing/count');
        if (!response.ok) return;
        
        const count = await response.json();
        const badge = document.getElementById('libraryProcessingBadge');
        
        if (badge) {
            if (count > 0) {
                badge.textContent = count;
                badge.style.display = 'inline-block';
            } else {
                badge.style.display = 'none';
            }
        }
    } catch (error) {
        console.error('Error updating processing count:', error);
    }
}

export async function loadProcessingVideos() {
    try {
        const response = await fetch('/api/library/processing/videos?page=1&pageSize=50');
        if (!response.ok) throw new Error('Failed to load processing videos');
        
        const data = await response.json();
        const container = document.getElementById('processingVideos');
        
        if (!container) return;
        
        // Update rescan all button visibility
        const rescanAllBtn = document.getElementById('rescanAllProcessingBtn');
        if (rescanAllBtn) {
            if (data.videos && data.videos.length > 0) {
                rescanAllBtn.style.display = 'inline-flex';
            } else {
                rescanAllBtn.style.display = 'none';
            }
        }
        
        if (!data.videos || data.videos.length === 0) {
            container.innerHTML = '<div class="empty-state"><p>No videos currently being redownloaded.</p></div>';
            return;
        }
        
        container.innerHTML = data.videos.map(video => {
            const startedAt = video.processingStartedAt 
                ? new Date(video.processingStartedAt).toLocaleString()
                : 'Unknown';
            const fileName = video.fileName || (video.filePath ? video.filePath.split(/[/\\]/).pop() : 'Unknown');
            const title = video.servarrType === 'Sonarr' 
                ? `${video.sonarrSeriesTitle} - S${String(video.sonarrSeasonNumber || 0).padStart(2, '0')}E${String(video.sonarrEpisodeNumber || 0).padStart(2, '0')}`
                : video.servarrType === 'Radarr'
                ? `${video.radarrMovieTitle}${video.radarrYear ? ` (${video.radarrYear})` : ''}`
                : fileName;
            
            return `
                <div class="processing-video-item">
                    <div class="processing-video-header">
                        <span class="processing-badge">⏳ Processing</span>
                        <strong>${escapeHtml(title)}</strong>
                        <div class="processing-video-actions">
                            <button class="btn btn-sm btn-secondary" onclick="rescanProcessingVideo(${video.id})" title="Rescan now">
                                <span class="icon">↻</span> Rescan
                            </button>
                            <button class="btn btn-sm btn-danger" onclick="deleteProcessingVideo(${video.id}, '${escapeHtml(title)}')" title="Delete">
                                <span class="icon">🗑</span> Delete
                            </button>
                        </div>
                    </div>
                    <div class="processing-video-details">
                        <div class="detail-row">
                            <span class="detail-label">File:</span>
                            <span class="detail-value">${escapeHtml(fileName)}</span>
                        </div>
                        <div class="detail-row">
                            <span class="detail-label">Started:</span>
                            <span class="detail-value">${startedAt}</span>
                        </div>
                        <div class="detail-row">
                            <span class="detail-label">Service:</span>
                            <span class="detail-value">${escapeHtml(video.servarrType || 'Unknown')}</span>
                        </div>
                    </div>
                </div>
            `;
        }).join('');
    } catch (error) {
        console.error('Error loading processing videos:', error);
        const container = document.getElementById('processingVideos');
        if (container) {
            container.innerHTML = `<div class="error-state"><p>Error loading processing videos: ${escapeHtml(error.message)}</p></div>`;
        }
    }
}

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

// Track active scan polling intervals
const activeScanIntervals = new Map();

export async function reconnectToRunningScans() {
    try {
        const response = await fetch('/api/library/scans');
        if (!response.ok) return;
        
        const scans = await response.json();
        const runningScans = scans.filter(scan => 
            scan.status === 'Running' || scan.status === 'Pending'
        );
        
        // Start polling for all running scans
        runningScans.forEach(scan => {
            if (!activeScanIntervals.has(scan.id)) {
                console.log(`Reconnecting to running scan: ${scan.id}`);
                startScanPolling(scan.id);
            }
        });
    } catch (error) {
        console.error('Error checking for running scans:', error);
    }
}

function createScanProgressElement(scanId, libraryPath) {
    const container = document.getElementById('scanProgressContainer');
    if (!container) return null;
    
    const scanElement = document.createElement('div');
    scanElement.className = 'scan-progress-item';
    scanElement.id = `scanProgressItem-${scanId}`;
    scanElement.innerHTML = `
        <div class="scan-progress-header">
            <h4 class="scan-progress-title">${escapeHtml(libraryPath)}</h4>
            <div class="scan-progress-header-actions">
                <span class="scan-progress-status" id="scanStatus-${scanId}">Running</span>
                <button class="btn btn-sm btn-danger scan-cancel-btn" id="cancelScanBtn-${scanId}" onclick="cancelScan(${scanId})" title="Cancel Scan">
                    <span class="icon">✕</span>
                </button>
            </div>
        </div>
        <div class="progress-container">
            <div class="progress-bar">
                <div id="scanProgressBar-${scanId}" class="progress-fill" style="width: 0%;"></div>
            </div>
            <div class="progress-text" id="scanProgressText-${scanId}">Initializing...</div>
        </div>
        <div class="scan-details" id="scanDetails-${scanId}">
            <div class="detail-row">
                <span class="detail-label">Current File:</span>
                <span id="currentFile-${scanId}" class="detail-value">-</span>
            </div>
            <div class="detail-row">
                <span class="detail-label">Speed:</span>
                <span id="scanSpeed-${scanId}" class="detail-value">-</span>
            </div>
            <div class="detail-row">
                <span class="detail-label">Elapsed:</span>
                <span id="scanElapsed-${scanId}" class="detail-value">-</span>
            </div>
            <div class="detail-row">
                <span class="detail-label">Remaining:</span>
                <span id="scanRemaining-${scanId}" class="detail-value">-</span>
            </div>
        </div>
    `;
    
    container.appendChild(scanElement);
    return scanElement;
}

function removeScanProgressElement(scanId) {
    const element = document.getElementById(`scanProgressItem-${scanId}`);
    if (element) {
        element.remove();
    }
    
    // Hide progress container if no more scans
    const container = document.getElementById('scanProgressContainer');
    const progressDiv = document.getElementById('scanProgress');
    if (container && progressDiv && container.children.length === 0) {
        progressDiv.style.display = 'none';
    }
}

export async function startScanPolling(scanId) {
    const progressDiv = document.getElementById('scanProgress');
    if (!progressDiv) return;
    
    // If already polling this scan, don't start again
    if (activeScanIntervals.has(scanId)) {
        return;
    }
    
    progressDiv.style.display = 'block';
    
    // Get scan info to create progress element
    let libraryPath = 'Unknown';
    try {
        const scanResponse = await fetch(`/api/library/scans/${scanId}`);
        if (scanResponse.ok) {
            const scan = await scanResponse.json();
            libraryPath = scan.libraryPath || 'Unknown';
        }
    } catch (error) {
        console.error('Error fetching scan info:', error);
    }
    
    // Create progress element for this scan
    const element = createScanProgressElement(scanId, libraryPath);
    if (!element) {
        console.error('Failed to create scan progress element');
        return;
    }
    
    const pollInterval = setInterval(async () => {
        try {
            const response = await fetch(`/api/library/scans/${scanId}`);
            if (!response.ok) {
                clearInterval(pollInterval);
                activeScanIntervals.delete(scanId);
                removeScanProgressElement(scanId);
                return;
            }
            
            const scan = await response.json();
            
            // Update status
            const statusEl = document.getElementById(`scanStatus-${scanId}`);
            const cancelBtn = document.getElementById(`cancelScanBtn-${scanId}`);
            if (statusEl) {
                const status = scan.status || 'Running';
                statusEl.textContent = status;
                statusEl.className = `scan-progress-status ${status.toLowerCase()}`;
                
                // Show/hide cancel button based on status
                if (cancelBtn) {
                    if (status === 'Running' || status === 'Pending') {
                        cancelBtn.style.display = 'inline-flex';
                    } else {
                        cancelBtn.style.display = 'none';
                    }
                }
            }
            
            // Update progress bar
            const progressEl = document.getElementById(`scanProgressBar-${scanId}`);
            const progressTextEl = document.getElementById(`scanProgressText-${scanId}`);
            if (progressEl) {
                if (scan.totalFiles > 0) {
                    const percent = Math.round((scan.processedFiles / scan.totalFiles) * 100);
                    progressEl.style.width = percent + '%';
                    if (progressTextEl) {
                        progressTextEl.textContent = `${scan.processedFiles} / ${scan.totalFiles} (${percent}%)`;
                    }
                } else {
                    // Ensure progress bar is visible even when totalFiles is 0
                    progressEl.style.width = '0%';
                    if (progressTextEl) {
                        progressTextEl.textContent = 'Initializing...';
                    }
                }
            }
            
            // Update detail fields
            const currentFileEl = document.getElementById(`currentFile-${scanId}`);
            if (currentFileEl) {
                const fileName = scan.currentFile || scan.lastAnalyzedFile;
                if (fileName && fileName !== 'Initializing...') {
                    const displayName = fileName.includes('/') || fileName.includes('\\') 
                        ? fileName.split(/[/\\]/).pop() 
                        : fileName;
                    currentFileEl.textContent = displayName;
                } else {
                    currentFileEl.textContent = fileName || '-';
                }
            }
            
            const speedEl = document.getElementById(`scanSpeed-${scanId}`);
            if (speedEl) {
                if (scan.filesPerSecond && scan.filesPerSecond > 0) {
                    speedEl.textContent = `${scan.filesPerSecond.toFixed(2)} files/sec`;
                } else {
                    speedEl.textContent = '-';
                }
            }
            
            const elapsedEl = document.getElementById(`scanElapsed-${scanId}`);
            if (elapsedEl) {
                elapsedEl.textContent = formatTimeSpan(scan.elapsedTime);
            }
            
            const remainingEl = document.getElementById(`scanRemaining-${scanId}`);
            if (remainingEl) {
                remainingEl.textContent = formatTimeSpan(scan.estimatedTimeRemaining);
            }
            
            // Handle scan completion or cancellation
            if (scan.status === 'Completed' || scan.status === 'Failed' || scan.status === 'Cancelled') {
                clearInterval(pollInterval);
                activeScanIntervals.delete(scanId);
                
                console.log(`Scan ${scanId} ${scan.status.toLowerCase()}, refreshing dashboard...`);
                
                // Update status to show completion
                if (statusEl) {
                    statusEl.textContent = scan.status;
                    statusEl.className = `scan-progress-status ${scan.status.toLowerCase()}`;
                }
                
                // Remove progress element after a delay
                setTimeout(() => {
                    removeScanProgressElement(scanId);
                    
                    // Refresh data
                    loadKnownLibraries();
                    loadRecentScans();
                    
                    const activeTab = document.querySelector('.nav-item.active');
                    if (activeTab && activeTab.getAttribute('data-tab') === 'browse') {
                        loadBrowseMedia();
                    }
                    
                    // Refresh dashboard
                    setTimeout(() => {
                        loadDashboard().catch(err => {
                            console.error('Failed to refresh dashboard:', err);
                        });
                    }, 2000);
                }, 3000);
            }
        } catch (error) {
            console.error(`Error polling scan ${scanId} status:`, error);
            clearInterval(pollInterval);
            activeScanIntervals.delete(scanId);
            removeScanProgressElement(scanId);
        }
    }, 2000); // Poll every 2 seconds
    
    activeScanIntervals.set(scanId, pollInterval);
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

export async function cancelScan(scanId) {
    if (!confirm('Are you sure you want to cancel this scan?')) {
        return;
    }
    
    try {
        const response = await fetch(`/api/library/scans/${scanId}/cancel`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to cancel scan');
        }
        
        // Update UI to show cancellation requested
        const statusEl = document.getElementById(`scanStatus-${scanId}`);
        const cancelBtn = document.getElementById(`cancelScanBtn-${scanId}`);
        
        if (statusEl) {
            statusEl.textContent = 'Cancelling...';
            statusEl.className = 'scan-progress-status cancelling';
        }
        
        if (cancelBtn) {
            cancelBtn.disabled = true;
            cancelBtn.style.opacity = '0.5';
        }
        
        console.log(`Scan ${scanId} cancellation requested`);
    } catch (error) {
        console.error('Cancel scan error:', error);
        alert(`Error cancelling scan: ${error.message}`);
    }
}

export async function rescanProcessingVideo(videoId) {
    if (!confirm('Are you sure you want to rescan this video now?\n\nThis will:\n- Clear the processing status\n- Re-analyze the video file\n- Update compatibility information')) {
        return;
    }
    
    try {
        const response = await fetch('/api/library/processing/videos/rescan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: [videoId] })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to rescan video');
        }
        
        const result = await response.json();
        alert(`Video rescanned successfully!`);
        
        // Reload processing videos list
        loadProcessingVideos();
        updateProcessingCount();
    } catch (error) {
        console.error('Error rescanning processing video:', error);
        alert(`Error rescanning video: ${error.message}`);
    }
}

export async function deleteProcessingVideo(videoId, videoTitle) {
    const confirmed = confirm(`Are you sure you want to delete "${videoTitle}"?\n\nThis will permanently remove the video from the database.\n\nThis action cannot be undone.`);
    
    if (!confirmed) {
        return;
    }
    
    try {
        const response = await fetch('/api/library/processing/videos/delete', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: [videoId] })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to delete video');
        }
        
        const result = await response.json();
        alert(`Video deleted successfully!`);
        
        // Reload processing videos list
        loadProcessingVideos();
        updateProcessingCount();
    } catch (error) {
        console.error('Error deleting processing video:', error);
        alert(`Error deleting video: ${error.message}`);
    }
}

export async function rescanAllProcessingVideos() {
    try {
        // First, get all processing video IDs
        const response = await fetch('/api/library/processing/videos/ids');
        if (!response.ok) throw new Error('Failed to load processing video IDs');
        
        const videoIds = await response.json();
        
        if (!videoIds || videoIds.length === 0) {
            alert('No processing videos to rescan.');
            return;
        }
        
        const count = videoIds.length;
        if (!confirm(`Are you sure you want to rescan all ${count} processing video(s)?\n\nThis will:\n- Clear the processing status for all videos\n- Re-analyze all video files\n- Update compatibility information\n\nThis may take a while.`)) {
            return;
        }
        
        const rescanAllBtn = document.getElementById('rescanAllProcessingBtn');
        if (rescanAllBtn) {
            rescanAllBtn.disabled = true;
            rescanAllBtn.innerHTML = '<span class="icon">↻</span> Rescanning...';
        }
        
        const rescanResponse = await fetch('/api/library/processing/videos/rescan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: videoIds })
        });
        
        if (!rescanResponse.ok) {
            const error = await rescanResponse.json();
            throw new Error(error.error || 'Failed to rescan videos');
        }
        
        const result = await rescanResponse.json();
        alert(`Rescan completed!\n\nTotal: ${result.total}\nSuccess: ${result.success}\nFailed: ${result.failed}`);
        
        // Reload processing videos list
        loadProcessingVideos();
        updateProcessingCount();
    } catch (error) {
        console.error('Error rescanning all processing videos:', error);
        alert(`Error rescanning videos: ${error.message}`);
    } finally {
        const rescanAllBtn = document.getElementById('rescanAllProcessingBtn');
        if (rescanAllBtn) {
            rescanAllBtn.disabled = false;
            rescanAllBtn.innerHTML = '<span class="icon">↻</span> Rescan All';
        }
    }
}

// Export to window for onclick handlers
window.rescanLibrary = rescanLibrary;
window.deleteLibrary = deleteLibrary;
window.cancelScan = cancelScan;
window.rescanProcessingVideo = rescanProcessingVideo;
window.deleteProcessingVideo = deleteProcessingVideo;
window.rescanAllProcessingVideos = rescanAllProcessingVideos;

