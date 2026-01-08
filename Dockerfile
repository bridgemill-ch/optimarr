# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies (this layer will be cached if csproj doesn't change)
COPY optimarr.csproj .
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Add labels for Docker Hub metadata
LABEL org.opencontainers.image.title="Optimarr"
LABEL org.opencontainers.image.description="A web-based application for analyzing video files to determine their compatibility with media server clients"
LABEL org.opencontainers.image.vendor="Optimarr"
LABEL org.opencontainers.image.version="1.1.2"
LABEL org.opencontainers.image.url="https://github.com/bridgemill-ch/optimarr"
LABEL org.opencontainers.image.documentation="https://github.com/bridgemill-ch/optimarr/blob/main/README.md"
LABEL org.opencontainers.image.source="https://github.com/bridgemill-ch/optimarr"
LABEL org.opencontainers.image.licenses="MIT"

WORKDIR /app

# Install MediaInfo CLI tool, curl for healthcheck, and gosu for user switching
# Create non-root user and directories in a single layer
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        mediainfo \
        ca-certificates \
        curl \
        gosu && \
    rm -rf /var/lib/apt/lists/* && \
    apt-get clean && \
    groupadd -r optimarr && \
    useradd -r -g optimarr -u 1000 optimarr && \
    mkdir -p /app/config /app/data /app/logs && \
    mediainfo --Version

# Copy published app
COPY --from=build /app/publish .

# Copy appsettings.json as default configuration (stored outside config dir to avoid being hidden by mounts)
# Entrypoint will copy this to /app/config/appsettings.json if the mounted config folder is empty
COPY appsettings.json /app/appsettings.json.default

# Copy entrypoint script (needs to be owned by root to run as root initially)
COPY --chmod=755 docker-entrypoint.sh /usr/local/bin/

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

