// Dashboard Functions
import { escapeHtml, formatFileSize } from './utils.js';
import { showMediaInfo } from './media-info.js';

export async function loadDashboard() {
    try {
        console.log('Loading dashboard...');
        
        // Show loading state
        const statElements = ['totalVideos', 'optimalCount', 'goodCount', 'poorCount', 'totalSize', 'hdrCount', 'sdrCount'];
        statElements.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.textContent = '...';
        });
        
        // Add timeout to fetch
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 10000); // 10 second timeout
        
        const response = await fetch('/api/library/dashboard/stats', {
            signal: controller.signal
        });
        clearTimeout(timeoutId);
        
        if (!response.ok) {
            throw new Error(`Failed to load dashboard: ${response.status} ${response.statusText}`);
        }
        
        const stats = await response.json();
        
        // Update stats - check if elements exist before updating and handle null/undefined
        const totalVideosEl = document.getElementById('totalVideos');
        if (totalVideosEl) totalVideosEl.textContent = (stats.totalVideos || 0).toLocaleString();
        
        const optimalCountEl = document.getElementById('optimalCount');
        if (optimalCountEl) optimalCountEl.textContent = (stats.optimalCount || 0).toLocaleString();
        
        const goodCountEl = document.getElementById('goodCount');
        if (goodCountEl) goodCountEl.textContent = (stats.goodCount || 0).toLocaleString();
        
        const poorCountEl = document.getElementById('poorCount');
        if (poorCountEl) poorCountEl.textContent = (stats.poorCount || 0).toLocaleString();
        
        const totalSizeEl = document.getElementById('totalSize');
        if (totalSizeEl) totalSizeEl.textContent = formatFileSize(stats.totalSize || 0);
        
        const hdrCountEl = document.getElementById('hdrCount');
        if (hdrCountEl) hdrCountEl.textContent = (stats.hdrCount || 0).toLocaleString();
        
        const sdrCountEl = document.getElementById('sdrCount');
        if (sdrCountEl) sdrCountEl.textContent = (stats.sdrCount || 0).toLocaleString();
        
        // Update charts
        updateCompatibilityChart(stats);
        updateCodecChart(stats.codecDistribution || {});
        updateContainerChart(stats.containerDistribution || {});
        updateAudioCodecChart(stats.audioCodecDistribution || {});
        updateAudioChannelChart(stats.audioChannelDistribution || {});
        updateHdrSdrChart(stats.hdrCount || 0, stats.sdrCount || 0);
        updateBitDepthChart(stats.bitDepthDistribution || {});
        updateSubtitleFormatChart(stats.subtitleFormatDistribution || {});
        updateBitrateRangeChart(stats.bitrateRangeDistribution || {});
        
        console.log('Dashboard refreshed successfully');
    } catch (error) {
        console.error('Error loading dashboard:', error);
        
        // Show error state
        const statElements = ['totalVideos', 'optimalCount', 'goodCount', 'poorCount', 'totalSize', 'hdrCount', 'sdrCount'];
        statElements.forEach(id => {
            const el = document.getElementById(id);
            if (el) el.textContent = error.name === 'AbortError' ? 'Timeout' : 'Error';
        });
        
        // Show error in charts
        const chart = document.getElementById('compatibilityChart');
        if (chart) {
            chart.innerHTML = `<p style="color: var(--danger-color);">Error loading dashboard: ${error.message}</p>`;
        }
    }
}

export function updateCompatibilityChart(stats) {
    const chart = document.getElementById('compatibilityChart');
    const total = stats.totalVideos || 0;
    if (total === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No videos analyzed yet. Start by scanning a library to see compatibility statistics.</p></div>';
        return;
    }
    
    const optimalCount = stats.optimalCount || 0;
    const goodCount = stats.goodCount || 0;
    const poorCount = stats.poorCount || 0;
    const brokenCount = stats.brokenCount || 0;
    
    const optimalPct = total > 0 ? ((optimalCount / total) * 100).toFixed(1) : '0.0';
    const goodPct = total > 0 ? ((goodCount / total) * 100).toFixed(1) : '0.0';
    const poorPct = total > 0 ? ((poorCount / total) * 100).toFixed(1) : '0.0';
    const brokenPct = total > 0 ? ((brokenCount / total) * 100).toFixed(1) : '0.0';
    
    chart.innerHTML = `
        <div class="chart-bars">
            <div class="chart-bar clickable-chart-bar" onclick="navigateToBrowseWithFilter('Optimal')" title="Click to view Optimal videos">
                <div class="bar-label">Optimal (${optimalPct}%)</div>
                <div class="bar-container">
                    <div class="bar-fill optimal" style="width: ${optimalPct}%"></div>
                </div>
            </div>
            <div class="chart-bar clickable-chart-bar" onclick="navigateToBrowseWithFilter('Good')" title="Click to view Good videos">
                <div class="bar-label">Good (${goodPct}%)</div>
                <div class="bar-container">
                    <div class="bar-fill good" style="width: ${goodPct}%"></div>
                </div>
            </div>
            <div class="chart-bar clickable-chart-bar" onclick="navigateToBrowseWithFilter('Poor')" title="Click to view Poor videos">
                <div class="bar-label">Poor (${poorPct}%)</div>
                <div class="bar-container">
                    <div class="bar-fill poor" style="width: ${poorPct}%"></div>
                </div>
            </div>
            ${brokenCount > 0 ? `
            <div class="chart-bar">
                <div class="bar-label-container" style="display: flex; justify-content: space-between; align-items: center; width: 100%;">
                    <div class="clickable-chart-bar" onclick="navigateToBrowseWithBroken()" title="Click to view Broken videos" style="flex: 1; display: flex; align-items: center; gap: 1rem;">
                        <div class="bar-label">Broken (${brokenPct}%)</div>
                        <div class="bar-container" style="flex: 1;">
                            <div class="bar-fill broken" style="width: ${brokenPct}%"></div>
                        </div>
                    </div>
                    <button class="btn btn-sm btn-secondary" onclick="event.stopPropagation(); rescanAllBrokenVideos();" title="Rescan all broken videos" style="margin-left: 0.5rem; padding: 0.25rem 0.75rem; font-size: 0.75rem; white-space: nowrap;">
                        <span>↻ Rescan</span>
                    </button>
                </div>
            </div>
            ` : ''}
        </div>
    `;
}

export function updateCodecChart(distribution) {
    const chart = document.getElementById('codecChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No codec data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    // Filter out null/empty codecs - completely ignore NULL values
    const validEntries = Object.entries(distribution)
        .filter(([codec]) => codec && codec.trim() !== '' && codec.trim().toUpperCase() !== 'NULL')
        .sort((a, b) => b[1] - a[1])
        .slice(0, 5);
    
    if (validEntries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No codec data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const entries = validEntries;
    
    const max = Math.max(...entries.map(e => e[1]));
    chart.innerHTML = entries.map(([codec, count], index) => {
        const pct = ((count / max) * 100).toFixed(0);
        const displayCodec = codec || 'NULL';
        const codecValue = codec || '';
        // Escape for JavaScript string (replace single quotes and backslashes)
        const jsEscapedCodec = codecValue.replace(/'/g, "\\'").replace(/\\/g, "\\\\");
        return `
            <div class="chart-item clickable-chart-item" onclick="navigateToBrowseWithCodec('${jsEscapedCodec}')" title="Click to view ${escapeHtml(displayCodec)} videos">
                <div class="chart-label">${escapeHtml(displayCodec)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export function updateContainerChart(distribution) {
    const chart = document.getElementById('containerChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No container data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    // Filter out null/empty containers - completely ignore NULL values
    const validEntries = Object.entries(distribution)
        .filter(([container]) => container && container.trim() !== '' && container.trim().toUpperCase() !== 'NULL')
        .sort((a, b) => b[1] - a[1])
        .slice(0, 5);
    
    if (validEntries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No container data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const entries = validEntries;
    
    const max = Math.max(...entries.map(e => e[1]));
    chart.innerHTML = entries.map(([container, count]) => {
        const pct = ((count / max) * 100).toFixed(0);
        const displayContainer = container || 'NULL';
        const containerValue = container || '';
        // Escape for JavaScript string (replace single quotes and backslashes)
        const jsEscapedContainer = containerValue.replace(/'/g, "\\'").replace(/\\/g, "\\\\");
        return `
            <div class="chart-item clickable-chart-item" onclick="navigateToBrowseWithContainer('${jsEscapedContainer}')" title="Click to view ${escapeHtml(displayContainer)} videos">
                <div class="chart-label">${escapeHtml(displayContainer)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export async function loadTopIssues() {
    try {
        const response = await fetch('/api/library/dashboard/issues?limit=10');
        if (!response.ok) throw new Error('Failed to load issues');
        
        const data = await response.json();
        const issues = data.issues || [];
        const summary = data.optimizationSummary || {};
        
        // Update optimization summary
        const summaryContainer = document.getElementById('optimizationSummary');
        if (summaryContainer && summary) {
            summaryContainer.innerHTML = `
                <div class="optimization-summary">
                    <div class="summary-item">
                        <div class="summary-label">Total Videos</div>
                        <div class="summary-value">${summary.totalVideos?.toLocaleString() || 0}</div>
                    </div>
                    <div class="summary-item">
                        <div class="summary-label">Optimal</div>
                        <div class="summary-value optimal">${summary.optimalVideos?.toLocaleString() || 0}</div>
                    </div>
                    <div class="summary-item">
                        <div class="summary-label">Good</div>
                        <div class="summary-value good">${summary.goodVideos?.toLocaleString() || 0}</div>
                    </div>
                    <div class="summary-item">
                        <div class="summary-label">Poor</div>
                        <div class="summary-value poor">${summary.poorVideos?.toLocaleString() || 0}</div>
                    </div>
                    <div class="summary-item highlight">
                        <div class="summary-label">Need Optimization</div>
                        <div class="summary-value warning">${summary.videosNeedingOptimization?.toLocaleString() || 0}</div>
                        <div class="summary-percent">${summary.optimizationPotential || 0}%</div>
                    </div>
                </div>
            `;
        }
        
        // Update issues list
        const container = document.getElementById('topIssuesList');
        if (!container) return;
        
        if (issues.length === 0) {
            container.innerHTML = '<div class="empty-state"><p>No optimization issues found. All videos are optimally configured!</p></div>';
            return;
        }
        
        container.innerHTML = issues.map((issue, index) => `
            <div class="issue-item clickable-issue-item" onclick="showMediaInfo(${issue.id || 0})" title="Click to view media information">
                <span class="issue-count">${issue.compatibilityRating !== undefined ? issue.compatibilityRating : 'N/A'}</span>
                <span class="issue-text">${escapeHtml(issue.fileName || 'Unknown')} - ${escapeHtml(issue.videoCodec || 'NULL')} / ${escapeHtml(issue.container || 'NULL')}</span>
            </div>
        `).join('');
    } catch (error) {
        console.error('Error loading top issues:', error);
        const container = document.getElementById('topIssuesList');
        if (container) {
            container.innerHTML = '<div class="error-state">Error loading issues</div>';
        }
    }
}

function getCompatibilityColor(percent) {
    if (percent >= 80) return 'var(--success-color)';
    if (percent >= 60) return 'var(--accent-color)';
    if (percent >= 40) return 'var(--warning-color)';
    if (percent >= 20) return '#e67e22';
    return 'var(--error-color)';
}

// Make navigateToBrowseWithFilter available globally for onclick handlers
export async function navigateToBrowseWithFilter(score) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const scoreFilter = document.getElementById('browseScoreFilter');
            if (scoreFilter) {
                scoreFilter.value = score || ''; // Empty string clears the filter
                // Clear other filters
                const codecFilter = document.getElementById('browseCodecFilter');
                const containerFilter = document.getElementById('browseContainerFilter');
                if (codecFilter) codecFilter.value = '';
                if (containerFilter) containerFilter.value = '';
                // Import dynamically to avoid circular dependency
                const browseModule = await import('./browse.js');
                // Reset page to 1 using the reset function (module exports are read-only)
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithCodec(codec) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const codecFilter = document.getElementById('browseCodecFilter');
            if (codecFilter) {
                codecFilter.value = codec || ''; // Empty string clears the filter
                // Clear other filters
                const scoreFilter = document.getElementById('browseScoreFilter');
                const containerFilter = document.getElementById('browseContainerFilter');
                if (scoreFilter) scoreFilter.value = '';
                if (containerFilter) containerFilter.value = '';
                // Import dynamically to avoid circular dependency
                const browseModule = await import('./browse.js');
                // Reset page to 1
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithContainer(container) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const containerFilter = document.getElementById('browseContainerFilter');
            if (containerFilter) {
                // Handle NULL container - need to check if we can filter by empty/null
                // For now, if container is empty or 'NULL', we'll try to set it to empty string
                // The backend should handle filtering for null/empty containers
                containerFilter.value = (container === 'NULL' || !container) ? '' : container;
                // Clear other filters
                const scoreFilter = document.getElementById('browseScoreFilter');
                const codecFilter = document.getElementById('browseCodecFilter');
                if (scoreFilter) scoreFilter.value = '';
                if (codecFilter) codecFilter.value = '';
                // Import dynamically to avoid circular dependency
                const browseModule = await import('./browse.js');
                // Reset page to 1
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithBroken() {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            // Clear all filters
            const scoreFilter = document.getElementById('browseScoreFilter');
            const codecFilter = document.getElementById('browseCodecFilter');
            const containerFilter = document.getElementById('browseContainerFilter');
            const brokenFilter = document.getElementById('browseBrokenFilter');
            if (scoreFilter) scoreFilter.value = '';
            if (codecFilter) codecFilter.value = '';
            if (containerFilter) containerFilter.value = '';
            if (brokenFilter) brokenFilter.checked = true;
            
            // Import dynamically to avoid circular dependency
            const browseModule = await import('./browse.js');
            // Reset page to 1
            browseModule.resetBrowsePage();
            browseModule.loadBrowseFilterOptions();
            browseModule.loadBrowseMedia();
        }, 200);
    }
}

export function updateAudioCodecChart(distribution) {
    const chart = document.getElementById('audioCodecChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No audio codec data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const validEntries = Object.entries(distribution)
        .filter(([codec]) => codec && codec.trim() !== '' && codec.trim().toUpperCase() !== 'NULL')
        .sort((a, b) => b[1] - a[1])
        .slice(0, 5);
    
    if (validEntries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No audio codec data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const max = Math.max(...validEntries.map(e => e[1]));
    chart.innerHTML = validEntries.map(([codec, count]) => {
        const pct = ((count / max) * 100).toFixed(0);
        const jsEscapedCodec = codec.replace(/'/g, "\\'").replace(/\\/g, "\\\\");
        return `
            <div class="chart-item clickable-chart-item" onclick="navigateToBrowseWithAudioCodec('${jsEscapedCodec}')" title="Click to view ${escapeHtml(codec)} audio codec videos">
                <div class="chart-label">${escapeHtml(codec)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export function updateAudioChannelChart(distribution) {
    const chart = document.getElementById('audioChannelChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No audio channel data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const entries = Object.entries(distribution)
        .filter(([_, count]) => count > 0)
        .sort((a, b) => b[1] - a[1]);
    
    if (entries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No audio channel data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const max = Math.max(...entries.map(e => e[1]));
    chart.innerHTML = entries.map(([channel, count]) => {
        const pct = ((count / max) * 100).toFixed(0);
        // Map display name to filter value
        let filterValue = '';
        if (channel.includes('Mono')) filterValue = 'mono';
        else if (channel.includes('Stereo')) filterValue = 'stereo';
        else if (channel.includes('Surround')) filterValue = 'surround';
        const onClick = filterValue ? `onclick="navigateToBrowseWithAudioChannel('${filterValue}')"` : '';
        const clickableClass = filterValue ? 'clickable-chart-item' : '';
        return `
            <div class="chart-item ${clickableClass}" ${onClick} title="${filterValue ? 'Click to view ' + escapeHtml(channel) + ' videos' : ''}">
                <div class="chart-label">${escapeHtml(channel)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export function updateHdrSdrChart(hdrCount, sdrCount) {
    const chart = document.getElementById('hdrSdrChart');
    const total = hdrCount + sdrCount;
    
    if (total === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No HDR/SDR data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const hdrPct = total > 0 ? ((hdrCount / total) * 100).toFixed(1) : '0.0';
    const sdrPct = total > 0 ? ((sdrCount / total) * 100).toFixed(1) : '0.0';
    
    chart.innerHTML = `
        <div class="chart-bars">
            <div class="chart-bar clickable-chart-bar" onclick="navigateToBrowseWithHdrSdr('hdr')" title="Click to view HDR videos">
                <div class="bar-label">HDR (${hdrPct}%)</div>
                <div class="bar-container">
                    <div class="bar-fill optimal" style="width: ${hdrPct}%"></div>
                </div>
            </div>
            <div class="chart-bar clickable-chart-bar" onclick="navigateToBrowseWithHdrSdr('sdr')" title="Click to view SDR videos">
                <div class="bar-label">SDR (${sdrPct}%)</div>
                <div class="bar-container">
                    <div class="bar-fill good" style="width: ${sdrPct}%"></div>
                </div>
            </div>
        </div>
    `;
}

export function updateBitDepthChart(distribution) {
    const chart = document.getElementById('bitDepthChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No bit depth data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const entries = Object.entries(distribution)
        .filter(([_, count]) => count > 0)
        .sort((a, b) => {
            // Sort by bit depth value (extract number from "8bit", "10bit", etc.)
            const aDepth = parseInt(a[0]) || 0;
            const bDepth = parseInt(b[0]) || 0;
            return aDepth - bDepth;
        });
    
    if (entries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No bit depth data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const max = Math.max(...entries.map(e => e[1]));
    chart.innerHTML = entries.map(([bitDepth, count]) => {
        const pct = ((count / max) * 100).toFixed(0);
        // Extract bit depth number (e.g., "8bit" -> 8)
        const bitDepthNum = parseInt(bitDepth) || 0;
        return `
            <div class="chart-item clickable-chart-item" onclick="navigateToBrowseWithBitDepth(${bitDepthNum})" title="Click to view ${escapeHtml(bitDepth)} videos">
                <div class="chart-label">${escapeHtml(bitDepth)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export function updateSubtitleFormatChart(distribution) {
    const chart = document.getElementById('subtitleFormatChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No subtitle format data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const validEntries = Object.entries(distribution)
        .filter(([format]) => format && format.trim() !== '' && format.trim().toUpperCase() !== 'NULL')
        .sort((a, b) => b[1] - a[1])
        .slice(0, 5);
    
    if (validEntries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No subtitle format data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const max = Math.max(...validEntries.map(e => e[1]));
    chart.innerHTML = validEntries.map(([format, count]) => {
        const pct = ((count / max) * 100).toFixed(0);
        const jsEscapedFormat = format.replace(/'/g, "\\'").replace(/\\/g, "\\\\");
        return `
            <div class="chart-item clickable-chart-item" onclick="navigateToBrowseWithSubtitleFormat('${jsEscapedFormat}')" title="Click to view ${escapeHtml(format)} subtitle format videos">
                <div class="chart-label">${escapeHtml(format)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export function updateBitrateRangeChart(distribution) {
    const chart = document.getElementById('bitrateRangeChart');
    if (!distribution || Object.keys(distribution).length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No bitrate data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    // Define order for bitrate ranges with filter values
    const rangeOrder = [
        { display: "0-5 Mbps", filter: "0-5" },
        { display: "5-10 Mbps", filter: "5-10" },
        { display: "10-20 Mbps", filter: "10-20" },
        { display: "20-50 Mbps", filter: "20-50" },
        { display: "50+ Mbps", filter: "50+" }
    ];
    const entries = rangeOrder
        .map(range => [range.display, range.filter, distribution[range.display] || 0])
        .filter(([_, __, count]) => count > 0);
    
    if (entries.length === 0) {
        chart.innerHTML = '<div class="empty-state"><p>No bitrate data available. Videos may not be analyzed yet.</p></div>';
        return;
    }
    
    const max = Math.max(...entries.map(e => e[2]));
    chart.innerHTML = entries.map(([range, filterValue, count]) => {
        const pct = ((count / max) * 100).toFixed(0);
        return `
            <div class="chart-item clickable-chart-item" onclick="navigateToBrowseWithBitrateRange('${filterValue}')" title="Click to view ${escapeHtml(range)} videos">
                <div class="chart-label">${escapeHtml(range)}</div>
                <div class="chart-bar-container">
                    <div class="chart-bar-fill" style="width: ${pct}%"></div>
                    <span class="chart-value">${count}</span>
                </div>
            </div>
        `;
    }).join('');
}

export async function rescanAllBrokenVideos() {
    if (!confirm('This will rescan all broken/unreadable media files. This may take a long time. Continue?')) {
        return;
    }
    
    try {
        const response = await fetch('/api/library/videos/rescan-broken', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' }
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to start rescan');
        }
        
        const result = await response.json();
        alert(`Rescan started for ${result.count || 0} broken video(s). The process will run in the background.`);
        
        // Refresh dashboard after a delay to show updated counts
        setTimeout(() => {
            loadDashboard();
        }, 2000);
    } catch (error) {
        console.error('Error rescanning broken videos:', error);
        alert(`Error rescanning broken videos: ${error.message}`);
    }
}

export async function navigateToBrowseWithAudioCodec(audioCodec) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const audioCodecFilter = document.getElementById('browseAudioCodecFilter');
            if (audioCodecFilter) {
                audioCodecFilter.value = audioCodec || '';
                // Clear other filters
                const codecFilter = document.getElementById('browseCodecFilter');
                const containerFilter = document.getElementById('browseContainerFilter');
                const scoreFilter = document.getElementById('browseScoreFilter');
                if (codecFilter) codecFilter.value = '';
                if (containerFilter) containerFilter.value = '';
                if (scoreFilter) scoreFilter.value = '';
                const browseModule = await import('./browse.js');
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithAudioChannel(audioChannel) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const audioChannelFilter = document.getElementById('browseAudioChannelFilter');
            if (audioChannelFilter) {
                audioChannelFilter.value = audioChannel || '';
                const browseModule = await import('./browse.js');
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithHdrSdr(hdrSdr) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const hdrSdrFilter = document.getElementById('browseHdrSdrFilter');
            if (hdrSdrFilter) {
                hdrSdrFilter.value = hdrSdr || '';
                const browseModule = await import('./browse.js');
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithBitDepth(bitDepth) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const bitDepthFilter = document.getElementById('browseBitDepthFilter');
            if (bitDepthFilter) {
                bitDepthFilter.value = bitDepth?.toString() || '';
                const browseModule = await import('./browse.js');
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithSubtitleFormat(subtitleFormat) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const subtitleFormatFilter = document.getElementById('browseSubtitleFormatFilter');
            if (subtitleFormatFilter) {
                subtitleFormatFilter.value = subtitleFormat || '';
                const browseModule = await import('./browse.js');
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

export async function navigateToBrowseWithBitrateRange(bitrateRange) {
    const browseTab = document.querySelector('[data-tab="browse"]');
    if (browseTab) {
        browseTab.click();
        setTimeout(async () => {
            const bitrateRangeFilter = document.getElementById('browseBitrateRangeFilter');
            if (bitrateRangeFilter) {
                bitrateRangeFilter.value = bitrateRange || '';
                const browseModule = await import('./browse.js');
                browseModule.resetBrowsePage();
                browseModule.loadBrowseFilterOptions();
                browseModule.loadBrowseMedia();
            }
        }, 200);
    }
}

// Export to window for onclick handlers
window.navigateToBrowseWithFilter = navigateToBrowseWithFilter;
window.navigateToBrowseWithCodec = navigateToBrowseWithCodec;
window.navigateToBrowseWithContainer = navigateToBrowseWithContainer;
window.navigateToBrowseWithBroken = navigateToBrowseWithBroken;
window.navigateToBrowseWithAudioCodec = navigateToBrowseWithAudioCodec;
window.navigateToBrowseWithAudioChannel = navigateToBrowseWithAudioChannel;
window.navigateToBrowseWithHdrSdr = navigateToBrowseWithHdrSdr;
window.navigateToBrowseWithBitDepth = navigateToBrowseWithBitDepth;
window.navigateToBrowseWithSubtitleFormat = navigateToBrowseWithSubtitleFormat;
window.navigateToBrowseWithBitrateRange = navigateToBrowseWithBitrateRange;
window.rescanAllBrokenVideos = rescanAllBrokenVideos;
