# Docker Deployment Guide

## Quick Start

### Build the Image

```bash
docker build -t optimarr .
```

### Run the Container

```bash
docker run -d \
  --name optimarr \
  -p 5000:8080 \
  -v $(pwd)/appsettings.json:/app/appsettings.json:ro \
  optimarr
```

### Using Docker Compose

```bash
docker-compose up -d
```

## Configuration

### Mount appsettings.json

The application reads configuration from `appsettings.json`. Mount it as a volume:

```bash
-v /path/to/appsettings.json:/app/appsettings.json:ro
```

### Environment Variables

You can override configuration using environment variables with double underscores:

```bash
-e Servarr__Sonarr__BaseUrl=http://sonarr:8989
-e Servarr__Sonarr__ApiKey=your-api-key
-e Servarr__Sonarr__Enabled=true
```

### Volume Mounts

#### For File Path Analysis

If you want to analyze files by path (not upload), mount your video directory:

```bash
-v /path/to/videos:/videos:ro
```

Then use paths like `/videos/movie.mp4` in the API.

#### For File Uploads

File uploads work without additional mounts - files are processed in memory or temporary storage.

## Networking

### Connect to Servarr Apps

If Sonarr/Radarr/Jellyfin are running in Docker:

1. Create a network:
   ```bash
   docker network create servarr-network
   ```

2. Run containers on the same network:
   ```bash
   docker run -d \
     --name optimarr \
     --network servarr-network \
     -p 5000:8080 \
     optimarr
   ```

3. Update `appsettings.json` to use container names:
   ```json
   {
     "Servarr": {
       "Sonarr": {
         "BaseUrl": "http://sonarr:8989"
       }
     }
   }
   ```

### External Servarr Apps

If Servarr apps are on the host or another machine:

- Use `host.docker.internal` (Docker Desktop) or host IP
- Example: `http://host.docker.internal:8989`

## Development

### Development Mode with Hot Reload

```bash
docker-compose -f docker-compose.dev.yml up
```

This mounts the source code and enables hot reload.

### View Logs

```bash
docker logs -f optimarr
```

### Stop and Remove

```bash
docker stop optimarr
docker rm optimarr
```

Or with compose:

```bash
docker-compose down
```

## Troubleshooting

### MediaInfo Not Working

If you get errors about MediaInfo libraries:

1. Check the container logs:
   ```bash
   docker logs optimarr
   ```

2. Verify libraries are installed:
   ```bash
   docker exec optimarr dpkg -l | grep mediainfo
   ```

3. If needed, rebuild with updated dependencies.

### Port Already in Use

Change the host port:

```bash
docker run -d -p 8080:8080 optimarr
```

### Permission Issues

If you need to access host files, ensure proper permissions:

```bash
docker run -d \
  --user $(id -u):$(id -g) \
  -v /path/to/videos:/videos:ro \
  optimarr
```

## Production Deployment

### Multi-Architecture Build

Build for multiple architectures:

```bash
docker buildx create --use
docker buildx build --platform linux/amd64,linux/arm64 -t optimarr .
```

### Push to Registry

```bash
docker tag optimarr your-registry/optimarr:latest
docker push your-registry/optimarr:latest
```

### Health Check

The application doesn't include a health check endpoint by default. You can add one or use:

```bash
docker run -d \
  --health-cmd="curl -f http://localhost:8080 || exit 1" \
  --health-interval=30s \
  optimarr
```

## Example docker-compose.yml

See `docker-compose.yml` for a complete example with optional Sonarr/Radarr integration.

