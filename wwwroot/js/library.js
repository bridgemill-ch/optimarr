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
            <span class="scan-progress-status" id="scanStatus-${scanId}">Running</span>
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
    createScanProgressElement(scanId, libraryPath);
    
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
            if (statusEl) {
                statusEl.textContent = scan.status || 'Running';
                statusEl.className = `scan-progress-status ${(scan.status || 'Running').toLowerCase()}`;
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
            
            // Handle scan completion
            if (scan.status === 'Completed' || scan.status === 'Failed') {
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

// Export to window for onclick handlers
window.rescanLibrary = rescanLibrary;
window.deleteLibrary = deleteLibrary;

