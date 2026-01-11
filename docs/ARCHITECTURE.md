# System Architecture Document
## Optimarr - Media Optimization Platform

**Version:** 1.2.0  
**Last Updated:** 2026-01-11  
**Status:** Active Development  
**Application Version:** See [CHANGELOG.md](CHANGELOG.md)

---

## 1. Architecture Overview

Optimarr follows a **layered architecture** pattern with clear separation between presentation, business logic, and data access layers. The application is built as a **Single Page Application (SPA)** with a RESTful API backend.

### 1.1 High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Client Browser                            │
│  ┌──────────────────────────────────────────────────────┐   │
│  │         Single Page Application (SPA)                │   │
│  │  - Vanilla JavaScript Modules                        │   │
│  │  - HTML5/CSS3 UI                                     │   │
│  │  - REST API Client                                   │   │
│  └──────────────────────────────────────────────────────┘   │
└───────────────────────┬─────────────────────────────────────┘
                        │ HTTP/REST
┌───────────────────────▼─────────────────────────────────────┐
│              ASP.NET Core 8.0 Web Application                │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Controllers Layer                        │   │
│  │  - AnalysisController                                │   │
│  │  - LibraryController                                 │   │
│  │  - PlaybackController                                │   │
│  │  - ServarrController                                 │   │
│  │  - SystemController                                  │   │
│  └───────────────────────┬───────────────────────────────┘   │
│                          │                                    │
│  ┌───────────────────────▼───────────────────────────────┐   │
│  │              Services Layer                           │   │
│  │  - VideoAnalyzerService                              │   │
│  │  - MediaPropertyRatingService                        │   │
│  │  - LibraryScannerService                             │   │
│  │  - SonarrService / RadarrService                     │   │
│  │  - ServarrSyncService                                │   │
│  │  - VideoServarrMatcherService                        │   │
│  │  - VideoMatchingProgressService                      │   │
│  │  - JellyfinService                                   │   │
│  │  - PlaybackSyncService (Background)                   │   │
│  └───────────────────────┬───────────────────────────────┘   │
│                          │                                    │
│  ┌───────────────────────▼───────────────────────────────┐   │
│  │              Data Access Layer                        │   │
│  │  - AppDbContext (Entity Framework Core)               │   │
│  │  - SQLite Database                                    │   │
│  └───────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
                        │
        ┌───────────────┼───────────────┐
        │               │               │
┌───────▼──────┐ ┌──────▼──────┐ ┌─────▼──────┐
│   Sonarr     │ │   Radarr    │ │  Jellyfin  │
│     API      │ │     API     │ │    API     │
└──────────────┘ └─────────────┘ └────────────┘
        │               │               │
┌───────▼──────────────────────────────────────┐
│         MediaInfo CLI                        │
│    (Video Metadata Extraction)               │
└─────────────────────────────────────────────┘
```

---

## 2. Component Architecture

### 2.1 Presentation Layer

#### 2.1.1 Frontend Structure
```
wwwroot/
├── index.html              # Main HTML entry point
├── styles.css              # Global styles (Servarr theme)
├── logo.svg                # Application logo
└── js/
    ├── app.js              # Main application entry point
    ├── navigation.js       # Tab navigation logic
    ├── dashboard.js        # Dashboard statistics
    ├── browse.js           # Video browsing and filtering
    ├── library.js          # Library management
    ├── library-modals.js   # Library modal dialogs
    ├── playback.js         # Playback history view
    ├── servarr.js          # Servarr integration UI
    ├── settings.js         # Settings management
    ├── media-info.js       # Media information display
    ├── path-browser.js     # Path selection UI
    ├── migration.js        # Data migration utilities
    └── utils.js            # Shared utilities
```

**Design Patterns:**
- **Module Pattern:** Each JavaScript file is a self-contained module
- **Event-Driven:** UI updates via event listeners
- **RESTful Communication:** All backend communication via `fetch` API

#### 2.1.2 UI Components
- **Tab Navigation:** Dashboard, Browse, Library, Playback, Settings
- **Modal Dialogs:** Library management, file selection, settings
- **Data Tables:** Sortable, filterable video lists
- **Progress Indicators:** Background operation status
- **Responsive Design:** Mobile-friendly layouts

### 2.2 Application Layer (Controllers)

#### 2.2.1 Controller Responsibilities

**AnalysisController**
- Handles video file analysis requests
- Supports file upload and path-based analysis
- Returns compatibility reports

**LibraryController**
- Manages library paths and scans
- Provides scan status and progress
- Handles library path CRUD operations

**PlaybackController**
- Manages Jellyfin playback history
- Provides playback statistics

**ServarrController**
- Manages Sonarr/Radarr integration
- Handles settings and connection testing
- Provides library sync endpoints
- Manages path mappings
- Video matching with progress tracking (v1.1.2)
- **Key Endpoints:**
  - `POST /api/servarr/match-videos` - Start video matching (returns matchId, runs in background)
  - `GET /api/servarr/match-videos/progress/{matchId}` - Get matching progress (v1.1.2)
  - `POST /api/servarr/match-videos/{libraryPathId}` - Match videos for specific library path
  - `POST /api/servarr/sonarr/sync` - Sync Sonarr library
  - `POST /api/servarr/radarr/sync` - Sync Radarr library
  - `POST /api/servarr/sync-all` - Sync all Servarr libraries

**SystemController**
- Provides system information
- Health check endpoints
- Configuration endpoints

### 2.3 Business Logic Layer (Services)

#### 2.3.1 Core Services

**VideoAnalyzerService**
- **Purpose:** Analyze video files for compatibility
- **Dependencies:** MediaInfo CLI, MediaPropertyRatingService, IConfiguration
- **Key Methods:**
  - `AnalyzeVideoAsync()`: Main analysis method
  - `RecalculateCompatibility()`: Recalculate compatibility using new rating system
- **Responsibilities:**
  - Extract video metadata using MediaInfo
  - Delegate rating calculation to MediaPropertyRatingService
  - Generate detailed compatibility reports with issues and recommendations

**MediaPropertyRatingService**
- **Purpose:** Calculate compatibility ratings based on media properties
- **Dependencies:** IConfiguration, ILogger
- **Key Methods:**
  - `CalculateRating()`: Calculate 0-100 rating based on media properties
  - `LoadMediaPropertySettings()`: Load supported/unsupported property settings
  - `LoadRatingWeights()`: Load configurable impact weights
  - `LoadRatingThresholds()`: Load configurable rating thresholds (Optimal, Good)
- **Responsibilities:**
  - Start with perfect score (100) and deduct points for unsupported properties
  - Apply configurable weights for different property types
  - Penalize stereo sound (≤2 channels) and SDR content (no HDR)
  - Generate issues and recommendations based on property analysis

**VideoServarrMatcherService** (v1.1.0, updated v1.1.2)
- **Purpose:** Match video files with Sonarr/Radarr metadata
- **Dependencies:** AppDbContext, SonarrService, RadarrService, IConfiguration
- **Key Methods:**
  - `MatchVideoWithServarrAsync()`: Match single video
  - `MatchAllVideosAsync()`: Match all videos in database (overload with progress support)
  - `MatchAllVideosAsync(progressService, matchId)`: Match all videos with progress tracking
  - `MatchVideosForLibraryPathAsync()`: Match videos for specific library
- **Responsibilities:**
  - Match video file paths with Sonarr episodes and Radarr movies
  - Extract and store Servarr metadata (series/movie titles, IDs, etc.)
  - Handle path normalization and year extraction
  - Update VideoAnalysis records with Servarr information
  - Report progress updates during matching operations (v1.1.2)

**VideoMatchingProgressService** (v1.1.2)
- **Purpose:** Track progress of video matching operations
- **Dependencies:** ILogger
- **Lifetime:** Singleton
- **Key Methods:**
  - `CreateProgress(matchId)`: Create new progress tracker
  - `GetProgress(matchId)`: Get current progress status
  - `UpdateProgress(matchId, processed, total, matched, errors, currentItem)`: Update progress
  - `CompleteProgress(matchId, matched, errors)`: Mark progress as completed
  - `FailProgress(matchId, errorMessage)`: Mark progress as failed
  - `RemoveProgress(matchId)`: Remove progress tracker
  - `CleanupOldProgress()`: Remove old completed progress records
- **Responsibilities:**
  - In-memory progress tracking using ConcurrentDictionary
  - Store matching progress (processed/total, matched count, errors)
  - Track current file being processed
  - Auto-cleanup of progress records older than 1 hour

**LibraryScannerService**
- **Purpose:** Scan library paths for video files
- **Dependencies:** AppDbContext, VideoAnalyzerService
- **Key Methods:**
  - `StartScanAsync()`: Initiate library scan
  - `GetScanStatusAsync()`: Get scan progress
  - `CancelScanAsync()`: Cancel running scan
- **Responsibilities:**
  - Recursively scan directories for video files
  - Queue files for analysis
  - Track scan progress and errors
  - Persist scan state

**ServarrSyncService**
- **Purpose:** Sync library paths from Sonarr/Radarr
- **Dependencies:** AppDbContext, SonarrService, RadarrService, IConfiguration
- **Key Methods:**
  - `SyncSonarrAsync()`: Sync Sonarr root folders
  - `SyncRadarrAsync()`: Sync Radarr root folders
  - `MapPath()`: Translate paths using mappings
- **Responsibilities:**
  - Fetch root folders from Servarr APIs
  - Apply path mappings for Docker environments
  - Update library paths in database
  - Handle duplicate path detection

**SonarrService / RadarrService**
- **Purpose:** Communicate with Servarr APIs
- **Dependencies:** HttpClient, IConfiguration, ILogger
- **Key Methods:**
  - `GetRootFoldersAsync()`: Fetch root folders
  - `GetSeriesAsync()` / `GetMoviesAsync()`: Fetch content
  - `TestConnectionAsync()`: Verify API connectivity
- **Responsibilities:**
  - HTTP communication with Servarr APIs
  - API authentication
  - Error handling and retry logic

**JellyfinService**
- **Purpose:** Communicate with Jellyfin API
- **Dependencies:** HttpClient, IConfiguration, ILogger
- **Key Methods:**
  - `GetPlaybackHistoryAsync()`: Fetch playback records
  - `TestConnectionAsync()`: Verify API connectivity
- **Responsibilities:**
  - HTTP communication with Jellyfin API
  - Playback history synchronization

#### 2.3.2 Background Services

**PlaybackSyncService** (IHostedService)
- **Purpose:** Periodically sync playback history from Jellyfin
- **Schedule:** Configurable interval (default: hourly)
- **Responsibilities:**
  - Fetch new playback records
  - Match with local library files
  - Update database

**DatabaseMigrationService** (IHostedService)
- **Purpose:** Ensure database schema is up-to-date
- **Schedule:** Runs on application startup
- **Responsibilities:**
  - Apply Entity Framework migrations
  - Create database if it doesn't exist
  - Verify schema integrity

### 2.4 Data Access Layer

#### 2.4.1 Database Schema

**AppDbContext** (Entity Framework Core)
- **Database:** SQLite (`data/optimarr.db`)
- **ORM:** Entity Framework Core 8.0

**Entities:**

**LibraryScan**
- Tracks library scan operations
- Fields: Id, Name, Status, StartedAt, CompletedAt, TotalFiles, ProcessedFiles, FailedFiles

**VideoAnalysis**
- Stores analysis results for video files
- Fields: Id, FilePath, LibraryScanId, AnalyzedAt, OverallScore, CompatibilityResult (JSON)
- **Servarr Integration Fields (v1.1.0):** ServarrType, SonarrSeriesId, SonarrSeriesTitle, SonarrEpisodeId, SonarrEpisodeNumber, SonarrSeasonNumber, RadarrMovieId, RadarrMovieTitle, RadarrYear, ServarrMatchedAt
- Relationships: Many-to-One with LibraryScan

**LibraryPath**
- Manages library path configurations
- Fields: Id, Path, Name, Category, ServarrType, ServarrRootFolderId, LastSyncedAt
- Constraints: UNIQUE on Path column

**PlaybackHistory**
- Stores Jellyfin playback records
- Fields: Id, ItemId, FilePath, ClientName, PlayedAt, PlayMethod, Duration

**FailedFile**
- Tracks files that failed analysis
- Fields: Id, FilePath, LibraryScanId, ErrorMessage, RetryCount

#### 2.4.2 Data Flow

```
User Action → Controller → Service → Database
                ↓
            External API
                ↓
            MediaInfo CLI
```

---

## 3. Integration Architecture

### 3.1 External API Integration

#### 3.1.1 Sonarr API
- **Base URL:** Configurable via `appsettings.json`
- **Authentication:** API Key in header
- **Endpoints Used:**
  - `GET /api/v3/rootfolder` - Root folders
  - `GET /api/v3/series` - TV series
  - `GET /api/v3/episode` - Episodes
  - `GET /api/v3/system/status` - Connection test

#### 3.1.2 Radarr API
- **Base URL:** Configurable via `appsettings.json`
- **Authentication:** API Key in header
- **Endpoints Used:**
  - `GET /api/v3/rootfolder` - Root folders
  - `GET /api/v3/movie` - Movies
  - `GET /api/v3/system/status` - Connection test

#### 3.1.3 Jellyfin API
- **Base URL:** Configurable via `appsettings.json`
- **Authentication:** API Key in header
- **Endpoints Used:**
  - `GET /Sessions` - Active sessions
  - `GET /Items/{ItemId}/PlaybackInfo` - Playback information
  - `GET /Users/{UserId}/Items/{ItemId}/PlaybackInfo` - User playback info

### 3.2 MediaInfo Integration

**MediaInfo CLI** is used for video metadata extraction:
- **Command:** `mediainfo --Output=XML <file>`
- **Output:** XML format with complete media information
- **Parsing:** System.Xml.Linq (XDocument)
- **Error Handling:** Graceful degradation if MediaInfo unavailable

### 3.3 Path Mapping System

**Purpose:** Translate file paths between different environments (e.g., Docker containers)

**Configuration Structure:**
```json
{
  "Servarr": {
    "Sonarr": {
      "PathMappings": [
        { "From": "/tv", "To": "/mnt/media/tv" }
      ]
    },
    "Radarr": {
      "PathMappings": [
        { "From": "/movies", "To": "/mnt/media/movies" }
      ]
    }
  }
}
```

**Mapping Logic:**
1. Check if path starts with "From" value
2. Replace "From" with "To" value
3. Apply to all paths from Servarr APIs

---

## 4. Configuration Architecture

### 4.1 Configuration Sources

**Priority Order:**
1. `config/appsettings.json` (preferred in Docker)
2. `appsettings.json` (root, for development)
3. Environment variables
4. Default values

### 4.2 Configuration Structure

```json
{
  "Servarr": {
    "Sonarr": {
      "BaseUrl": "http://sonarr:8989",
      "ApiKey": "...",
      "Enabled": true,
      "PathMappings": [...]
    },
    "Radarr": {
      "BaseUrl": "http://radarr:7878",
      "ApiKey": "...",
      "Enabled": true,
      "PathMappings": [...]
    }
  },
  "MediaPropertySettings": {
    "VideoCodecs": {
      "H.264": true,
      "H.265": true,
      "AV1": false
    },
    "AudioCodecs": {
      "AAC": true,
      "MP3": true,
      "AC3": false
    },
    "Containers": {
      "MP4": true,
      "MKV": true
    }
  },
  "RatingWeights": {
    "UnsupportedVideoCodec": 35,
    "UnsupportedAudioCodec": 25,
    "UnsupportedContainer": 30,
    "UnsupportedBitDepth": 18,
    "UnsupportedSubtitleFormat": 8,
    "HDR": 8,
    "SurroundSound": 3,
    "HighBitrate": 5,
    "IncorrectCodecTag": 12,
    "HighBitrateThresholdMbps": 40.0
  },
  "Jellyfin": {
    "BaseUrl": "http://jellyfin:8096",
    "ApiKey": "...",
    "Enabled": true
  }
}
```

### 4.3 Configuration Management

- **Reading:** Via `IConfiguration` dependency injection
- **Writing:** Direct JSON manipulation for settings UI
- **Reloading:** `IConfigurationRoot.Reload()` after updates
- **Validation:** Client-side and server-side validation

---

## 5. Security Architecture

### 5.1 Authentication & Authorization

**Current State:** No authentication (single-user application)

**Future Considerations:**
- API key authentication
- JWT tokens
- Role-based access control

### 5.2 Data Security

- **API Keys:** Stored in `appsettings.json` (should be encrypted in production)
- **File Paths:** Sanitized to prevent directory traversal
- **Input Validation:** All user inputs validated
- **SQL Injection:** Prevented by Entity Framework parameterized queries

### 5.3 Network Security

- **CORS:** Currently allows all origins (development)
- **HTTPS:** Recommended for production
- **API Rate Limiting:** Not implemented (future feature)

---

## 6. Deployment Architecture

### 6.1 Docker Deployment

**Container Structure:**
```
optimarr:latest
├── ASP.NET Core Runtime
├── MediaInfo CLI
├── Application Files
└── Volume Mounts:
    ├── /app/config (read-only)
    ├── /app/data (read-write)
    ├── /app/logs (read-write)
    └── /videos (read-only, optional)
```

**Docker Compose:**
- Service definition with volume mounts
- Network configuration for API access
- Health checks (future)

### 6.2 Development Environment

- **Hot Reload:** ASP.NET Core watch mode
- **Swagger UI:** Available in development
- **Logging:** Console and file output

---

## 7. Performance Architecture

### 7.1 Optimization Strategies

**Database:**
- Indexed columns: FilePath, LibraryScanId, AnalyzedAt, OverallScore
- Efficient queries with LINQ
- Connection pooling via EF Core

**Background Processing:**
- Asynchronous operations throughout
- Background services for long-running tasks
- Progress tracking for user feedback

**Caching:**
- Codec thresholds cached in memory
- Compatibility data loaded once at startup
- No external cache (future: Redis)

### 7.2 Scalability Considerations

**Current Limitations:**
- Single-instance deployment
- SQLite database (not ideal for high concurrency)

**Future Scalability:**
- PostgreSQL for multi-instance support
- Message queue for analysis jobs
- Distributed caching (Redis)

---

## 8. Error Handling & Logging

### 8.1 Logging Strategy

**Framework:** Serilog

**Log Levels:**
- **Information:** Normal operations, startup, shutdown
- **Warning:** Recoverable errors, missing files
- **Error:** Exceptions, API failures
- **Fatal:** Application startup failures

**Log Outputs:**
- Console (development)
- File (`logs/optimarr-YYYY-MM-DD.log`)
- Structured logging with context

### 8.2 Error Handling Patterns

**Service Layer:**
- Try-catch blocks around external API calls
- Retry logic for transient failures
- Graceful degradation

**Controller Layer:**
- HTTP status codes (200, 400, 500)
- Error response objects with messages
- Exception logging

**Frontend:**
- User-friendly error messages
- Retry mechanisms for failed operations
- Progress indicators for long operations

---

## 9. Testing Strategy

### 9.1 Current Testing

- **Manual Testing:** Primary method
- **Integration Testing:** Via Docker Compose
- **No Unit Tests:** (Future improvement)

### 9.2 Recommended Testing

**Unit Tests:**
- Service layer logic
- Path mapping functionality
- Compatibility calculations

**Integration Tests:**
- API endpoints
- Database operations
- External API mocking

**End-to-End Tests:**
- Full user workflows
- Library scanning
- Servarr synchronization

---

## 10. Future Architecture Considerations

### 10.1 Planned Enhancements

1. **Multi-User Support:**
   - Authentication system
   - User roles and permissions
   - Per-user library views

2. **Advanced Analytics:**
   - Historical trend analysis
   - Optimization recommendations
   - Cost-benefit calculations

3. **Automation:**
   - Scheduled scans
   - Automated re-downloads
   - Notification system

4. **Performance:**
   - Distributed processing
   - Job queue system
   - Caching layer

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.2.0 | 2026-01-11 | AI Assistant | Updated for v1.2.0: Added MediaPropertyRatingService, removed JellyfinCompatibilityData, updated rating system documentation |
| 1.0 | 2025-01-XX | AI Assistant | Initial architecture document following B-MAD methodology |
