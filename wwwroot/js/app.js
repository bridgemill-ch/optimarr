// Main Application Entry Point
import { initNavigation } from './navigation.js';
import { loadDashboard } from './dashboard.js';
import { loadKnownLibraries, loadRecentScans, startScanPolling, reconnectToRunningScans, startProcessingCountPolling, loadProcessingVideos } from './library.js';
import { loadBrowseFilterOptions, loadBrowseMedia, setupBrowseEventListeners } from './browse.js';
import { loadJellyfinSettings, loadSonarrSettings, loadRadarrSettings, loadSonarrPathMappings, loadRadarrPathMappings } from './settings.js';
import { loadServarrStatus, checkActiveMatches } from './servarr.js';
import { showAddLibraryModal, closeAddLibraryModal } from './library-modals.js';
import { closeMediaModal, closeTrackDetailsModal, closeRatingDetailsModal } from './media-info.js';
import { closePathBrowser as closePathBrowserModal } from './path-browser.js';
import { checkMigrationStatus } from './migration.js';

// Rating Info Box Functions - Removed (banner no longer exists)

// System Functions
async function loadAppVersion() {
    try {
        const response = await fetch('/api/system/version');
        if (!response.ok) {
            const versionEl = document.getElementById('appVersion');
            if (versionEl) versionEl.textContent = 'Unknown';
            return;
        }
        
        const data = await response.json();
        const versionEl = document.getElementById('appVersion');
        if (versionEl) versionEl.textContent = data.version || 'Unknown';
    } catch (error) {
        console.error('Error loading app version:', error);
        const versionEl = document.getElementById('appVersion');
        if (versionEl) versionEl.textContent = 'Unknown';
    }
}

async function handleReboot() {
    if (!confirm('Are you sure you want to reboot the application?\n\nThis will restart the Optimarr service.')) {
        return;
    }
    
    const button = document.getElementById('rebootButton');
    if (button) {
        button.disabled = true;
        button.innerHTML = '<span class="nav-icon">⏳</span><span>Rebooting...</span>';
    }
    
    try {
        const response = await fetch('/api/system/reboot', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            }
        });
        
        if (!response.ok) {
            throw new Error('Failed to initiate reboot');
        }
        
        const data = await response.json();
        alert(data.message || 'Reboot request sent. The application will restart shortly.');
        
        // Wait a moment then reload the page
        setTimeout(() => {
            window.location.reload();
        }, 2000);
    } catch (error) {
        console.error('Reboot error:', error);
        alert('Error initiating reboot: ' + error.message + '\n\nPlease restart the application manually.');
        
        if (button) {
            button.disabled = false;
            button.innerHTML = '<span class="nav-icon">🔄</span><span>Reboot</span>';
        }
    }
}

// Initialize application when DOM is ready
document.addEventListener('DOMContentLoaded', function() {
    // Load app version
    loadAppVersion();
    
    // Initialize navigation
    initNavigation();
    
    // Rating settings form
    // Load Servarr status on page load
    loadServarrStatus();
    
    // Check for active matches and resume if needed
    checkActiveMatches();
    
    loadAppVersion();
    
    // Load dashboard on page load
    loadDashboard();
    
    // Load Jellyfin settings on page load
    loadJellyfinSettings();
    
    // Load Sonarr and Radarr settings on page load
    loadSonarrSettings();
    loadRadarrSettings();
    loadSonarrPathMappings();
    loadRadarrPathMappings();
    
    // Load library scans
    loadRecentScans();
    
    // Reconnect to any running scans (survives page reload)
    reconnectToRunningScans();
    
    // Start polling for processing count
    startProcessingCountPolling();
    
    // Check if browse tab is active and load media
    const activeTab = document.querySelector('.nav-item.active');
    if (activeTab && activeTab.getAttribute('data-tab') === 'browse') {
        loadBrowseMedia();
        setupBrowseEventListeners();
    }
    
    // Check migration status on startup
    checkMigrationStatus();
    
    // Add Library Form Handler
    const addLibraryForm = document.getElementById('addLibraryForm');
    if (addLibraryForm) {
        addLibraryForm.addEventListener('submit', async function(e) {
            e.preventDefault();
            const name = document.getElementById('newLibraryName').value;
            const path = document.getElementById('newLibraryPath').value;
            const category = document.getElementById('newLibraryCategory').value;
            
            if (!name || !path) {
                alert('Please fill in all fields');
                return;
            }
            
            try {
                const response = await fetch('/api/library/scan', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ path, name, category })
                });
                
                if (!response.ok) {
                    const error = await response.json();
                    throw new Error(error.error || 'Failed to start scan');
                }
                
                const scan = await response.json();
                closeAddLibraryModal();
                addLibraryForm.reset();
                
                // Show scan progress (startScanPolling will handle displaying)
                startScanPolling(scan.id);
                
                // Reload libraries
                loadKnownLibraries();
            } catch (error) {
                console.error('Error adding library:', error);
                alert(`Error adding library: ${error.message}`);
            }
        });
    }
    
    // Close modals when clicking outside
    document.addEventListener('click', function(event) {
        const mediaModal = document.getElementById('mediaInfoModal');
        const addModal = document.getElementById('addLibraryModal');
        const pathModal = document.getElementById('pathBrowserModal');
        const trackModal = document.getElementById('trackDetailsModal');
        const ratingModal = document.getElementById('ratingDetailsModal');
        if (event.target === mediaModal) closeMediaModal();
        if (event.target === trackModal) closeTrackDetailsModal();
        if (event.target === addModal) closeAddLibraryModal();
        if (event.target === pathModal) closePathBrowserModal();
        if (event.target === ratingModal) closeRatingDetailsModal();
    });
});

// Removed handleReboot export - reboot button removed from UI
// Removed closeRatingInfoBox export - banner removed from UI

