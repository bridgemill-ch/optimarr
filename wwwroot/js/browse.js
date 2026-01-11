// Browse Media Functions
import { escapeHtml, getRatingCategory, getTitleFromFileName } from './utils.js';
import { showMediaInfo } from './media-info.js';

export let browseCurrentPage = 1;

export async function loadBrowseMedia() {
    const libraryId = document.getElementById('browseLibraryFilter')?.value || '';
    const codec = document.getElementById('browseCodecFilter')?.value || '';
    const container = document.getElementById('browseContainerFilter')?.value || '';
    const score = document.getElementById('browseScoreFilter')?.value || '';
    const servarrFilter = document.getElementById('browseServarrFilter')?.value || '';
    const audioCodec = document.getElementById('browseAudioCodecFilter')?.value || '';
    const audioChannel = document.getElementById('browseAudioChannelFilter')?.value || '';
    const hdrSdr = document.getElementById('browseHdrSdrFilter')?.value || '';
    const bitDepth = document.getElementById('browseBitDepthFilter')?.value || '';
    const subtitleFormat = document.getElementById('browseSubtitleFormatFilter')?.value || '';
    const bitrateRange = document.getElementById('browseBitrateRangeFilter')?.value || '';
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
        if (audioCodec) params.append('audioCodec', audioCodec);
        if (audioChannel) params.append('audioChannel', audioChannel);
        if (hdrSdr) params.append('hdrSdr', hdrSdr);
        if (bitDepth) params.append('bitDepth', bitDepth);
        if (subtitleFormat) params.append('subtitleFormat', subtitleFormat);
        if (bitrateRange) params.append('bitrateRange', bitrateRange);
        if (search) params.append('search', search);
        if (sortBy) params.append('sortBy', sortBy);
        if (sortOrder) params.append('sortOrder', sortOrder);
        if (isBroken) params.append('isBroken', 'true');
        
        const response = await fetch(`/api/library/videos?${params}`);
        if (!response.ok) throw new Error(`Failed to load media: ${response.status}`);
        
        const result = await response.json();
        
        // Check for empty results - handle both camelCase and PascalCase from backend
        const items = result.items || result.Items || [];
        const total = result.total !== undefined ? result.total : (result.Total !== undefined ? result.Total : 0);
        
        if (!items || items.length === 0 || total === 0) {
            if (grid) {
                grid.innerHTML = '<div class="empty-state"><p>No media found. Try adjusting your filters.</p></div>';
            }
            // Clear pagination when no results
            updateBrowsePagination({ items: [], total: 0, totalPages: 0, page: 1 });
            return;
        }
        
        if (grid) {
            grid.innerHTML = items.map(video => {
                const rating = video.compatibilityRating ?? 0; // Now 0-100 scale
                
                // Determine audio type (surround/stereo)
                let audioType = 'Unknown';
                let audioCodec = 'N/A';
                try {
                    if (video.audioTracksJson) {
                        const audioTracks = JSON.parse(video.audioTracksJson);
                        if (Array.isArray(audioTracks) && audioTracks.length > 0) {
                            // Get the first audio track for display
                            const firstTrack = audioTracks[0];
                            audioCodec = firstTrack.codec || firstTrack.Codec || 'N/A';
                            
                            // Determine if surround (channels > 2) or stereo (channels = 2)
                            // Handle both camelCase and PascalCase property names
                            const channelValues = audioTracks.map(t => {
                                // Try camelCase first, then PascalCase
                                const channels = t.channels !== undefined && t.channels !== null 
                                    ? t.channels 
                                    : (t.Channels !== undefined && t.Channels !== null ? t.Channels : null);
                                return channels;
                            }).filter(c => c !== null && c !== undefined && c > 0);
                            
                            if (channelValues.length === 0) {
                                // No valid channel data, can't determine
                                audioType = 'Unknown';
                            } else {
                                const maxChannels = Math.max(...channelValues);
                                if (maxChannels > 2) {
                                    audioType = 'Surround';
                                } else if (maxChannels === 2) {
                                    audioType = 'Stereo';
                                } else if (maxChannels === 1) {
                                    audioType = 'Mono';
                                }
                                
                                // Debug logging if we detect surround but logic fails
                                if (maxChannels > 2 && audioType !== 'Surround') {
                                    console.warn('Audio type detection issue - maxChannels:', maxChannels, 'audioTracks:', audioTracks, 'channelValues:', channelValues);
                                }
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
                
                // Processing status badge
                const processingBadge = video.processingStatus === 'Processing' 
                    ? '<span class="processing-badge" title="Video is being redownloaded, will be rescanned after 24 hours">⏳ Processing</span>' 
                    : '';
                
                // Determine rating category for color coding
                const ratingCategory = getRatingCategory(rating);
                
                return `
                    <div class="media-card ${video.isBroken ? 'broken-media' : ''} ${video.processingStatus === 'Processing' ? 'processing-media' : ''}" data-video-id="${video.id}">
                        <div class="media-card-checkbox" onclick="event.stopPropagation();">
                            <input type="checkbox" class="browse-video-checkbox" value="${video.id}" onchange="updateBrowseSelection()">
                        </div>
                        <div class="media-card-content" onclick="showMediaInfo(${video.id})">
                        <div class="media-card-header">
                            ${servarrIcons}
                            ${processingBadge}
                            ${video.isBroken ? '<span class="broken-badge" title="Broken or unreadable media file">⚠️ Broken</span>' : `<span class="rating-badge rating-${ratingCategory}">${rating}/100</span>`}
                            ${video.isHDR ? '<span class="hdr-badge">HDR</span>' : '<span class="sdr-badge">SDR</span>'}
                            ${audioType !== 'Unknown' ? `<span class="audio-badge audio-${audioType.toLowerCase()}">${audioType}</span>` : ''}
                            ${qualityTag ? `<span class="quality-badge">${qualityTag}</span>` : ''}
                        </div>
                        <div class="media-card-body">
                            <div class="media-card-title" title="${escapeHtml(video.fileName || 'Unknown')}">
                                ${escapeHtml(getTitleFromFileName(video.fileName))}
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
        
        // Update pagination with the result data
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
    
    // Handle both camelCase and PascalCase from backend JSON serialization
    const items = result.items || result.Items || [];
    const total = result.total !== undefined ? result.total : (result.Total !== undefined ? result.Total : 0);
    const totalPages = result.totalPages !== undefined ? result.totalPages : (result.TotalPages !== undefined ? result.TotalPages : 0);
    const currentPage = result.page !== undefined ? result.page : (result.Page !== undefined ? result.Page : 1);
    
    // Hide pagination if no results, invalid result, or only one page
    if (!result || 
        items.length === 0 || 
        total === 0 ||
        totalPages <= 1) {
        pagination.innerHTML = '';
        return;
    }
    
    let html = '';
    if (currentPage > 1) {
        html += `<button class="btn btn-secondary" onclick="window.setBrowsePage(${currentPage - 1}); window.loadBrowseMedia();">Previous</button>`;
    }
    html += `<span>Page ${currentPage} of ${totalPages}</span>`;
    if (currentPage < totalPages) {
        html += `<button class="btn btn-secondary" onclick="window.setBrowsePage(${currentPage + 1}); window.loadBrowseMedia();">Next</button>`;
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

        // Populate audio codec filter
        const audioCodecSelect = document.getElementById('browseAudioCodecFilter');
        if (audioCodecSelect && filters.audioCodecs) {
            const allAudioCodecsOption = audioCodecSelect.querySelector('option[value=""]');
            audioCodecSelect.innerHTML = '';
            if (allAudioCodecsOption) {
                audioCodecSelect.appendChild(allAudioCodecsOption);
            }
            
            filters.audioCodecs.forEach(codec => {
                const option = document.createElement('option');
                option.value = codec;
                option.textContent = codec;
                audioCodecSelect.appendChild(option);
            });
        }

        // Populate subtitle format filter
        const subtitleFormatSelect = document.getElementById('browseSubtitleFormatFilter');
        if (subtitleFormatSelect && filters.subtitleFormats) {
            const allSubtitleFormatsOption = subtitleFormatSelect.querySelector('option[value=""]');
            subtitleFormatSelect.innerHTML = '';
            if (allSubtitleFormatsOption) {
                subtitleFormatSelect.appendChild(allSubtitleFormatsOption);
            }
            
            filters.subtitleFormats.forEach(format => {
                const option = document.createElement('option');
                option.value = format;
                option.textContent = format;
                subtitleFormatSelect.appendChild(option);
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
    const rescanBtn = document.getElementById('rescanSelectedBtn');
    const selectAllCheckbox = document.getElementById('selectAllBrowse');
    
    if (countSpan) countSpan.textContent = `${count} selected`;
    if (toolbar) toolbar.style.display = count > 0 ? 'block' : 'none';
    if (redownloadBtn) redownloadBtn.disabled = count === 0;
    if (rescanBtn) rescanBtn.disabled = count === 0;
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

export async function selectAllFromAllPages() {
    try {
        // Get current filter values
        const libraryId = document.getElementById('browseLibraryFilter')?.value || '';
        const codec = document.getElementById('browseCodecFilter')?.value || '';
        const container = document.getElementById('browseContainerFilter')?.value || '';
        const score = document.getElementById('browseScoreFilter')?.value || '';
        const servarrFilter = document.getElementById('browseServarrFilter')?.value || '';
        const search = document.getElementById('browseSearch')?.value || '';
        const isBroken = document.getElementById('browseBrokenFilter')?.checked || false;
        
        // Build query parameters
        const params = new URLSearchParams();
        if (libraryId) params.append('libraryPathId', libraryId);
        if (codec) params.append('codec', codec);
        if (container) params.append('container', container);
        if (score) params.append('score', score);
        if (servarrFilter) params.append('servarrFilter', servarrFilter);
        if (search) params.append('search', search);
        if (isBroken) params.append('isBroken', 'true');
        
        // Fetch all matching video IDs
        const response = await fetch(`/api/library/videos/ids?${params}`);
        if (!response.ok) {
            throw new Error(`Failed to fetch video IDs: ${response.status}`);
        }
        
        const allIds = await response.json();
        
        if (!Array.isArray(allIds) || allIds.length === 0) {
            alert('No videos found matching the current filters.');
            return;
        }
        
        // Add all IDs to selection
        allIds.forEach(id => selectedVideoIds.add(id));
        
        // Update checkboxes on current page
        const checkboxes = document.querySelectorAll('.browse-video-checkbox');
        checkboxes.forEach(cb => {
            const videoId = parseInt(cb.value);
            if (selectedVideoIds.has(videoId)) {
                cb.checked = true;
            }
        });
        
        // Update UI
        updateBrowseSelection();
        
        // Show confirmation
        const countSpan = document.getElementById('selectedCount');
        if (countSpan) {
            // Temporarily show a message
            const originalText = countSpan.textContent;
            countSpan.textContent = `${allIds.length} selected (all pages)`;
            setTimeout(() => {
                countSpan.textContent = originalText;
            }, 2000);
        }
    } catch (error) {
        console.error('Error selecting all from all pages:', error);
        alert(`Error selecting all videos: ${error.message}`);
    }
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
    if (!confirm(`Are you sure you want to redownload ${count} video(s)?\n\nThis will:\n- Delete the file(s) from disk\n- Remove them from Sonarr/Radarr\n- Trigger a new download\n- Mark them as Processing (will be rescanned after 24h)`)) {
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

export async function rescanSelected() {
    if (selectedVideoIds.size === 0) {
        alert('No videos selected');
        return;
    }
    
    const count = selectedVideoIds.size;
    if (!confirm(`Are you sure you want to rescan ${count} video(s)?\n\nThis will:\n- Re-analyze the video file(s)\n- Update compatibility information\n- This may take a while for large files`)) {
        return;
    }
    
    const rescanBtn = document.getElementById('rescanSelectedBtn');
    if (rescanBtn) {
        rescanBtn.disabled = true;
        rescanBtn.textContent = 'Processing...';
    }
    
    try {
        const response = await fetch('/api/library/videos/rescan', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ videoIds: Array.from(selectedVideoIds) })
        });
        
        if (!response.ok) {
            const error = await response.json();
            throw new Error(error.error || 'Failed to rescan videos');
        }
        
        const result = await response.json();
        
        alert(`Rescan completed!\n\nTotal: ${result.total}\nSuccess: ${result.success}\nFailed: ${result.failed}`);
        
        // Clear selection and reload
        clearBrowseSelection();
        loadBrowseMedia();
    } catch (error) {
        console.error('Error rescanning videos:', error);
        alert(`Error rescanning videos: ${error.message}`);
    } finally {
        if (rescanBtn) {
            rescanBtn.disabled = false;
            rescanBtn.textContent = 'Rescan Selected';
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
window.rescanSelected = rescanSelected;
window.setBrowsePage = setBrowsePage;
window.selectAllFromAllPages = selectAllFromAllPages;

