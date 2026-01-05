// Servarr Integration Functions
import { escapeHtml } from './utils.js';

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
    } catch (error) {
        console.error('Error loading Servarr status:', error);
    }
    
    // Load Jellyfin status separately
    loadJellyfinStatus();
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
    const button = event.target.closest('button');
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

    if (button) button.disabled = true;
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
        if (button) button.disabled = false;
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
        const response = await fetch('/api/playback/clients');
        if (!response.ok) {
            // If endpoint fails or no data, hide the section
            const box = document.getElementById('jellyfinClientsBox');
            if (box) box.style.display = 'none';
            return;
        }
        
        const data = await response.json();
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

export async function syncJellyfinPlayback() {
    const button = event.target.closest('button');
    const statusSpan = document.getElementById('jellyfinSyncStatus');
    const daysInput = document.getElementById('syncDays');
    const days = daysInput ? parseInt(daysInput.value) || 30 : 30;

    if (button) button.disabled = true;
    if (statusSpan) statusSpan.textContent = '🔄 Syncing...';

    try {
        const response = await fetch(`/api/playback/sync?days=${days}`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to sync playback history');
        }

        const result = await response.json();
        
        if (statusSpan) statusSpan.textContent = '✓ ';
        alert(`Playback history synced successfully!\n\nSynced: ${result.synced} records\nMatched: ${result.matched} with local libraries\nTotal: ${result.total} records found`);
        
        // Reload Jellyfin clients to show updated data
        loadJellyfinClients();
    } catch (error) {
        if (statusSpan) statusSpan.textContent = '✗ ';
        alert(`Error syncing playback history: ${error.message}`);
    } finally {
        if (button) button.disabled = false;
        setTimeout(() => {
            if (statusSpan) statusSpan.textContent = '';
        }, 5000);
    }
}

// Export to window for onclick handlers
window.testJellyfinConnection = testJellyfinConnection;
window.syncJellyfinPlayback = syncJellyfinPlayback;

