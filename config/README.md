# Configuration Directory

This directory contains the `appsettings.json` file that will be mounted into the Docker container.

## Setup Instructions

1. **Copy the default configuration** (if the file doesn't exist):
   ```bash
   cp ../appsettings.json ./appsettings.json
   ```
   
   **Note:** If this folder is empty, the container will use the default `appsettings.json` from the image. 
   However, if you mount this folder (as configured in docker-compose.yml), you should ensure `appsettings.json` exists here.

2. **Edit `appsettings.json`** to configure:
   - Servarr API keys and URLs
   - Logging levels
   - Other application settings

3. **The configuration file is mounted as read-only** in the container to prevent accidental modifications.

## Important Notes

- **If the config folder is empty on the host**, the container will still work using the default configuration built into the image
- **To customize settings**, copy `appsettings.json` from the project root to this folder before starting the container
- The application will automatically copy `appsettings.json` from root to config folder on startup if config is writable (but config is mounted as read-only, so this won't work with Docker volumes)

## Volume Mounts

The following volumes are configured in `docker-compose.yml`:

- **Config**: `./config` → `/app/config` (read-only)
- **Database**: `./data` → `/app/data` (persistent, visible on host)
- **Logs**: `./logs` → `/app/logs` (persistent, visible on host)
- **Videos**: `./videos` → `/videos` (read-only)

## Notes

- Changes to `appsettings.json` require container restart to take effect
- The database is stored in `./data/optimarr.db` on the host
- Logs are stored in `./logs/` on the host and can be accessed directly
- All folders are created automatically on first boot if they don't exist

