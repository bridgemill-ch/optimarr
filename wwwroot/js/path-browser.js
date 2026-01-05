// Path Browser Functions
import { escapeHtml } from './utils.js';

let currentBrowserPath = null;

export function showPathBrowser() {
    const modal = document.getElementById('pathBrowserModal');
    if (modal) {
        modal.style.display = 'block';
        currentBrowserPath = null;
        loadPathBrowser(null);
    }
}

export function closePathBrowser() {
    const modal = document.getElementById('pathBrowserModal');
    if (modal) modal.style.display = 'none';
}

export function navigatePathBrowser(path) {
    if (path === '..') {
        // Navigate to parent
        loadPathBrowser(currentBrowserPath ? getParentPath(currentBrowserPath) : null);
    } else {
        // Navigate to selected directory
        loadPathBrowser(path);
    }
}

function getParentPath(path) {
    // Handle both Windows and Unix paths
    const normalized = path.replace(/\\/g, '/');
    const lastSlash = normalized.lastIndexOf('/');
    if (lastSlash <= 0) {
        return null; // Already at root
    }
    return normalized.substring(0, lastSlash);
}

export async function loadPathBrowser(path) {
    const contentDiv = document.getElementById('pathBrowserContent');
    const currentPathInput = document.getElementById('pathBrowserCurrent');
    const upButton = document.getElementById('pathBrowserUp');
    
    if (!contentDiv || !currentPathInput) return;
    
    contentDiv.innerHTML = '<div class="loading-placeholder">Loading directories...</div>';
    
    try {
        const url = path ? `/api/library/browse?path=${encodeURIComponent(path)}` : '/api/library/browse';
        const response = await fetch(url);
        const data = await response.json();
        
        if (data.error) {
            contentDiv.innerHTML = `<div style="color: var(--error-color); padding: 1rem;">Error: ${data.error}</div>`;
            return;
        }
        
        currentBrowserPath = data.currentPath;
        currentPathInput.value = data.currentPath;
        
        // Enable/disable up button
        if (upButton) {
            upButton.disabled = !data.parentPath;
        }
        
        if (!data.items || data.items.length === 0) {
            contentDiv.innerHTML = '<div style="padding: 1rem; color: var(--text-secondary);">No subdirectories found</div>';
            return;
        }
        
        // Display directories
        let html = '<div style="display: flex; flex-direction: column; gap: 4px;">';
        data.items.forEach(item => {
            if (item.isDirectory) {
                html += `
                    <div class="path-browser-item" onclick="navigatePathBrowser('${item.fullPath.replace(/'/g, "\\'")}')" style="padding: 0.5rem; cursor: pointer; border: 1px solid var(--border-color); border-radius: 4px; display: flex; align-items: center; gap: 0.5rem;">
                        <span style="font-size: 1.2em;">📁</span>
                        <span style="flex: 1;">${escapeHtml(item.name)}</span>
                        <span style="color: var(--text-secondary);">→</span>
                    </div>
                `;
            }
        });
        html += '</div>';
        contentDiv.innerHTML = html;
    } catch (error) {
        console.error('Error loading path browser:', error);
        contentDiv.innerHTML = `<div style="color: var(--error-color); padding: 1rem;">Error loading directories: ${error.message}</div>`;
    }
}

export function selectPathFromBrowser() {
    if (currentBrowserPath) {
        const pathInput = document.getElementById('newLibraryPath');
        if (pathInput) {
            pathInput.value = currentBrowserPath;
            closePathBrowser();
        }
    }
}

// Export to window for onclick handlers
window.showPathBrowser = showPathBrowser;
window.closePathBrowser = closePathBrowser;
window.navigatePathBrowser = navigatePathBrowser;
window.selectPathFromBrowser = selectPathFromBrowser;

