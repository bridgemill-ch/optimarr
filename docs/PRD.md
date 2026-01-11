# Product Requirement Document (PRD)
## Optimarr - Media Optimization Platform

**Version:** 1.2.0  
**Last Updated:** 2026-01-11  
**Status:** Active Development

---

## 1. Executive Summary

Optimarr is a web-based media optimization platform designed to analyze video files and determine their compatibility based on media properties (codecs, containers, bit depth, HDR, etc.). The application integrates with the Servarr ecosystem (Sonarr, Radarr) to provide automated media library management and optimization recommendations.

### 1.1 Product Vision
To help media server administrators optimize their video libraries based on media properties (codecs, containers, bit depth, HDR, etc.), enabling better compatibility and reducing server transcoding load.

### 1.2 Target Users
- **Primary:** Media server administrators running Jellyfin with Sonarr/Radarr
- **Secondary:** Home media enthusiasts managing large video libraries
- **Tertiary:** System administrators optimizing media infrastructure

---

## 2. Problem Statement

### 2.1 Current Pain Points
1. **Transcoding Overhead:** Many video files require server-side transcoding, consuming CPU resources and bandwidth
2. **Compatibility Uncertainty:** Administrators don't know which media properties (codecs, containers, etc.) are compatible with their setup
3. **Manual Analysis:** No automated way to identify optimization opportunities across large libraries
4. **Fragmented Tools:** Separate tools for library management (Sonarr/Radarr) and playback analysis (Jellyfin)
5. **Lack of Historical Data:** No visibility into playback patterns to inform optimization decisions

### 2.2 Market Opportunity
The home media server market is growing, with Jellyfin, Plex, and Emby serving millions of users. Optimization tools that reduce transcoding can significantly improve user experience and reduce infrastructure costs.

---

## 3. Product Goals & Success Metrics

### 3.1 Primary Goals
1. **Compatibility Analysis:** Accurately determine compatibility for video files based on media properties
2. **Library Integration:** Seamlessly sync with Sonarr/Radarr to analyze existing libraries
3. **Actionable Insights:** Provide clear recommendations for media optimization
4. **User Experience:** Deliver a modern, intuitive web interface matching Servarr design patterns
5. **Configurable Rating System:** Allow users to customize which media properties are considered supported

### 3.2 Success Metrics
- **Accuracy:** >95% compatibility prediction accuracy
- **Performance:** Analyze 1000+ files per hour
- **Adoption:** Support libraries with 10,000+ video files
- **Integration:** Successfully sync with Sonarr, Radarr, and Jellyfin APIs

---

## 4. Core Features & Requirements

### 4.1 Video Analysis Engine

#### 4.1.1 Video File Analysis
- **REQ-001:** Support analysis of MP4, MKV, AVI, TS, WebM, OGG containers
- **REQ-002:** Extract video codec information (H.264, H.265, VP9, AV1, MPEG-4)
- **REQ-003:** Extract audio codec information (AAC, MP3, AC3, EAC3, DTS, FLAC, Opus, Vorbis, ALAC)
- **REQ-004:** Analyze video resolution, bitrate, frame rate, and other technical parameters
- **REQ-005:** Support external subtitle file analysis (SRT, VTT, ASS, SSA, SUB, VobSub)

#### 4.1.2 Compatibility Scoring
- **REQ-006:** Calculate compatibility score (0-100) based on media properties
- **REQ-007:** Start with perfect score (100) and deduct points for unsupported properties
- **REQ-008:** Support configurable media property settings (supported/unsupported)
- **REQ-009:** Support configurable rating impact weights for each property type
- **REQ-010:** Support configurable rating thresholds (Optimal, Good, Poor) - defaults: Optimal ≥80, Good ≥60, Poor <60
- **REQ-011:** Provide detailed rating breakdown showing deductions and issues

### 4.2 Library Management

#### 4.2.1 Library Scanning
- **REQ-011:** Scan multiple library paths recursively
- **REQ-012:** Run scans in background with progress tracking
- **REQ-013:** Persist scan state across application restarts
- **REQ-014:** Track scan history and statistics
- **REQ-015:** Handle scan failures gracefully with retry logic

#### 4.2.2 Library Path Management
- **REQ-016:** Support multiple library paths
- **REQ-017:** Sync library paths from Sonarr root folders
- **REQ-018:** Sync library paths from Radarr root folders
- **REQ-019:** Support path mapping between Optimarr and Servarr environments (Docker containers)
- **REQ-020:** Track path metadata (name, category, last synced time)

### 4.3 Servarr Integration

#### 4.3.1 Sonarr Integration
- **REQ-021:** Connect to Sonarr API using base URL and API key
- **REQ-022:** Test connection before saving settings
- **REQ-023:** Sync TV series library paths from Sonarr
- **REQ-024:** Analyze individual series or episodes
- **REQ-025:** Queue series for batch analysis
- **REQ-026:** Support path mappings for Sonarr root folders

#### 4.3.2 Radarr Integration
- **REQ-027:** Connect to Radarr API using base URL and API key
- **REQ-028:** Test connection before saving settings
- **REQ-029:** Sync movie library paths from Radarr
- **REQ-030:** Analyze individual movies
- **REQ-031:** Batch analyze multiple movies
- **REQ-032:** Support path mappings for Radarr root folders

### 4.4 Jellyfin Integration

#### 4.4.1 Playback History Sync
- **REQ-033:** Connect to Jellyfin API using base URL and API key
- **REQ-034:** Sync playback history from Jellyfin
- **REQ-035:** Match playback records with local library files
- **REQ-036:** Track Direct Play vs Transcode patterns
- **REQ-037:** Run playback sync as background service

#### 4.4.2 Media Property Configuration
- **REQ-038:** Allow users to configure which video codecs are supported
- **REQ-039:** Allow users to configure which audio codecs are supported
- **REQ-040:** Allow users to configure which containers are supported
- **REQ-041:** Allow users to configure which subtitle formats are supported
- **REQ-042:** Allow users to configure supported bit depths
- **REQ-043:** Provide default settings for common media properties

### 4.5 User Interface

#### 4.5.1 Web Application
- **REQ-042:** Modern, responsive web interface with Servarr-style dark theme
- **REQ-043:** Single Page Application (SPA) architecture
- **REQ-044:** Support desktop and mobile browsers
- **REQ-045:** Real-time updates for background operations

#### 4.5.2 Dashboard
- **REQ-046:** Display overview statistics (total files, average score, etc.)
- **REQ-047:** Show library scan status and progress
- **REQ-048:** Display recent activity and errors

#### 4.5.3 Browse View
- **REQ-049:** Filter videos by compatibility score
- **REQ-050:** Sort by score, file name, date analyzed
- **REQ-051:** Search videos by file name
- **REQ-052:** Display detailed compatibility information per file
- **REQ-053:** Support bulk selection and operations

#### 4.5.4 Settings
- **REQ-054:** Configure Jellyfin connection settings
- **REQ-055:** Configure Sonarr connection settings
- **REQ-056:** Configure Radarr connection settings
- **REQ-057:** Manage media property settings (supported/unsupported)
- **REQ-058:** Manage rating impact weights
- **REQ-059:** Configure path mappings for Sonarr/Radarr
- **REQ-060:** Test connections before saving
- **REQ-061:** Initialize default media property settings

### 4.6 API & Integration

#### 4.6.1 RESTful API
- **REQ-061:** Provide comprehensive REST API for all operations
- **REQ-062:** Support JSON request/response format
- **REQ-063:** Include Swagger/OpenAPI documentation
- **REQ-064:** Support file upload for analysis

#### 4.6.2 Data Persistence
- **REQ-065:** Use SQLite database for data storage
- **REQ-066:** Support database migrations
- **REQ-067:** Store video analysis results
- **REQ-068:** Store library scan history
- **REQ-069:** Store playback history from Jellyfin

---

## 5. Technical Requirements

### 5.1 Platform Requirements
- **REQ-070:** Built on ASP.NET Core 8.0
- **REQ-071:** Support Docker containerization
- **REQ-072:** Support Linux, Windows, and macOS
- **REQ-073:** Use MediaInfo CLI for video metadata extraction

### 5.2 Performance Requirements
- **REQ-074:** Analyze single video file in <30 seconds
- **REQ-075:** Support concurrent analysis of multiple files
- **REQ-076:** Handle libraries with 10,000+ files
- **REQ-077:** Background operations should not block UI

### 5.3 Reliability Requirements
- **REQ-078:** Graceful error handling for broken media files
- **REQ-079:** Retry logic for external API calls
- **REQ-080:** Comprehensive logging with Serilog
- **REQ-081:** Database transaction safety

### 5.4 Security Requirements
- **REQ-082:** Store API keys securely in configuration
- **REQ-083:** Validate API key format before saving
- **REQ-084:** Sanitize file paths to prevent directory traversal
- **REQ-085:** Support read-only configuration in Docker

---

## 6. Non-Functional Requirements

### 6.1 Usability
- **REQ-086:** Intuitive interface matching Servarr design patterns
- **REQ-087:** Clear error messages and user feedback
- **REQ-088:** Responsive design for mobile devices
- **REQ-089:** Accessible keyboard navigation

### 6.2 Maintainability
- **REQ-090:** Well-documented codebase
- **REQ-091:** Modular service architecture
- **REQ-092:** Comprehensive logging for debugging
- **REQ-093:** Configuration-driven behavior

### 6.3 Scalability
- **REQ-094:** Support horizontal scaling (stateless design)
- **REQ-095:** Efficient database queries with proper indexing
- **REQ-096:** Background job processing for long operations

---

## 7. Out of Scope (Future Considerations)

### 7.1 Planned Features
- Automated quality profile recommendations
- Batch video re-encoding suggestions
- Plex integration for playback history
- Export/Import compatibility reports
- Scheduled library scans
- Email notifications
- Multi-user support with roles
- API authentication and rate limiting

### 7.2 Known Limitations
- No video transcoding capabilities (analysis only)
- No automatic file replacement
- Single-user application (no multi-tenancy)
- Limited to Jellyfin client compatibility (no Plex/Emby)

## 8. Version History

See [CHANGELOG.md](../docs/CHANGELOG.md) for detailed version history and story tracking.

**Current Version:** 1.1.0

**Recent Changes:**
- **v1.1.0 (2025-01-XX)**: Added Servarr video matching integration, UI icons, and combined information display
- **v1.0.0 (2025-01-XX)**: Initial release with core functionality

---

## 8. Dependencies & Integrations

### 8.1 External Services
- **Sonarr:** TV series management
- **Radarr:** Movie management
- **Jellyfin:** Media server and playback history

### 8.2 External Tools
- **MediaInfo CLI:** Video metadata extraction
- **Docker:** Containerization platform

### 8.3 Technology Stack
- **Backend:** ASP.NET Core 8.0, C#
- **Database:** SQLite with Entity Framework Core
- **Frontend:** Vanilla JavaScript, HTML5, CSS3
- **Logging:** Serilog
- **API Documentation:** Swagger/OpenAPI

---

## 9. User Stories

### 9.1 As a Media Server Administrator
- I want to analyze my video library to identify files that need optimization
- I want to see which media properties (codecs, containers, etc.) are compatible with my setup
- I want to sync my library paths from Sonarr/Radarr automatically
- I want to view playback history to understand transcoding patterns

### 9.2 As a Power User
- I want to configure custom compatibility thresholds (Optimal/Good/Poor)
- I want to configure which media properties are supported/unsupported
- I want to customize rating impact weights for different property types
- I want to set up path mappings for Docker environments
- I want to bulk analyze multiple series or movies

### 9.3 As a Developer
- I want to integrate Optimarr via REST API
- I want to upload files for analysis programmatically
- I want to access compatibility data via API
- I want to automate library optimization workflows

---

## 10. Acceptance Criteria

### 10.1 Core Functionality
- ✅ Application starts successfully in Docker
- ✅ Can connect to and test Sonarr/Radarr/Jellyfin APIs
- ✅ Can analyze video files and generate compatibility reports
- ✅ Can sync library paths from Servarr applications
- ✅ Can display analysis results in web UI

### 10.2 Integration
- ✅ Successfully syncs with Sonarr root folders
- ✅ Successfully syncs with Radarr root folders
- ✅ Successfully syncs playback history from Jellyfin
- ✅ Path mappings work correctly in Docker environments

### 10.3 User Experience
- ✅ Settings can be saved and persisted
- ✅ Library scans run in background without blocking UI
- ✅ Error messages are clear and actionable
- ✅ Interface is responsive on mobile devices

---

## 11. Risk Assessment

### 11.1 Technical Risks
- **MediaInfo CLI availability:** Mitigated by clear error messages and documentation
- **Large library performance:** Mitigated by background processing and efficient queries
- **API compatibility:** Mitigated by version checking and graceful degradation

### 11.2 Integration Risks
- **Servarr API changes:** Mitigated by using stable API endpoints
- **Jellyfin compatibility data changes:** Mitigated by maintaining compatibility matrix
- **Path mapping complexity:** Mitigated by clear documentation and validation

---

## 12. Success Criteria

The product will be considered successful when:
1. Users can successfully analyze their video libraries
2. Integration with Sonarr/Radarr/Jellyfin works reliably
3. Compatibility predictions are accurate (>95%)
4. Application performs well with large libraries (10,000+ files)
5. User feedback is positive regarding UI/UX

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2025-01-XX | AI Assistant | Initial PRD creation following B-MAD methodology |
