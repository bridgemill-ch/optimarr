#!/bin/bash
set -e

# Fix permissions for mounted volumes and initialize config if needed
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

# Function to initialize config if needed
init_config() {
    local config_dir="/app/config"
    local config_file="$config_dir/appsettings.json"
    # Default config is stored outside the config directory to avoid being hidden by mounts
    local default_config="/app/appsettings.json.default"
    
    # If config file doesn't exist, try to copy from default
    if [ ! -f "$config_file" ]; then
        if [ -f "$default_config" ]; then
            # Check if config directory is writable
            if [ -w "$config_dir" ] 2>/dev/null; then
                echo "Initializing config file from default..."
                cp "$default_config" "$config_file"
                chown optimarr:optimarr "$config_file" 2>/dev/null || true
                chmod 644 "$config_file" 2>/dev/null || true
            else
                echo "Warning: Config directory is read-only and appsettings.json doesn't exist."
                echo "Please provide appsettings.json in the mounted config folder."
            fi
        fi
    fi
}

# Only fix permissions if running as root (which we are initially)
if [ "$(id -u)" = "0" ]; then
    # Initialize config if needed (before fixing permissions)
    init_config
    
    # Fix permissions for data and logs directories
    fix_permissions /app/data
    fix_permissions /app/logs
    
    # Fix config directory permissions if writable
    if [ -d "/app/config" ] && [ -w "/app/config" ]; then
        fix_permissions /app/config
    fi
    
    # Switch to non-root user and run the application
    exec gosu optimarr "$@"
else
    # Already running as non-root, just execute
    exec "$@"
fi

