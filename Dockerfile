# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies (this layer will be cached if csproj doesn't change)
COPY optimarr.csproj .
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore || (echo "ERROR: dotnet publish failed" && exit 1)
RUN echo "Verifying publish output exists..." && \
    if [ ! -d /app/publish ]; then \
        echo "ERROR: /app/publish directory was not created!" && \
        exit 1; \
    fi && \
    echo "Publish output directory exists. Contents:" && \
    ls -la /app/publish/ | head -20 && \
    echo "" && \
    echo "Checking for MediaInfo native libraries in publish output..." && \
    find /app/publish -name "*mediainfo*" -o -name "*MediaInfo*" 2>/dev/null | head -20 || echo "No MediaInfo files in publish output" && \
    echo "" && \
    echo "Checking runtimes folder structure..." && \
    find /app/publish/runtimes -type f 2>/dev/null | head -20 || echo "No runtimes folder found"

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Add labels for Docker Hub metadata
LABEL org.opencontainers.image.title="Optimarr"
LABEL org.opencontainers.image.description="A web-based application for analyzing video files to determine their compatibility with media server clients"
LABEL org.opencontainers.image.vendor="Optimarr"
LABEL org.opencontainers.image.version="0.0.1"
LABEL org.opencontainers.image.url="https://github.com/yourusername/optimarr"
LABEL org.opencontainers.image.documentation="https://github.com/yourusername/optimarr/blob/main/README.md"
LABEL org.opencontainers.image.source="https://github.com/yourusername/optimarr"
LABEL org.opencontainers.image.licenses="MIT"

WORKDIR /app

# Install MediaInfo CLI tool, curl for healthcheck, and gosu for user switching
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        mediainfo \
        ca-certificates \
        curl \
        gosu && \
    rm -rf /var/lib/apt/lists/* && \
    apt-get clean

# Verify MediaInfo CLI is installed
RUN mediainfo --Version || echo "Warning: MediaInfo CLI not found"

# Create a non-root user for security
RUN groupadd -r optimarr && useradd -r -g optimarr -u 1000 optimarr && \
    mkdir -p /app/config /app/data /app/logs

# Copy published app
COPY --from=build /app/publish .

# Verify mediainfo CLI is available
RUN echo "=== MediaInfo CLI Verification ===" && \
    mediainfo --Version && \
    echo "✓ MediaInfo CLI installed successfully"

# Copy appsettings.json to config folder as default configuration
# This ensures the config folder has a default configuration file
# If the host mounts a config folder, it will override this
COPY appsettings.json /app/config/appsettings.json

# Copy entrypoint script (needs to be owned by root to run as root initially)
COPY docker-entrypoint.sh /usr/local/bin/
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

# Set ownership of app files (entrypoint stays root-owned)
RUN chown -R optimarr:optimarr /app

# Expose port
EXPOSE 8080

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Add healthcheck (runs as root, but that's okay for healthchecks)
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl --fail http://localhost:8080/api/system/health || exit 1

# Use entrypoint script to fix permissions, then switch to non-root user
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh", "dotnet", "optimarr.dll"]

