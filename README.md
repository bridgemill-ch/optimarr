# Optimarr - Media Optimization

A web-based application for analyzing video files to determine their compatibility with media server clients. Built in the Servarr family style with integration support for Sonarr, Radarr, and Jellyfin.

## ⚠️ Disclaimer

**This software is in early development stage. Use it at your own risk. The developers are not responsible for any data loss, system damage, or other issues that may arise from using this software. Always backup your data before installation and use.**

### Support the Project

If you find Optimarr useful, consider supporting the project:

[![Buy Me A Coffee](https://img.shields.io/badge/Buy%20Me%20A%20Coffee-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black)](https://buymeacoffee.com/bridgemill)

[Support on Buy Me a Coffee](https://buymeacoffee.com/bridgemill)

## Features

### Core Functionality
- **Video Analysis**: Analyze video files to determine Direct Play compatibility with media server clients
- **Subtitle Support**: Analyze external subtitle files alongside video files
- **Compatibility Rating**: 0-11 rating scale based on number of clients that can Direct Play
- **Comprehensive Reports**: Detailed compatibility reports with per-client breakdown
- **Library Management**: Scan and manage multiple video libraries
- **Background Scanning**: Library scans run in background and persist across page reloads

### Integrations
- **Sonarr Integration**: Connect with Sonarr for TV series analysis and automated redownloads
- **Radarr Integration**: Connect with Radarr for movie analysis and automated redownloads
- **Jellyfin Integration**: 
  - Sync playback history from Jellyfin
  - Match playback data with local library files
  - View client compatibility statistics
  - Track direct play vs transcode patterns

### User Interface
- **Modern Web UI**: Servarr-style dark theme interface
- **Browse View**: Filter, sort, and search through analyzed videos
- **Playback History**: View Jellyfin playback history with filtering and statistics
- **Dashboard**: Overview statistics and charts
- **Bulk Operations**: Select and redownload multiple videos at once

### API & Technical
- **RESTful API**: Full API support for integration with other tools
- **Docker Support**: Pre-built Docker images available on Docker Hub
- **Auto Configuration**: Automatic configuration initialization on first run

## Installation

**Docker Hub Image:** `bridgemill/optimarr:latest`

### Using Docker Compose (Recommended)

1. Clone or download this repository
2. Edit `docker-compose.yml` to configure volumes for your setup
3. Start the container:
   ```bash
   docker-compose up -d
   ```
4. Open your browser and navigate to `http://localhost:5000`

The container automatically initializes `appsettings.json` in the config folder if it doesn't exist.

### Using Docker Run

Pull and run the pre-built image from Docker Hub:

```bash
docker pull bridgemill/optimarr:latest

docker run -d \
  --name optimarr \
  -p 5000:8080 \
  -v $(pwd)/config:/app/config \
  -v $(pwd)/data:/app/data \
  -v $(pwd)/logs:/app/logs \
  -v $(pwd)/videos:/videos:ro \
  --restart unless-stopped \
  bridgemill/optimarr:latest
```

### Building from Source

1. Build the Docker image:
   ```bash
   docker build -t bridgemill/optimarr:latest .
   ```

2. Run the container using the same `docker run` command above, or use `docker-compose.yml`

## Configuration

Edit `appsettings.json` to configure Servarr integrations:

```json
{
  "Servarr": {
    "Sonarr": {
      "BaseUrl": "http://localhost:8989",
      "ApiKey": "your-sonarr-api-key",
      "Enabled": true,
      "BasicAuthUsername": "optional-username",
      "BasicAuthPassword": "optional-password"
    },
    "Radarr": {
      "BaseUrl": "http://localhost:7878",
      "ApiKey": "your-radarr-api-key",
      "Enabled": true,
      "BasicAuthUsername": "optional-username",
      "BasicAuthPassword": "optional-password"
    }
  }
}
```

**Note:** Basic Auth credentials are optional. Only add them if your Sonarr/Radarr instance requires Basic Authentication. If Basic Auth is not required, you can omit these fields.

### Getting API Keys

- **Sonarr**: Settings → General → Security → API Key
- **Radarr**: Settings → General → Security → API Key
- **Jellyfin**: Dashboard → API Keys

## Usage

### Web Interface

1. Open the application in your browser
2. Navigate to the "Analyze" tab
3. Select a video file (MP4, MKV, AVI, TS, WebM, OGG)
4. Optionally select a subtitle file (SRT, VTT, ASS, SSA, SUB)
5. Click "Analyze Video"
6. Review the detailed compatibility report

### API Endpoints

#### Analyze Video
```
POST /api/analysis/analyze
Content-Type: application/json

{
  "videoPath": "C:\\Videos\\movie.mp4",
  "subtitlePath": "C:\\Videos\\movie.srt" // optional
}
```

#### Analyze Uploaded File
```
POST /api/analysis/analyze-file
Content-Type: multipart/form-data

videoFile: [file]
subtitleFile: [file] // optional
```

#### Servarr Status
```
GET /api/servarr/status
```

#### Analyze Sonarr Series
```
POST /api/servarr/sonarr/analyze-series/{seriesId}
```

#### Analyze Radarr Movie
```
POST /api/servarr/radarr/analyze-movie/{movieId}
```

## Compatibility Matrix

The application uses the latest Jellyfin compatibility data as of December 2025, including:

- **Video Codecs**: H.264, H.265, VP9, AV1, MPEG-4
- **Audio Codecs**: AAC, MP3, AC3, EAC3, DTS, FLAC, Opus, Vorbis, ALAC
- **Containers**: MP4, MKV, WebM, TS, OGG, AVI
- **Subtitle Formats**: SRT, VTT, ASS, SSA, VobSub, MP4TT, PGSSUB, EIA-608/708

## Integration with Servarr Apps

### Sonarr
- Analyze TV series episodes for media optimization
- Get episode file paths from Sonarr API
- Queue analysis for entire series
- Sync library paths from Sonarr root folders
- Path mapping support for Docker environments

### Radarr
- Analyze movie files for media optimization
- Get movie file paths from Radarr API
- Batch analyze multiple movies
- Sync library paths from Radarr root folders
- Path mapping support for Docker environments

### Jellyfin
- Sync playback history to identify direct play vs transcode patterns
- Match playback data with local library files
- View client compatibility statistics

## Documentation

This project follows the **B-MAD (Breakthrough Method for Agentic Development)** methodology for comprehensive documentation:

- **[Product Requirement Document (PRD)](docs/PRD.md)** - Complete product specifications, requirements, and user stories
- **[System Architecture Document](docs/ARCHITECTURE.md)** - Technical architecture, component design, and system interactions
- **[Developer Guide](docs/DEVELOPER_GUIDE.md)** - Getting started, code patterns, and development workflows

These documents provide context-rich information for developers, contributors, and stakeholders.

## Database Migration

If you're upgrading from a previous version and need to add Servarr integration fields to your existing database, you can:

1. **Automatic (New Installations):** New databases will automatically include the Servarr fields.

2. **Manual Migration (Existing Databases):** 
   - Run the SQL script located at `Data/Migrations/AddServarrFields.sql` against your database
   - Or delete your database file (`data/optimarr.db`) and let the application recreate it (⚠️ **WARNING:** This will delete all your data)

3. **Using Entity Framework Migrations:**
   ```bash
   dotnet ef migrations add AddServarrFields
   dotnet ef database update
   ```

## TODO / Roadmap

### Planned Features
- [ ] Automated quality profile recommendations based on compatibility
- [ ] Batch video re-encoding suggestions
- [ ] Integration with Plex for playback history
- [ ] Export/Import compatibility reports
- [ ] Scheduled library scans
- [ ] Email notifications for scan completion
- [ ] Advanced filtering and search options
- [ ] Video comparison tool
- [ ] API rate limiting and authentication
- [ ] Multi-user support with roles

### Known Issues / Improvements
- [ ] Improve error handling for broken media files
- [ ] Optimize large library scan performance
- [ ] Add more comprehensive logging
- [ ] Improve mobile responsiveness
- [ ] Add unit tests

## License

This project is open source and available under the MIT License.

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## Support

For issues and questions, please open an issue on the project repository.



## Acknowledgments

- Built with ASP.NET Core 8.0
- Uses MediaInfo.NET for video metadata extraction
- Inspired by the Servarr family of applications
- Media optimization data based on official Jellyfin client documentation

