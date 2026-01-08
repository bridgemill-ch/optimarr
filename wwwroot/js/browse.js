// Browse Media Functions
import { escapeHtml } from './utils.js';
import { showMediaInfo } from './media-info.js';

export let browseCurrentPage = 1;

export async function loadBrowseMedia() {
    const libraryId = document.getElementById('browseLibraryFilter')?.value || '';
    const codec = document.getElementById('browseCodecFilter')?.value || '';
    const container = document.getElementById('browseContainerFilter')?.value || '';
    const score = document.getElementById('browseScoreFilter')?.value || '';
    const servarrFilter = document.getElementById('browseServarrFilter')?.value || '';
    const search = document.getElementById('browseSearch')?.value || '';
    const sortBy = document.getElementById('browseSortBy')?.value || 'analyzedAt';
    const sortOrder = document.getElementById('browseSortOrder')?.value || 'desc';
    const isBroken = document.getElementById('browseBrokenFilter')?.checked || false;
    const grid = document.getElementById('browseMediaGrid');
    
    if (grid) {
        grid.innerHTML = '<div class="loading-placeholder">Loading media...</div>';
    }
    
    try {
        const params = new URLSearchParams({
            page: browseCurrentPage,
            pageSize: 24
        });
        if (libraryId) params.append('libraryPathId', libraryId);
        if (codec) params.append('codec', codec);
        if (container) params.append('container', container);
        if (score) params.append('score', score);
        if (servarrFilter) params.append('servarrFilter', servarrFilter);
        if (search) params.append('search', search);
        if (sortBy) params.append('sortBy', sortBy);
        if (sortOrder) params.append('sortOrder', sortOrder);
        if (isBroken) params.append('isBroken', 'true');
        
        const response = await fetch(`/api/library/videos?${params}`);
        if (!response.ok) throw new Error(`Failed to load media: ${response.status}`);
        
        const result = await response.json();
        
        if (!result.items || result.items.length === 0) {
            if (grid) {
                grid.innerHTML = '<div class="empty-state"><p>No media found. Try adjusting your filters.</p></div>';
            }
            return;
        }
        
        if (grid) {
            grid.innerHTML = result.items.map(video => {
                const rating = video.compatibilityRating ?? 0;
                
                // Determine audio type (surround/stereo)
                let audioType = 'Unknown';
                let audioCodec = 'N/A';
                try {
                    if (video.audioTracksJson) {
                        const audioTracks = JSON.parse(video.audioTracksJson);
                        if (Array.isArray(audioTracks) && audioTracks.length > 0) {
                            // Get the first audio track for display
                            const firstTrack = audioTracks[0];
                            audioCodec = firstTrack.codec || 'N/A';
                            
                            // Determine if surround (channels > 2) or stereo (channels = 2)
                            const maxChannels = Math.max(...audioTracks.map(t => t.channels || 2));
                            if (maxChannels > 2) {
                                audioType = 'Surround';
                            } else if (maxChannels === 2) {
                                audioType = 'Stereo';
                            }
                        }
                    }
                } catch (e) {
                    console.warn('Error parsing audio tracks:', e);
                }
                
                // Determine quality/resolution tag
                const height = video.height || 0;
                let qualityTag = '';
                if (height >= 2160) {
                    qualityTag = '4K';
                } else if (height >= 1440) {
                    qualityTag = '1440p';
                } else if (height >= 1080) {
                    qualityTag = '1080p';
                } else if (height >= 720) {
                    qualityTag = '720p';
                } else if (height >= 480) {
                    qualityTag = '480p';
                } else if (height > 0) {
                    qualityTag = `${height}p`;
                }
                
                // Servarr icons
                let servarrIcons = '';
                if (video.servarrType === 'Sonarr') {
                    servarrIcons = '<span class="servarr-icon sonarr-icon" title="Found in Sonarr: ' + escapeHtml(video.sonarrSeriesTitle || 'Unknown Series') + ' S' + (video.sonarrSeasonNumber || '?') + 'E' + (video.sonarrEpisodeNumber || '?') + '">📺</span>';
                } else if (video.servarrType === 'Radarr') {
                    servarrIcons = '<span class="servarr-icon radarr-icon" title="Found in Radarr: ' + escapeHtml(video.radarrMovieTitle || 'Unknown Movie') + (video.radarrYear ? ' (' + video.radarrYear + ')' : '') + '">🎬</span>';
                }
                
                return `
                    <div class="media-card ${video.isBroken ? 'broken-media' : ''}" data-video-id="${video.id}">
                        <div class="media-card-checkbox" onclick="event.stopPropagation();">
                            <input type="checkbox" class="browse-video-checkbox" value="${video.id}" onchange="updateBrowseSelection()">
                        </div>
                        <div class="media-card-content" onclick="showMediaInfo(${video.id})">
                        <div class="media-card-header">
                            ${servarrIcons}
                            ${video.isBroken ? '<span class="broken-badge" title="Broken or unreadable media file">⚠️ Broken</span>' : `<span class="rating-badge rating-${rating}">${rating}/11</span>`}
                            ${video.isHDR ? '<span class="hdr-badge">HDR</span>' : '<span class="sdr-badge">SDR</span>'}
                            ${audioType !== 'Unknown' ? `<span class="audio-badge audio-${audioType.toLowerCase()}">${audioType}</span>` : ''}
                            ${qualityTag ? `<span class="quality-badge">${qualityTag}</span>` : ''}
                        </div>
                        <div class="media-card-body">
                            <div class="media-card-title" title="${escapeHtml(video.fileName || 'Unknown')}">
                                ${escapeHtml(video.fileName || 'Unknown')}
                            </div>
                            <div class="media-card-info">
                                <div class="info-row">
                                    <span class="info-label">Codec:</span>
                                    <span class="info-value">${escapeHtml(video.videoCodec || 'NULL')}</span>
                                </div>
                                <div class="info-row">
                                    <span class="info-label">Audio:</span>
                                    <span class="info-value">${escapeHtml(audioCodec)}</span>
                                </div>
                                <div class="info-row">
                                    <span class="info-label">Container:</span>
                                    <span class="info-value">${escapeHtml(video.container || 'NULL')}</span>
                                </div>
                            </div>
                        </div>
                        </div>
                    </div>
                `;
            }).join('');
        }
        
        // Update pagination
        updateBrowsePagination(result);
        
        // Load library filter options if not already loaded
        const libraryFilter = document.getElementById('browseLibraryFilter');
        if (libraryFilter && libraryFilter.querySelectorAll('option').length <= 1) {
            loadBrowseLibraryFilters();
        }
        
        // Load codec and container filter options if not already loaded
        const codecFilter = document.getElementById('browseCodecFilter');
        const containerFilter = document.getElementById('browseContainerFilter');
        if (codecFilter && codecFilter.querySelectorAll('option').length <= 1) {
            loadBrowseFilterOptions();
        }
        if (containerFilter && containerFilter.querySelectorAll('option').length <= 1) {
            loadBrowseFilterOptions();
        }
    } catch (error) {
        console.error('Error loading browse media:', error);
        if (grid) {
            grid.innerHTML = `<div class="error-state"><p>Error loading media: ${escapeHtml(error.message)}</p></div>`;
        }
    }
}

export function updateBrowsePagination(result) {
    const pagination = document.getElementById('browsePagination');
    if (!pagination) return;
    
    if (result.totalPages <= 1) {
        pagination.innerHTML = '';
        return;
    }
    
    let html = '';
    if (result.page > 1) {
        html += `<button class="btn btn-secondary" onclick="window.setBrowsePage(${result.page - 1}); window.loadBrowseMedia();">Previous</button>`;
    }
    html += `<span>Page ${result.page} of ${result.totalPages}</span>`;
    if (result.page < result.totalPages) {
        html += `<button class="btn btn-secondary" onclick="window.setBrowsePage(${result.page + 1}); window.loadBrowseMedia();">Next</button>`;
    }
    pagination.innerHTML = html;
}

export async function loadBrowseLibraryFilters() {
    try {
        const response = await fetch('/api/library/paths');
        if (!response.ok) return;
        
        const libraries = await response.json();
        const select = document.getElementById('browseLibraryFilter');
        if (!select) return;
        
        libraries.forEach(lib => {
            const option = document.createElement('option');
            option.value = lib.id;
            option.textContent = lib.name || lib.path;
            select.appendChild(option);
        });
    } catch (error) {
        console.error('Error loading library filters:', error);
    }
}

export async function loadBrowseFilterOptions() {
    try {
        const response = await fetch('/api/library/videos/filters');
        if (!response.ok) return;
        
        const filters = await response.json();
        
        // Populate codec filter
        const codecSelect = document.getElementById('browseCodecFilter');
        if (codecSelect && filters.codecs) {
            // Clear existing options except "All Codecs"
            const allCodecsOption = codecSelect.querySelector('option[value=""]');
            codecSelect.innerHTML = '';
            if (allCodecsOption) {
                codecSelect.appendChild(allCodecsOption);
            }
            
            filters.codecs.forEach(codec => {
                const option = document.createElement('option');
                option.value = codec;
                option.textContent = codec;
                codecSelect.appendChild(option);
            });
        }
        
        // Populate container filter
        const containerSelect = document.getElementById('browseContainerFilter');
        if (containerSelect && filters.containers) {
            // Clear existing options except "All Containers"
            const allContainersOption = containerSelect.querySelector('option[value=""]');
            containerSelect.innerHTML = '';
            if (allContainersOption) {
                containerSelect.appendChild(allContainersOption);
            }
            
            filters.containers.forEach(container => {
                const option = document.createElement('option');
                option.value = container;
                option.textContent = container;
                containerSelect.appendChild(option);
            });
        }
    } catch (error) {
        console.error('Error loading filter options:', error);
    }
}

// Function to reset page to 1
export function resetBrowsePage() {
    browseCurrentPage = 1;
    if (window.browseCurrentPage !== undefined) {
        window.browseCurrentPage = 1;
    }
}

// Function to set the current page
export function setBrowsePage(page) {
    browseCurrentPage = page;
    window.browseCurrentPage = page;
}

// Debounce function for search
let searchTimeout = null;
function debounceSearch() {
    if (searchTimeout) {
        clearTimeout(searchTimeout);
    }
    searchTimeout = setTimeout(() => {
        resetBrowsePage();
        loadBrowseMedia();
    }, 500); // Wait 500ms after user stops typing
}

// Setup event listeners for auto-search and sort changes
export function setupBrowseEventListeners() {
    const searchInput = document.getElementById('browseSearch');
    const sortBy = document.getElementById('browseSortBy');
    const sortOrder = document.getElementById('browseSortOrder');
    const servarrFilter = document.getElementById('browseServarrFilter');
    const libraryFilter = document.getElementById('browseLibraryFilter');
    const codecFilter = document.getElementById('browseCodecFilter');
    const containerFilter = document.getElementById('browseContainerFilter');
    const scoreFilter = document.getElementById('browseScoreFilter');
    const brokenFilter = document.getElementById('browseBrokenFilter');
    
    if (searchInput) {
        searchInput.addEventListener('input', () => {
            debounceSearch();
        });
    }
    
    if (sortBy) {
        sortBy.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (sortOrder) {
        sortOrder.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (servarrFilter) {
        servarrFilter.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (libraryFilter) {
        libraryFilter.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (codecFilter) {
        codecFilter.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (containerFilter) {
        containerFilter.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (scoreFilter) {
        scoreFilter.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
    
    if (brokenFilter) {
        brokenFilter.addEventListener('change', () => {
            resetBrowsePage();
            loadBrowseMedia();
        });
    }
}

// Selection management
let selectedVideoIds = new Set();

export function updateBrowseSelection() {
    const checkboxes = document.querySelectorAll('.browse-video-checkbox:checked');
    selectedVideoIds = new Set(Array.from(checkboxes).map(cb => parseInt(cb.value)));
    
    const count = selectedVideoIds.size;
    const countSpan = document.getElementById('selectedCount');
    const toolbar = document.querySelector('.browse-selection-toolbar');
    const redownloadBtn = document.getElementById('redownloadSelectedBtn');
    const selectAllCheckbox = document.getElementById('selectAllBrowse');
    
    if (countSpan) countSpan.textContent = `${count} selected`;
    if (toolbar) toolbar.style.display = count > 0 ? 'block' : 'none';
    if (redownloadBtn) redownloadBtn.disabled = count === 0;
    if (selectAllCheckbox) {
        const allCheckboxes = document.querySelectorAll('.browse-video-checkbox');
        selectAllCheckbox.checked = allCheckboxes.length > 0 && allCheckboxes.length === checkboxes.length;
    }
}

export function toggleSelectAllBrowse() {
    const selectAll = document.getElementById('selectAllBrowse');
    const checkboxes = document.querySelectorAll('.browse-video-checkbox');
    
    if (selectAll && selectAll.checked) {
        checkboxes.forEach(cb => {
            cb.checked = true;
            selectedVideoIds.add(parseInt(cb.value));
        });
    } else {
        checkboxes.forEach(cb => {
            cb.checked = false;
        });
        selectedVideoIds.clear();
    }
    
    updateBrowseSelection();
}

export function clearBrowseSelection() {
    selectedVideoIds.clear();
    const checkboxes = document.querySelectorAll('.browse-video-checkbox');
    checkboxes.forEach(cb => cb.checked = false);
    const selectAll = document.getElementById('selectAllBrowse');
    if (selectAll) selectAll.checked = false;
    updateBrowseSelection();
}

export async function redownloadSelected() {
    if (selectedVideoIds.size === 0) {
        alert('No videos selected');
        return;
    }
    
    const count = selectedVideoIds.size;
    if (!confirm(`Are you sure you want to redownload ${count} video(s)?\n\nThis will:\n- Delete the file(s) from disk\n- Remove them from Sonarr/Radarr\n- Trigger a new download\n- Remove them from the database`)) {
        return;
    }
    
    const redownloadBtn = document.getElementById('redownloadSelectedBtn');
    if (redownloadBtn) {
        redownloadBtn.disabled = true;
        redownloadBtn.textContent = 'Processing...';
    }
    
    try {
        const response = await fetch('/api/library/videos/redownload', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: Array.from(selectedVideoIds) })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to redownload videos');
        }
        
        const result = await response.json();
        
        alert(`Redownload completed!\n\nTotal: ${result.total}\nSuccess: ${result.success}\nFailed: ${result.failed}`);
        
        // Clear selection and reload
        clearBrowseSelection();
        loadBrowseMedia();
    } catch (error) {
        console.error('Error redownloading videos:', error);
        alert(`Error redownloading videos: ${error.message}`);
    } finally {
        if (redownloadBtn) {
            redownloadBtn.disabled = false;
            redownloadBtn.textContent = 'Redownload Selected';
        }
    }
}

// Export to window for onclick handlers
window.loadBrowseMedia = loadBrowseMedia;
window.browseCurrentPage = browseCurrentPage;
window.resetBrowsePage = resetBrowsePage;
window.updateBrowseSelection = updateBrowseSelection;
window.toggleSelectAllBrowse = toggleSelectAllBrowse;
window.clearBrowseSelection = clearBrowseSelection;
window.redownloadSelected = redownloadSelected;
window.setBrowsePage = setBrowsePage;

