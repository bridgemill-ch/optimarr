// Playback History Functions
import { escapeHtml, formatTimeSpan } from './utils.js';
import { showMediaInfo } from './media-info.js';

export let playbackCurrentPage = 1;

export async function loadPlaybackHistory() {
    const libraryId = document.getElementById('playbackLibraryFilter')?.value || '';
    const playMethod = document.getElementById('playbackMethodFilter')?.value || '';
    const deviceName = document.getElementById('playbackDeviceFilter')?.value || '';
    const clientName = document.getElementById('playbackClientFilter')?.value || '';
    const userName = document.getElementById('playbackUserFilter')?.value || '';
    const search = document.getElementById('playbackSearch')?.value || '';
    const sortBy = document.getElementById('playbackSortBy')?.value || 'playbackStartTime';
    const sortOrder = document.getElementById('playbackSortOrder')?.value || 'desc';
    const grid = document.getElementById('playbackHistoryGrid');
    
    if (grid) {
        grid.innerHTML = '<div class="loading-placeholder">Loading playback history...</div>';
    }
    
    try {
        const params = new URLSearchParams({
            page: playbackCurrentPage,
            pageSize: 24
        });
        if (libraryId) params.append('libraryPathId', libraryId);
        if (playMethod === 'transcode') params.append('isTranscode', 'true');
        if (playMethod === 'directplay') params.append('isDirectPlay', 'true');
        if (playMethod === 'directstream') params.append('isDirectStream', 'true');
        if (deviceName) params.append('deviceName', deviceName);
        if (clientName) params.append('clientName', clientName);
        if (userName) params.append('userName', userName);
        if (search) params.append('search', search);
        if (sortBy) params.append('sortBy', sortBy);
        if (sortOrder) params.append('sortOrder', sortOrder);
        
        const response = await fetch(`/api/playback/history?${params}`);
        if (!response.ok) throw new Error(`Failed to load playback history: ${response.status}`);
        
        const result = await response.json();
        
        if (!result.items || result.items.length === 0) {
            if (grid) {
                grid.innerHTML = '<div class="empty-state"><p>No playback history found. Try adjusting your filters.</p></div>';
            }
            return;
        }
        
        if (grid) {
            grid.innerHTML = result.items.map(playback => {
                const playMethodClass = playback.isDirectPlay ? 'directplay' : 
                                       playback.isDirectStream ? 'directstream' : 
                                       'transcode';
                const playMethodLabel = playback.isDirectPlay ? 'Direct Play' : 
                                       playback.isDirectStream ? 'Direct Stream' : 
                                       'Transcode';
                const playMethodIcon = playback.isDirectPlay ? '✓' : 
                                      playback.isDirectStream ? '~' : 
                                      '✗';
                
                const startTime = new Date(playback.playbackStartTime);
                const duration = playback.playbackDuration ? formatTimeSpan(playback.playbackDuration) : '-';
                const dateStr = startTime.toLocaleDateString();
                const timeStr = startTime.toLocaleTimeString();
                
                return `
                    <div class="playback-list-item" ${playback.videoAnalysisId ? `onclick="showMediaInfo(${playback.videoAnalysisId})" style="cursor: pointer;"` : ''}>
                        <div class="playback-list-item-main">
                            <div class="playback-list-item-title">
                                <span class="playback-method-badge ${playMethodClass}" title="${playMethodLabel}">${playMethodIcon}</span>
                                <span class="playback-item-name" title="${escapeHtml(playback.itemName || 'Unknown')}">${escapeHtml(playback.itemName || 'Unknown')}</span>
                                ${playback.isTranscode && playback.transcodeReason ? `<span class="transcode-reason-badge" title="Transcode Reason: ${escapeHtml(playback.transcodeReason)}">${escapeHtml(playback.transcodeReason)}</span>` : ''}
                                ${playback.videoAnalysisId ? `<span class="playback-linked-badge" title="Linked to video analysis">✓</span>` : ''}
                            </div>
                            <div class="playback-list-item-meta">
                                <span class="playback-meta-item">
                                    <span class="playback-meta-label">${dateStr}</span>
                                    <span class="playback-meta-value">${timeStr}</span>
                                </span>
                                <span class="playback-meta-separator">•</span>
                                <span class="playback-meta-item">
                                    <span class="playback-meta-label">${escapeHtml(playback.clientName || 'Unknown')}</span>
                                </span>
                                <span class="playback-meta-separator">•</span>
                                <span class="playback-meta-item">
                                    <span class="playback-meta-label">${escapeHtml(playback.deviceName || 'Unknown')}</span>
                                </span>
                                ${playback.userName ? `
                                <span class="playback-meta-separator">•</span>
                                <span class="playback-meta-item">
                                    <span class="playback-meta-label">${escapeHtml(playback.userName)}</span>
                                </span>
                                ` : ''}
                                <span class="playback-meta-separator">•</span>
                                <span class="playback-meta-item">
                                    <span class="playback-meta-label">${duration}</span>
                                </span>
                                ${playback.libraryPathName ? `
                                <span class="playback-meta-separator">•</span>
                                <span class="playback-meta-item">
                                    <span class="playback-meta-label">${escapeHtml(playback.libraryPathName)}</span>
                                </span>
                                ` : ''}
                            </div>
                        </div>
                    </div>
                `;
            }).join('');
        }
        
        // Update pagination
        updatePlaybackPagination(result);
        
        // Load filter options if not already loaded
        loadPlaybackFilterOptions();
    } catch (error) {
        console.error('Error loading playback history:', error);
        if (grid) {
            grid.innerHTML = `<div class="error-state"><p>Error loading playback history: ${escapeHtml(error.message)}</p></div>`;
        }
    }
}

export function updatePlaybackPagination(result) {
    const pagination = document.getElementById('playbackPagination');
    if (!pagination) return;
    
    if (result.totalPages <= 1) {
        pagination.innerHTML = '';
        return;
    }
    
    let html = '';
    if (result.page > 1) {
        html += `<button class="btn btn-secondary" onclick="window.setPlaybackPage(${result.page - 1}); window.loadPlaybackHistory();">Previous</button>`;
    }
    html += `<span>Page ${result.page} of ${result.totalPages}</span>`;
    if (result.page < result.totalPages) {
        html += `<button class="btn btn-secondary" onclick="window.setPlaybackPage(${result.page + 1}); window.loadPlaybackHistory();">Next</button>`;
    }
    pagination.innerHTML = html;
}

export async function loadPlaybackFilterOptions() {
    try {
        // Load libraries
        const libraryResponse = await fetch('/api/library/paths');
        if (libraryResponse.ok) {
            const libraries = await libraryResponse.json();
            const librarySelect = document.getElementById('playbackLibraryFilter');
            if (librarySelect && librarySelect.querySelectorAll('option').length <= 1) {
                libraries.forEach(lib => {
                    const option = document.createElement('option');
                    option.value = lib.id;
                    option.textContent = lib.name || lib.path;
                    librarySelect.appendChild(option);
                });
            }
        }
        
        // Load devices and clients from playback history
        const historyResponse = await fetch('/api/playback/history?pageSize=1000');
        if (historyResponse.ok) {
            const history = await historyResponse.json();
            const devices = new Set();
            const clients = new Set();
            const users = new Set();
            
            history.items.forEach(item => {
                if (item.deviceName) devices.add(item.deviceName);
                if (item.clientName) clients.add(item.clientName);
                if (item.userName) users.add(item.userName);
            });
            
            // Populate device filter
            const deviceSelect = document.getElementById('playbackDeviceFilter');
            if (deviceSelect && deviceSelect.querySelectorAll('option').length <= 1) {
                Array.from(devices).sort().forEach(device => {
                    const option = document.createElement('option');
                    option.value = device;
                    option.textContent = device;
                    deviceSelect.appendChild(option);
                });
            }
            
            // Populate client filter
            const clientSelect = document.getElementById('playbackClientFilter');
            if (clientSelect && clientSelect.querySelectorAll('option').length <= 1) {
                Array.from(clients).sort().forEach(client => {
                    const option = document.createElement('option');
                    option.value = client;
                    option.textContent = client;
                    clientSelect.appendChild(option);
                });
            }
            
            // Populate user filter
            const userSelect = document.getElementById('playbackUserFilter');
            if (userSelect && userSelect.querySelectorAll('option').length <= 1) {
                Array.from(users).sort().forEach(user => {
                    const option = document.createElement('option');
                    option.value = user;
                    option.textContent = user;
                    userSelect.appendChild(option);
                });
            }
        }
    } catch (error) {
        console.error('Error loading playback filter options:', error);
    }
}

export function setupPlaybackEventListeners() {
    const searchInput = document.getElementById('playbackSearch');
    if (searchInput) {
        let searchTimeout;
        searchInput.addEventListener('input', () => {
            clearTimeout(searchTimeout);
            searchTimeout = setTimeout(() => {
                playbackCurrentPage = 1;
                loadPlaybackHistory();
            }, 500);
        });
    }
    
    const filterSelects = ['playbackLibraryFilter', 'playbackMethodFilter', 'playbackDeviceFilter', 
                          'playbackClientFilter', 'playbackUserFilter', 'playbackSortBy', 'playbackSortOrder'];
    filterSelects.forEach(id => {
        const select = document.getElementById(id);
        if (select) {
            select.addEventListener('change', () => {
                playbackCurrentPage = 1;
                loadPlaybackHistory();
            });
        }
    });
}

export function setPlaybackPage(page) {
    playbackCurrentPage = page;
}

export async function loadPlaybackDashboard() {
    try {
        const response = await fetch('/api/playback/statistics');
        if (!response.ok) return;
        
        const stats = await response.json();
        const dashboard = document.getElementById('playbackDashboard');
        if (!dashboard) return;
        
        const directPlayPercent = stats.total > 0 ? Math.round((stats.directPlay / stats.total) * 100) : 0;
        const directStreamPercent = stats.total > 0 ? Math.round((stats.directStream / stats.total) * 100) : 0;
        const transcodePercent = stats.total > 0 ? Math.round((stats.transcode / stats.total) * 100) : 0;
        
        dashboard.innerHTML = `
            <div class="stats-row">
                <div class="stat-box">
                    <div class="stat-icon">📊</div>
                    <div class="stat-info">
                        <div class="stat-label">Total Playbacks</div>
                        <div class="stat-value">${stats.total.toLocaleString()}</div>
                    </div>
                </div>
                <div class="stat-box">
                    <div class="stat-icon">✓</div>
                    <div class="stat-info">
                        <div class="stat-label">Direct Play</div>
                        <div class="stat-value">${stats.directPlay.toLocaleString()} (${directPlayPercent}%)</div>
                    </div>
                </div>
                <div class="stat-box">
                    <div class="stat-icon">~</div>
                    <div class="stat-info">
                        <div class="stat-label">Direct Stream</div>
                        <div class="stat-value">${stats.directStream.toLocaleString()} (${directStreamPercent}%)</div>
                    </div>
                </div>
                <div class="stat-box">
                    <div class="stat-icon">✗</div>
                    <div class="stat-info">
                        <div class="stat-label">Transcode</div>
                        <div class="stat-value">${stats.transcode.toLocaleString()} (${transcodePercent}%)</div>
                    </div>
                </div>
            </div>
            ${stats.transcodeReasons && stats.transcodeReasons.length > 0 ? `
            <div class="content-box" style="margin-top: 1.5rem;">
                <div class="box-header">
                    <h3>Top Transcode Reasons</h3>
                </div>
                <div class="box-content">
                    <div class="transcode-reasons-list">
                        ${stats.transcodeReasons.map(reason => `
                            <div class="transcode-reason-item">
                                <span class="reason-text">${escapeHtml(reason.reason || 'Unknown')}</span>
                                <span class="reason-count">${reason.count}</span>
                            </div>
                        `).join('')}
                    </div>
                </div>
            </div>
            ` : ''}
            ${stats.byClient && stats.byClient.length > 0 ? `
            <div class="content-box" style="margin-top: 1.5rem;">
                <div class="box-header">
                    <h3>Top Clients</h3>
                </div>
                <div class="box-content">
                    <div class="client-list">
                        ${stats.byClient.map(client => `
                            <div class="client-item">
                                <span class="client-name">${escapeHtml(client.client || 'Unknown')}</span>
                                <span class="client-count">${client.count}</span>
                            </div>
                        `).join('')}
                    </div>
                </div>
            </div>
            ` : ''}
        `;
    } catch (error) {
        console.error('Error loading playback dashboard:', error);
    }
}

// Export to window for onclick handlers
window.loadPlaybackHistory = loadPlaybackHistory;
window.setPlaybackPage = setPlaybackPage;

