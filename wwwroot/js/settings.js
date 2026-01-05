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

export async function loadRatingSettings() {
    try {
        const response = await fetch('/api/library/settings/rating');
        if (!response.ok) return;
        
        const settings = await response.json();
        
        // Update Settings page fields
        const optimalInput = document.getElementById('optimalThreshold');
        const goodDirectInput = document.getElementById('goodDirectThreshold');
        const goodCombinedInput = document.getElementById('goodCombinedThreshold');
        
        if (optimalInput) optimalInput.value = settings.optimalDirectPlayThreshold;
        if (goodDirectInput) goodDirectInput.value = settings.goodDirectPlayThreshold;
        if (goodCombinedInput) goodCombinedInput.value = settings.goodCombinedThreshold;
        
        // Update Settings page info box with current values
        const currentOptimal = document.getElementById('currentOptimalThreshold');
        const currentGoodDirect = document.getElementById('currentGoodDirectThreshold');
        const currentGoodCombined = document.getElementById('currentGoodCombinedThreshold');
        
        if (currentOptimal) currentOptimal.textContent = settings.optimalDirectPlayThreshold;
        if (currentGoodDirect) currentGoodDirect.textContent = settings.goodDirectPlayThreshold;
        if (currentGoodCombined) currentGoodCombined.textContent = settings.goodCombinedThreshold;
        
        // Update Dashboard info box with current values
        const dashboardOptimal = document.getElementById('dashboardOptimalThreshold');
        const dashboardGoodDirect = document.getElementById('dashboardGoodDirectThreshold');
        const dashboardGoodCombined = document.getElementById('dashboardGoodCombinedThreshold');
        
        if (dashboardOptimal) dashboardOptimal.textContent = settings.optimalDirectPlayThreshold;
        if (dashboardGoodDirect) dashboardGoodDirect.textContent = settings.goodDirectPlayThreshold;
        if (dashboardGoodCombined) dashboardGoodCombined.textContent = settings.goodCombinedThreshold;
    } catch (error) {
        console.error('Error loading rating settings:', error);
    }
}

export async function loadCodecThresholds() {
    const container = document.getElementById('codecThresholdsContainer');
    if (!container) return;
    
    try {
        const response = await fetch('/api/library/settings/rating/codecs');
        if (!response.ok) {
            container.innerHTML = '<div class="error-state">Failed to load codec thresholds</div>';
            return;
        }
        
        const data = await response.json();
        const codecs = data.codecs || [];
        
        if (codecs.length === 0) {
            container.innerHTML = '<div class="empty-state">No codec data available</div>';
            return;
        }
        
        container.innerHTML = codecs.map(codec => {
            const supportInfo = `Expected: ${codec.expectedDirectPlay} Direct Play, ${codec.expectedRemux} Remux, ${codec.expectedTranscode} Transcode (out of ${codec.totalClients} clients)`;
            const isDefault = codec.optimalThreshold === codec.defaultOptimalThreshold && 
                             codec.goodDirectThreshold === codec.defaultGoodDirectThreshold &&
                             codec.goodCombinedThreshold === codec.defaultGoodCombinedThreshold;
            
            return `
                <div class="codec-threshold-card" style="margin-bottom: 1.5rem; padding: 1rem; background-color: var(--bg-tertiary); border: 1px solid var(--border-color); border-radius: 6px;">
                    <div style="display: flex; justify-content: space-between; align-items: start; margin-bottom: 1rem;">
                        <div>
                            <h5 style="margin: 0 0 0.5rem 0; color: var(--text-primary); font-size: 1rem;">${escapeHtml(codec.codec)}</h5>
                            <p style="margin: 0; font-size: 0.875rem; color: var(--text-secondary);">${supportInfo}</p>
                        </div>
                        ${isDefault ? '<span style="font-size: 0.75rem; color: var(--text-muted);">Default</span>' : '<span style="font-size: 0.75rem; color: var(--accent-color);">Custom</span>'}
                    </div>
                    <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 1rem;">
                        <div class="form-group" style="margin-bottom: 0;">
                            <label style="font-size: 0.875rem;">Optimal Threshold</label>
                            <input type="number" 
                                   class="form-control codec-threshold-input" 
                                   data-codec="${escapeHtml(codec.codec)}" 
                                   data-type="optimal"
                                   value="${codec.optimalThreshold}" 
                                   min="1" 
                                   max="${codec.totalClients}" 
                                   style="font-size: 0.875rem;">
                            <small class="form-help" style="font-size: 0.75rem;">Default: ${codec.defaultOptimalThreshold}</small>
                        </div>
                        <div class="form-group" style="margin-bottom: 0;">
                            <label style="font-size: 0.875rem;">Good Direct Play</label>
                            <input type="number" 
                                   class="form-control codec-threshold-input" 
                                   data-codec="${escapeHtml(codec.codec)}" 
                                   data-type="goodDirect"
                                   value="${codec.goodDirectThreshold}" 
                                   min="1" 
                                   max="${codec.totalClients}" 
                                   style="font-size: 0.875rem;">
                            <small class="form-help" style="font-size: 0.75rem;">Default: ${codec.defaultGoodDirectThreshold}</small>
                        </div>
                        <div class="form-group" style="margin-bottom: 0;">
                            <label style="font-size: 0.875rem;">Good Combined</label>
                            <input type="number" 
                                   class="form-control codec-threshold-input" 
                                   data-codec="${escapeHtml(codec.codec)}" 
                                   data-type="goodCombined"
                                   value="${codec.goodCombinedThreshold}" 
                                   min="1" 
                                   max="${codec.totalClients * 2}" 
                                   style="font-size: 0.875rem;">
                            <small class="form-help" style="font-size: 0.75rem;">Default: ${codec.defaultGoodCombinedThreshold}</small>
                        </div>
                    </div>
                </div>
            `;
        }).join('');
        
    } catch (error) {
        console.error('Error loading codec thresholds:', error);
        container.innerHTML = `<div class="error-state">Error loading codec thresholds: ${escapeHtml(error.message)}</div>`;
    }
}

export async function saveSettings() {
    try {
        // Save rating settings
        const ratingSettings = {
            optimalDirectPlayThreshold: parseInt(document.getElementById('optimalThreshold').value) || 8,
            goodDirectPlayThreshold: parseInt(document.getElementById('goodDirectThreshold').value) || 5,
            goodCombinedThreshold: parseInt(document.getElementById('goodCombinedThreshold').value) || 8
        };
        
        const ratingResponse = await fetch('/api/library/settings/rating', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(ratingSettings)
        });
        
        if (!ratingResponse.ok) {
            const error = await ratingResponse.json();
            throw new Error(error.error || 'Failed to save rating settings');
        }
        
        await ratingResponse.json();
        
        // Show success message
        alert('Rating settings saved successfully! Changes will apply to newly scanned videos.');
        
        // Reload status
        setTimeout(() => {
            loadServarrStatus();
            loadJellyfinStatus();
            loadRatingSettings(); // Reload to show updated values
        }, 1000);
    } catch (error) {
        console.error('Error saving settings:', error);
        alert(`Error saving settings: ${error.message}`);
    }
}

// Compatibility Settings Functions
let compatibilityData = null;
let compatibilityOverrides = [];

export async function loadCompatibilitySettings() {
    try {
        const response = await fetch('/api/library/settings/compatibility');
        if (!response.ok) throw new Error('Failed to load compatibility settings');
        
        const data = await response.json();
        compatibilityData = data;
        compatibilityOverrides = data.overrides || [];
        
        renderCompatibilitySettings();
    } catch (error) {
        console.error('Error loading compatibility settings:', error);
        const container = document.getElementById('compatibilitySettingsContainer');
        if (container) {
            container.innerHTML = '<div class="error-message">Failed to load compatibility settings. Please try again.</div>';
        }
    }
}

function renderCompatibilitySettings() {
    const container = document.getElementById('compatibilitySettingsContainer');
    if (!container || !compatibilityData) return;

    const categories = [
        { name: 'Video Codecs', key: 'video', codecs: compatibilityData.videoCodecs },
        { name: 'Audio Codecs', key: 'audio', codecs: compatibilityData.audioCodecs },
        { name: 'Containers', key: 'container', codecs: compatibilityData.containers },
        { name: 'Subtitle Formats', key: 'subtitle', codecs: compatibilityData.subtitleFormats }
    ];

    let html = '<div class="compatibility-settings">';
    
    categories.forEach(category => {
        html += '<div class="compatibility-category" style="margin-bottom: 2rem;">';
        html += '<h5 style="margin-bottom: 1rem; color: var(--text-primary);">' + category.name + '</h5>';
        html += '<div class="table-container" style="overflow-x: auto;">';
        html += '<table class="compatibility-table data-table">';
        html += '<thead><tr><th style="min-width: 150px;">Codec</th>';
        
        compatibilityData.clients.forEach(client => {
            html += '<th style="min-width: 100px; text-align: center;">' + escapeHtml(client) + '</th>';
        });
        html += '</tr></thead><tbody>';
        
        category.codecs.forEach(codec => {
            html += '<tr>';
            html += '<td><strong>' + escapeHtml(codec) + '</strong></td>';
            
            compatibilityData.clients.forEach(client => {
                const defaultLevel = getDefaultSupportLevel(category.key, codec, client);
                const override = getOverride(category.key, codec, client);
                const currentLevel = override ? override.SupportLevel : defaultLevel;
                
                html += '<td style="text-align: center;">';
                html += '<select class="compatibility-select" data-category="' + category.key + '" data-codec="' + escapeHtml(codec) + '" data-client="' + escapeHtml(client) + '" onchange="updateCompatibilityOverride(this)">';
                html += '<option value="Supported"' + (currentLevel === 'Supported' ? ' selected' : '') + '>Supported</option>';
                html += '<option value="Partial"' + (currentLevel === 'Partial' ? ' selected' : '') + '>Partial</option>';
                html += '<option value="Unsupported"' + (currentLevel === 'Unsupported' ? ' selected' : '') + '>Unsupported</option>';
                html += '</select>';
                if (override) {
                    html += '<span class="override-indicator" title="Custom override">*</span>';
                }
                html += '</td>';
            });
            
            html += '</tr>';
        });
        
        html += '</tbody></table></div></div>';
    });
    
    html += '<div style="margin-top: 1.5rem;">';
    html += '<button type="button" class="btn btn-primary" onclick="saveCompatibilitySettings()">Save Compatibility Settings</button>';
    html += '<button type="button" class="btn btn-secondary" onclick="resetCompatibilitySettings()" style="margin-left: 0.5rem;">Reset to Defaults</button>';
    html += '<span class="form-help" style="margin-left: 1rem;">* = Custom override</span>';
    html += '</div>';
    html += '</div>';
    
    container.innerHTML = html;
}

function getDefaultSupportLevel(category, codec, client) {
    if (!compatibilityData || !compatibilityData.defaults) return 'Unsupported';
    
    const defaults = compatibilityData.defaults[category];
    if (!defaults || !defaults[codec]) return 'Unsupported';
    
    return defaults[codec][client] || 'Unsupported';
}

function getOverride(category, codec, client) {
    return compatibilityOverrides.find(o => 
        o.Category.toLowerCase() === category.toLowerCase() &&
        o.Codec === codec &&
        o.Client === client
    );
}

export function updateCompatibilityOverride(select) {
    const category = select.dataset.category;
    const codec = select.dataset.codec;
    const client = select.dataset.client;
    const newLevel = select.value;
    
    const defaultLevel = getDefaultSupportLevel(category, codec, client);
    
    // Remove existing override if it matches default
    compatibilityOverrides = compatibilityOverrides.filter(o => 
        !(o.Category.toLowerCase() === category.toLowerCase() &&
          o.Codec === codec &&
          o.Client === client)
    );
    
    // Add override if different from default
    if (newLevel !== defaultLevel) {
        compatibilityOverrides.push({
            Codec: codec,
            Client: client,
            SupportLevel: newLevel,
            Category: category.charAt(0).toUpperCase() + category.slice(1)
        });
    }
    
    // Re-render to update indicators
    renderCompatibilitySettings();
}

export async function saveCompatibilitySettings() {
    try {
        const response = await fetch('/api/library/settings/compatibility', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(compatibilityOverrides)
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.message || 'Failed to save compatibility settings');
        }
        
        const result = await response.json();
        alert('Compatibility settings saved successfully! Changes will apply to newly analyzed videos.');
        
        // Reload to show updated state
        loadCompatibilitySettings();
    } catch (error) {
        console.error('Error saving compatibility settings:', error);
        alert('Failed to save compatibility settings: ' + error.message);
    }
}

export async function resetCompatibilitySettings() {
    if (!confirm('Are you sure you want to reset all compatibility overrides to defaults? This cannot be undone.')) {
        return;
    }
    
    compatibilityOverrides = [];
    renderCompatibilitySettings();
    
    // Save empty overrides
    try {
        const response = await fetch('/api/library/settings/compatibility', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify([])
        });
        
        if (!response.ok) throw new Error('Failed to reset compatibility settings');
        
        alert('Compatibility settings reset to defaults successfully!');
        loadCompatibilitySettings();
    } catch (error) {
        console.error('Error resetting compatibility settings:', error);
        alert('Failed to reset compatibility settings: ' + error.message);
    }
}

// Export to window for onclick handlers
window.updateCompatibilityOverride = updateCompatibilityOverride;
window.saveCompatibilitySettings = saveCompatibilitySettings;
window.resetCompatibilitySettings = resetCompatibilitySettings;
window.saveJellyfinSettings = saveJellyfinSettings;

