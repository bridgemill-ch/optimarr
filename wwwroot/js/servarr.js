// Servarr Integration Functions
import { escapeHtml } from './utils.js';

// Check for active matches on page load and resume polling
export async function checkActiveMatches() {
    try {
        // First check localStorage for stored matchId
        const storedMatchId = localStorage.getItem('activeMatchId');
        
        if (storedMatchId) {
            // Verify the match is still active on the server
            const response = await fetch(`/api/servarr/match-videos/progress/${storedMatchId}`);
            if (response.ok) {
                const progress = await response.json();
                if (progress.status === 'running') {
                    // Match is still active, resume polling
                    console.log('Resuming active match:', storedMatchId);
                    const button = document.querySelector('button[onclick="matchVideosWithServarr()"]');
                    const statusSpan = document.getElementById('matchVideosStatus') || 
                                     document.getElementById('matchVideosStatusSonarr') || 
                                     document.getElementById('matchVideosStatusRadarr');
                    const originalButtonText = button ? button.textContent.trim() : 'Match Videos';
                    
                    if (button) {
                        button.disabled = true;
                        button.style.opacity = '0.7';
                        const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
                        if (statusSpanInButton) {
                            statusSpanInButton.textContent = '🔄';
                        } else {
                            button.textContent = 'Matching...';
                        }
                    }
                    
                    // Resume polling
                    pollMatchProgress(storedMatchId, statusSpan, button, originalButtonText);
                    return;
                } else {
                    // Match completed or errored, clear localStorage
                    localStorage.removeItem('activeMatchId');
                }
            } else {
                // Match not found, clear localStorage
                localStorage.removeItem('activeMatchId');
            }
        }
        
        // Also check server for any active matches (in case localStorage was cleared)
        const activeResponse = await fetch('/api/servarr/match-videos/active');
        if (activeResponse.ok) {
            const activeData = await activeResponse.json();
            if (activeData.hasActiveMatch && activeData.activeMatchId) {
                // Store the active matchId
                localStorage.setItem('activeMatchId', activeData.activeMatchId);
                
                // Resume polling
                const button = document.querySelector('button[onclick="matchVideosWithServarr()"]');
                const statusSpan = document.getElementById('matchVideosStatus') || 
                                 document.getElementById('matchVideosStatusSonarr') || 
                                 document.getElementById('matchVideosStatusRadarr');
                const originalButtonText = button ? button.textContent.trim() : 'Match Videos';
                
                if (button) {
                    button.disabled = true;
                    button.style.opacity = '0.7';
                    const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
                    if (statusSpanInButton) {
                        statusSpanInButton.textContent = '🔄';
                    } else {
                        button.textContent = 'Matching...';
                    }
                }
                
                pollMatchProgress(activeData.activeMatchId, statusSpan, button, originalButtonText);
            }
        }
    } catch (error) {
        console.error('Error checking for active matches:', error);
        // Clear localStorage on error to avoid getting stuck
        localStorage.removeItem('activeMatchId');
    }
}

export async function loadServarrStatus() {
    try {
        // Add timeout to fetch
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 5000); // 5 second timeout
        
        const response = await fetch('/api/servarr/status', {
            signal: controller.signal
        });
        clearTimeout(timeoutId);
        
        if (!response.ok) {
            throw new Error(`Failed to fetch status: ${response.status} ${response.statusText}`);
        }

        const status = await response.json();
        
        // Update status display in settings
        updateServarrStatusDisplay(status);
    } catch (error) {
        console.error('Error loading Servarr status:', error);
        // Set status to unknown on error
        const radarrStatus = document.getElementById('radarrStatusText');
        const sonarrStatus = document.getElementById('sonarrStatusText');
        if (radarrStatus) radarrStatus.textContent = 'Unknown';
        if (sonarrStatus) sonarrStatus.textContent = 'Unknown';
    }
    
    // Load Jellyfin status separately
    loadJellyfinStatus();
}

function updateServarrStatusDisplay(status) {
    const radarrStatus = document.getElementById('radarrStatusText');
    const sonarrStatus = document.getElementById('sonarrStatusText');
    
    if (radarrStatus && status.radarr) {
        radarrStatus.className = 'status-badge';
        if (!status.radarr.enabled) {
            radarrStatus.textContent = 'Disabled';
            radarrStatus.classList.add('disabled');
        } else if (status.radarr.connected) {
            radarrStatus.textContent = `Connected (${status.radarr.version || 'Unknown'})`;
            radarrStatus.classList.add('connected');
        } else {
            radarrStatus.textContent = 'Disconnected';
            radarrStatus.classList.add('disconnected');
        }
    }
    
    if (sonarrStatus && status.sonarr) {
        sonarrStatus.className = 'status-badge';
        if (!status.sonarr.enabled) {
            sonarrStatus.textContent = 'Disabled';
            sonarrStatus.classList.add('disabled');
        } else if (status.sonarr.connected) {
            sonarrStatus.textContent = `Connected (${status.sonarr.version || 'Unknown'})`;
            sonarrStatus.classList.add('connected');
        } else {
            sonarrStatus.textContent = 'Disconnected';
            sonarrStatus.classList.add('disconnected');
        }
    }
}

export function updateStatusBadge(elementId, serviceStatus) {
    const badge = document.getElementById(elementId);
    if (!badge) return;

    badge.className = 'status-badge';
    
    if (!serviceStatus.enabled) {
        badge.textContent = 'Disabled';
        badge.classList.add('disabled');
    } else if (serviceStatus.connected) {
        badge.textContent = `Connected (${serviceStatus.version || 'Unknown'})`;
        badge.classList.add('connected');
    } else {
        badge.textContent = 'Disconnected';
        badge.classList.add('disconnected');
    }
}

// Test Connection Functions
export async function testJellyfinConnection() {
    const button = event?.target?.closest('button') || document.querySelector('button[onclick="testJellyfinConnection()"]');
    const statusSpan = document.getElementById('jellyfinTestStatus');
    const baseUrl = document.getElementById('jellyfinUrl').value;
    const apiKey = document.getElementById('jellyfinApiKey').value;
    const username = document.getElementById('jellyfinUsername').value;
    const password = document.getElementById('jellyfinPassword').value;

    if (!baseUrl) {
        alert('Please enter Base URL before testing');
        return;
    }

    if (!apiKey && (!username || !password)) {
        alert('Please enter either API Key or Username/Password before testing');
        return;
    }

    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';

    try {
        const response = await fetch('/api/playback/test', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ baseUrl, apiKey, username, password })
        });

        const result = await response.json();
        
        if (result.success) {
            if (statusSpan) statusSpan.textContent = '✓ ';
            alert(`Connection successful!${result.version ? `\nVersion: ${result.version}` : ''}`);
        } else {
            if (statusSpan) statusSpan.textContent = '✗ ';
            alert(`Connection failed: ${result.message}`);
        }
    } catch (error) {
        if (statusSpan) statusSpan.textContent = '✗ ';
        alert(`Error testing connection: ${error.message}`);
    } finally {
        if (button) {
            button.disabled = false;
            button.style.opacity = '1';
        }
        setTimeout(() => {
            if (statusSpan) statusSpan.textContent = '';
        }, 3000);
    }
}

export async function loadJellyfinStatus() {
    try {
        const response = await fetch('/api/playback/status');
        if (!response.ok) return;
        
        const status = await response.json();
        updateJellyfinStatusBadge(status.enabled, status.connected);
    } catch (error) {
        console.error('Error loading Jellyfin status:', error);
        updateJellyfinStatusBadge(false, false);
    }
}

export function updateJellyfinStatusBadge(enabled, connected) {
    const badge = document.getElementById('jellyfinStatus');
    if (!badge) return;

    badge.className = 'status-badge';
    
    if (!enabled) {
        badge.textContent = 'Disabled';
        badge.classList.add('disabled');
    } else if (connected) {
        badge.textContent = 'Connected';
        badge.classList.add('connected');
    } else {
        badge.textContent = 'Disconnected';
        badge.classList.add('disconnected');
    }
}

export async function loadJellyfinClients() {
    try {
        const response = await fetch('/api/playback/clients/used');
        if (!response.ok) {
            // If endpoint fails or no data, hide the section
            const box = document.getElementById('jellyfinClientsBox');
            if (box) box.style.display = 'none';
            return;
        }
        
        // Check if response is actually JSON
        const contentType = response.headers.get('content-type');
        if (!contentType || !contentType.includes('application/json')) {
            console.warn('Expected JSON response but got:', contentType);
            const box = document.getElementById('jellyfinClientsBox');
            if (box) box.style.display = 'none';
            return;
        }
        
        let data;
        try {
            data = await response.json();
        } catch (jsonError) {
            console.error('Error parsing JSON response:', jsonError);
            // If JSON parsing fails, the response might be HTML (error page)
            const box = document.getElementById('jellyfinClientsBox');
            if (box) box.style.display = 'none';
            return;
        }
        
        const container = document.getElementById('jellyfinClients');
        const box = document.getElementById('jellyfinClientsBox');
        
        if (!container || !box) return;
        
        // If no clients found, hide the section
        if (!data.clients || data.clients.length === 0) {
            box.style.display = 'none';
            return;
        }
        
        // Show the section
        box.style.display = 'block';
        
        if (data.clients.length === 0) {
            container.innerHTML = '<p style="color: var(--text-secondary);">No Jellyfin clients found. Sync playback history to see client information.</p>';
            return;
        }
        
        // Group by client name for better display
        const clientsByName = {};
        data.clients.forEach(client => {
            if (!clientsByName[client.clientName]) {
                clientsByName[client.clientName] = {
                    name: client.clientName,
                    devices: [],
                    totalPlaybacks: 0,
                    totalDirectPlay: 0,
                    totalDirectStream: 0,
                    totalTranscode: 0,
                    lastUsed: null
                };
            }
            clientsByName[client.clientName].devices.push({
                deviceName: client.deviceName,
                playbackCount: client.playbackCount,
                directPlayCount: client.directPlayCount,
                directStreamCount: client.directStreamCount,
                transcodeCount: client.transcodeCount,
                lastUsed: client.lastUsed
            });
            clientsByName[client.clientName].totalPlaybacks += client.playbackCount;
            clientsByName[client.clientName].totalDirectPlay += client.directPlayCount;
            clientsByName[client.clientName].totalDirectStream += client.directStreamCount;
            clientsByName[client.clientName].totalTranscode += client.transcodeCount;
            if (!clientsByName[client.clientName].lastUsed || 
                (client.lastUsed && new Date(client.lastUsed) > new Date(clientsByName[client.clientName].lastUsed))) {
                clientsByName[client.clientName].lastUsed = client.lastUsed;
            }
        });
        
        // Sort by total playbacks
        const sortedClients = Object.values(clientsByName)
            .sort((a, b) => b.totalPlaybacks - a.totalPlaybacks);
        
        let html = '<div class="clients-list">';
        sortedClients.forEach(client => {
            const directPlayPercent = client.totalPlaybacks > 0 
                ? Math.round((client.totalDirectPlay / client.totalPlaybacks) * 100) 
                : 0;
            const transcodePercent = client.totalPlaybacks > 0 
                ? Math.round((client.totalTranscode / client.totalPlaybacks) * 100) 
                : 0;
            
            const lastUsedDate = client.lastUsed ? new Date(client.lastUsed).toLocaleDateString() : 'Never';
            const deviceCount = client.devices.length;
            
            html += `
                <div class="client-card">
                    <div class="client-header">
                        <div class="client-name">
                            <strong>${escapeHtml(client.name)}</strong>
                            ${deviceCount > 1 ? `<span class="device-count">${deviceCount} devices</span>` : ''}
                        </div>
                        <div class="client-stats-summary">
                            <span class="stat-badge">${client.totalPlaybacks} playbacks</span>
                            <span class="stat-badge direct-play">${directPlayPercent}% Direct Play</span>
                            ${transcodePercent > 0 ? `<span class="stat-badge transcode">${transcodePercent}% Transcode</span>` : ''}
                        </div>
                    </div>
                    ${deviceCount > 1 ? `
                    <div class="client-devices">
                        ${client.devices.map(device => {
                            const deviceDirectPlayPercent = device.playbackCount > 0 
                                ? Math.round((device.directPlayCount / device.playbackCount) * 100) 
                                : 0;
                            return `
                                <div class="device-item">
                                    <span class="device-name">${escapeHtml(device.deviceName || 'Unknown Device')}</span>
                                    <span class="device-stats">
                                        ${device.playbackCount} playbacks • ${deviceDirectPlayPercent}% Direct Play
                                    </span>
                                </div>
                            `;
                        }).join('')}
                    </div>
                    ` : ''}
                    <div class="client-footer">
                        <small style="color: var(--text-secondary);">Last used: ${lastUsedDate}</small>
                    </div>
                </div>
            `;
        });
        html += '</div>';
        
        container.innerHTML = html;
    } catch (error) {
        console.error('Error loading Jellyfin clients:', error);
        const container = document.getElementById('jellyfinClients');
        const box = document.getElementById('jellyfinClientsBox');
        if (container) {
            container.innerHTML = '<p style="color: var(--text-secondary);">Unable to load client information.</p>';
        }
        if (box) {
            box.style.display = 'none';
        }
    }
}

export function toggleSyncDaysInput() {
    const syncAllCheckbox = document.getElementById('syncAllHistory');
    const daysInput = document.getElementById('syncDays');
    const helpText = document.getElementById('syncHelpText');
    
    if (syncAllCheckbox && daysInput && helpText) {
        if (syncAllCheckbox.checked) {
            daysInput.disabled = true;
            daysInput.style.opacity = '0.5';
            helpText.textContent = 'Import all historical playback data from Jellyfin. This may take a while for large libraries.';
        } else {
            daysInput.disabled = false;
            daysInput.style.opacity = '1';
            const days = daysInput.value || 30;
            helpText.textContent = `Import playback history from the last ${days} days. Check "Sync All" to import complete history.`;
        }
    }
}

export async function syncJellyfinPlayback(event) {
    const button = event?.target?.closest('button') || document.querySelector('button[onclick="syncJellyfinPlayback(event)"]');
    const statusSpan = document.getElementById('jellyfinSyncStatus');
    const syncAllCheckbox = document.getElementById('syncAllHistory');
    const daysInput = document.getElementById('syncDays');
    
    const syncAll = syncAllCheckbox?.checked || false;
    const days = syncAll ? null : (daysInput ? parseInt(daysInput.value) || 30 : 30);

    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';

    try {
        const url = syncAll 
            ? '/api/playback/sync'
            : `/api/playback/sync?days=${days}`;
            
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to sync playback history');
        }

        const result = await response.json();
        const syncId = result.syncId;
        
        if (!syncId) {
            // Fallback to old behavior if no syncId
            if (statusSpan) statusSpan.textContent = '✓ ';
            const syncType = syncAll ? 'all historical data' : `last ${days} days`;
            alert(`Playback history synced successfully!\n\nSynced: ${result.synced} records\nMatched: ${result.matched} with local libraries\nTotal: ${result.total} records found\n\nSynced: ${syncType}`);
            
            // Reload Jellyfin clients to show updated data
            loadJellyfinClients();
            
            // Reload playback dashboard if on playback tab
            const activeTab = document.querySelector('.nav-item.active');
            if (activeTab && activeTab.getAttribute('data-tab') === 'playback') {
                const { loadPlaybackDashboard, loadPlaybackHistory } = await import('./playback.js');
                loadPlaybackDashboard();
                loadPlaybackHistory();
            }
            
            if (button) {
                button.disabled = false;
                button.style.opacity = '1';
            }
            setTimeout(() => {
                if (statusSpan) statusSpan.textContent = '';
            }, 5000);
            return;
        }
        
        // Poll for progress
        await pollSyncProgress(syncId, statusSpan, button, syncAll, days);
        
    } catch (error) {
        if (statusSpan) statusSpan.textContent = '✗ ';
        alert(`Error syncing playback history: ${error.message}`);
        if (button) {
            button.disabled = false;
            button.style.opacity = '1';
        }
        setTimeout(() => {
            if (statusSpan) statusSpan.textContent = '';
        }, 5000);
    }
}

async function pollSyncProgress(syncId, statusSpan, button, syncAll, days) {
    const maxAttempts = 3600; // 30 minutes max (3600 * 0.5s = 30 minutes)
    let attempts = 0;
    let lastProcessed = 0;
    
    const updateStatus = (progress) => {
        if (statusSpan) {
            if (progress.status === 'completed') {
                statusSpan.textContent = '✓ ';
            } else if (progress.status === 'error') {
                statusSpan.textContent = '✗ ';
            } else {
                const percent = progress.total > 0 
                    ? Math.round((progress.processed / progress.total) * 100) 
                    : 0;
                statusSpan.textContent = `🔄 ${progress.processed}${progress.total > 0 ? `/${progress.total} user(s)` : ''} (${percent}%)`;
                if (progress.currentItem) {
                    statusSpan.title = progress.currentItem;
                }
            }
        }
    };
    
    while (attempts < maxAttempts) {
        try {
            const response = await fetch(`/api/playback/sync/progress/${syncId}`);
            if (!response.ok) {
                throw new Error('Failed to get sync progress');
            }
            
            const progress = await response.json();
            updateStatus(progress);
            
            // Log progress if it changed (users change less frequently)
            if (progress.processed > lastProcessed || progress.status !== 'running') {
                console.log(`Sync progress: ${progress.processed}/${progress.total} users processed, ${progress.synced} synced, ${progress.matched} matched. Status: ${progress.status}`);
                lastProcessed = progress.processed;
            }
            
            if (progress.status === 'completed') {
                if (statusSpan) statusSpan.textContent = '✓ ';
                const syncType = syncAll ? 'all historical data' : `last ${days} days`;
                alert(`Playback history synced successfully!\n\nProcessed: ${progress.processed} user(s)\nSynced: ${progress.synced} records\nMatched: ${progress.matched} with local libraries\n\nSynced: ${syncType}`);
                
                // Reload Jellyfin clients to show updated data
                loadJellyfinClients();
                
                // Reload playback dashboard if on playback tab
                const activeTab = document.querySelector('.nav-item.active');
                if (activeTab && activeTab.getAttribute('data-tab') === 'playback') {
                    const { loadPlaybackDashboard, loadPlaybackHistory } = await import('./playback.js');
                    loadPlaybackDashboard();
                    loadPlaybackHistory();
                }
                
                if (button) {
                    button.disabled = false;
                    button.style.opacity = '1';
                }
                setTimeout(() => {
                    if (statusSpan) statusSpan.textContent = '';
                }, 5000);
                return;
            }
            
            if (progress.status === 'error') {
                if (statusSpan) statusSpan.textContent = '✗ ';
                alert(`Error syncing playback history: ${progress.error || 'Unknown error'}`);
                if (button) {
                    button.disabled = false;
                    button.style.opacity = '1';
                }
                setTimeout(() => {
                    if (statusSpan) statusSpan.textContent = '';
                }, 5000);
                return;
            }
            
            // Wait 500ms before next poll
            await new Promise(resolve => setTimeout(resolve, 500));
            attempts++;
        } catch (error) {
            console.error('Error polling sync progress:', error);
            // Continue polling even on error
            await new Promise(resolve => setTimeout(resolve, 1000));
            attempts++;
        }
    }
    
    // Check if sync is still running before giving up
    try {
        const finalResponse = await fetch(`/api/playback/sync/progress/${syncId}`);
        if (finalResponse.ok) {
            const finalProgress = await finalResponse.json();
            if (finalProgress.status === 'running' || finalProgress.status === 'fetching' || finalProgress.status === 'migrating') {
                // Sync is still running, continue polling but with longer intervals
                if (statusSpan) {
                    statusSpan.textContent = '🔄 ...';
                    statusSpan.title = 'Sync is still running in the background. Progress will update automatically.';
                }
                if (button) {
                    button.disabled = false;
                    button.style.opacity = '1';
                }
                
                // Continue polling but less frequently (every 5 seconds)
                const continuePolling = setInterval(async () => {
                    try {
                        const response = await fetch(`/api/playback/sync/progress/${syncId}`);
                        if (response.ok) {
                            const progress = await response.json();
                            updateStatus(progress);
                            
                            if (progress.status === 'completed' || progress.status === 'error') {
                                clearInterval(continuePolling);
                                if (progress.status === 'completed') {
                                    if (statusSpan) statusSpan.textContent = '✓ ';
                                    const syncType = syncAll ? 'all historical data' : `last ${days} days`;
                                    alert(`Playback history synced successfully!\n\nProcessed: ${progress.processed} user(s)\nSynced: ${progress.synced} records\nMatched: ${progress.matched} with local libraries\n\nSynced: ${syncType}`);
                                    
                                    loadJellyfinClients();
                                    
                                    const activeTab = document.querySelector('.nav-item.active');
                                    if (activeTab && activeTab.getAttribute('data-tab') === 'playback') {
                                        const { loadPlaybackDashboard, loadPlaybackHistory } = await import('./playback.js');
                                        loadPlaybackDashboard();
                                        loadPlaybackHistory();
                                    }
                                } else {
                                    if (statusSpan) statusSpan.textContent = '✗ ';
                                    alert(`Error syncing playback history: ${progress.error || 'Unknown error'}`);
                                }
                                
                                if (button) {
                                    button.disabled = false;
                                    button.style.opacity = '1';
                                }
                                setTimeout(() => {
                                    if (statusSpan) statusSpan.textContent = '';
                                }, 5000);
                            }
                        }
                    } catch (error) {
                        console.error('Error checking sync progress:', error);
                    }
                }, 5000); // Poll every 5 seconds
                
                return; // Exit the function but keep polling in background
            }
        }
    } catch (error) {
        console.error('Error checking final sync status:', error);
    }
    
    // Timeout - sync not found or completed
    if (statusSpan) statusSpan.textContent = '⚠ ';
    alert('Sync is taking longer than expected. It may still be running in the background. You can check the status by refreshing the page.');
    if (button) {
        button.disabled = false;
        button.style.opacity = '1';
    }
}

export async function rematchPlaybackHistory(event) {
    if (!confirm('Re-match all playback history with libraries?\n\nThis will attempt to match existing playback records with your current library paths and video analyses. This is useful if you added libraries after syncing playback history.')) {
        return;
    }

    const button = event?.target?.closest('button') || document.querySelector('button[onclick="rematchPlaybackHistory(event)"]');
    
    if (button) {
        button.disabled = true;
        button.textContent = 'Re-matching...';
        button.style.opacity = '0.7';
    }

    try {
        const response = await fetch('/api/playback/rematch', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to re-match playback history');
        }

        const result = await response.json();
        
        alert(`Playback history re-matched successfully!\n\nMatched: ${result.matched} records\n- ${result.videoAnalysisMatches} with video analysis\n- ${result.libraryPathMatches} with library paths\n\nTotal processed: ${result.total} records`);
        
        // Reload playback history if on playback tab
        const activeTab = document.querySelector('.nav-item.active');
        if (activeTab && activeTab.getAttribute('data-tab') === 'playback') {
            const { loadPlaybackHistory, loadPlaybackDashboard } = await import('./playback.js');
            loadPlaybackHistory();
            loadPlaybackDashboard();
        }
    } catch (error) {
        alert(`Error re-matching playback history: ${error.message}`);
    } finally {
        if (button) {
            button.disabled = false;
            button.textContent = 'Re-match with Libraries';
            button.style.opacity = '1';
        }
    }
}

export async function matchVideosWithServarr() {
    const button = event?.target?.closest('button') || document.querySelector('button[onclick="matchVideosWithServarr()"]');
    const statusSpan = document.getElementById('matchVideosStatus');
    
    // Check for both possible status spans (Sonarr and Radarr)
    const sonarrStatusSpan = document.getElementById('matchVideosStatusSonarr');
    const radarrStatusSpan = document.getElementById('matchVideosStatusRadarr');
    const activeStatusSpan = statusSpan || sonarrStatusSpan || radarrStatusSpan;
    
    // Store original button text
    const originalButtonText = button ? button.textContent.trim() : 'Match Videos';
    
    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
        // Update button text while keeping the status span
        const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
        if (statusSpanInButton) {
            statusSpanInButton.textContent = '🔄';
        } else {
            button.textContent = 'Matching...';
        }
    }
    if (activeStatusSpan) activeStatusSpan.textContent = '🔄 Starting...';
    
    try {
        const response = await fetch('/api/servarr/match-videos', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to match videos');
        }
        
        const result = await response.json();
        
        if (result.matchId) {
            // Store matchId in localStorage to survive page reloads
            localStorage.setItem('activeMatchId', result.matchId);
            
            // Start polling for progress
            await pollMatchProgress(result.matchId, activeStatusSpan, button, originalButtonText);
        } else {
            // Fallback to old behavior if no matchId
            if (activeStatusSpan) activeStatusSpan.textContent = '✓ Matched';
            alert(`Video matching completed!\n\nMatched: ${result.matched || 0} videos`);
            if (button) {
                button.disabled = false;
                button.style.opacity = '1';
                const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
                if (statusSpanInButton) {
                    statusSpanInButton.textContent = '';
                } else {
                    button.textContent = originalButtonText;
                }
            }
            setTimeout(() => {
                if (activeStatusSpan) activeStatusSpan.textContent = '';
            }, 3000);
        }
    } catch (error) {
        console.error('Error matching videos:', error);
        if (activeStatusSpan) activeStatusSpan.textContent = '✗ Error';
        alert(`Error matching videos: ${error.message}`);
        if (button) {
            button.disabled = false;
            button.style.opacity = '1';
            const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
            if (statusSpanInButton) {
                statusSpanInButton.textContent = '';
            } else {
                button.textContent = originalButtonText;
            }
        }
        setTimeout(() => {
            if (activeStatusSpan) activeStatusSpan.textContent = '';
        }, 5000);
    }
}

async function pollMatchProgress(matchId, statusSpan, button, originalButtonText) {
    const maxAttempts = 3600; // 30 minutes max (3600 * 0.5s = 30 minutes)
    let attempts = 0;
    let lastProcessed = 0;
    
    const updateStatus = (progress) => {
        if (statusSpan) {
            if (progress.status === 'completed') {
                statusSpan.textContent = '✓ Matched';
            } else if (progress.status === 'error') {
                statusSpan.textContent = '✗ Error';
            } else {
                const percent = progress.total > 0 
                    ? Math.round((progress.processed / progress.total) * 100) 
                    : 0;
                statusSpan.textContent = `🔄 ${progress.processed}${progress.total > 0 ? `/${progress.total}` : ''} (${percent}%)`;
                if (progress.currentItem) {
                    statusSpan.title = progress.currentItem;
                }
            }
        }
    };
    
    while (attempts < maxAttempts) {
        try {
            const response = await fetch(`/api/servarr/match-videos/progress/${matchId}`);
            if (!response.ok) {
                throw new Error('Failed to get match progress');
            }
            
            const progress = await response.json();
            updateStatus(progress);
            
            // Log progress if it changed
            if (progress.processed > lastProcessed || progress.status !== 'running') {
                console.log(`Match progress: ${progress.processed}/${progress.total} videos processed, ${progress.matched} matched. Status: ${progress.status}`);
                lastProcessed = progress.processed;
            }
            
            if (progress.status === 'completed') {
                // Clear localStorage when match completes
                localStorage.removeItem('activeMatchId');
                
                if (statusSpan) statusSpan.textContent = '✓ Matched';
                alert(`Video matching completed!\n\nProcessed: ${progress.processed} videos\nMatched: ${progress.matched} videos${progress.errors > 0 ? `\nErrors: ${progress.errors}` : ''}`);
                
                if (button) {
                    button.disabled = false;
                    button.style.opacity = '1';
                    const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
                    if (statusSpanInButton) {
                        statusSpanInButton.textContent = '';
                    } else {
                        button.textContent = originalButtonText;
                    }
                }
                setTimeout(() => {
                    if (statusSpan) statusSpan.textContent = '';
                }, 5000);
                return;
            }
            
            if (progress.status === 'error') {
                // Clear localStorage when match errors
                localStorage.removeItem('activeMatchId');
                
                if (statusSpan) statusSpan.textContent = '✗ Error';
                alert(`Error matching videos: ${progress.errorMessage || 'Unknown error'}`);
                if (button) {
                    button.disabled = false;
                    button.style.opacity = '1';
                    const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
                    if (statusSpanInButton) {
                        statusSpanInButton.textContent = '';
                    } else {
                        button.textContent = originalButtonText;
                    }
                }
                setTimeout(() => {
                    if (statusSpan) statusSpan.textContent = '';
                }, 5000);
                return;
            }
            
            // Wait 500ms before next poll
            await new Promise(resolve => setTimeout(resolve, 500));
            attempts++;
        } catch (error) {
            console.error('Error polling match progress:', error);
            // Continue polling even on error
            await new Promise(resolve => setTimeout(resolve, 1000));
            attempts++;
        }
    }
    
    // Timeout
    if (statusSpan) statusSpan.textContent = '⚠ Timeout';
    alert('Matching is taking longer than expected. It may still be running in the background.');
    if (button) {
        button.disabled = false;
        button.style.opacity = '1';
        const statusSpanInButton = button.querySelector('span[id^="matchVideosStatus"]');
        if (statusSpanInButton) {
            statusSpanInButton.textContent = '';
        } else {
            button.textContent = originalButtonText;
        }
    }
}

export async function syncRadarrLibrary() {
    const button = document.querySelector('button[onclick="syncRadarrLibrary()"]');
    const statusSpan = document.getElementById('radarrSyncStatus');
    const resultDiv = document.getElementById('radarrSyncResult');
    
    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';
    if (resultDiv) {
        resultDiv.style.display = 'none';
        resultDiv.innerHTML = '';
    }

    try {
        const response = await fetch('/api/servarr/radarr/sync', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        const result = await response.json();
        
        if (result.success) {
            if (statusSpan) statusSpan.textContent = '✓ ';
            if (resultDiv) {
                resultDiv.style.display = 'block';
                resultDiv.innerHTML = `
                    <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--success-color);">
                        <strong>Sync completed successfully!</strong><br>
                        Root folders found: ${result.rootFoldersFound}<br>
                        Movies found: ${result.itemsFound}<br>
                        Created paths: ${result.createdPaths}<br>
                        Updated paths: ${result.updatedPaths}<br>
                        Removed paths: ${result.removedPaths}<br>
                        Duration: ${Math.round(result.duration?.totalSeconds || 0)}s
                    </div>
                `;
            }
            
            // Automatically match videos after successful sync
            setTimeout(async () => {
                try {
                    const matchResponse = await fetch('/api/servarr/match-videos', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' }
                    });
                    if (matchResponse.ok) {
                        const matchResult = await matchResponse.json();
                        console.log(`Automatically matched ${matchResult.matched} videos with Sonarr`);
                    }
                } catch (error) {
                    console.warn('Error auto-matching videos after Sonarr sync:', error);
                }
            }, 1000);
            
            // Reload libraries if on library tab
            const activeTab = document.querySelector('.nav-item.active');
            if (activeTab && activeTab.getAttribute('data-tab') === 'library') {
                const { loadKnownLibraries } = await import('./library.js');
                loadKnownLibraries();
            }
        } else {
            if (statusSpan) statusSpan.textContent = '✗ ';
            if (resultDiv) {
                resultDiv.style.display = 'block';
                resultDiv.innerHTML = `
                    <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--danger-color);">
                        <strong>Sync failed:</strong> ${escapeHtml(result.message || 'Unknown error')}
                    </div>
                `;
            }
        }
    } catch (error) {
        if (statusSpan) statusSpan.textContent = '✗ ';
        if (resultDiv) {
            resultDiv.style.display = 'block';
            resultDiv.innerHTML = `
                <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--danger-color);">
                    <strong>Error:</strong> ${escapeHtml(error.message)}
                </div>
            `;
        }
    } finally {
        if (button) {
            button.disabled = false;
            button.style.opacity = '1';
        }
        setTimeout(() => {
            if (statusSpan) statusSpan.textContent = '';
        }, 5000);
    }
}

export async function syncSonarrLibrary() {
    const button = document.querySelector('button[onclick="syncSonarrLibrary()"]');
    const statusSpan = document.getElementById('sonarrSyncStatus');
    const resultDiv = document.getElementById('sonarrSyncResult');
    
    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';
    if (resultDiv) {
        resultDiv.style.display = 'none';
        resultDiv.innerHTML = '';
    }

    try {
        const response = await fetch('/api/servarr/sonarr/sync', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        const result = await response.json();
        
        if (result.success) {
            if (statusSpan) statusSpan.textContent = '✓ ';
            if (resultDiv) {
                resultDiv.style.display = 'block';
                resultDiv.innerHTML = `
                    <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--success-color);">
                        <strong>Sync completed successfully!</strong><br>
                        Root folders found: ${result.rootFoldersFound}<br>
                        Series found: ${result.itemsFound}<br>
                        Created paths: ${result.createdPaths}<br>
                        Updated paths: ${result.updatedPaths}<br>
                        Removed paths: ${result.removedPaths}<br>
                        Duration: ${Math.round(result.duration?.totalSeconds || 0)}s
                    </div>
                `;
            }
            
            // Automatically match videos after successful sync
            setTimeout(async () => {
                try {
                    const matchResponse = await fetch('/api/servarr/match-videos', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' }
                    });
                    if (matchResponse.ok) {
                        const matchResult = await matchResponse.json();
                        console.log(`Automatically matched ${matchResult.matched} videos with Sonarr`);
                    }
                } catch (error) {
                    console.warn('Error auto-matching videos after Sonarr sync:', error);
                }
            }, 1000);
            
            // Reload libraries if on library tab
            const activeTab = document.querySelector('.nav-item.active');
            if (activeTab && activeTab.getAttribute('data-tab') === 'library') {
                const { loadKnownLibraries } = await import('./library.js');
                loadKnownLibraries();
            }
        } else {
            if (statusSpan) statusSpan.textContent = '✗ ';
            if (resultDiv) {
                resultDiv.style.display = 'block';
                resultDiv.innerHTML = `
                    <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--danger-color);">
                        <strong>Sync failed:</strong> ${escapeHtml(result.message || 'Unknown error')}
                    </div>
                `;
            }
        }
    } catch (error) {
        if (statusSpan) statusSpan.textContent = '✗ ';
        if (resultDiv) {
            resultDiv.style.display = 'block';
            resultDiv.innerHTML = `
                <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--danger-color);">
                    <strong>Error:</strong> ${escapeHtml(error.message)}
                </div>
            `;
        }
    } finally {
        if (button) {
            button.disabled = false;
            button.style.opacity = '1';
        }
        setTimeout(() => {
            if (statusSpan) statusSpan.textContent = '';
        }, 5000);
    }
}

export async function syncAllServarrLibraries() {
    const button = document.querySelector('button[onclick="syncAllServarrLibraries()"]');
    const statusSpan = document.getElementById('allServarrSyncStatus');
    const resultDiv = document.getElementById('servarrSyncResult');
    
    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';
    if (resultDiv) {
        resultDiv.style.display = 'none';
        resultDiv.innerHTML = '';
    }

    try {
        const response = await fetch('/api/servarr/sync-all', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        const result = await response.json();
        
        if (result.overallSuccess) {
            if (statusSpan) statusSpan.textContent = '✓ ';
            if (resultDiv) {
                resultDiv.style.display = 'block';
                const radarr = result.radarr || {};
                const sonarr = result.sonarr || {};
                resultDiv.innerHTML = `
                    <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--success-color);">
                        <strong>All syncs completed successfully!</strong><br><br>
                        <strong>Radarr:</strong><br>
                        Root folders: ${radarr.rootFoldersFound || 0}, Movies: ${radarr.itemsFound || 0}<br>
                        Created: ${radarr.createdPaths || 0}, Updated: ${radarr.updatedPaths || 0}, Removed: ${radarr.removedPaths || 0}<br><br>
                        <strong>Sonarr:</strong><br>
                        Root folders: ${sonarr.rootFoldersFound || 0}, Series: ${sonarr.itemsFound || 0}<br>
                        Created: ${sonarr.createdPaths || 0}, Updated: ${sonarr.updatedPaths || 0}, Removed: ${sonarr.removedPaths || 0}
                    </div>
                `;
            }
            
            // Reload libraries if on library tab
            const activeTab = document.querySelector('.nav-item.active');
            if (activeTab && activeTab.getAttribute('data-tab') === 'library') {
                const { loadKnownLibraries } = await import('./library.js');
                loadKnownLibraries();
            }
        } else {
            if (statusSpan) statusSpan.textContent = '⚠ ';
            if (resultDiv) {
                resultDiv.style.display = 'block';
                const radarr = result.radarr || {};
                const sonarr = result.sonarr || {};
                resultDiv.innerHTML = `
                    <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--warning-color);">
                        <strong>Sync completed with warnings:</strong><br><br>
                        <strong>Radarr:</strong> ${radarr.success ? '✓' : '✗'} ${escapeHtml(radarr.message || 'Unknown')}<br>
                        <strong>Sonarr:</strong> ${sonarr.success ? '✓' : '✗'} ${escapeHtml(sonarr.message || 'Unknown')}
                    </div>
                `;
            }
        }
    } catch (error) {
        if (statusSpan) statusSpan.textContent = '✗ ';
        if (resultDiv) {
            resultDiv.style.display = 'block';
            resultDiv.innerHTML = `
                <div style="padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--danger-color);">
                    <strong>Error:</strong> ${escapeHtml(error.message)}
                </div>
            `;
        }
    } finally {
        if (button) {
            button.disabled = false;
            button.style.opacity = '1';
        }
        setTimeout(() => {
            if (statusSpan) statusSpan.textContent = '';
        }, 5000);
    }
}

// Export to window for onclick handlers
window.testJellyfinConnection = testJellyfinConnection;
window.syncJellyfinPlayback = syncJellyfinPlayback;
window.toggleSyncDaysInput = toggleSyncDaysInput;
window.rematchPlaybackHistory = rematchPlaybackHistory;
window.syncRadarrLibrary = syncRadarrLibrary;
window.syncSonarrLibrary = syncSonarrLibrary;
window.matchVideosWithServarr = matchVideosWithServarr;
window.syncAllServarrLibraries = syncAllServarrLibraries;

