// Media Info Modal Functions
import { escapeHtml, formatFileSize, formatDuration } from './utils.js';

export function getNonOptimalFields(video) {
    const nonOptimal = {
        container: false,
        videoCodec: false,
        bitDepth: false,
        hdr: false,
        audioCodecs: false,
        subtitleFormats: false,
        fastStart: false,
        codecTag: false
    };

    try {
        // Handle both camelCase (from JSON) and PascalCase (from C#)
        const issuesRaw = video.issues || video.Issues || '';
        const issues = issuesRaw ? (typeof issuesRaw === 'string' ? JSON.parse(issuesRaw) : issuesRaw) : [];
        
        if (!Array.isArray(issues)) return nonOptimal;
        
        issues.forEach(issue => {
            if (!issue) return;
            const issueLower = issue.toLowerCase();
            
            // Container issues
            if (issueLower.includes('mkv') || (issueLower.includes('container') && !issueLower.includes('subtitle'))) {
                nonOptimal.container = true;
            }
            
            // Video codec issues
            if (issueLower.includes('h.265') || issueLower.includes('hevc') || 
                issueLower.includes('av1') || (issueLower.includes('codec') && !issueLower.includes('audio'))) {
                nonOptimal.videoCodec = true;
            }
            
            // Codec tag issues
            if (issueLower.includes('codec tag') || issueLower.includes('incorrect codec tag')) {
                nonOptimal.codecTag = true;
            }
            
            // Bit depth issues (usually combined with codec)
            if (issueLower.includes('10-bit') || issueLower.includes('bit depth')) {
                nonOptimal.bitDepth = true;
            }
            
            // HDR issues
            if (issueLower.includes('hdr') || issueLower.includes('tone-mapping')) {
                nonOptimal.hdr = true;
            }
            
            // Audio issues - check for specific problematic codecs or general audio codec issues
            if (issueLower.includes('dts') || issueLower.includes('alac') || 
                issueLower.includes('ac3') || issueLower.includes('eac3') ||
                (issueLower.includes('audio') && (issueLower.includes('codec') || issueLower.includes('transcoding') || issueLower.includes('not supported') || issueLower.includes('unsupported')))) {
                nonOptimal.audioCodecs = true;
            }
            
            // Subtitle issues
            if (issueLower.includes('subtitle') || issueLower.includes('burn-in') || 
                issueLower.includes('utf-8') || issueLower.includes('utf8') ||
                issueLower.includes('srt') || issueLower.includes('ass') || 
                issueLower.includes('ssa') || issueLower.includes('vtt')) {
                nonOptimal.subtitleFormats = true;
            }
        });
        
        // Check for fast start issues
        if ((video.container === 'MP4' || video.container === 'M4V' || video.container === 'MOV') && !video.isFastStart) {
            nonOptimal.fastStart = true;
        }
        
        // Check for codec tag issues
        if (video.videoCodecTag && !video.isCodecTagCorrect) {
            nonOptimal.codecTag = true;
        }
    } catch (e) {
        // Silently handle parsing errors - issues are optional
    }

    return nonOptimal;
}

export async function showMediaInfo(videoId) {
    const modal = document.getElementById('mediaInfoModal');
    const content = document.getElementById('modalMediaContent');
    const title = document.getElementById('modalMediaTitle');
    
    if (!modal || !content) return;
    
    modal.style.display = 'block';
    content.innerHTML = '<div class="loading-placeholder">Loading media information...</div>';
    
    try {
        const response = await fetch(`/api/library/videos/${videoId}`);
        if (!response.ok) throw new Error('Failed to load media info');
        
        const video = await response.json();
        title.textContent = escapeHtml(video.fileName || 'Media Information');
        
        // Check if file is broken
        if (video.isBroken) {
            content.innerHTML = `
                <div class="broken-media-warning" style="padding: 2rem; background-color: rgba(231, 76, 60, 0.1); border: 2px solid var(--error-color); border-radius: 8px; margin-bottom: 1.5rem;">
                    <h3 style="color: var(--error-color); margin-top: 0; display: flex; align-items: center; gap: 0.5rem;">
                        <span>⚠️</span>
                        <span>Broken or Unreadable Media File</span>
                    </h3>
                    <p style="color: var(--text-primary); margin: 0.5rem 0;">
                        <strong>Reason:</strong> ${escapeHtml(video.brokenReason || 'Unknown error')}
                    </p>
                    <p style="color: var(--text-secondary); font-size: 0.875rem; margin: 0.5rem 0 0 0;">
                        This file could not be properly analyzed. It may be corrupted, incomplete, or in an unsupported format. 
                        Try rescanning the file or check if the file is accessible.
                    </p>
                </div>
                <div class="info-section">
                    <h4>File Information</h4>
                    <div class="info-item">
                        <span class="info-label">File Path:</span>
                        <span class="info-value">${escapeHtml(video.filePath || 'N/A')}</span>
                    </div>
                    <div class="info-item">
                        <span class="info-label">File Size:</span>
                        <span class="info-value">${video.fileSize ? formatFileSize(video.fileSize) : 'N/A'}</span>
                    </div>
                    <div class="info-item">
                        <span class="info-label">Analyzed:</span>
                        <span class="info-value">${video.analyzedAt ? new Date(video.analyzedAt).toLocaleString() : 'N/A'}</span>
                    </div>
                </div>
            `;
            return;
        }
        
        // Determine which fields are non-optimal
        const nonOptimal = getNonOptimalFields(video);
        
        // Helper to create info item with optional warning
        const createInfoItem = (label, value, isNonOptimal = false) => {
            const warningIcon = isNonOptimal ? '<span class="warning-icon" title="This field may cause compatibility issues">⚠️</span>' : '';
            return `
                <div class="info-item ${isNonOptimal ? 'non-optimal' : ''}">
                    <span class="info-label">${escapeHtml(label)}:</span>
                    <span class="info-value">
                        ${warningIcon}
                        ${value}
                    </span>
                </div>
            `;
        };
        
        // Parse issues and recommendations if available
        let issuesList = [];
        let recommendationsList = [];
        try {
            if (video.issues) {
                const issues = typeof video.issues === 'string' ? JSON.parse(video.issues) : video.issues;
                if (Array.isArray(issues)) {
                    issuesList = issues;
                }
            }
            if (video.recommendations) {
                const recommendations = typeof video.recommendations === 'string' ? JSON.parse(video.recommendations) : video.recommendations;
                if (Array.isArray(recommendations)) {
                    recommendationsList = recommendations;
                }
            }
        } catch (e) {
            // Silently handle parsing errors - issues/recommendations are optional
        }
        
        // Format audio codecs as a list
        const audioCodecsList = video.audioCodecs ? video.audioCodecs.split(',').map(c => c.trim()).filter(c => c) : [];
        const audioCodecsDisplay = audioCodecsList.length > 0 
            ? audioCodecsList.map(codec => `<span class="codec-tag ${nonOptimal.audioCodecs ? 'non-optimal' : ''}">${escapeHtml(codec)}</span>`).join(' ')
            : 'None';
        
        // Check for audio track warnings (missing language)
        let audioTracksWarning = false;
        try {
            const audioTracksJson = video.audioTracksJson || video.AudioTracksJson || '';
            if (audioTracksJson && audioTracksJson.trim() !== '') {
                const audioTracks = typeof audioTracksJson === 'string' ? JSON.parse(audioTracksJson) : audioTracksJson;
                if (Array.isArray(audioTracks)) {
                    audioTracksWarning = audioTracks.some(track => {
                        const language = track.language || track.Language || '';
                        return !language.trim() || language.trim().toLowerCase() === 'unknown';
                    });
                }
            }
        } catch (e) {
            // Ignore parsing errors
        }
        
        // Format subtitle formats as a list, separating embedded and external
        const subtitleFormatsList = video.subtitleFormats ? video.subtitleFormats.split(',').map(f => f.trim()).filter(f => f) : [];
        const subtitleFormatsDisplay = subtitleFormatsList.length > 0 
            ? subtitleFormatsList.map(format => {
                const isExternal = format.includes('(External)');
                const formatName = format.replace(' (External)', '');
                const tagClass = nonOptimal.subtitleFormats ? 'non-optimal' : '';
                const externalBadge = isExternal ? '<span style="font-size: 0.75rem; color: var(--accent-color); margin-left: 0.25rem;" title="External subtitle file">📄</span>' : '';
                return `<span class="codec-tag ${tagClass}">${escapeHtml(formatName)}${externalBadge}</span>`;
            }).join(' ')
            : 'None';
        
        // Check for subtitle track warnings (missing language)
        let subtitleTracksWarning = false;
        try {
            const subtitleTracksJson = video.subtitleTracksJson || video.SubtitleTracksJson || '';
            if (subtitleTracksJson && subtitleTracksJson.trim() !== '') {
                const subtitleTracks = typeof subtitleTracksJson === 'string' ? JSON.parse(subtitleTracksJson) : subtitleTracksJson;
                if (Array.isArray(subtitleTracks)) {
                    subtitleTracksWarning = subtitleTracks.some(track => {
                        const language = track.language || track.Language || '';
                        return !language.trim() || language.trim().toLowerCase() === 'unknown';
                    });
                }
            }
        } catch (e) {
            // Ignore parsing errors
        }
        
        // Calculate aspect ratio
        const aspectRatio = video.width && video.height ? (video.width / video.height).toFixed(2) : 'N/A';
        const displayAspectRatio = video.width && video.height ? 
            (video.width >= video.height ? `${(video.width / video.height).toFixed(2)}:1` : `1:${(video.height / video.width).toFixed(2)}`) : 'N/A';
        
        // Calculate bitrate estimate (if file size and duration are available)
        let estimatedBitrate = 'N/A';
        if (video.fileSize && video.duration && video.duration > 0) {
            const bitrateMbps = ((video.fileSize * 8) / (video.duration * 1000000)).toFixed(2);
            estimatedBitrate = `${bitrateMbps} Mbps`;
        }
        
        // Format analyzed date
        const analyzedDate = video.analyzedAt ? new Date(video.analyzedAt).toLocaleString() : 'N/A';
        const rescanIcon = `<button type="button" class="rescan-icon-btn" onclick="rescanVideo(${video.id})" id="rescanVideoBtn" title="Rescan video" style="background: none; border: none; cursor: pointer; padding: 0; margin-left: 0.5rem; font-size: 1rem; color: var(--text-secondary); transition: color 0.2s;" onmouseover="this.style.color='var(--primary-color)'" onmouseout="this.style.color='var(--text-secondary)'"><span id="rescanIconSymbol" style="display: inline-block;">↻</span></button>`;
        
        content.innerHTML = `
            <div class="media-info-grid">
                <div class="info-section">
                    <h4>File Information</h4>
                    ${createInfoItem('File Name', escapeHtml(video.fileName || 'N/A'), false)}
                    ${createInfoItem('File Path', escapeHtml(video.filePath || 'N/A'), false)}
                    ${createInfoItem('File Size', formatFileSize(video.fileSize || 0), false)}
                    ${createInfoItem('Container', escapeHtml(video.container || 'NULL'), nonOptimal.container)}
                    <div class="info-item">
                        <span class="info-label">Analyzed:</span>
                        <span class="info-value">
                            ${analyzedDate}${rescanIcon}
                        </span>
                    </div>
                </div>
                
                <div class="info-section">
                    <h4>Video Information</h4>
                    ${createInfoItem('Codec', escapeHtml(video.videoCodec || 'NULL'), nonOptimal.videoCodec)}
                    ${video.videoCodecTag ? createInfoItem('Codec Tag', escapeHtml(video.videoCodecTag) + (video.isCodecTagCorrect ? ' ✓' : ' ⚠'), nonOptimal.codecTag) : ''}
                    ${createInfoItem('Resolution', `${video.width || 0}x${video.height || 0}`, false)}
                    ${createInfoItem('Aspect Ratio', displayAspectRatio, false)}
                    ${createInfoItem('Frame Rate', video.frameRate ? video.frameRate.toFixed(3) + ' fps' : 'N/A', false)}
                    ${createInfoItem('Bit Depth', `${video.bitDepth || 8}-bit`, nonOptimal.bitDepth)}
                    ${createInfoItem('HDR', video.isHDR ? (video.hdrType || 'Yes') : 'No', nonOptimal.hdr)}
                    ${createInfoItem('Duration', video.duration ? formatDuration(video.duration) : 'N/A', false)}
                    ${createInfoItem('Estimated Bitrate', estimatedBitrate, false)}
                    ${(video.container === 'MP4' || video.container === 'M4V' || video.container === 'MOV') 
                        ? createInfoItem('Fast Start', video.isFastStart ? 'Yes ✓' : 'No ⚠', nonOptimal.fastStart) 
                        : ''}
                </div>
                
                <div class="info-section audio-tracks-section" ${video.audioTrackCount > 0 ? `style="cursor: pointer;" onclick="showTrackDetails(${videoId}, 'audio')" title="Click to view all audio tracks"` : ''}>
                    <h4>Audio Tracks ${audioTracksWarning ? '<span style="color: var(--warning-color); margin-left: 0.5rem;" title="Some audio tracks have missing language information">⚠️</span>' : ''} ${video.audioTrackCount > 0 ? '<span style="font-size: 0.875rem; color: var(--text-secondary); font-weight: normal;">(click to view details)</span>' : ''}</h4>
                    ${video.audioTrackCount > 0 
                        ? `${createInfoItem('Count', video.audioTrackCount.toString(), false)}
                           <div class="info-item">
                               <span class="info-label">Codecs:</span>
                               <span class="info-value" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
                                   ${audioCodecsDisplay}
                               </span>
                           </div>`
                        : '<div class="info-item"><span class="info-value">No audio tracks</span></div>'
                    }
                </div>
                
                <div class="info-section subtitle-tracks-section" ${video.subtitleTrackCount > 0 ? `style="cursor: pointer;" onclick="showTrackDetails(${videoId}, 'subtitle')" title="Click to view all subtitle tracks"` : ''}>
                    <h4>Subtitle Tracks ${subtitleTracksWarning ? '<span style="color: var(--warning-color); margin-left: 0.5rem;" title="Some subtitle tracks have missing language information">⚠️</span>' : ''} ${video.subtitleTrackCount > 0 ? '<span style="font-size: 0.875rem; color: var(--text-secondary); font-weight: normal;">(click to view details)</span>' : ''}</h4>
                    ${video.subtitleTrackCount > 0 
                        ? `${createInfoItem('Count', video.subtitleTrackCount.toString(), false)}
                           <div class="info-item">
                               <span class="info-label">Formats:</span>
                               <span class="info-value" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
                                   ${subtitleFormatsDisplay}
                               </span>
                           </div>`
                        : '<div class="info-item"><span class="info-value">No subtitle tracks</span></div>'
                    }
                </div>
                
                <div class="info-section">
                    <h4>Compatibility</h4>
                    <div class="info-item">
                        <span class="info-label">Rating:</span>
                        <span class="info-value">
                            <span class="rating-badge rating-${video.compatibilityRating || 0} client-compat-clickable" data-video-id="${video.id}" style="cursor: pointer;" title="Click to see client details">${video.compatibilityRating ?? 0} Direct Play</span>
                            <span class="score-badge ${(video.overallScore || '').toLowerCase()} client-compat-clickable" data-video-id="${video.id}" style="margin-left: 0.5rem; cursor: pointer;" title="Click to see client details">${escapeHtml(video.overallScore || 'Unknown')}</span>
                        </span>
                    </div>
                    <div class="info-item client-compat-clickable" data-video-id="${video.id}" style="cursor: pointer;" title="Click to see client details">
                        <span class="info-label">Direct Play:</span>
                        <span class="info-value">${video.directPlayClients || 0} clients <span style="color: var(--text-muted); font-size: 0.75rem;">(click for details)</span></span>
                    </div>
                    <div class="info-item client-compat-clickable" data-video-id="${video.id}" style="cursor: pointer;" title="Click to see client details">
                        <span class="info-label">Remux:</span>
                        <span class="info-value">${video.remuxClients || 0} clients <span style="color: var(--text-muted); font-size: 0.75rem;">(click for details)</span></span>
                    </div>
                    <div class="info-item client-compat-clickable" data-video-id="${video.id}" style="cursor: pointer;" title="Click to see client details">
                        <span class="info-label">Transcode:</span>
                        <span class="info-value">${video.transcodeClients || 0} clients <span style="color: var(--text-muted); font-size: 0.75rem;">(click for details)</span></span>
                    </div>
                </div>
                
                ${issuesList.length > 0 || recommendationsList.length > 0 ? `
                <div class="info-section" style="grid-column: 1 / -1;">
                    ${issuesList.length > 0 ? `
                    <h4 style="color: var(--error-color); margin-top: 0;">⚠️ Issues</h4>
                    <ul style="margin: 0.5rem 0; padding-left: 1.5rem; color: var(--text-secondary);">
                        ${issuesList.map(issue => `<li>${escapeHtml(issue)}</li>`).join('')}
                    </ul>
                    ` : ''}
                    ${recommendationsList.length > 0 ? `
                    <h4 style="color: var(--accent-color); margin-top: ${issuesList.length > 0 ? '1rem' : '0'};">💡 Recommendations</h4>
                    <ul style="margin: 0.5rem 0; padding-left: 1.5rem; color: var(--text-secondary);">
                        ${recommendationsList.map(rec => `<li>${escapeHtml(rec)}</li>`).join('')}
                    </ul>
                    ` : ''}
                </div>
                ` : ''}
            </div>
        `;
        
        // Attach event listeners to client compatibility clickable elements
        const clickableElements = content.querySelectorAll('.client-compat-clickable');
        clickableElements.forEach(element => {
            element.addEventListener('click', function(e) {
                e.stopPropagation();
                const videoId = element.getAttribute('data-video-id');
                if (videoId) {
                    showClientCompatibility(parseInt(videoId));
                }
            });
        });
    } catch (error) {
        console.error('Error loading media info:', error);
        content.innerHTML = `<div class="error-state"><p>Error loading media information: ${escapeHtml(error.message)}</p></div>`;
    }
}

export function closeMediaModal() {
    const modal = document.getElementById('mediaInfoModal');
    if (modal) modal.style.display = 'none';
}

export async function showClientCompatibility(videoId) {
    const modal = document.getElementById('clientCompatibilityModal');
    const content = document.getElementById('clientCompatibilityContent');
    const title = document.getElementById('clientCompatibilityTitle');
    
    if (!modal || !content) return;
    
    modal.style.display = 'block';
    content.innerHTML = '<div class="loading-placeholder">Loading client compatibility...</div>';
    
    try {
        const response = await fetch(`/api/library/videos/${videoId}`);
        if (!response.ok) throw new Error('Failed to load video info');
        
        const video = await response.json();
        title.textContent = escapeHtml(video.fileName || 'Client Compatibility') + ' - Client Details';
        
        // Parse client results
        let clientResults = {};
        try {
            const clientResultsRaw = video.clientResults || video.ClientResults || '{}';
            clientResults = typeof clientResultsRaw === 'string' ? JSON.parse(clientResultsRaw) : clientResultsRaw;
        } catch (e) {
            console.error('Error parsing client results:', e);
            content.innerHTML = '<div class="error-state"><p>Client compatibility data not available for this video. It may need to be re-scanned.</p></div>';
            return;
        }
        
        if (!clientResults || Object.keys(clientResults).length === 0) {
            content.innerHTML = '<div class="empty-state"><p>No client compatibility data available. The video may need to be re-scanned.</p></div>';
            return;
        }
        
        // Group clients by status
        const directPlayClients = [];
        const remuxClients = [];
        const transcodeClients = [];
        
        Object.entries(clientResults).forEach(([client, result]) => {
            const status = result.status || result.Status || 'Unknown';
            const reason = result.reason || result.Reason || '';
            let warnings = result.warnings || result.Warnings || [];
            
            // Ensure warnings is an array
            if (typeof warnings === 'string') {
                try {
                    warnings = JSON.parse(warnings);
                } catch {
                    warnings = [warnings];
                }
            }
            if (!Array.isArray(warnings)) {
                warnings = [];
            }
            
            // Remove duplicates and empty warnings
            warnings = [...new Set(warnings.filter(w => w && w.trim()))];
            
            const clientInfo = {
                name: client,
                status: status,
                reason: reason,
                warnings: warnings
            };
            
            if (status === 'Direct Play') {
                directPlayClients.push(clientInfo);
            } else if (status === 'Remux') {
                remuxClients.push(clientInfo);
            } else if (status === 'Transcode') {
                transcodeClients.push(clientInfo);
            }
        });
        
        content.innerHTML = `
            <div class="client-compatibility-grid">
                <div class="client-group">
                    <h4 style="color: var(--success-color); margin-bottom: 1rem;">
                        ✓ Direct Play (${directPlayClients.length} clients)
                    </h4>
                    ${directPlayClients.length > 0 
                        ? directPlayClients.map(client => `
                            <div class="client-item client-direct-play">
                                <div class="client-name">${escapeHtml(client.name)}</div>
                                ${client.reason ? `<div class="client-reason">${escapeHtml(client.reason)}</div>` : ''}
                                ${client.warnings && client.warnings.length > 0 
                                    ? `<div class="client-warnings">⚠️ ${client.warnings.slice(0, 3).map(w => escapeHtml(w)).join('; ')}${client.warnings.length > 3 ? ` (+${client.warnings.length - 3} more)` : ''}</div>` 
                                    : ''}
                            </div>
                        `).join('')
                        : '<div class="empty-state">No clients support Direct Play</div>'
                    }
                </div>
                
                <div class="client-group">
                    <h4 style="color: var(--warning-color); margin-bottom: 1rem;">
                        ~ Remux (${remuxClients.length} clients)
                    </h4>
                    ${remuxClients.length > 0 
                        ? remuxClients.map(client => `
                            <div class="client-item client-remux">
                                <div class="client-name">${escapeHtml(client.name)}</div>
                                ${client.reason ? `<div class="client-reason">${escapeHtml(client.reason)}</div>` : ''}
                                ${client.warnings && client.warnings.length > 0 
                                    ? `<div class="client-warnings">⚠️ ${client.warnings.slice(0, 3).map(w => escapeHtml(w)).join('; ')}${client.warnings.length > 3 ? ` (+${client.warnings.length - 3} more)` : ''}</div>` 
                                    : ''}
                            </div>
                        `).join('')
                        : '<div class="empty-state">No clients require Remux</div>'
                    }
                </div>
                
                <div class="client-group">
                    <h4 style="color: var(--danger-color); margin-bottom: 1rem;">
                        ✗ Transcode (${transcodeClients.length} clients)
                    </h4>
                    ${transcodeClients.length > 0 
                        ? transcodeClients.map(client => `
                            <div class="client-item client-transcode">
                                <div class="client-name">${escapeHtml(client.name)}</div>
                                ${client.reason ? `<div class="client-reason">${escapeHtml(client.reason)}</div>` : ''}
                                ${client.warnings && client.warnings.length > 0 
                                    ? `<div class="client-warnings">⚠️ ${client.warnings.slice(0, 3).map(w => escapeHtml(w)).join('; ')}${client.warnings.length > 3 ? ` (+${client.warnings.length - 3} more)` : ''}</div>` 
                                    : ''}
                            </div>
                        `).join('')
                        : '<div class="empty-state">No clients require Transcode</div>'
                    }
                </div>
            </div>
        `;
    } catch (error) {
        console.error('Error loading client compatibility:', error);
        content.innerHTML = `<div class="error-state"><p>Error loading client compatibility: ${escapeHtml(error.message)}</p></div>`;
    }
}

export function closeClientCompatibilityModal() {
    const modal = document.getElementById('clientCompatibilityModal');
    if (modal) modal.style.display = 'none';
}

export async function rescanVideo(videoId) {
    const button = document.getElementById('rescanVideoBtn');
    const iconSymbol = document.getElementById('rescanIconSymbol');
    
    if (!button || !iconSymbol) return;
    
    button.disabled = true;
    button.style.opacity = '0.6';
    button.style.cursor = 'not-allowed';
    // Add rotation animation class
    iconSymbol.classList.add('rescan-rotating');
    
    try {
        console.log(`Starting rescan for video ID: ${videoId}`);
        
        // Add timeout to fetch (10 minutes for video analysis to match server timeout)
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), 600000); // 10 minutes
        
        const response = await fetch(`/api/library/videos/${videoId}/rescan`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            signal: controller.signal
        }).catch(fetchError => {
            console.error('Fetch error details:', {
                name: fetchError.name,
                message: fetchError.message,
                stack: fetchError.stack,
                cause: fetchError.cause
            });
            throw fetchError;
        });
        
        clearTimeout(timeoutId);
        
        console.log(`Rescan response status: ${response.status} ${response.statusText}`);
        
        if (!response.ok) {
            let errorMessage = 'Failed to rescan video';
            try {
                const error = await response.json();
                errorMessage = error.error || error.message || errorMessage;
                console.error('Server error response:', error);
            } catch (e) {
                // If response is not JSON, use status text
                const responseText = await response.text().catch(() => '');
                errorMessage = `${response.status} ${response.statusText}${responseText ? ': ' + responseText : ''}`;
                console.error('Non-JSON error response:', responseText);
            }
            throw new Error(errorMessage);
        }
        
        const updatedVideo = await response.json();
        console.log('Rescan completed successfully');
        
        // Remove rotation and show success briefly
        iconSymbol.classList.remove('rescan-rotating');
        iconSymbol.style.color = 'var(--success-color)';
        
        // Reload the media info to show updated data
        setTimeout(() => {
            showMediaInfo(videoId);
        }, 1000);
        
    } catch (error) {
        console.error('Error rescanning video:', error);
        console.error('Error details:', {
            name: error.name,
            message: error.message,
            stack: error.stack
        });
        
        // Remove rotation and show error briefly
        iconSymbol.classList.remove('rescan-rotating');
        iconSymbol.style.color = 'var(--error-color)';
        
        let errorMessage = 'Failed to rescan video';
        if (error.name === 'AbortError') {
            errorMessage = 'Request timed out. The video analysis is taking longer than expected. Please try again.';
        } else if (error.message) {
            errorMessage = error.message;
        } else if (error instanceof TypeError && error.message.includes('fetch')) {
            errorMessage = 'Network error. Please check your connection and try again. If the problem persists, check the server logs.';
        }
        
        alert(`Error rescanning video: ${errorMessage}`);
    } finally {
        setTimeout(() => {
            if (button) {
                button.disabled = false;
                button.style.opacity = '1';
                button.style.cursor = 'pointer';
            }
            if (iconSymbol) {
                iconSymbol.style.color = '';
            }
        }, 3000);
    }
}

export async function showTrackDetails(videoId, trackType) {
    try {
        const response = await fetch(`/api/library/videos/${videoId}`);
        if (!response.ok) throw new Error('Failed to load video details');
        
        const video = await response.json();
        console.log('Video data received:', video);
        
        let tracks = [];
        let title = '';
        let content = '';
        
        if (trackType === 'audio') {
            title = 'Audio Tracks';
            // Try both camelCase and PascalCase property names
            const audioTracksJson = video.audioTracksJson || video.AudioTracksJson || '';
            console.log('Audio tracks JSON:', audioTracksJson);
            
            if (audioTracksJson && audioTracksJson.trim() !== '') {
                try {
                    tracks = typeof audioTracksJson === 'string' ? JSON.parse(audioTracksJson) : audioTracksJson;
                    console.log('Parsed audio tracks:', tracks);
                } catch (e) {
                    console.error('Error parsing audio tracks:', e, 'Raw JSON:', audioTracksJson);
                    tracks = [];
                }
            } else {
                console.warn('Audio tracks JSON is empty or missing');
            }
            
            if (tracks.length === 0) {
                content = '<div class="empty-state">No audio track details available. The video may need to be rescanned to populate track information.</div>';
            } else {
                content = `
                    <div class="tracks-list">
                        ${tracks.map((track, index) => {
                            // Handle both camelCase and PascalCase property names
                            const codec = track.codec || track.Codec || 'N/A';
                            const rawLanguage = track.language || track.Language || '';
                            const language = rawLanguage.trim() || 'Unknown';
                            const hasLanguage = rawLanguage.trim() !== '' && rawLanguage.trim().toLowerCase() !== 'unknown';
                            const channels = track.channels !== undefined ? track.channels : (track.Channels !== undefined ? track.Channels : 'N/A');
                            const sampleRate = track.sampleRate !== undefined ? track.sampleRate : (track.SampleRate !== undefined ? track.SampleRate : null);
                            const bitrate = track.bitrate !== undefined ? track.bitrate : (track.Bitrate !== undefined ? track.Bitrate : null);
                            
                            // Warning styling for tracks without language
                            const warningStyle = !hasLanguage ? 'border-left-color: var(--warning-color, #f39c12); background-color: rgba(243, 156, 18, 0.1);' : '';
                            const languageStyle = !hasLanguage ? 'color: var(--warning-color, #f39c12); font-weight: 600;' : '';
                            
                            return `
                            <div class="track-item" style="padding: 1rem; margin-bottom: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--primary-color); ${warningStyle}">
                                <div style="display: flex; justify-content: space-between; align-items: start; margin-bottom: 0.5rem;">
                                    <h5 style="margin: 0; color: var(--text-primary);">Track ${index + 1}</h5>
                                    ${!hasLanguage ? '<span style="font-size: 0.75rem; padding: 0.25rem 0.5rem; background-color: var(--warning-color, #f39c12); color: white; border-radius: 3px; font-weight: 600;">⚠ No Language</span>' : ''}
                                </div>
                                <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0.75rem;">
                                    <div><strong>Codec:</strong> ${escapeHtml(codec)}</div>
                                    <div><strong>Language:</strong> <span style="${languageStyle}">${escapeHtml(language)}</span></div>
                                    <div><strong>Channels:</strong> ${channels}</div>
                                    <div><strong>Sample Rate:</strong> ${sampleRate ? (sampleRate / 1000).toFixed(1) + ' kHz' : 'N/A'}</div>
                                    ${bitrate ? `<div><strong>Bitrate:</strong> ${bitrate} kbps</div>` : ''}
                                </div>
                            </div>
                            `;
                        }).join('')}
                    </div>
                `;
            }
        } else if (trackType === 'subtitle') {
            title = 'Subtitle Tracks';
            // Try both camelCase and PascalCase property names
            const subtitleTracksJson = video.subtitleTracksJson || video.SubtitleTracksJson || '';
            console.log('Subtitle tracks JSON:', subtitleTracksJson);
            
            if (subtitleTracksJson && subtitleTracksJson.trim() !== '') {
                try {
                    tracks = typeof subtitleTracksJson === 'string' ? JSON.parse(subtitleTracksJson) : subtitleTracksJson;
                    console.log('Parsed subtitle tracks:', tracks);
                } catch (e) {
                    console.error('Error parsing subtitle tracks:', e, 'Raw JSON:', subtitleTracksJson);
                    tracks = [];
                }
            } else {
                console.warn('Subtitle tracks JSON is empty or missing');
            }
            
            if (tracks.length === 0) {
                content = '<div class="empty-state">No subtitle track details available. The video may need to be rescanned to populate track information.</div>';
            } else {
                content = `
                    <div class="tracks-list">
                        ${tracks.map((track, index) => {
                            // Handle both camelCase and PascalCase property names
                            const format = track.format || track.Format || 'N/A';
                            const rawLanguage = track.language || track.Language || '';
                            const language = rawLanguage.trim() || 'Unknown';
                            const hasLanguage = rawLanguage.trim() !== '' && rawLanguage.trim().toLowerCase() !== 'unknown';
                            const isEmbedded = track.isEmbedded !== undefined ? track.isEmbedded : (track.IsEmbedded !== undefined ? track.IsEmbedded : false);
                            const filePath = track.filePath || track.FilePath || '';
                            
                            // Warning styling for tracks without language
                            const warningStyle = !hasLanguage ? 'border-left-color: var(--warning-color, #f39c12); background-color: rgba(243, 156, 18, 0.1);' : '';
                            const languageStyle = !hasLanguage ? 'color: var(--warning-color, #f39c12); font-weight: 600;' : '';
                            
                            return `
                            <div class="track-item" style="padding: 1rem; margin-bottom: 0.75rem; background-color: var(--bg-secondary); border-radius: 4px; border-left: 3px solid var(--primary-color); ${warningStyle}">
                                <div style="display: flex; justify-content: space-between; align-items: start; margin-bottom: 0.5rem; gap: 0.5rem; flex-wrap: wrap;">
                                    <h5 style="margin: 0; color: var(--text-primary);">Track ${index + 1}</h5>
                                    <div style="display: flex; gap: 0.5rem; align-items: center;">
                                        ${!hasLanguage ? '<span style="font-size: 0.75rem; padding: 0.25rem 0.5rem; background-color: var(--warning-color, #f39c12); color: white; border-radius: 3px; font-weight: 600;">⚠ No Language</span>' : ''}
                                        <span style="font-size: 0.875rem; padding: 0.25rem 0.5rem; background-color: ${isEmbedded ? 'var(--success-color)' : 'var(--accent-color)'}; color: white; border-radius: 3px;">
                                            ${isEmbedded ? 'Embedded' : 'External'}
                                        </span>
                                    </div>
                                </div>
                                <div style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 0.75rem;">
                                    <div><strong>Format:</strong> ${escapeHtml(format)}</div>
                                    <div><strong>Language:</strong> <span style="${languageStyle}">${escapeHtml(language)}</span></div>
                                    ${filePath ? `<div style="grid-column: 1 / -1;"><strong>File Path:</strong> <code style="font-size: 0.875rem; color: var(--text-secondary);">${escapeHtml(filePath)}</code></div>` : ''}
                                </div>
                            </div>
                            `;
                        }).join('')}
                    </div>
                `;
            }
        }
        
        // Show in separate track details modal (on top of media info modal)
        const trackModal = document.getElementById('trackDetailsModal');
        const trackModalTitle = document.getElementById('trackDetailsTitle');
        const trackModalContent = document.getElementById('trackDetailsContent');
        
        if (trackModal && trackModalTitle && trackModalContent) {
            trackModalTitle.textContent = title;
            trackModalContent.innerHTML = content;
            trackModal.style.display = 'block';
        }
    } catch (error) {
        console.error('Error loading track details:', error);
        alert(`Error loading track details: ${error.message}`);
    }
}

export function closeTrackDetailsModal() {
    const trackModal = document.getElementById('trackDetailsModal');
    if (trackModal) {
        trackModal.style.display = 'none';
    }
    // Don't close the media info modal - user can go back to it
}

// Export to window for onclick handlers
window.showMediaInfo = showMediaInfo;
window.closeMediaModal = closeMediaModal;
window.closeClientCompatibilityModal = closeClientCompatibilityModal;
window.closeTrackDetailsModal = closeTrackDetailsModal;
window.rescanVideo = rescanVideo;
window.showTrackDetails = showTrackDetails;

