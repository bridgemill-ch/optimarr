// Settings Management Functions
import { escapeHtml } from './utils.js';
import { loadServarrStatus, loadJellyfinStatus } from './servarr.js';

export async function loadJellyfinSettings() {
    try {
        const response = await fetch('/api/playback/settings');
        if (!response.ok) return;
        
        const settings = await response.json();
        
        // Update Jellyfin settings fields
        const baseUrlInput = document.getElementById('jellyfinUrl');
        const apiKeyInput = document.getElementById('jellyfinApiKey');
        const usernameInput = document.getElementById('jellyfinUsername');
        const passwordInput = document.getElementById('jellyfinPassword');
        const enabledInput = document.getElementById('jellyfinEnabled');
        
        if (baseUrlInput) baseUrlInput.value = settings.baseUrl || '';
        if (apiKeyInput) apiKeyInput.value = settings.apiKey || '';
        if (usernameInput) usernameInput.value = settings.username || '';
        // Don't set password - it's never returned from the API
        if (enabledInput) enabledInput.checked = settings.enabled || false;
        
        // Setup sync days input listener
        const syncDaysInput = document.getElementById('syncDays');
        if (syncDaysInput) {
            syncDaysInput.addEventListener('input', () => {
                const syncAllCheckbox = document.getElementById('syncAllHistory');
                const helpText = document.getElementById('syncHelpText');
                if (helpText && (!syncAllCheckbox || !syncAllCheckbox.checked)) {
                    const days = syncDaysInput.value || 30;
                    helpText.textContent = `Import playback history from the last ${days} days. Check "Sync All" to import complete history.`;
                }
            });
        }
    } catch (error) {
        console.error('Error loading Jellyfin settings:', error);
    }
}

export async function saveJellyfinSettings() {
    try {
        const baseUrl = document.getElementById('jellyfinUrl').value;
        const apiKey = document.getElementById('jellyfinApiKey').value;
        const username = document.getElementById('jellyfinUsername').value;
        const password = document.getElementById('jellyfinPassword').value;
        const enabled = document.getElementById('jellyfinEnabled').checked;

        if (!baseUrl) {
            alert('Please enter Base URL');
            return;
        }

        if (!apiKey && (!username || !password)) {
            alert('Please enter either API Key or Username/Password');
            return;
        }

        const settings = {
            baseUrl: baseUrl,
            apiKey: apiKey || null,
            username: username || null,
            password: password || null,
            enabled: enabled
        };

        const response = await fetch('/api/playback/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save Jellyfin settings');
        }

        const result = await response.json();
        alert('Jellyfin settings saved successfully! The service will reconnect with the new settings.');

        // Reload status
        setTimeout(() => {
            loadJellyfinStatus();
        }, 1000);
    } catch (error) {
        console.error('Error saving Jellyfin settings:', error);
        alert(`Error saving Jellyfin settings: ${error.message}`);
    }
}

// Old rating settings functions removed - no longer needed with new property-based rating system
// The new system uses hardcoded thresholds (Optimal=80, Good=60) via RatingThresholds model

// Client Settings Functions

// Sonarr Settings Functions
export async function loadSonarrSettings() {
    try {
        const response = await fetch('/api/servarr/sonarr/settings');
        if (!response.ok) return;
        
        const settings = await response.json();
        
        const baseUrlInput = document.getElementById('sonarrUrl');
        const apiKeyInput = document.getElementById('sonarrApiKey');
        const enabledInput = document.getElementById('sonarrEnabled');
        
        if (baseUrlInput) baseUrlInput.value = settings.baseUrl || '';
        if (apiKeyInput) apiKeyInput.value = settings.apiKey || '';
        if (enabledInput) enabledInput.checked = settings.enabled || false;
    } catch (error) {
        console.error('Error loading Sonarr settings:', error);
    }
}

async function saveSonarrSettings() {
    try {
        const baseUrl = document.getElementById('sonarrUrl').value;
        const apiKey = document.getElementById('sonarrApiKey').value;
        const enabled = document.getElementById('sonarrEnabled').checked;

        if (!baseUrl) {
            alert('Please enter Base URL');
            return;
        }

        if (!apiKey) {
            alert('Please enter API Key');
            return;
        }

        const settings = {
            baseUrl: baseUrl,
            apiKey: apiKey,
            enabled: enabled
        };

        const response = await fetch('/api/servarr/sonarr/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save Sonarr settings');
        }

        alert('Sonarr settings saved successfully! The service will reconnect with the new settings.');

        setTimeout(() => {
            loadServarrStatus();
        }, 1000);
    } catch (error) {
        console.error('Error saving Sonarr settings:', error);
        alert(`Error saving Sonarr settings: ${error.message}`);
    }
}

export { saveSonarrSettings };
window.saveSonarrSettings = saveSonarrSettings;

async function testSonarrConnection(event) {
    const button = event?.target?.closest('button') || document.querySelector('button[onclick="testSonarrConnection(event)"]');
    const statusSpan = document.getElementById('sonarrTestStatus');
    const baseUrl = document.getElementById('sonarrUrl').value;
    const apiKey = document.getElementById('sonarrApiKey').value;

    if (!baseUrl) {
        alert('Please enter Base URL before testing');
        return;
    }

    if (!apiKey) {
        alert('Please enter API Key before testing');
        return;
    }

    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';

    try {
        const response = await fetch('/api/servarr/sonarr/test', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ baseUrl, apiKey })
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

export { testSonarrConnection };
window.testSonarrConnection = testSonarrConnection;

// Radarr Settings Functions
export async function loadRadarrSettings() {
    try {
        const response = await fetch('/api/servarr/radarr/settings');
        if (!response.ok) return;
        
        const settings = await response.json();
        
        const baseUrlInput = document.getElementById('radarrUrl');
        const apiKeyInput = document.getElementById('radarrApiKey');
        const enabledInput = document.getElementById('radarrEnabled');
        
        if (baseUrlInput) baseUrlInput.value = settings.baseUrl || '';
        if (apiKeyInput) apiKeyInput.value = settings.apiKey || '';
        if (enabledInput) enabledInput.checked = settings.enabled || false;
    } catch (error) {
        console.error('Error loading Radarr settings:', error);
    }
}

async function saveRadarrSettings() {
    try {
        const baseUrl = document.getElementById('radarrUrl').value;
        const apiKey = document.getElementById('radarrApiKey').value;
        const enabled = document.getElementById('radarrEnabled').checked;

        if (!baseUrl) {
            alert('Please enter Base URL');
            return;
        }

        if (!apiKey) {
            alert('Please enter API Key');
            return;
        }

        const settings = {
            baseUrl: baseUrl,
            apiKey: apiKey,
            enabled: enabled
        };

        const response = await fetch('/api/servarr/radarr/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(settings)
        });

        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save Radarr settings');
        }

        alert('Radarr settings saved successfully! The service will reconnect with the new settings.');

        setTimeout(() => {
            loadServarrStatus();
        }, 1000);
    } catch (error) {
        console.error('Error saving Radarr settings:', error);
        alert(`Error saving Radarr settings: ${error.message}`);
    }
}

export { saveRadarrSettings };
window.saveRadarrSettings = saveRadarrSettings;

async function testRadarrConnection(event) {
    const button = event?.target?.closest('button') || document.querySelector('button[onclick="testRadarrConnection(event)"]');
    const statusSpan = document.getElementById('radarrTestStatus');
    const baseUrl = document.getElementById('radarrUrl').value;
    const apiKey = document.getElementById('radarrApiKey').value;

    if (!baseUrl) {
        alert('Please enter Base URL before testing');
        return;
    }

    if (!apiKey) {
        alert('Please enter API Key before testing');
        return;
    }

    if (button) {
        button.disabled = true;
        button.style.opacity = '0.7';
    }
    if (statusSpan) statusSpan.textContent = '🔄 ';

    try {
        const response = await fetch('/api/servarr/radarr/test', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ baseUrl, apiKey })
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

export { testRadarrConnection };
window.testRadarrConnection = testRadarrConnection;

// Path Mapping Functions
let sonarrPathMappings = [];
let radarrPathMappings = [];

export async function loadSonarrPathMappings() {
    try {
        const response = await fetch('/api/servarr/sonarr/path-mappings');
        if (!response.ok) {
            sonarrPathMappings = [];
            renderSonarrPathMappings();
            return;
        }
        
        const data = await response.json();
        sonarrPathMappings = data.mappings || [];
        renderSonarrPathMappings();
    } catch (error) {
        console.error('Error loading Sonarr path mappings:', error);
        sonarrPathMappings = [];
        renderSonarrPathMappings();
    }
}

function renderSonarrPathMappings() {
    const container = document.getElementById('sonarrPathMappingsContainer');
    if (!container) return;
    
    if (sonarrPathMappings.length === 0) {
        container.innerHTML = '<p style="color: var(--text-secondary); font-size: 0.875rem; margin: 0;">No path mappings configured. Add a mapping if Sonarr and Optimarr see different paths.</p>';
        return;
    }
    
    container.innerHTML = sonarrPathMappings.map((mapping, index) => `
        <div class="path-mapping-item" style="display: flex; gap: 0.75rem; align-items: center; margin-bottom: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color);">
            <div style="flex: 1; display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
                <div style="flex: 1; min-width: 200px;">
                    <label style="font-size: 0.75rem; color: var(--text-secondary); display: block; margin-bottom: 0.25rem;">From (Sonarr path)</label>
                    <input type="text" class="form-control path-mapping-from" data-index="${index}" value="${escapeHtml(mapping.From)}" placeholder="/data/tv" style="font-size: 0.875rem;">
                </div>
                <div style="flex: 1; min-width: 200px;">
                    <label style="font-size: 0.75rem; color: var(--text-secondary); display: block; margin-bottom: 0.25rem;">To (Optimarr path)</label>
                    <input type="text" class="form-control path-mapping-to" data-index="${index}" value="${escapeHtml(mapping.To)}" placeholder="/mnt/media/tv" style="font-size: 0.875rem;">
                </div>
            </div>
            <button type="button" class="btn btn-secondary btn-sm" onclick="removeSonarrPathMapping(${index})" style="flex-shrink: 0;">Remove</button>
        </div>
    `).join('');
}

export function addSonarrPathMapping() {
    sonarrPathMappings.push({ From: '', To: '' });
    renderSonarrPathMappings();
}

export function removeSonarrPathMapping(index) {
    sonarrPathMappings.splice(index, 1);
    renderSonarrPathMappings();
}

export async function saveSonarrPathMappings() {
    try {
        // Update mappings from inputs
        document.querySelectorAll('.path-mapping-from').forEach((input, index) => {
            if (sonarrPathMappings[index]) {
                sonarrPathMappings[index].From = input.value.trim();
            }
        });
        document.querySelectorAll('.path-mapping-to').forEach((input, index) => {
            if (sonarrPathMappings[index]) {
                sonarrPathMappings[index].To = input.value.trim();
            }
        });
        
        // Filter out empty mappings
        const validMappings = sonarrPathMappings.filter(m => m.From && m.To);
        
        const response = await fetch('/api/servarr/sonarr/path-mappings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(validMappings)
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save path mappings');
        }
        
        sonarrPathMappings = validMappings;
        renderSonarrPathMappings();
        alert('Path mappings saved successfully!');
    } catch (error) {
        console.error('Error saving Sonarr path mappings:', error);
        alert(`Error saving path mappings: ${error.message}`);
    }
}

export async function loadRadarrPathMappings() {
    try {
        const response = await fetch('/api/servarr/radarr/path-mappings');
        if (!response.ok) {
            radarrPathMappings = [];
            renderRadarrPathMappings();
            return;
        }
        
        const data = await response.json();
        radarrPathMappings = data.mappings || [];
        renderRadarrPathMappings();
    } catch (error) {
        console.error('Error loading Radarr path mappings:', error);
        radarrPathMappings = [];
        renderRadarrPathMappings();
    }
}

function renderRadarrPathMappings() {
    const container = document.getElementById('radarrPathMappingsContainer');
    if (!container) return;
    
    if (radarrPathMappings.length === 0) {
        container.innerHTML = '<p style="color: var(--text-secondary); font-size: 0.875rem; margin: 0;">No path mappings configured. Add a mapping if Radarr and Optimarr see different paths.</p>';
        return;
    }
    
    container.innerHTML = radarrPathMappings.map((mapping, index) => `
        <div class="path-mapping-item" style="display: flex; gap: 0.75rem; align-items: center; margin-bottom: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color);">
            <div style="flex: 1; display: flex; gap: 0.5rem; align-items: center; flex-wrap: wrap;">
                <div style="flex: 1; min-width: 200px;">
                    <label style="font-size: 0.75rem; color: var(--text-secondary); display: block; margin-bottom: 0.25rem;">From (Radarr path)</label>
                    <input type="text" class="form-control path-mapping-from" data-index="${index}" value="${escapeHtml(mapping.From)}" placeholder="/data/movies" style="font-size: 0.875rem;">
                </div>
                <div style="flex: 1; min-width: 200px;">
                    <label style="font-size: 0.75rem; color: var(--text-secondary); display: block; margin-bottom: 0.25rem;">To (Optimarr path)</label>
                    <input type="text" class="form-control path-mapping-to" data-index="${index}" value="${escapeHtml(mapping.To)}" placeholder="/mnt/media/movies" style="font-size: 0.875rem;">
                </div>
            </div>
            <button type="button" class="btn btn-secondary btn-sm" onclick="removeRadarrPathMapping(${index})" style="flex-shrink: 0;">Remove</button>
        </div>
    `).join('');
}

export function addRadarrPathMapping() {
    radarrPathMappings.push({ From: '', To: '' });
    renderRadarrPathMappings();
}

export function removeRadarrPathMapping(index) {
    radarrPathMappings.splice(index, 1);
    renderRadarrPathMappings();
}

export async function saveRadarrPathMappings() {
    try {
        // Update mappings from inputs
        document.querySelectorAll('#radarrPathMappingsContainer .path-mapping-from').forEach((input, index) => {
            if (radarrPathMappings[index]) {
                radarrPathMappings[index].From = input.value.trim();
            }
        });
        document.querySelectorAll('#radarrPathMappingsContainer .path-mapping-to').forEach((input, index) => {
            if (radarrPathMappings[index]) {
                radarrPathMappings[index].To = input.value.trim();
            }
        });
        
        // Filter out empty mappings
        const validMappings = radarrPathMappings.filter(m => m.From && m.To);
        
        const response = await fetch('/api/servarr/radarr/path-mappings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(validMappings)
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save path mappings');
        }
        
        radarrPathMappings = validMappings;
        renderRadarrPathMappings();
        alert('Path mappings saved successfully!');
    } catch (error) {
        console.error('Error saving Radarr path mappings:', error);
        alert(`Error saving path mappings: ${error.message}`);
    }
}

// Helper function to refresh visible grids after settings changes
async function refreshVisibleGrids() {
    // Check which tab is currently active
    const activeTab = document.querySelector('.nav-item.active')?.dataset.tab;
    
    if (activeTab === 'browse') {
        // Refresh browse grid
        try {
            const { loadBrowseMedia } = await import('./browse.js');
            loadBrowseMedia();
        } catch (error) {
            console.error('Error refreshing browse grid:', error);
        }
    } else if (activeTab === 'dashboard') {
        // Refresh dashboard
        try {
            const { loadDashboard } = await import('./dashboard.js');
            loadDashboard();
        } catch (error) {
            console.error('Error refreshing dashboard:', error);
        }
    }
}

// Export to window for onclick handlers
window.loadSonarrPathMappings = loadSonarrPathMappings;
window.addSonarrPathMapping = addSonarrPathMapping;
window.removeSonarrPathMapping = removeSonarrPathMapping;
window.saveSonarrPathMappings = saveSonarrPathMappings;
window.loadRadarrPathMappings = loadRadarrPathMappings;
window.addRadarrPathMapping = addRadarrPathMapping;
window.removeRadarrPathMapping = removeRadarrPathMapping;
window.saveRadarrPathMappings = saveRadarrPathMappings;

// Media Property Settings Functions
let mediaPropertySettings = null;
let ratingWeights = null;
let ratingThresholds = null;
let settingsLoaded = false; // Track if settings have been loaded at least once
let lastKnownUserThresholdValues = {}; // Track last known user-entered threshold values
let lastKnownUserPropertySettings = null; // Track last known user checkbox states

// Load last known user settings from localStorage on module load
(function() {
    try {
        const saved = localStorage.getItem('optimarr_lastKnownUserPropertySettings');
        if (saved) {
            lastKnownUserPropertySettings = JSON.parse(saved);
        }
    } catch (e) {
        // Ignore errors reading from localStorage
    }
})();

export async function loadMediaPropertySettings(forceReload = false) {
    // Store forceReload flag in a way that renderMediaPropertySettings can access it
    // We'll pass it as a parameter or use a module-level variable
    window._forceReloadMediaSettings = forceReload;
    try {
        // Always fetch from server - don't skip on first load
        // Only preserve input values if settings have already been loaded (user might be actively editing)
        // But skip preservation if forceReload is true (e.g., after saving)
        const currentInputValues = {};
        const currentThresholdValues = {};
        const existingInputs = document.querySelectorAll('.rating-weight-input');
        const existingThresholdInputs = document.querySelectorAll('.rating-threshold-input');
        
        // Preserve threshold input values if user has modified them
        if (!forceReload && existingThresholdInputs.length > 0) {
            existingThresholdInputs.forEach(input => {
                const key = input.dataset.threshold;
                if (key) {
                    const value = parseInt(input.value.trim(), 10);
                    if (!isNaN(value) && value >= 0 && value <= 100) {
                        // Check if this differs from the current module value (indicating user modification)
                        const currentValue = ratingThresholds?.[key];
                        if (currentValue === undefined || value !== currentValue) {
                            currentThresholdValues[key] = value;
                        }
                    }
                }
            });
        }
        
        // Only preserve inputs if settings have been loaded before AND user has made changes
        // AND we're not forcing a reload (e.g., after save)
        if (!forceReload && settingsLoaded && existingInputs.length > 0 && ratingWeights) {
            // Inputs exist and settings were loaded before, check if user has made changes
            existingInputs.forEach(input => {
                const key = input.dataset.weight;
                if (key) {
                    const value = input.value.trim();
                    if (value !== '') {
                        if (key === 'HighBitrateThresholdMbps') {
                            const parsed = parseFloat(value);
                            if (!isNaN(parsed) && parsed > 0) {
                                currentInputValues[key] = parsed;
                            }
                        } else {
                            const parsed = parseInt(value, 10);
                            if (!isNaN(parsed) && parsed >= 0) {
                                currentInputValues[key] = parsed;
                            }
                        }
                    }
                }
            });
            
            // Only skip reload if user has made changes that differ from stored values
            const hasModifiedInputs = Object.keys(currentInputValues).length > 0;
            const inputsMatchStored = Object.keys(currentInputValues).every(key => {
                return ratingWeights[key] === currentInputValues[key];
            });
            
            if (hasModifiedInputs && !inputsMatchStored) {
                // Update ratingWeights with preserved values
                Object.keys(currentInputValues).forEach(key => {
                    ratingWeights[key] = currentInputValues[key];
                });
                // Don't reload from server or re-render, just return
                return;
            }
        }
        
        // Add cache-busting parameter to ensure we always get fresh data from server
        const cacheBuster = forceReload ? `?t=${Date.now()}` : '';
        const response = await fetch(`/api/library/settings/media-properties${cacheBuster}`, {
            cache: forceReload ? 'no-cache' : 'default',
            headers: {
                'Cache-Control': forceReload ? 'no-cache' : 'default'
            }
        });
        if (!response.ok) throw new Error('Failed to load media property settings');
        
        const data = await response.json();
        
        // Load properties from server - use what server returns (saved values or empty)
        // Don't merge with defaults here - we'll handle that later if needed
        mediaPropertySettings = data.properties || {};
        
        // Ensure we have at least empty objects with proper structure
        if (!mediaPropertySettings.VideoCodecs) mediaPropertySettings.VideoCodecs = {};
        if (!mediaPropertySettings.AudioCodecs) mediaPropertySettings.AudioCodecs = {};
        if (!mediaPropertySettings.Containers) mediaPropertySettings.Containers = {};
        if (!mediaPropertySettings.SubtitleFormats) mediaPropertySettings.SubtitleFormats = {};
        if (!mediaPropertySettings.BitDepths) mediaPropertySettings.BitDepths = {};
        
        // Check if server returned saved properties (not empty)
        const hasAnyProperties = Object.keys(mediaPropertySettings.VideoCodecs).length > 0 ||
                                 Object.keys(mediaPropertySettings.AudioCodecs).length > 0 ||
                                 Object.keys(mediaPropertySettings.Containers).length > 0 ||
                                 Object.keys(mediaPropertySettings.SubtitleFormats).length > 0 ||
                                 Object.keys(mediaPropertySettings.BitDepths).length > 0;
        
        // If server returned saved properties, update localStorage to keep in sync
        if (hasAnyProperties) {
            lastKnownUserPropertySettings = JSON.parse(JSON.stringify(mediaPropertySettings));
            try {
                localStorage.setItem('optimarr_lastKnownUserPropertySettings', JSON.stringify(lastKnownUserPropertySettings));
            } catch (e) {
                // Ignore errors writing to localStorage
            }
        }
        
        // Load rating thresholds - update module-level variable
        // ASP.NET Core serializes to camelCase by default, so check both Optimal/optimal and Good/good
        if (data.thresholds && typeof data.thresholds === 'object') {
            // Handle both camelCase (optimal, good) and PascalCase (Optimal, Good)
            const optimal = data.thresholds.Optimal !== undefined && data.thresholds.Optimal !== null 
                ? data.thresholds.Optimal 
                : (data.thresholds.optimal !== undefined && data.thresholds.optimal !== null ? data.thresholds.optimal : 80);
            const good = data.thresholds.Good !== undefined && data.thresholds.Good !== null 
                ? data.thresholds.Good 
                : (data.thresholds.good !== undefined && data.thresholds.good !== null ? data.thresholds.good : 60);
            
            ratingThresholds = { Optimal: optimal, Good: good };
        } else {
            // Use defaults if not loaded
            ratingThresholds = { Optimal: 80, Good: 60 };
        }
        
        // Default weight values (matching the C# model defaults)
        const defaultWeights = {
            SurroundSound: 3,
            HDR: 8,
            HighBitrate: 5,
            IncorrectCodecTag: 12,
            UnsupportedVideoCodec: 35,
            UnsupportedAudioCodec: 25,
            UnsupportedContainer: 30,
            UnsupportedSubtitleFormat: 8,
            UnsupportedBitDepth: 18,
            FastStart: 5,
            HighBitrateThresholdMbps: 40.0
        };
        
        // Merge loaded weights with defaults (loaded values take precedence)
        // Use weightsSectionExists from backend to know if weights are actually saved in config
        const weightsSectionExists = data.weightsSectionExists === true;
        
        // Use weights directly from server - backend RatingWeights model has all properties with defaults
        // So data.weights will always have all properties (either saved values or defaults)
        // Always start with defaults, then merge in values from server
        ratingWeights = { ...defaultWeights };
        
        if (data.weights && typeof data.weights === 'object') {
            // Map camelCase keys from server to PascalCase keys used in frontend
            // Backend returns camelCase (surroundSound, hdr) but frontend uses PascalCase (SurroundSound, HDR)
            const camelToPascalMap = {
                'surroundSound': 'SurroundSound',
                'hdr': 'HDR',
                'highBitrate': 'HighBitrate',
                'incorrectCodecTag': 'IncorrectCodecTag',
                'unsupportedVideoCodec': 'UnsupportedVideoCodec',
                'unsupportedAudioCodec': 'UnsupportedAudioCodec',
                'unsupportedContainer': 'UnsupportedContainer',
                'unsupportedSubtitleFormat': 'UnsupportedSubtitleFormat',
                'unsupportedBitDepth': 'UnsupportedBitDepth',
                'fastStart': 'FastStart',
                'highBitrateThresholdMbps': 'HighBitrateThresholdMbps'
            };
            
            // Merge server values into defaults (server values take precedence)
            // Check both camelCase (from server) and PascalCase (for backward compatibility)
            Object.keys(defaultWeights).forEach(pascalKey => {
                // Try camelCase first (what server returns)
                const camelKey = Object.keys(camelToPascalMap).find(k => camelToPascalMap[k] === pascalKey);
                if (camelKey && data.weights.hasOwnProperty(camelKey) && data.weights[camelKey] !== null && data.weights[camelKey] !== undefined) {
                    ratingWeights[pascalKey] = data.weights[camelKey];
                }
                // Also check PascalCase (for backward compatibility or if server changes)
                else if (data.weights.hasOwnProperty(pascalKey) && data.weights[pascalKey] !== null && data.weights[pascalKey] !== undefined) {
                    ratingWeights[pascalKey] = data.weights[pascalKey];
                }
            });
        }
        
        // Preserve current input values if user has typed but not saved
        // This prevents resetting values when switching to settings tab
        // Only preserve if inputs exist and have been modified AND we're not forcing a reload
        // When forceReload is true, always use server values, never preserve local input
        if (!forceReload && Object.keys(currentInputValues).length > 0) {
            Object.keys(currentInputValues).forEach(key => {
                ratingWeights[key] = currentInputValues[key];
            });
        }
        
        // Preserve threshold values if user has modified them
        if (!forceReload && Object.keys(currentThresholdValues).length > 0) {
            Object.keys(currentThresholdValues).forEach(key => {
                ratingThresholds[key] = currentThresholdValues[key];
            });
        }
        
        // Check if settings exist - if weights section exists in config, settings definitely exist
        // Only initialize defaults if BOTH properties are empty AND weights section does NOT exist in config
        // (hasAnyProperties was already calculated above)
        
        // Only initialize if both properties AND weights section are completely missing
        // If weights section exists in config, don't initialize (even if properties are empty)
        // IMPORTANT: Only auto-initialize on first load when nothing exists - don't re-initialize if user has already set values
        const shouldInitialize = !hasAnyProperties && !weightsSectionExists;
        
        if (shouldInitialize) {
            // Settings are completely empty, automatically initialize with defaults
            await initializeDefaultMediaPropertySettings();
        } else {
            // Settings exist (either properties or weights section), just render them
            // If properties are empty but weights exist, check if we have last-known user settings to restore
            // This handles the case where properties were saved but server returns empty (or on page reload)
            if (!hasAnyProperties && weightsSectionExists && lastKnownUserPropertySettings) {
                // Properties are empty from server, but we have last-known user settings - use those
                mediaPropertySettings = JSON.parse(JSON.stringify(lastKnownUserPropertySettings));
            } else if (!hasAnyProperties && weightsSectionExists) {
                // Properties are empty and no last-known settings - use defaults for display only
                // This is the first time loading and properties haven't been saved yet
                const defaultProperties = {
                    VideoCodecs: {
                        "H.264": true,
                        "H.264 8-bit": true,
                        "H.265": true,
                        "H.265 8-bit": true,
                        "H.265 10-bit": false,
                        "VP9": true,
                        "AV1": false
                    },
                    AudioCodecs: {
                        "AAC": true,
                        "MP3": true,
                        "AC3": false,
                        "EAC3": false,
                        "DTS": false,
                        "FLAC": true,
                        "Opus": true
                    },
                    Containers: {
                        "MP4": true,
                        "M4V": true,
                        "MOV": true,
                        "MKV": false,
                        "WebM": true,
                        "TS": false
                    },
                    SubtitleFormats: {
                        "SRT": true,
                        "VTT": true,
                        "ASS": false,
                        "SSA": false
                    },
                    BitDepths: {
                        "8": true,
                        "10": false,
                        "12": false
                    }
                };
                
                // Merge defaults with existing (empty) properties for display only
                mediaPropertySettings = {
                    VideoCodecs: { ...defaultProperties.VideoCodecs, ...(mediaPropertySettings.VideoCodecs || {}) },
                    AudioCodecs: { ...defaultProperties.AudioCodecs, ...(mediaPropertySettings.AudioCodecs || {}) },
                    Containers: { ...defaultProperties.Containers, ...(mediaPropertySettings.Containers || {}) },
                    SubtitleFormats: { ...defaultProperties.SubtitleFormats, ...(mediaPropertySettings.SubtitleFormats || {}) },
                    BitDepths: { ...defaultProperties.BitDepths, ...(mediaPropertySettings.BitDepths || {}) }
                };
            }
            
            renderMediaPropertySettings();
        }
        
        // Mark settings as loaded
        settingsLoaded = true;
        
        // Always render rating weights when loading from server (on page refresh or first load)
        // The ratingWeights variable should now be populated with values from server
        renderRatingWeights();
        
        // Render rating thresholds - use module-level variable (which was just updated from server)
        // BUT renderRatingThresholds will preserve any user modifications that exist in the DOM
        // This is important: if user has changed values but not saved yet, we don't want to overwrite them
        renderRatingThresholds(ratingThresholds);
        
        // Clear the force reload flag after rendering
        window._forceReloadMediaSettings = false;
    } catch (error) {
        console.error('Error loading media property settings:', error);
        const container = document.getElementById('mediaPropertySettingsContainer');
        if (container) {
            container.innerHTML = '<div class="error-message">Failed to load media property settings. Please try again.</div>';
        }
        window._forceReloadMediaSettings = false;
    }
}

// Initialize default media property settings
async function initializeDefaultMediaPropertySettings() {
    // Default settings matching the backend GetDefaultPropertySettings()
    const defaultSettings = {
        properties: {
            VideoCodecs: {
                "H.264": true,
                "H.264 8-bit": true,
                "H.265": true,
                "H.265 8-bit": true,
                "H.265 10-bit": false,
                "VP9": true,
                "AV1": false
            },
            AudioCodecs: {
                "AAC": true,
                "MP3": true,
                "AC3": false,
                "EAC3": false,
                "DTS": false,
                "FLAC": true,
                "Opus": true
            },
            Containers: {
                "MP4": true,
                "M4V": true,
                "MOV": true,
                "MKV": false,
                "WebM": true,
                "TS": false
            },
            SubtitleFormats: {
                "SRT": true,
                "VTT": true,
                "ASS": false,
                "SSA": false
            },
            BitDepths: {
                "8": true,
                "10": false,
                "12": false
            }
        },
        // Include default weights to ensure they're saved too
        weights: {
            SurroundSound: 3,
            HDR: 8,
            HighBitrate: 5,
            IncorrectCodecTag: 12,
            UnsupportedVideoCodec: 35,
            UnsupportedAudioCodec: 25,
            UnsupportedContainer: 30,
            UnsupportedSubtitleFormat: 8,
            UnsupportedBitDepth: 18,
            HighBitrateThresholdMbps: 40.0
        }
    };
    
    try {
        const response = await fetch('/api/library/settings/media-properties', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(defaultSettings)
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to initialize default settings');
        }
        
        // Update local state immediately
        mediaPropertySettings = defaultSettings.properties;
        ratingWeights = defaultSettings.weights;
        
        // Render the settings
        renderMediaPropertySettings();
        renderRatingWeights();
    } catch (error) {
        console.error('Error initializing default settings:', error);
        // Show error message in the container
        const container = document.getElementById('mediaPropertySettingsContainer');
        if (container) {
            container.innerHTML = `
                <div class="error-message" style="padding: 1.5rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid #dc3545;">
                    <p style="margin: 0 0 1rem 0; color: var(--text-primary);">
                        Failed to initialize default settings: ${error.message}
                    </p>
                    <button type="button" class="btn btn-primary" onclick="initializeDefaultMediaPropertySettings()">
                        Try Again
                    </button>
                </div>
            `;
        }
    }
}

function renderMediaPropertySettings() {
    const container = document.getElementById('mediaPropertySettingsContainer');
    if (!container || !mediaPropertySettings) return;

    // Before rendering, capture current checkbox states from DOM if they exist
    // This preserves user modifications even if mediaPropertySettings module variable has old values
    const currentCheckboxStates = {};
    const existingCheckboxes = container.querySelectorAll('.media-property-checkbox');
    if (existingCheckboxes.length > 0) {
        existingCheckboxes.forEach(checkbox => {
            const category = checkbox.dataset.category;
            const property = checkbox.dataset.property;
            if (category && property) {
                if (!currentCheckboxStates[category]) {
                    currentCheckboxStates[category] = {};
                }
                currentCheckboxStates[category][property] = checkbox.checked;
            }
        });
        
        // If we have user-modified states, merge them with mediaPropertySettings
        // This ensures user changes are preserved
        if (Object.keys(currentCheckboxStates).length > 0) {
            Object.keys(currentCheckboxStates).forEach(category => {
                if (mediaPropertySettings[category]) {
                    Object.keys(currentCheckboxStates[category]).forEach(property => {
                        mediaPropertySettings[category][property] = currentCheckboxStates[category][property];
                    });
                }
            });
            // Also update last known user settings
            lastKnownUserPropertySettings = JSON.parse(JSON.stringify(mediaPropertySettings));
            
            // Persist to localStorage so it survives page reloads
            try {
                localStorage.setItem('optimarr_lastKnownUserPropertySettings', JSON.stringify(lastKnownUserPropertySettings));
            } catch (e) {
                // Ignore errors writing to localStorage
            }
        }
    } else if (lastKnownUserPropertySettings && !window._forceReloadMediaSettings) {
        // No checkboxes in DOM yet, but we have last known user settings - use those
        // Only do this if not force reloading (after save, we want server values)
        mediaPropertySettings = JSON.parse(JSON.stringify(lastKnownUserPropertySettings));
    }

    let html = '<div class="media-property-settings">';
    
    // Video Codecs
    html += '<div class="property-category" style="margin-bottom: 2rem;">';
    html += '<h5 style="margin-bottom: 1rem; color: var(--text-primary);">Video Codecs</h5>';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.75rem;">';
    
    const videoCodecs = Object.keys(mediaPropertySettings.VideoCodecs || {}).sort();
    videoCodecs.forEach(codec => {
        const isSupported = mediaPropertySettings.VideoCodecs[codec];
        html += `
            <label class="checkbox-label" style="display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color); cursor: pointer;">
                <input type="checkbox" 
                       class="media-property-checkbox" 
                       data-category="VideoCodecs"
                       data-property="${escapeHtml(codec)}" 
                       ${isSupported ? 'checked' : ''}
                       onchange="updateMediaPropertySetting(this)">
                <span style="font-weight: 500; color: var(--text-primary);">${escapeHtml(codec)}</span>
            </label>
        `;
    });
    html += '</div></div>';

    // Audio Codecs
    html += '<div class="property-category" style="margin-bottom: 2rem;">';
    html += '<h5 style="margin-bottom: 1rem; color: var(--text-primary);">Audio Codecs</h5>';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.75rem;">';
    
    const audioCodecs = Object.keys(mediaPropertySettings.AudioCodecs || {}).sort();
    audioCodecs.forEach(codec => {
        const isSupported = mediaPropertySettings.AudioCodecs[codec];
        html += `
            <label class="checkbox-label" style="display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color); cursor: pointer;">
                <input type="checkbox" 
                       class="media-property-checkbox" 
                       data-category="AudioCodecs"
                       data-property="${escapeHtml(codec)}" 
                       ${isSupported ? 'checked' : ''}
                       onchange="updateMediaPropertySetting(this)">
                <span style="font-weight: 500; color: var(--text-primary);">${escapeHtml(codec)}</span>
            </label>
        `;
    });
    html += '</div></div>';

    // Containers
    html += '<div class="property-category" style="margin-bottom: 2rem;">';
    html += '<h5 style="margin-bottom: 1rem; color: var(--text-primary);">Containers</h5>';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.75rem;">';
    
    const containers = Object.keys(mediaPropertySettings.Containers || {}).sort();
    containers.forEach(container => {
        const isSupported = mediaPropertySettings.Containers[container];
        html += `
            <label class="checkbox-label" style="display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color); cursor: pointer;">
                <input type="checkbox" 
                       class="media-property-checkbox" 
                       data-category="Containers"
                       data-property="${escapeHtml(container)}" 
                       ${isSupported ? 'checked' : ''}
                       onchange="updateMediaPropertySetting(this)">
                <span style="font-weight: 500; color: var(--text-primary);">${escapeHtml(container)}</span>
            </label>
        `;
    });
    html += '</div></div>';

    // Subtitle Formats
    html += '<div class="property-category" style="margin-bottom: 2rem;">';
    html += '<h5 style="margin-bottom: 1rem; color: var(--text-primary);">Subtitle Formats</h5>';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.75rem;">';
    
    const subtitleFormats = Object.keys(mediaPropertySettings.SubtitleFormats || {}).sort();
    subtitleFormats.forEach(format => {
        const isSupported = mediaPropertySettings.SubtitleFormats[format];
        html += `
            <label class="checkbox-label" style="display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color); cursor: pointer;">
                <input type="checkbox" 
                       class="media-property-checkbox" 
                       data-category="SubtitleFormats"
                       data-property="${escapeHtml(format)}" 
                       ${isSupported ? 'checked' : ''}
                       onchange="updateMediaPropertySetting(this)">
                <span style="font-weight: 500; color: var(--text-primary);">${escapeHtml(format)}</span>
            </label>
        `;
    });
    html += '</div></div>';

    // Bit Depths
    html += '<div class="property-category" style="margin-bottom: 2rem;">';
    html += '<h5 style="margin-bottom: 1rem; color: var(--text-primary);">Bit Depths</h5>';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 0.75rem;">';
    
    const bitDepths = Object.keys(mediaPropertySettings.BitDepths || {}).sort();
    bitDepths.forEach(bitDepth => {
        const isSupported = mediaPropertySettings.BitDepths[bitDepth];
        html += `
            <label class="checkbox-label" style="display: flex; align-items: center; gap: 0.75rem; padding: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border: 1px solid var(--border-color); cursor: pointer;">
                <input type="checkbox" 
                       class="media-property-checkbox" 
                       data-category="BitDepths"
                       data-property="${escapeHtml(bitDepth)}" 
                       ${isSupported ? 'checked' : ''}
                       onchange="updateMediaPropertySetting(this)">
                <span style="font-weight: 500; color: var(--text-primary);">${escapeHtml(bitDepth)}-bit</span>
            </label>
        `;
    });
    html += '</div></div>';

    html += '<div style="margin-top: 1.5rem;">';
    html += '<button type="button" class="btn btn-primary" onclick="saveMediaPropertySettings()">Save Media Property Settings</button>';
    html += '</div>';
    html += '</div>';
    
    container.innerHTML = html;
}

function renderRatingWeights() {
    const container = document.getElementById('ratingWeightsContainer');
    if (!container) return;
    
    // Default values matching the C# model - always available as fallback
    const defaultWeights = {
        SurroundSound: 3,
        HDR: 8,
        HighBitrate: 5,
        IncorrectCodecTag: 12,
        UnsupportedVideoCodec: 35,
        UnsupportedAudioCodec: 25,
        UnsupportedContainer: 30,
        UnsupportedSubtitleFormat: 8,
        UnsupportedBitDepth: 18,
        HighBitrateThresholdMbps: 40.0
    };
    
    // If ratingWeights is not set or is empty, initialize with defaults
    if (!ratingWeights || Object.keys(ratingWeights).length === 0) {
        ratingWeights = { ...defaultWeights };
        // Try to load asynchronously (won't help current render, but will fix next time)
        loadMediaPropertySettings().catch(() => {
            // Silently fail - will retry on next render
        });
    } else {
        // Ensure all required keys are present (merge with defaults for missing keys)
        Object.keys(defaultWeights).forEach(key => {
            if (!ratingWeights.hasOwnProperty(key) || ratingWeights[key] === null || ratingWeights[key] === undefined) {
                ratingWeights[key] = defaultWeights[key];
            }
        });
    }
    

    let html = '<div class="rating-weights-settings">';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem;">';
    
    const weightFields = [
        { key: 'SurroundSound', label: 'Stereo Sound', help: 'Impact when audio is stereo (2 channels or less)' },
        { key: 'HDR', label: 'SDR Content', help: 'Impact when video is SDR (no HDR)' },
        { key: 'HighBitrate', label: 'High Bitrate', help: 'Impact when bitrate exceeds threshold' },
        { key: 'IncorrectCodecTag', label: 'Incorrect Codec Tag', help: 'Impact when codec tag is incorrect' },
        { key: 'UnsupportedVideoCodec', label: 'Unsupported Video Codec', help: 'Impact when video codec is unsupported' },
        { key: 'UnsupportedAudioCodec', label: 'Unsupported Audio Codec', help: 'Impact when audio codec is unsupported' },
        { key: 'UnsupportedContainer', label: 'Unsupported Container', help: 'Impact when container is unsupported' },
        { key: 'UnsupportedSubtitleFormat', label: 'Unsupported Subtitle Format', help: 'Impact when subtitle format is unsupported' },
        { key: 'UnsupportedBitDepth', label: 'Unsupported Bit Depth', help: 'Impact when bit depth is unsupported' },
        { key: 'FastStart', label: 'Missing Fast Start (MP4)', help: 'Impact when MP4 file lacks fast start optimization' }
    ];
    
    weightFields.forEach(field => {
        // Use the value from ratingWeights - it should always be set after loadMediaPropertySettings
        // Allow 0 as a valid value (don't treat it as missing)
        // Check if the key exists in ratingWeights object (even if value is 0)
        let value;
        if (ratingWeights && ratingWeights.hasOwnProperty(field.key)) {
            // Key exists in ratingWeights, use it (even if 0)
            value = ratingWeights[field.key];
            // Only use default if value is explicitly null or undefined
            if (value === null || value === undefined) {
                value = defaultWeights[field.key] !== undefined ? defaultWeights[field.key] : 0;
            }
        } else {
            // Key doesn't exist, use default from model
            value = defaultWeights[field.key] !== undefined ? defaultWeights[field.key] : 0;
        }
        html += `
            <div class="form-group">
                <label style="font-weight: 500;">${field.label}</label>
                <input type="number" 
                       class="form-control rating-weight-input" 
                       data-weight="${field.key}"
                       value="${value}" 
                       min="0" 
                       max="100" 
                       step="1">
                <small class="form-help">${field.help}</small>
            </div>
        `;
    });

    html += '</div>';
    
    // High Bitrate Threshold
    html += '<div class="form-group" style="margin-top: 1.5rem; max-width: 100%;">';
    html += '<label style="font-weight: 500;">High Bitrate Threshold (Mbps)</label>';
    let thresholdValue;
    if (ratingWeights && ratingWeights.hasOwnProperty('HighBitrateThresholdMbps')) {
        thresholdValue = ratingWeights.HighBitrateThresholdMbps;
        // Only use default if value is explicitly null or undefined
        if (thresholdValue === null || thresholdValue === undefined) {
            thresholdValue = defaultWeights.HighBitrateThresholdMbps;
        }
    } else {
        thresholdValue = defaultWeights.HighBitrateThresholdMbps;
    }
    html += `<input type="number" 
                    class="form-control rating-weight-input" 
                    data-weight="HighBitrateThresholdMbps"
                    value="${thresholdValue}" 
                    min="1" 
                    max="500" 
                    step="0.1">`;
    html += '<small class="form-help">Bitrate above this threshold triggers the High Bitrate penalty</small>';
    html += '</div>';

    html += '<div style="margin-top: 1.5rem;">';
    html += '<button type="button" class="btn btn-primary" onclick="saveRatingWeights()">Save Rating Weights</button>';
    html += '</div>';
    html += '</div>';
    
    container.innerHTML = html;
}

function renderRatingThresholds(thresholds) {
    const container = document.getElementById('ratingThresholdsContainer');
    if (!container) return;
    
    // Use provided thresholds or defaults
    const defaultThresholds = { Optimal: 80, Good: 60 };
    // Prefer provided parameter, then module variable, then defaults
    const currentThresholds = thresholds || ratingThresholds || defaultThresholds;
    
    // Check if there are existing inputs with user-modified values before we overwrite them
    // If user has modified values, preserve them instead of overwriting with server values
    const existingInputs = container.querySelectorAll('.rating-threshold-input');
    const existingValues = {};
    let hasUserModifications = false;
    let hasActiveFocus = false;
    
    // First, check if inputs exist and have been modified by the user
    existingInputs.forEach(input => {
        const key = input.dataset.threshold;
        if (key) {
            const existingValue = parseInt(input.value.trim(), 10);
            const expectedValue = currentThresholds[key];
            existingValues[key] = input.value; // Store raw value for comparison
            
            // Check if input has focus (user is actively editing)
            if (document.activeElement === input) {
                hasActiveFocus = true;
            }
            
            // Check if this value differs from what we're about to render
            // AND it's not just the default (to avoid false positives on first render)
            if (!isNaN(existingValue) && existingValue !== expectedValue) {
                // Check if this looks like a user modification (not just uninitialized)
                // User modifications are valid numbers that differ from expected
                if (existingValue >= 0 && existingValue <= 100) {
                    hasUserModifications = true;
                }
            }
        }
    });
    
    // If user has focus on an input or has modified values, ALWAYS preserve them
    if (hasActiveFocus || (hasUserModifications && Object.keys(existingValues).length > 0)) {
        if (hasActiveFocus) {
            return; // Don't render at all if user is actively editing
        }
        
        // Update the thresholds we'll render with the user's values
        Object.keys(existingValues).forEach(key => {
            const parsed = parseInt(existingValues[key].trim(), 10);
            if (!isNaN(parsed) && parsed >= 0 && parsed <= 100) {
                currentThresholds[key] = parsed;
            }
        });
        // Also update the module variable so it's in sync
        ratingThresholds = { ...currentThresholds };
    }
    
    let html = '<div class="rating-thresholds-settings">';
    html += '<div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(250px, 1fr)); gap: 1.5rem;">';
    
    // Optimal threshold
    html += `
        <div class="form-group">
            <label style="font-weight: 500;">Optimal Threshold</label>
            <input type="number" 
                   class="form-control rating-threshold-input" 
                   data-threshold="Optimal"
                   value="${currentThresholds.Optimal !== undefined && currentThresholds.Optimal !== null ? currentThresholds.Optimal : 80}" 
                   min="0" 
                   max="100" 
                   step="1">
            <small class="form-help">Minimum rating (0-100) for "Optimal" classification. Videos with rating ≥ this value are marked as Optimal.</small>
        </div>
    `;
    
    // Good threshold
    html += `
        <div class="form-group">
            <label style="font-weight: 500;">Good Threshold</label>
            <input type="number" 
                   class="form-control rating-threshold-input" 
                   data-threshold="Good"
                   value="${currentThresholds.Good !== undefined && currentThresholds.Good !== null ? currentThresholds.Good : 60}" 
                   min="0" 
                   max="100" 
                   step="1">
            <small class="form-help">Minimum rating (0-100) for "Good" classification. Videos with rating ≥ this value (but &lt; Optimal) are marked as Good.</small>
        </div>
    `;
    
    html += '</div>';
    
    // Info box explaining the categories
    html += '<div class="info-box" style="margin-top: 1.5rem; padding: 1rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--accent-color);">';
    html += '<p style="margin: 0 0 0.5rem 0; font-weight: 600; color: var(--text-primary);">Rating Categories:</p>';
    html += '<ul style="margin: 0; padding-left: 1.5rem; color: var(--text-secondary); font-size: 0.875rem;">';
    const optimalValue = currentThresholds.Optimal !== undefined && currentThresholds.Optimal !== null ? currentThresholds.Optimal : 80;
    const goodValue = currentThresholds.Good !== undefined && currentThresholds.Good !== null ? currentThresholds.Good : 60;
    html += `<li><strong>Optimal:</strong> Rating ≥ ${optimalValue} - Best compatibility, no issues expected</li>`;
    html += `<li><strong>Good:</strong> Rating ≥ ${goodValue} and &lt; ${optimalValue} - Generally compatible, minor issues possible</li>`;
    html += `<li><strong>Poor:</strong> Rating &lt; ${goodValue} - May have compatibility issues, transcoding likely required</li>`;
    html += '</ul>';
    html += '</div>';
    
    html += '<div style="margin-top: 1.5rem;">';
    html += '<button type="button" class="btn btn-primary" onclick="saveRatingThresholds()">Save Rating Thresholds</button>';
    html += '</div>';
    html += '</div>';
    
    container.innerHTML = html;
    
    // Store thresholds for later use
    ratingThresholds = currentThresholds;
    
    // After rendering, restore any last-known user values if they exist
    // This ensures user modifications persist across re-renders
    if (Object.keys(lastKnownUserThresholdValues).length > 0) {
        const renderedInputs = container.querySelectorAll('.rating-threshold-input');
        renderedInputs.forEach(input => {
            const key = input.dataset.threshold;
            if (key && lastKnownUserThresholdValues[key] !== undefined) {
                input.value = lastKnownUserThresholdValues[key];
            }
        });
        // Update module variable to match restored values
        ratingThresholds = { ...ratingThresholds, ...lastKnownUserThresholdValues };
    }
    
    // Add event listeners to track when user changes threshold values
    // Re-query inputs after innerHTML update to get fresh references
    // (old elements are destroyed by innerHTML, so no need to worry about duplicates)
    const freshInputs = container.querySelectorAll('.rating-threshold-input');
    freshInputs.forEach(input => {
        const key = input.dataset.threshold;
        if (key) {
            // Add listener to track user changes
            input.addEventListener('input', () => {
                const value = parseInt(input.value.trim(), 10);
                if (!isNaN(value) && value >= 0 && value <= 100) {
                    lastKnownUserThresholdValues[key] = value;
                }
            });
            
            // Also track on blur (when user finishes editing)
            input.addEventListener('blur', () => {
                const value = parseInt(input.value.trim(), 10);
                if (!isNaN(value) && value >= 0 && value <= 100) {
                    lastKnownUserThresholdValues[key] = value;
                }
            });
        }
    });
}

export async function saveRatingThresholds() {
    // saveRatingThresholds now just calls saveMediaPropertySettings
    // which reads properties, weights, and thresholds from DOM
    await saveMediaPropertySettings();
}


export function updateMediaPropertySetting(checkbox) {
    const category = checkbox.dataset.category;
    const property = checkbox.dataset.property;
    const isSupported = checkbox.checked;

    if (!mediaPropertySettings) return;

    if (mediaPropertySettings[category] && mediaPropertySettings[category][property] !== undefined) {
        mediaPropertySettings[category][property] = isSupported;
        
        // Update last known user settings to track this change
        if (!lastKnownUserPropertySettings) {
            lastKnownUserPropertySettings = JSON.parse(JSON.stringify(mediaPropertySettings));
        } else {
            if (!lastKnownUserPropertySettings[category]) {
                lastKnownUserPropertySettings[category] = {};
            }
            lastKnownUserPropertySettings[category][property] = isSupported;
        }
        
        // Persist to localStorage so it survives page reloads
        try {
            localStorage.setItem('optimarr_lastKnownUserPropertySettings', JSON.stringify(lastKnownUserPropertySettings));
        } catch (e) {
            // Ignore errors writing to localStorage
        }
    }
}

export async function saveMediaPropertySettings() {
    try {
        // CRITICAL: Read threshold values IMMEDIATELY from DOM inputs
        // Don't rely on module variables - the DOM inputs are the source of truth
        // This must happen FIRST, before any other processing
        const thresholdInputs = document.querySelectorAll('.rating-threshold-input');
        const currentDomThresholds = {};
        thresholdInputs.forEach(input => {
            const key = input.dataset.threshold;
            if (key) {
                // Read the actual current value from the input element
                const rawValue = input.value;
                const parsedValue = parseInt(rawValue.trim(), 10);
                if (!isNaN(parsedValue) && parsedValue >= 0 && parsedValue <= 100) {
                    currentDomThresholds[key] = parsedValue;
                }
            }
        });
        
        // If DOM has defaults but we have last-known user values, use those instead
        // This handles the case where inputs were reset to defaults but user had changed them
        if (Object.keys(lastKnownUserThresholdValues).length > 0) {
            const hasDefaults = Object.keys(currentDomThresholds).every(key => {
                return currentDomThresholds[key] === 80 || currentDomThresholds[key] === 60;
            });
            
            if (hasDefaults && Object.keys(currentDomThresholds).length === 2) {
                // DOM has defaults, but user had changed values - use last known values
                Object.keys(lastKnownUserThresholdValues).forEach(key => {
                    currentDomThresholds[key] = lastKnownUserThresholdValues[key];
                });
            }
        }
        
        // Update module variable with what we just read from DOM (or last known values)
        if (Object.keys(currentDomThresholds).length > 0) {
            ratingThresholds = { ...currentDomThresholds };
        }
        
        // Start with current mediaPropertySettings (which may have defaults for display)
        // This ensures we don't lose values if DOM hasn't been rendered yet
        const properties = {
            VideoCodecs: { ...(mediaPropertySettings?.VideoCodecs || {}) },
            AudioCodecs: { ...(mediaPropertySettings?.AudioCodecs || {}) },
            Containers: { ...(mediaPropertySettings?.Containers || {}) },
            SubtitleFormats: { ...(mediaPropertySettings?.SubtitleFormats || {}) },
            BitDepths: { ...(mediaPropertySettings?.BitDepths || {}) }
        };
        
        // Read all checkbox values from DOM and override with current DOM state
        // This ensures what user sees is what gets saved
        const checkboxStatesFromDOM = {};
        document.querySelectorAll('.media-property-checkbox').forEach(checkbox => {
            const category = checkbox.dataset.category;
            const property = checkbox.dataset.property;
            const isSupported = checkbox.checked;
            
            if (category && property && properties[category]) {
                properties[category][property] = isSupported;
                
                // Track checkbox states for debugging
                if (!checkboxStatesFromDOM[category]) {
                    checkboxStatesFromDOM[category] = {};
                }
                checkboxStatesFromDOM[category][property] = isSupported;
            }
        });
        
        // Ensure we have at least the structure even if empty
        if (!properties.VideoCodecs) properties.VideoCodecs = {};
        if (!properties.AudioCodecs) properties.AudioCodecs = {};
        if (!properties.Containers) properties.Containers = {};
        if (!properties.SubtitleFormats) properties.SubtitleFormats = {};
        if (!properties.BitDepths) properties.BitDepths = {};
        
        // Read rating weights from DOM inputs (same as saveRatingWeights)
        const defaults = {
            'SurroundSound': 3,
            'HDR': 8,
            'HighBitrate': 5,
            'IncorrectCodecTag': 12,
            'UnsupportedVideoCodec': 35,
            'UnsupportedAudioCodec': 25,
            'UnsupportedContainer': 30,
            'UnsupportedSubtitleFormat': 8,
            'UnsupportedBitDepth': 18,
            'FastStart': 5,
            'HighBitrateThresholdMbps': 40.0
        };
        
        const weights = {};
        document.querySelectorAll('.rating-weight-input').forEach(input => {
            const key = input.dataset.weight;
            if (!key) return;
            
            const inputValue = input.value.trim();
            
            if (key === 'HighBitrateThresholdMbps') {
                const parsed = parseFloat(inputValue);
                if (!isNaN(parsed) && parsed > 0) {
                    weights[key] = parsed;
                } else {
                    weights[key] = defaults[key];
                }
            } else {
                const parsed = parseInt(inputValue, 10);
                if (!isNaN(parsed) && parsed >= 0) {
                    weights[key] = parsed;
                } else {
                    weights[key] = defaults[key];
                }
            }
        });
        
        // Ensure all required weight properties are present
        Object.keys(defaults).forEach(key => {
            if (weights[key] === undefined || weights[key] === null) {
                weights[key] = defaults[key];
            }
        });
        
        // Use the thresholds we already read at the start of the function
        // This ensures we're using the values that were in the DOM when save was called
        const thresholds = { ...currentDomThresholds };
        console.log('Using thresholds read from DOM:', thresholds);
        
        // If we didn't get thresholds from DOM, try reading again (fallback)
        if (Object.keys(thresholds).length === 0) {
            console.warn('No thresholds read from DOM initially, trying again...');
            const thresholdInputs = document.querySelectorAll('.rating-threshold-input');
            thresholdInputs.forEach(input => {
                const key = input.dataset.threshold;
                if (key) {
                    const value = parseInt(input.value.trim(), 10);
                    if (!isNaN(value) && value >= 0 && value <= 100) {
                        thresholds[key] = value;
                    }
                }
            });
        }
        
        // Validate thresholds: Optimal should be >= Good
        if (thresholds.Optimal !== undefined && thresholds.Good !== undefined) {
            if (thresholds.Optimal < thresholds.Good) {
                alert('Error: Optimal threshold must be greater than or equal to Good threshold.');
                return;
            }
        }
        
        // If thresholds are empty, use current values from module variable
        if (Object.keys(thresholds).length === 0 && ratingThresholds) {
            thresholds.Optimal = ratingThresholds.Optimal;
            thresholds.Good = ratingThresholds.Good;
        }
        
        // Ensure properties are not empty - if they are, use defaults
        // This handles the case where properties were empty in config but displayed with defaults
        const hasAnyProperties = Object.keys(properties.VideoCodecs).length > 0 ||
                                 Object.keys(properties.AudioCodecs).length > 0 ||
                                 Object.keys(properties.Containers).length > 0 ||
                                 Object.keys(properties.SubtitleFormats).length > 0 ||
                                 Object.keys(properties.BitDepths).length > 0;
        
        if (!hasAnyProperties) {
            // Properties are empty, use defaults
            const defaultProperties = {
                VideoCodecs: {
                    "H.264": true,
                    "H.264 8-bit": true,
                    "H.265": true,
                    "H.265 8-bit": true,
                    "H.265 10-bit": false,
                    "VP9": true,
                    "AV1": false
                },
                AudioCodecs: {
                    "AAC": true,
                    "MP3": true,
                    "AC3": false,
                    "EAC3": false,
                    "DTS": false,
                    "FLAC": true,
                    "Opus": true
                },
                Containers: {
                    "MP4": true,
                    "M4V": true,
                    "MOV": true,
                    "MKV": false,
                    "WebM": true,
                    "TS": false
                },
                SubtitleFormats: {
                    "SRT": true,
                    "VTT": true,
                    "ASS": false,
                    "SSA": false
                },
                BitDepths: {
                    "8": true,
                    "10": false,
                    "12": false
                }
            };
            properties = defaultProperties;
        }
        
        // Prepare request
        const request = {
            properties: properties,
            weights: weights
        };
        
        // Always include thresholds - use what we read from DOM at the start
        // This is the source of truth - what the user actually entered
        if (Object.keys(thresholds).length > 0) {
            request.thresholds = thresholds;
        } else if (ratingThresholds && Object.keys(ratingThresholds).length > 0) {
            // Fallback to module variable (which we updated from DOM at start)
            request.thresholds = ratingThresholds;
        } else {
            // Last resort: use defaults (shouldn't happen if inputs exist)
            request.thresholds = { Optimal: 80, Good: 60 };
        }
        
        const response = await fetch('/api/library/settings/media-properties', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(request)
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to save media property settings');
        }
        
        // Update local state
        mediaPropertySettings = properties;
        ratingWeights = weights;
        if (Object.keys(thresholds).length > 0) {
            ratingThresholds = thresholds;
            // Also update last known user values since we're saving these
            lastKnownUserThresholdValues = { ...thresholds };
        }
        
        // Update last known user property settings since we're saving these
        lastKnownUserPropertySettings = JSON.parse(JSON.stringify(properties));
        
        // Persist to localStorage so it survives page reloads
        try {
            localStorage.setItem('optimarr_lastKnownUserPropertySettings', JSON.stringify(lastKnownUserPropertySettings));
        } catch (e) {
            // Ignore errors writing to localStorage
        }
        
        alert('Media property settings saved successfully! Compatibility is being recalculated for all videos.');
        
        // Before reloading, capture any user-modified threshold values that weren't saved
        // (in case user changed thresholds but only saved properties/weights)
        const unsavedThresholdValues = {};
        document.querySelectorAll('.rating-threshold-input').forEach(input => {
            const key = input.dataset.threshold;
            if (key) {
                const value = parseInt(input.value.trim(), 10);
                if (!isNaN(value) && value >= 0 && value <= 100) {
                    unsavedThresholdValues[key] = value;
                }
            }
        });
        // Also capture current checkbox states before reload
        const currentCheckboxStates = {};
        document.querySelectorAll('.media-property-checkbox').forEach(checkbox => {
            const category = checkbox.dataset.category;
            const property = checkbox.dataset.property;
            if (category && property) {
                if (!currentCheckboxStates[category]) {
                    currentCheckboxStates[category] = {};
                }
                currentCheckboxStates[category][property] = checkbox.checked;
            }
        });
        
        // Update last known user settings with current states
        if (Object.keys(currentCheckboxStates).length > 0) {
            lastKnownUserPropertySettings = JSON.parse(JSON.stringify(currentCheckboxStates));
            
            // Persist to localStorage so it survives page reloads
            try {
                localStorage.setItem('optimarr_lastKnownUserPropertySettings', JSON.stringify(lastKnownUserPropertySettings));
            } catch (e) {
                // Ignore errors writing to localStorage
            }
        }
        
        // Reload settings from server to get the saved values (with a small delay to ensure config is reloaded)
        // Force reload to skip any input preservation logic and always read from server
        // Increased delay to ensure backend has time to write and reload config file
        await new Promise(resolve => setTimeout(resolve, 1000));
        await loadMediaPropertySettings(true);
        
        // After reload, if user had unsaved threshold changes, restore them
        if (Object.keys(unsavedThresholdValues).length > 0) {
            // Update the module variable
            ratingThresholds = { ...ratingThresholds, ...unsavedThresholdValues };
            // Re-render thresholds with the preserved values
            renderRatingThresholds(ratingThresholds);
        }
        
        // After reload, if we have last-known user property settings, restore checkbox states
        if (lastKnownUserPropertySettings) {
            // Update mediaPropertySettings with last known values
            Object.keys(lastKnownUserPropertySettings).forEach(category => {
                if (lastKnownUserPropertySettings[category]) {
                    if (!mediaPropertySettings[category]) {
                        mediaPropertySettings[category] = {};
                    }
                    Object.keys(lastKnownUserPropertySettings[category]).forEach(property => {
                        mediaPropertySettings[category][property] = lastKnownUserPropertySettings[category][property];
                    });
                }
            });
            // Re-render with the preserved values
            renderMediaPropertySettings();
        }
        
        // Refresh grids if they're visible
        refreshVisibleGrids();
    } catch (error) {
        console.error('Error saving media property settings:', error);
        alert('Failed to save media property settings: ' + error.message);
    }
}

export async function saveRatingWeights() {
    // saveRatingWeights now just calls saveMediaPropertySettings
    // which reads both properties and weights from DOM
    await saveMediaPropertySettings();
}


// Export to window for onclick handlers
window.saveJellyfinSettings = saveJellyfinSettings;
window.loadSonarrSettings = loadSonarrSettings;
window.loadRadarrSettings = loadRadarrSettings;
window.loadMediaPropertySettings = loadMediaPropertySettings;
window.initializeDefaultMediaPropertySettings = initializeDefaultMediaPropertySettings;
window.updateMediaPropertySetting = updateMediaPropertySetting;
window.saveMediaPropertySettings = saveMediaPropertySettings;
window.saveRatingWeights = saveRatingWeights;
window.saveRatingThresholds = saveRatingThresholds;

