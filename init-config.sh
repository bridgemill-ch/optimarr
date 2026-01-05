#!/bin/bash
# Bash script to initialize config folder
# This ensures appsettings.json exists in the config folder before starting Docker

CONFIG_DIR="config"
CONFIG_FILE="$CONFIG_DIR/appsettings.json"
ROOT_CONFIG_FILE="appsettings.json"

if [ ! -d "$CONFIG_DIR" ]; then
    echo "Creating config directory..."
    mkdir -p "$CONFIG_DIR"
fi

if [ ! -f "$CONFIG_FILE" ]; then
    if [ -f "$ROOT_CONFIG_FILE" ]; then
        echo "Copying appsettings.json to config folder..."
        cp "$ROOT_CONFIG_FILE" "$CONFIG_FILE"
        echo "✓ Config file initialized: $CONFIG_FILE"
    else
        echo "Warning: appsettings.json not found in root directory!"
        echo "Please ensure appsettings.json exists in the project root."
    fi
else
    echo "✓ Config file already exists: $CONFIG_FILE"
fi

