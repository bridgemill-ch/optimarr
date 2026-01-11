# Changelog
## Optimarr - Media Optimization Platform

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.2.0] - 2026-01-11

### Changed - Major Architecture Update
- **Rating System Overhaul**: Completely redesigned compatibility rating system from client-based (0-11 scale) to property-based (0-100 scale)
  - Videos are now rated based on their media properties (codecs, containers, bit depth, HDR, etc.) rather than Jellyfin client compatibility
  - Rating starts at 100 and deducts points for unsupported properties based on configurable weights
  - Users can configure which media properties are "supported" or "unsupported" in settings
  - Rating impact weights are configurable for each property type (video codec, audio codec, container, etc.)
  - Overall score thresholds (Optimal, Good, Poor) are now configurable in settings (defaults: Optimal ≥80, Good ≥60, Poor <60)

### Removed
- **Jellyfin Client Compatibility System**: Removed all client-based compatibility features
  - Deleted `JellyfinCompatibilityData.cs` service containing hardcoded client compatibility matrix
  - Removed client compatibility modal and UI components
  - Removed "Jellyfin Clients" section from settings
  - Removed "Client Compatibility Overrides" section from settings
  - Removed "Understanding Compatibility Ratings" information banner from dashboard
  - Removed "Client Compatibility Overview" from dashboard
  - Removed `GetClientCompatibilityStats()` API endpoint
  - Removed client-related statistics from dashboard (avgDirectPlayClients, videosWithTranscoding)
  - Removed old client-based fallback code from `VideoAnalyzerService`
  - Removed unused helper methods: `GetEnabledClients()`, `LoadCodecThresholds()`, `GetCompatibilityOverrides()`, etc.

### Added
- **Media Property Settings**: New settings section for configuring supported media properties
  - Configure supported/unsupported status for video codecs, audio codecs, containers, subtitle formats, and bit depths
  - Default settings are provided for common media properties
  - Settings can be initialized with defaults if empty

- **Rating Impact Weights**: Configurable weights for how different properties affect the rating
  - Stereo Sound impact weight (penalizes when all audio tracks are stereo, ≤2 channels)
  - SDR Content impact weight (penalizes when video is SDR, no HDR)
  - High Bitrate impact weight
  - Incorrect Codec Tag impact weight
  - Unsupported Video Codec impact weight
  - Unsupported Audio Codec impact weight
  - Unsupported Container impact weight
  - Unsupported Subtitle Format impact weight
  - Unsupported Bit Depth impact weight
  - High Bitrate Threshold (Mbps)

- **Configurable Rating Thresholds**: Users can configure Optimal/Good/Poor thresholds
  - Optimal Threshold (default: 80) - minimum rating for "Optimal" classification
  - Good Threshold (default: 60) - minimum rating for "Good" classification
  - Ratings below Good threshold are classified as "Poor"

- **Rating Details Modal**: Click on rating in media info to see detailed breakdown
  - Shows starting rating (100), deductions, and final rating
  - Displays all issues and recommendations
  - Explains how the rating system works

### Changed
- **Database Migration**: Automatic migration of old 0-11 ratings to new 0-100 scale
  - Old ratings are detected and recalculated using the new property-based system
  - Old client-based fields (DirectPlayClients, RemuxClients, TranscodeClients, ClientResults) are cleared during migration
  - Migration runs automatically on application startup

- **API Changes**:
  - `GetMediaPropertySettings()` now returns configurable thresholds (Optimal, Good)
  - `SaveMediaPropertySettings()` now accepts thresholds for saving
  - Removed `GetClientCompatibilityStats()` endpoint
  - Dashboard endpoints no longer return client-related statistics
  - Dashboard now uses configurable thresholds for compatibility distribution

- **UI Changes**:
  - Rating display changed from "X/11" to "X/100" throughout the application
  - Dashboard issues now show compatibility rating instead of transcode client count
  - Removed client-related summary items from dashboard

### Technical Details
- **New Service**: `MediaPropertyRatingService` handles all rating calculations
- **Models**: `MediaPropertySettings`, `RatingWeights`, `RatingThresholds` (configurable)
- **Migration**: `DatabaseMigrationService` includes automatic rating migration logic
- **Backward Compatibility**: Old ratings are automatically migrated to new system
- **Rating Logic**: Stereo Sound and SDR Content now penalize (inverted from previous logic)

### Database Impact
- **Migration Required**: Yes - Ratings are automatically migrated from 0-11 to 0-100 scale
- **Backward Compatible**: Yes - Old data is preserved and migrated automatically
- **Deprecated Fields**: DirectPlayClients, RemuxClients, TranscodeClients, ClientResults (kept for backward compatibility but cleared)

---

## [1.1.4] - 2025-01-09

### Fixed
- **ProcessingStatus Migration**: Fixed database error "no such column: v.ProcessingStartedAt" by adding automatic migration detection and execution for ProcessingStatus fields
  - Added `CheckIfProcessingStatusMigrationNeededAsync()` method to detect missing `ProcessingStatus` and `ProcessingStartedAt` columns
  - Added `ApplyProcessingStatusMigrationAsync()` method to execute `AddProcessingStatusFields.sql` migration
  - Migration service now checks for ProcessingStatus migration after Servarr migration
  - Refactored common SQL execution logic into `ExecuteSqlMigrationAsync()` for code reuse
  - Added embedded SQL fallback for ProcessingStatus migration
  - Migration executes automatically on startup when columns are missing

#### Technical Details
- Updated `DatabaseMigrationService` to support multiple sequential migrations
- ProcessingStatus migration is detected and applied automatically if columns are missing
- Migration verification ensures columns are created successfully
- Both Servarr and ProcessingStatus migrations can run in sequence if needed

#### Database Impact
- **Migration Required**: Yes - ProcessingStatus migration will run automatically on first startup after update if columns are missing
- **Backward Compatible**: Yes - Existing databases will be migrated automatically
- **Migration Script**: `Data/Migrations/AddProcessingStatusFields.sql`

---

## [1.1.3] - 2025-01-09

### Fixed
- **Database Migration System**: Fixed database migration not working correctly after updates
  - Added automatic SQL migration detection and execution for Servarr fields
  - Migration service now checks if `ServarrType` column exists before applying migration
  - Improved path resolution for migration SQL file (searches multiple locations)
  - Added proper transaction handling with rollback on errors
  - Migration now executes automatically on startup when needed

- **Migration Warning Popup**: Fixed migration status banner display issues
  - Banner no longer shows for "unknown" status (when migration service hasn't started)
  - Error states now persist and don't auto-hide (users can see error messages)
  - Error details are now displayed in banner details section
  - Migration names are shown as comma-separated list instead of just counts
  - Improved error handling and status reporting

#### Technical Details
- Updated `DatabaseMigrationService` to detect and execute SQL migrations
- Added `CheckIfSqlMigrationNeededAsync()` method to check for missing columns
- Added `ApplySqlMigrationAsync()` method to execute SQL migration scripts
- Enhanced `migration.js` to handle all migration states correctly
- Error property now properly set in `MigrationProgress` class

#### Database Impact
- **Migration Required**: Yes - SQL migration will run automatically on first startup after update
- **Backward Compatible**: Yes - Existing databases will be migrated automatically

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
| 1.1.3 | 2025-01-09 | - | Database migration fixes and warning popup improvements |
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
