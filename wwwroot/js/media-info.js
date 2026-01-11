// Media Info Modal Functions
import { escapeHtml, formatFileSize, formatDuration, formatTimeSpan, getRatingCategory } from './utils.js';

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
            
            // HDR/SDR issues - but these are visual quality, not compatibility issues
            // We track them but won't show compatibility warning icon
            if (issueLower.includes('sdr') && issueLower.includes('visual quality')) {
                // SDR content is a visual quality issue, not a compatibility issue
                // We'll handle this separately in the display
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
        
        // Parse issues and recommendations if available (must be done before using issuesList)
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
        
        // Helper to create info item with optional warning
        const createInfoItem = (label, value, isNonOptimal = false, isVisualQuality = false) => {
            let warningIcon = '';
            if (isNonOptimal) {
                if (isVisualQuality) {
                    // Visual quality issues (like SDR) get a different icon/message
                    warningIcon = '<span class="warning-icon" title="This field may have reduced visual quality compared to alternatives" style="opacity: 0.7;">💡</span>';
                } else {
                    // Compatibility issues get the standard warning
                    warningIcon = '<span class="warning-icon" title="This field may cause compatibility issues">⚠️</span>';
                }
            }
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
        
        // Check if HDR/SDR is a visual quality issue (not compatibility)
        // The backend message is: "SDR content may have reduced visual quality compared to HDR"
        const isHDRVisualQualityIssue = issuesList.some(issue => {
            const issueLower = issue.toLowerCase();
            return issueLower.includes('sdr') && (issueLower.includes('visual quality') || issueLower.includes('reduced visual quality'));
        });
        
        // Format audio codecs as a list
        // Check each codec individually to see if it's problematic
        const audioCodecsList = video.audioCodecs ? video.audioCodecs.split(',').map(c => c.trim()).filter(c => c) : [];
        const problematicAudioCodecs = new Set();
        
        // Normalize codec names for matching (remove hyphens, spaces, dots, etc.)
        const normalizeCodecName = (name) => {
            let normalized = name.toLowerCase().replace(/[-\s\.]/g, '');
            // Handle equivalent codec names
            // EC-3 and E-AC-3 are the same (Enhanced AC-3), normalize both to 'eac3'
            if (normalized === 'ec3' || normalized === 'eac3') {
                normalized = 'eac3';
            }
            return normalized;
        };
        
        // Check issues for specific problematic codecs
        issuesList.forEach(issue => {
            const issueLower = issue.toLowerCase();
            let issueNormalized = normalizeCodecName(issue); // Normalize the entire issue for better matching
            // Also normalize common codec variations in issue text for matching
            // Replace "ec3" with "eac3" in issue text since they're equivalent (EC-3 = E-AC-3)
            issueNormalized = issueNormalized.replace(/ec3/g, 'eac3');
            const isAudioIssue = issueLower.includes('audio') && 
                (issueLower.includes('not supported') || 
                 issueLower.includes('unsupported') || 
                 issueLower.includes('transcoding') ||
                 issueLower.includes('codec not supported') ||
                 issueLower.includes('audio codec not supported'));
            
            if (!isAudioIssue) return;
            
            // Check each codec against the issue using normalized matching
            audioCodecsList.forEach(codec => {
                const codecLower = codec.toLowerCase();
                const normalizedCodec = normalizeCodecName(codec);
                
                // Strategy 1: Direct match (case-insensitive) in original issue text
                if (issueLower.includes(codecLower)) {
                    problematicAudioCodecs.add(codec);
                    return;
                }
                
                // Strategy 2: Normalized codec name in normalized issue text
                // This handles cases like "ec-3" in issue matching "EC-3" codec
                if (issueNormalized.includes(normalizedCodec)) {
                    problematicAudioCodecs.add(codec);
                    return;
                }
                
                // Strategy 3: Check if normalized codec appears as substring in normalized issue
                // This handles partial matches and variations
                if (normalizedCodec.length >= 2 && issueNormalized.includes(normalizedCodec)) {
                    problematicAudioCodecs.add(codec);
                }
            });
            
            // Also check for known problematic codec patterns
            // DTS (but not DTS-HD)
            if (issueNormalized.includes('dts') && !issueNormalized.includes('dtshd')) {
                audioCodecsList.forEach(codec => {
                    const codecNormalized = normalizeCodecName(codec);
                    if (codecNormalized.includes('dts') && !codecNormalized.includes('dtshd')) {
                        problematicAudioCodecs.add(codec);
                    }
                });
            }
            // AC3, EAC3, EC-3, E-AC-3 (all variations)
            // Check normalized issue for any AC3/EAC3/EC-3 pattern
            // Note: EC-3 and E-AC-3 are the same codec, both normalize to 'eac3'
            // After normalization, "ec3" in issue text is replaced with "eac3"
            if (issueNormalized.includes('ac3') || issueNormalized.includes('eac3')) {
                audioCodecsList.forEach(codec => {
                    const codecNormalized = normalizeCodecName(codec);
                    // Match AC3 (normalizes to 'ac3')
                    // Match EAC3/E-AC-3/EC-3 (all normalize to 'eac3' after equivalence handling)
                    if (codecNormalized === 'ac3' || codecNormalized === 'eac3') {
                        problematicAudioCodecs.add(codec);
                    }
                });
            }
            // ALAC
            if (issueNormalized.includes('alac')) {
                audioCodecsList.forEach(codec => {
                    if (normalizeCodecName(codec).includes('alac')) {
                        problematicAudioCodecs.add(codec);
                    }
                });
            }
        });
        
        const audioCodecsDisplay = audioCodecsList.length > 0 
            ? audioCodecsList.map(codec => {
                const isProblematic = problematicAudioCodecs.has(codec);
                return `<span class="codec-tag ${isProblematic ? 'non-optimal' : ''}">${escapeHtml(codec)}</span>`;
            }).join(' ')
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
        // Formula: (fileSize in bytes * 8 bits/byte) / (duration in seconds * 1,000,000 bits/Mbps) = Mbps
        // Note: 1 Mbps = 1,000,000 bits per second
        // Also correct the duration value if it seems wrong (for display purposes)
        let estimatedBitrate = 'N/A';
        let correctedDuration = video.duration; // Use corrected duration for display
        
        // First, check if duration is extremely suspicious (< 10 seconds) - always try correction
        if (video.duration && video.duration > 0 && video.duration < 10 && video.fileSize) {
            // For durations < 10 seconds, almost certainly wrong - try multiplying by 1000
            const testDuration = video.duration * 1000.0;
            const testBps = (video.fileSize * 8) / testDuration;
            const testMbps = testBps / 1000000;
            
            // If the corrected duration gives a reasonable bitrate (< 2000 Mbps) and duration >= 10 seconds, use it
            if (testMbps < 2000 && testMbps > 0 && testDuration >= 10) {
                correctedDuration = testDuration;
                console.log(`Auto-correcting extremely short duration: ${video.duration}s -> ${correctedDuration}s (bitrate: ${testMbps.toFixed(2)} Mbps)`);
            }
        }
        
        if (video.fileSize && video.duration && video.duration > 0) {
            let durationSeconds = correctedDuration; // Start with corrected duration if available
            
            // Calculate initial bitrate to check if duration unit is correct
            const initialBps = (video.fileSize * 8) / durationSeconds;
            const initialMbps = initialBps / 1000000;
            
            // Check if duration seems wrong based on file size and bitrate
            // Multiple heuristics to catch different cases:
            // 1. Bitrate is unreasonably high (> 1000 Mbps)
            // 2. Duration is very small (< 100 seconds) for a large file (> 100MB)
            // 3. Duration is suspiciously small (< 60 seconds) for any video file
            // 4. Duration seems too short relative to file size (heuristic: fileSize/duration ratio)
            // 5. Duration is very small (< 10 seconds) - almost certainly wrong for any video
            const isLargeFile = video.fileSize > 100 * 1024 * 1024; // > 100MB
            const isSuspiciouslyShort = durationSeconds < 100 && isLargeFile;
            const isBitrateTooHigh = initialMbps > 1000;
            const isVeryShortForAnyFile = durationSeconds < 60; // Most videos are at least 1 minute
            const isExtremelyShort = durationSeconds < 10; // Almost certainly wrong
            
            // Additional heuristic: if file size per second is unreasonably high (> 50MB/s), duration might be wrong
            // This catches cases where duration is wrong but bitrate calculation doesn't trigger
            const bytesPerSecond = video.fileSize / durationSeconds;
            const mbPerSecond = bytesPerSecond / (1024 * 1024);
            const isUnreasonableFileSizePerSecond = mbPerSecond > 50; // > 50 MB/s is very high
            
            // Try correction if any suspicious condition is met
            if (isBitrateTooHigh || isSuspiciouslyShort || isVeryShortForAnyFile || isUnreasonableFileSizePerSecond || isExtremelyShort) {
                // Duration is likely in wrong unit
                if (isBitrateTooHigh) {
                    console.warn(`Bitrate ${initialMbps.toFixed(2)} Mbps seems too high. Duration ${durationSeconds} might be in wrong unit.`);
                } else if (isSuspiciouslyShort) {
                    console.warn(`Duration ${durationSeconds}s seems too short for file size ${(video.fileSize / (1024*1024)).toFixed(2)}MB. Duration might be in wrong unit.`);
                } else if (isVeryShortForAnyFile) {
                    console.warn(`Duration ${durationSeconds}s seems suspiciously short (< 60s) for a video file. Duration might be in wrong unit.`);
                } else if (isUnreasonableFileSizePerSecond) {
                    console.warn(`File size per second ${mbPerSecond.toFixed(2)}MB/s seems unreasonably high. Duration ${durationSeconds}s might be in wrong unit.`);
                }
                
                // Try different conversions
                // Option 1: If duration < 1, it might be in milliseconds -> divide by 1000
                // Option 2: If duration is small (1-100 seconds) but file is large, maybe it's actually in milliseconds -> multiply by 1000
                // Option 3: If duration is very small (< 0.01), it might be in microseconds -> divide by 1,000,000
                
                let foundCorrection = false;
                
                if (durationSeconds < 0.01) {
                    // Very small - might be in microseconds
                    const testDuration = video.duration * 1000.0;
                    const testBps = (video.fileSize * 8) / testDuration;
                    const testMbps = testBps / 1000000;
                    if (testMbps < 1000 && testMbps > 0) {
                        durationSeconds = testDuration;
                        correctedDuration = durationSeconds;
                        foundCorrection = true;
                        console.log(`Trying microseconds conversion: ${video.duration} * 1000 = ${durationSeconds} seconds`);
                    }
                } else if (durationSeconds < 1) {
                    // Less than 1 second - might be in milliseconds
                    const testDuration = video.duration / 1000.0;
                    const testBps = (video.fileSize * 8) / testDuration;
                    const testMbps = testBps / 1000000;
                    if (testMbps < 1000 && testMbps > 0) {
                        durationSeconds = testDuration;
                        correctedDuration = durationSeconds;
                        foundCorrection = true;
                        console.log(`Trying milliseconds conversion: ${video.duration} / 1000 = ${durationSeconds} seconds`);
                    }
                } else if (durationSeconds < 100 || isUnreasonableFileSizePerSecond || isExtremelyShort) {
                    // Small duration (1-100 seconds) OR unreasonable file size per second OR extremely short
                    // Maybe duration is actually in milliseconds - try multiplying by 1000
                    const alternativeDuration = video.duration * 1000.0;
                    const alternativeBps = (video.fileSize * 8) / alternativeDuration;
                    const alternativeMbps = alternativeBps / 1000000;
                    const alternativeMbPerSecond = (video.fileSize / alternativeDuration) / (1024 * 1024);
                    
                    // For extremely short durations (< 10s), be more lenient with validation
                    // Accept the correction if:
                    // 1. Bitrate becomes reasonable (< 1000 Mbps)
                    // 2. The alternative duration is at least 60 seconds (more reasonable for videos)
                    //    OR if original was extremely short, accept if alternative is at least 10 seconds
                    // 3. The alternative bitrate is > 0 (valid)
                    // 4. File size per second becomes more reasonable (< 50 MB/s)
                    const minAcceptableDuration = isExtremelyShort ? 10 : 60;
                    if (alternativeMbps < 1000 && alternativeMbps > 0 && alternativeDuration >= minAcceptableDuration && alternativeMbPerSecond < 50) {
                        durationSeconds = alternativeDuration;
                        correctedDuration = durationSeconds;
                        foundCorrection = true;
                        console.log(`Trying reverse conversion (duration was incorrectly converted): ${video.duration} * 1000 = ${durationSeconds} seconds`);
                    } else if (isExtremelyShort && alternativeDuration >= 10) {
                        // For extremely short durations, be even more lenient - just check if it's at least 10 seconds
                        // and bitrate is reasonable
                        if (alternativeMbps < 2000 && alternativeMbps > 0) {
                            durationSeconds = alternativeDuration;
                            correctedDuration = durationSeconds;
                            foundCorrection = true;
                            console.log(`Trying reverse conversion (extremely short duration, lenient check): ${video.duration} * 1000 = ${durationSeconds} seconds`);
                        }
                    }
                }
                
                // Recalculate with corrected duration
                const recalculatedBps = (video.fileSize * 8) / durationSeconds;
                const recalculatedMbps = recalculatedBps / 1000000;
                
                // If still unreasonably high, the duration value is likely corrupted
                if (recalculatedMbps > 1000 && foundCorrection) {
                    console.error(`Bitrate ${recalculatedMbps.toFixed(2)} Mbps is still unreasonably high after conversion attempts. Duration value may be corrupted.`);
                    console.error(`FileSize=${video.fileSize} bytes, OriginalDuration=${video.duration}, FinalDuration=${durationSeconds}`);
                    // Show "N/A" or a warning instead of an impossible value
                    estimatedBitrate = 'N/A (invalid duration)';
                    correctedDuration = video.duration; // Revert to original
                } else {
                    estimatedBitrate = `${recalculatedMbps.toFixed(2)} Mbps`;
                    // correctedDuration already set above if foundCorrection is true
                }
            } else {
                // Bitrate is reasonable, use as-is
                estimatedBitrate = `${initialMbps.toFixed(2)} Mbps`;
            }
            
            // Always use the final durationSeconds value (which may have been corrected)
            // This ensures correctedDuration matches what was used in bitrate calculation
            correctedDuration = durationSeconds;
            
            // Debug logging
            if (estimatedBitrate !== 'N/A (invalid duration)') {
                console.log(`Bitrate calculation: FileSize=${video.fileSize} bytes, Duration=${durationSeconds} seconds, Bitrate=${estimatedBitrate}`);
                if (correctedDuration !== video.duration) {
                    console.log(`Duration corrected for display: ${video.duration}s -> ${correctedDuration}s`);
                }
            }
        }
        
        // Format analyzed date
        const analyzedDate = video.analyzedAt ? new Date(video.analyzedAt).toLocaleString() : 'N/A';
        const rescanIcon = `<button type="button" class="rescan-icon-btn" onclick="rescanVideo(${video.id})" id="rescanVideoBtn" title="Rescan video" style="background: none; border: none; cursor: pointer; padding: 0; margin-left: 0.5rem; font-size: 1rem; color: var(--text-secondary); transition: color 0.2s;" onmouseover="this.style.color='var(--primary-color)'" onmouseout="this.style.color='var(--text-secondary)'"><span id="rescanIconSymbol" style="display: inline-block;">↻</span></button>`;
        
        // Load playback history for this video
        let playbackHistory = [];
        try {
            const playbackResponse = await fetch(`/api/playback/history/video/${videoId}`);
            if (playbackResponse.ok) {
                const playbackData = await playbackResponse.json();
                playbackHistory = playbackData.items || [];
            }
        } catch (error) {
            console.warn('Error loading playback history:', error);
        }
        
        // Servarr information
        let servarrSection = '';
        if (video.servarrType === 'Sonarr' && video.sonarrSeriesTitle) {
            servarrSection = `
                <div class="info-section servarr-section">
                    <h4 style="display: flex; align-items: center; gap: 0.5rem;">
                        <span>📺</span>
                        <span>Sonarr Information</span>
                    </h4>
                    ${createInfoItem('Series', escapeHtml(video.sonarrSeriesTitle || 'N/A'), false)}
                    ${video.sonarrSeasonNumber !== null && video.sonarrSeasonNumber !== undefined 
                        ? createInfoItem('Season', video.sonarrSeasonNumber.toString(), false) 
                        : ''}
                    ${video.sonarrEpisodeNumber !== null && video.sonarrEpisodeNumber !== undefined 
                        ? createInfoItem('Episode', video.sonarrEpisodeNumber.toString(), false) 
                        : ''}
                    ${video.sonarrSeriesId 
                        ? createInfoItem('Series ID', video.sonarrSeriesId.toString(), false) 
                        : ''}
                    ${video.servarrMatchedAt 
                        ? createInfoItem('Matched', new Date(video.servarrMatchedAt).toLocaleString(), false) 
                        : ''}
                </div>
            `;
        } else if (video.servarrType === 'Radarr' && video.radarrMovieTitle) {
            servarrSection = `
                <div class="info-section servarr-section">
                    <h4 style="display: flex; align-items: center; gap: 0.5rem;">
                        <span>🎬</span>
                        <span>Radarr Information</span>
                    </h4>
                    ${createInfoItem('Movie', escapeHtml(video.radarrMovieTitle || 'N/A'), false)}
                    ${video.radarrYear 
                        ? createInfoItem('Year', video.radarrYear.toString(), false) 
                        : ''}
                    ${video.radarrMovieId 
                        ? createInfoItem('Movie ID', video.radarrMovieId.toString(), false) 
                        : ''}
                    ${video.servarrMatchedAt 
                        ? createInfoItem('Matched', new Date(video.servarrMatchedAt).toLocaleString(), false) 
                        : ''}
                </div>
            `;
        }
        
        content.innerHTML = `
            <div class="media-info-grid">
                <div class="info-section">
                    <h4>File Information</h4>
                    ${createInfoItem('File Name', escapeHtml(video.fileName || 'N/A'), false)}
                    <div class="info-item" data-field="file-path">
                        <span class="info-label">File Path:</span>
                        <span class="info-value" style="word-break: break-all; overflow-wrap: anywhere; text-align: left; justify-content: flex-start; max-width: 100%; white-space: normal;">
                            <code style="font-size: 0.875rem; color: var(--text-secondary);">${escapeHtml(video.filePath || 'N/A')}</code>
                        </span>
                    </div>
                    ${createInfoItem('File Size', formatFileSize(video.fileSize || 0), false)}
                    ${createInfoItem('Container', escapeHtml(video.container || 'NULL'), nonOptimal.container)}
                    <div class="info-item">
                        <span class="info-label">Analyzed:</span>
                        <span class="info-value">
                            ${analyzedDate}${rescanIcon}
                        </span>
                    </div>
                </div>
                ${servarrSection}
                
                <div class="info-section">
                    <h4>Video Information</h4>
                    ${createInfoItem('Codec', escapeHtml(video.videoCodec || 'NULL'), nonOptimal.videoCodec)}
                    ${video.videoCodecTag ? createInfoItem('Codec Tag', escapeHtml(video.videoCodecTag) + (video.isCodecTagCorrect ? ' ✓' : ' ⚠'), nonOptimal.codecTag) : ''}
                    ${createInfoItem('Resolution', `${video.width || 0}x${video.height || 0}`, false)}
                    ${createInfoItem('Aspect Ratio', displayAspectRatio, false)}
                    ${createInfoItem('Frame Rate', video.frameRate ? video.frameRate.toFixed(3) + ' fps' : 'N/A', false)}
                    ${createInfoItem('Bit Depth', `${video.bitDepth || 8}-bit`, nonOptimal.bitDepth)}
                    ${createInfoItem('HDR', video.isHDR ? (video.hdrType || 'Yes') : 'No', isHDRVisualQualityIssue, true)}
                    ${createInfoItem('Duration', correctedDuration && correctedDuration > 0 ? formatDuration(correctedDuration) : 'N/A', false)}
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
                            <span class="rating-badge rating-${getRatingCategory(video.compatibilityRating ?? 0)} rating-clickable" 
                                  style="font-size: 1.1rem; font-weight: 600; cursor: pointer; text-decoration: underline;" 
                                  onclick="showRatingDetails(${video.id})" 
                                  title="Click to view rating details">${video.compatibilityRating ?? 0}/100</span>
                            <span class="score-badge ${(video.overallScore || '').toLowerCase()}" style="margin-left: 0.5rem;">${escapeHtml(video.overallScore || 'Unknown')}</span>
                        </span>
                    </div>
                </div>
                
                ${playbackHistory.length > 0 ? `
                <div class="info-section" style="grid-column: 1 / -1;">
                    <h4>Playback History (${playbackHistory.length})</h4>
                    <div style="max-height: 300px; overflow-y: auto; margin-top: 0.75rem;">
                        <table style="width: 100%; border-collapse: collapse; font-size: 0.875rem;">
                            <thead>
                                <tr style="border-bottom: 1px solid var(--border-color);">
                                    <th style="text-align: left; padding: 0.5rem; color: var(--text-secondary); font-weight: 600;">Date</th>
                                    <th style="text-align: left; padding: 0.5rem; color: var(--text-secondary); font-weight: 600;">Client</th>
                                    <th style="text-align: left; padding: 0.5rem; color: var(--text-secondary); font-weight: 600;">Device</th>
                                    <th style="text-align: left; padding: 0.5rem; color: var(--text-secondary); font-weight: 600;">User</th>
                                    <th style="text-align: left; padding: 0.5rem; color: var(--text-secondary); font-weight: 600;">Method</th>
                                    <th style="text-align: left; padding: 0.5rem; color: var(--text-secondary); font-weight: 600;">Duration</th>
                                </tr>
                            </thead>
                            <tbody>
                                ${playbackHistory.map(playback => {
                                    const playMethodClass = playback.isDirectPlay ? 'directplay' : 
                                                           playback.isDirectStream ? 'directstream' : 
                                                           'transcode';
                                    const playMethodLabel = playback.isDirectPlay ? 'Direct Play' : 
                                                           playback.isDirectStream ? 'Direct Stream' : 
                                                           'Transcode';
                                    const playMethodIcon = playback.isDirectPlay ? '✓' : 
                                                          playback.isDirectStream ? '~' : 
                                                          '✗';
                                    const startTime = new Date(playback.playbackStartTime);
                                    const duration = playback.playbackDuration ? formatTimeSpan(playback.playbackDuration) : '-';
                                    const transcodeReason = playback.isTranscode && playback.transcodeReason 
                                        ? `<br><small style="color: var(--text-secondary); font-size: 0.75rem;">${escapeHtml(playback.transcodeReason)}</small>` 
                                        : '';
                                    
                                    return `
                                        <tr style="border-bottom: 1px solid var(--border-color);">
                                            <td style="padding: 0.5rem; color: var(--text-primary);">${startTime.toLocaleString()}</td>
                                            <td style="padding: 0.5rem; color: var(--text-primary);">${escapeHtml(playback.clientName || 'Unknown')}</td>
                                            <td style="padding: 0.5rem; color: var(--text-primary);">${escapeHtml(playback.deviceName || 'Unknown')}</td>
                                            <td style="padding: 0.5rem; color: var(--text-primary);">${escapeHtml(playback.userName || '-')}</td>
                                            <td style="padding: 0.5rem;">
                                                <span class="playback-method-badge ${playMethodClass}" style="font-size: 0.75rem; padding: 0.25rem 0.5rem;">
                                                    ${playMethodIcon} ${playMethodLabel}
                                                </span>
                                                ${transcodeReason}
                                            </td>
                                            <td style="padding: 0.5rem; color: var(--text-primary);">${duration}</td>
                                        </tr>
                                    `;
                                }).join('')}
                            </tbody>
                        </table>
                    </div>
                </div>
                ` : ''}
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
        
    } catch (error) {
        console.error('Error loading media info:', error);
        content.innerHTML = `<div class="error-state"><p>Error loading media information: ${escapeHtml(error.message)}</p></div>`;
    }
}

export function closeMediaModal() {
    const modal = document.getElementById('mediaInfoModal');
    if (modal) modal.style.display = 'none';
}

export function closeRatingDetailsModal() {
    const modal = document.getElementById('ratingDetailsModal');
    if (modal) modal.style.display = 'none';
}

export async function showRatingDetails(videoId) {
    const modal = document.getElementById('ratingDetailsModal');
    const content = document.getElementById('ratingDetailsContent');
    const title = document.getElementById('ratingDetailsTitle');
    
    if (!modal || !content) return;
    
    modal.style.display = 'block';
    content.innerHTML = '<div class="loading-placeholder">Loading rating details...</div>';
    
    try {
        const response = await fetch(`/api/library/videos/${videoId}`);
        if (!response.ok) throw new Error('Failed to load video info');
        
        const video = await response.json();
        title.textContent = escapeHtml(video.fileName || 'Rating Details');
        
        // Parse issues and recommendations
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
            console.error('Error parsing issues/recommendations:', e);
        }
        
        const rating = video.compatibilityRating ?? 0;
        const maxRating = 100;
        const deductions = maxRating - rating;
        const ratingPercent = rating;
        
        // Determine rating color
        let ratingColor = 'var(--error-color)';
        let ratingLevel = 'Poor';
        if (rating >= 80) {
            ratingColor = 'var(--success-color)';
            ratingLevel = 'Optimal';
        } else if (rating >= 60) {
            ratingColor = 'var(--warning-color)';
            ratingLevel = 'Good';
        }
        
        content.innerHTML = `
            <div class="rating-details">
                <div style="text-align: center; margin-bottom: 2rem; padding: 1.5rem; background-color: var(--bg-secondary); border-radius: 8px;">
                    <div style="font-size: 3rem; font-weight: 700; color: ${ratingColor}; margin-bottom: 0.5rem;">
                        ${rating}/100
                    </div>
                    <div style="font-size: 1.25rem; color: var(--text-secondary); margin-bottom: 0.5rem;">
                        ${escapeHtml(video.overallScore || 'Unknown')}
                    </div>
                    <div style="width: 100%; height: 8px; background-color: var(--bg-tertiary); border-radius: 4px; overflow: hidden; margin-top: 1rem;">
                        <div style="width: ${ratingPercent}%; height: 100%; background-color: ${ratingColor}; transition: width 0.3s ease;"></div>
                    </div>
                </div>
                
                ${deductions > 0 ? `
                <div style="margin-bottom: 2rem;">
                    <h4 style="color: var(--text-primary); margin-bottom: 1rem;">Rating Breakdown</h4>
                    <div style="background-color: var(--bg-secondary); border-radius: 4px; padding: 1rem;">
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
                            <span style="color: var(--text-primary);">Starting Rating:</span>
                            <span style="font-weight: 600; color: var(--success-color);">100 points</span>
                        </div>
                        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.5rem;">
                            <span style="color: var(--text-primary);">Deductions:</span>
                            <span style="font-weight: 600; color: var(--error-color);">-${deductions} points</span>
                        </div>
                        <div style="border-top: 1px solid var(--border-color); margin-top: 0.5rem; padding-top: 0.5rem; display: flex; justify-content: space-between; align-items: center;">
                            <span style="color: var(--text-primary); font-weight: 600;">Final Rating:</span>
                            <span style="font-weight: 700; font-size: 1.25rem; color: ${ratingColor};">${rating} points</span>
                        </div>
                    </div>
                </div>
                ` : `
                <div style="margin-bottom: 2rem; padding: 1rem; background-color: rgba(46, 204, 113, 0.1); border-left: 3px solid var(--success-color); border-radius: 4px;">
                    <p style="margin: 0; color: var(--text-primary);">
                        <strong>Perfect Rating!</strong> This video has no compatibility issues and received the maximum rating of 100/100.
                    </p>
                </div>
                `}
                
                ${issuesList.length > 0 ? `
                <div style="margin-bottom: 2rem;">
                    <h4 style="color: var(--error-color); margin-bottom: 1rem;">⚠️ Issues Found (${issuesList.length})</h4>
                    <ul style="margin: 0; padding-left: 1.5rem; color: var(--text-primary); line-height: 1.8;">
                        ${issuesList.map(issue => `<li style="margin-bottom: 0.5rem;">${escapeHtml(issue)}</li>`).join('')}
                    </ul>
                </div>
                ` : `
                <div style="margin-bottom: 2rem; padding: 1rem; background-color: rgba(46, 204, 113, 0.1); border-left: 3px solid var(--success-color); border-radius: 4px;">
                    <p style="margin: 0; color: var(--text-primary);">
                        <strong>No Issues Found</strong> - This video has no compatibility issues.
                    </p>
                </div>
                `}
                
                ${recommendationsList.length > 0 ? `
                <div style="margin-bottom: 2rem;">
                    <h4 style="color: var(--accent-color); margin-bottom: 1rem;">💡 Recommendations (${recommendationsList.length})</h4>
                    <ul style="margin: 0; padding-left: 1.5rem; color: var(--text-primary); line-height: 1.8;">
                        ${recommendationsList.map(rec => `<li style="margin-bottom: 0.5rem;">${escapeHtml(rec)}</li>`).join('')}
                    </ul>
                </div>
                ` : ''}
                
                <div style="margin-top: 2rem; padding: 1rem; background-color: var(--bg-secondary); border-radius: 4px; font-size: 0.875rem; color: var(--text-secondary);">
                    <p style="margin: 0 0 0.5rem 0;"><strong>How the rating is calculated:</strong></p>
                    <p style="margin: 0;">
                        Videos start with a perfect score of 100. Points are deducted for unsupported codecs, containers, 
                        subtitle formats, bit depths, and other compatibility factors. The final rating (0-100) determines 
                        the overall score: <strong>Optimal</strong> (≥80), <strong>Good</strong> (≥60), or <strong>Poor</strong> (&lt;60).
                    </p>
                </div>
            </div>
        `;
    } catch (error) {
        console.error('Error loading rating details:', error);
        content.innerHTML = `<div class="error-state"><p>Error loading rating details: ${escapeHtml(error.message)}</p></div>`;
    }
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
window.closeTrackDetailsModal = closeTrackDetailsModal;
window.closeRatingDetailsModal = closeRatingDetailsModal;
window.showRatingDetails = showRatingDetails;
window.rescanVideo = rescanVideo;
window.showTrackDetails = showTrackDetails;

