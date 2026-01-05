#!/bin/bash
set -e

# Fix permissions for mounted volumes
# This script runs as root initially, fixes permissions, then switches to the non-root user

# Function to fix directory permissions
fix_permissions() {
    local dir=$1
    if [ -d "$dir" ]; then
        # Always try to fix permissions for mounted volumes
        echo "Fixing permissions for $dir (if needed)"
        chown -R optimarr:optimarr "$dir" 2>/dev/null || true
        chmod -R 755 "$dir" 2>/dev/null || true
    else
        # Create directory if it doesn't exist
        echo "Creating directory: $dir"
        mkdir -p "$dir"
        chown -R optimarr:optimarr "$dir"
        chmod -R 755 "$dir"
    fi
}

# Only fix permissions if running as root (which we are initially)
if [ "$(id -u)" = "0" ]; then
    # Fix permissions for data and logs directories (config is read-only, so skip it)
    fix_permissions /app/data
    fix_permissions /app/logs
    
    # Switch to non-root user and run the application
    exec gosu optimarr "$@"
else
    # Already running as non-root, just execute
    exec "$@"
fi

