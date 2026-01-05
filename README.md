# Optimarr - Media Optimization

A web-based application for analyzing video files to determine their compatibility with media server clients. Built in the Servarr family style with integration support for Sonarr, Radarr, and Jellyfin.

## Features

- **Video Analysis**: Analyze video files to determine Direct Play compatibility with media server clients
- **Subtitle Support**: Analyze external subtitle files alongside video files
- **Servarr Integration**: Connect with Sonarr, Radarr, and Jellyfin for automated analysis
- **Modern Web UI**: Servarr-style dark theme interface
- **RESTful API**: Full API support for integration with other tools
- **Comprehensive Reports**: Detailed compatibility reports with per-client breakdown

## Requirements

- .NET 8.0 SDK
- Windows, Linux, or macOS
- MediaInfo library (included via NuGet package)

## Installation

### Option 1: Docker (Recommended)

#### Using Docker Hub (Easiest)

Pull and run the pre-built image from Docker Hub:

```bash
docker pull yourusername/optimarr:latest
```

Run the container:
```bash
docker run -d \
  --name optimarr \
  -p 5000:8080 \
  -v $(pwd)/config:/app/config:ro \
  -v $(pwd)/data:/app/data \
  -v $(pwd)/logs:/app/logs \
  -v $(pwd)/videos:/videos:ro \
  --restart unless-stopped \
  yourusername/optimarr:latest
```

   Or use docker-compose with the Docker Hub image:
   ```bash
   # First, initialize the config folder (Windows)
   .\init-config.ps1

   # Or on Linux/Mac
   chmod +x init-config.sh
   ./init-config.sh

   # Then start the container using the Docker Hub compose file
   docker-compose -f docker-compose.hub.yml up -d
   
   # Or edit docker-compose.yml and uncomment the 'image' line, then:
   # docker-compose up -d
   ```

**Note:** Replace `yourusername/optimarr` with your actual Docker Hub username/repository name.

#### Building from Source

1. Build the Docker image:
   ```bash
   docker build -t optimarr .
   ```

2. Run the container:
   ```bash
   docker run -d \
     --name optimarr \
     -p 5000:8080 \
     -v $(pwd)/config:/app/config:ro \
     -v $(pwd)/data:/app/data \
     -v $(pwd)/logs:/app/logs \
     -v $(pwd)/videos:/videos:ro \
     --restart unless-stopped \
     optimarr
   ```

   **Note:** 
   - The config folder must have `appsettings.json` before starting. If the folder is empty, Docker will hide the container's default configuration. Run the initialization script above to copy the default config file.
   - The container automatically fixes permissions for mounted volumes on startup. The `data` and `logs` directories will be owned by UID 1000 (optimarr user) automatically.

3. Open your browser and navigate to `http://localhost:5000`

### Option 2: Native Installation

1. Clone or download this repository
2. Restore NuGet packages:
   ```bash
   dotnet restore
   ```
3. Build the project:
   ```bash
   dotnet build
   ```
4. Run the application:
   ```bash
   dotnet run
   ```
5. Open your browser and navigate to `http://localhost:5000` (or the port shown in the console)

## Configuration

Edit `appsettings.json` to configure Servarr integrations:

```json
{
  "Servarr": {
    "Sonarr": {
      "BaseUrl": "http://localhost:8989",
      "ApiKey": "your-sonarr-api-key",
      "Enabled": true
    },
    "Radarr": {
      "BaseUrl": "http://localhost:7878",
      "ApiKey": "your-radarr-api-key",
      "Enabled": true
    }
  }
}
```

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

## Development

### Docker Development

For development with hot reload:

```bash
docker-compose -f docker-compose.dev.yml up
```

This uses `Dockerfile.dev` which includes the .NET SDK and enables hot reload.

### Project Structure

```
optimarr/
├── Controllers/          # API controllers
├── Models/              # Data models
├── Services/            # Business logic services
├── wwwroot/             # Web UI files
│   ├── index.html
│   ├── styles.css
│   └── app.js
├── Program.cs           # Application entry point
└── appsettings.json     # Configuration
```

### Building for Production

#### Native Build

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

For Linux:
```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

#### Docker Build

Build the Docker image:
```bash
docker build -t optimarr .
```

Tag and push to Docker Hub:
```bash
# Tag the image
docker tag optimarr yourusername/optimarr:latest
docker tag optimarr yourusername/optimarr:0.0.1

# Login to Docker Hub
docker login

# Push the image
docker push yourusername/optimarr:latest
docker push yourusername/optimarr:0.0.1
```

For detailed instructions, see [DOCKER_HUB.md](DOCKER_HUB.md).

Tag for other registries:
```bash
docker tag optimarr your-registry/optimarr:latest
docker push your-registry/optimarr:latest
```

### Docker Deployment

#### Using Docker Compose

1. Edit `docker-compose.yml` to configure volumes and networks
2. Update `appsettings.json` with your Servarr API keys
3. Run:
   ```bash
   docker-compose up -d
   ```

#### Docker Run Command

```bash
docker run -d \
  --name optimarr \
  -p 5000:8080 \
  -v /path/to/appsettings.json:/app/appsettings.json:ro \
  -v /path/to/videos:/videos:ro \
  --restart unless-stopped \
  optimarr
```

#### Environment Variables

You can override configuration using environment variables:

```bash
docker run -d \
  --name optimarr \
  -p 5000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Servarr__Sonarr__BaseUrl=http://sonarr:8989 \
  -e Servarr__Sonarr__ApiKey=your-api-key \
  -e Servarr__Sonarr__Enabled=true \
  optimarr
```

#### Networking with Servarr Apps

If running in Docker alongside Sonarr/Radarr/Jellyfin, use Docker networks:

```bash
# Create a network
docker network create servarr-network

# Run optimarr
docker run -d \
  --name optimarr \
  --network servarr-network \
  -p 5000:8080 \
  optimarr

# Run Sonarr (example)
docker run -d \
  --name sonarr \
  --network servarr-network \
  -p 8989:8989 \
  lscr.io/linuxserver/sonarr:latest
```

Then update `appsettings.json` to use container names:
```json
{
  "Servarr": {
    "Sonarr": {
      "BaseUrl": "http://sonarr:8989",
      "ApiKey": "your-api-key",
      "Enabled": true
    }
  }
}
```

## Integration with Servarr Apps

### Sonarr
- Analyze TV series episodes for media optimization
- Get episode file paths from Sonarr API
- Queue analysis for entire series

### Radarr
- Analyze movie files for media optimization
- Get movie file paths from Radarr API
- Batch analyze multiple movies

### Jellyfin
- Sync playback history to identify direct play vs transcode patterns
- Match playback data with local library files
- View client compatibility statistics

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

