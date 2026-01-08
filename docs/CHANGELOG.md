# Changelog
## Optimarr - Media Optimization Platform

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.1.2] - 2025-01-XX

### Added
- **Basic Authentication Support for Sonarr/Radarr**: Added optional Basic Auth support to eliminate authentication warnings in Sonarr/Radarr logs
  - Optional `BasicAuthUsername` and `BasicAuthPassword` configuration fields
  - Automatically sends Basic Auth header when credentials are provided
  - Backward compatible - works with API key only if Basic Auth not configured
  - Fixes "Basic was not authenticated" log messages from Sonarr/Radarr

- **Progress Bar for Video Matching**: Added real-time progress tracking for rematching videos with Sonarr/Radarr
  - New `VideoMatchingProgressService` for tracking matching progress
  - Background task execution for video matching operations
  - Real-time progress updates showing processed/total count and percentage
  - Current file being processed displayed in tooltip
  - Progress polling every 500ms with status updates
  - Works for both Sonarr and Radarr matching operations

#### Technical Details
- New service: `VideoMatchingProgressService` (singleton) for in-memory progress tracking
- Updated `VideoServarrMatcherService` to support progress reporting
- New API endpoint: `GET /api/servarr/match-videos/progress/{matchId}` for polling progress
- Modified `POST /api/servarr/match-videos` to return `matchId` and run matching in background
- Frontend polling mechanism similar to library scan progress tracking
- Progress updates every 10 videos processed

#### UI Changes
- Status span next to "Match Videos" button shows real-time progress
- Progress format: "🔄 X/Y (Z%)" with current file in tooltip
- Button state management during matching operation
- Completion/error messages with detailed statistics

---
## [1.1.1] - 2025-01-XX

### Fixed
- **UI Bug Fix:** Fixed checkbox overlapping tags in media card header
  - Reduced checkbox z-index to prevent overlap with rating badges and tags
  - Added left padding to media-card-header to accommodate checkbox
  - Tags (SDR, Surround, 1080p, etc.) now display properly without being covered

---

## [1.1.0] - 2025-01-XX

### Added - Story: Servarr Video Matching Integration
**Story ID:** STORY-001  
**Developer:** AI Assistant  
**Date:** 2025-01-XX

#### Features
- **Servarr Video Matching Service**: Created `VideoServarrMatcherService` to automatically match video files with Sonarr series and Radarr movies based on file paths
- **Database Schema Enhancement**: Added Servarr integration fields to `VideoAnalysis` model:
  - `ServarrType` (Sonarr/Radarr indicator)
  - Sonarr fields: `SonarrSeriesId`, `SonarrSeriesTitle`, `SonarrEpisodeId`, `SonarrEpisodeNumber`, `SonarrSeasonNumber`
  - Radarr fields: `RadarrMovieId`, `RadarrMovieTitle`, `RadarrYear`
  - `ServarrMatchedAt` timestamp
- **UI Enhancements**:
  - Added Servarr icons (📺 Sonarr, 🎬 Radarr) in browse grid view media cards
  - Added Servarr information section in media info modal showing series/movie details
  - Added "Match Videos" buttons in Settings for manual matching
  - Automatic video matching after library sync operations
- **API Endpoints**:
  - `POST /api/servarr/match-videos` - Match all videos with Servarr data
  - `POST /api/servarr/match-videos/{libraryPathId}` - Match videos for specific library path
- **Database Migration**: Created SQL migration script `Data/Migrations/AddServarrFields.sql` for existing databases

#### Technical Details
- Service registered in `Program.cs` as scoped service
- Path normalization and matching logic implemented
- Year extraction from file paths for Radarr movies
- Automatic matching triggered after successful Sonarr/Radarr library syncs

#### Database Impact
- **Breaking Change**: Yes - New fields added to `VideoAnalyses` table
- **Migration Required**: Yes - See `Data/Migrations/AddServarrFields.sql`
- **Backward Compatible**: Yes - New fields are nullable, existing data unaffected

#### Documentation Updates
- Updated README.md with migration instructions
- B-MAD documentation structure established (PRD, ARCHITECTURE, DEVELOPER_GUIDE)

---

## [1.0.0] - 2025-01-XX

### Initial Release
**Story ID:** STORY-000  
**Developer:** Initial Development  
**Date:** 2025-01-XX

#### Features
- Video file analysis with MediaInfo CLI
- Jellyfin compatibility scoring (0-11 rating scale)
- Library scanning and management
- Sonarr/Radarr integration for library path syncing
- Jellyfin playback history synchronization
- Web-based UI with Servarr-style dark theme
- RESTful API
- Docker support

#### Technical Stack
- ASP.NET Core 8.0
- SQLite database
- Entity Framework Core
- Vanilla JavaScript frontend

---

## Version History

| Version | Date | Story ID | Description |
|---------|------|----------|-------------|
| 1.1.2 | 2025-01-XX | STORY-002 | Progress bar for video matching with Sonarr/Radarr |
| 1.1.1 | 2025-01-XX | - | UI Bug Fix: Checkbox overlap with tags |
| 1.1.0 | 2025-01-XX | STORY-001 | Servarr Video Matching Integration |
| 1.0.0 | 2025-01-XX | STORY-000 | Initial Release |

---

## Story Tracking

### Completed Stories

**STORY-002: Progress Bar for Video Matching**
- Status: ✅ Completed
- Started: 2025-01-XX
- Completed: 2025-01-XX
- Developer: AI Assistant
- Description: Added real-time progress tracking for video matching operations with Sonarr/Radarr, showing processed/total count, percentage, and current file being processed
- Database Changes: No database changes
- Migration Script: None required
- UI Changes: Progress display in status span, real-time updates, completion messages
- API Changes: Modified `/api/servarr/match-videos` to return matchId, added `/api/servarr/match-videos/progress/{matchId}` endpoint
- New Services: `VideoMatchingProgressService` for progress tracking

**STORY-001: Servarr Video Matching Integration**
- Status: ✅ Completed
- Started: 2025-01-XX
- Completed: 2025-01-XX
- Developer: AI Assistant
- Description: Integrate Sonarr/Radarr metadata with video analysis results, display icons in UI, and combine all information sources (MediaInfo, Sonarr, Radarr, Jellyfin) in media info modal
- Database Changes: Added 10 new nullable fields to VideoAnalyses table
- Migration Script: `Data/Migrations/AddServarrFields.sql`
- UI Changes: Browse grid icons, media info modal sections, settings buttons
- API Changes: 2 new endpoints for video matching

**STORY-000: Initial Release**
- Status: ✅ Completed
- Started: 2025-01-XX
- Completed: 2025-01-XX
- Developer: Initial Development
- Description: Core application functionality and basic integrations

### Pending Stories

_No pending stories at this time_

---

## Migration Guide

### Upgrading from 1.0.0 to 1.1.0

1. **Backup your database** (`data/optimarr.db`)
2. **Run the migration script**:
   ```bash
   sqlite3 data/optimarr.db < Data/Migrations/AddServarrFields.sql
   ```
   Or manually execute the SQL commands from `Data/Migrations/AddServarrFields.sql`
3. **Restart the application**
4. **Match existing videos**:
   - Go to Settings → Sonarr/Radarr
   - Click "Match Videos" button to match existing videos with Servarr data
   - Or videos will be automatically matched after syncing libraries

### Database Schema Changes

**Table: VideoAnalyses**

New columns (all nullable):
- `ServarrType` TEXT
- `SonarrSeriesId` INTEGER
- `SonarrSeriesTitle` TEXT
- `SonarrEpisodeId` INTEGER
- `SonarrEpisodeNumber` INTEGER
- `SonarrSeasonNumber` INTEGER
- `RadarrMovieId` INTEGER
- `RadarrMovieTitle` TEXT
- `RadarrYear` INTEGER
- `ServarrMatchedAt` TEXT

---

## Notes

- Version numbers follow Semantic Versioning (MAJOR.MINOR.PATCH)
- Stories are tracked with unique IDs (STORY-XXX)
- All database changes require migration scripts
- UI version display is automatically updated from version file
