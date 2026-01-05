# PowerShell script to initialize config folder
# This ensures appsettings.json exists in the config folder before starting Docker

$configDir = "config"
$configFile = Join-Path $configDir "appsettings.json"
$rootConfigFile = "appsettings.json"

if (-not (Test-Path $configDir)) {
    Write-Host "Creating config directory..."
    New-Item -ItemType Directory -Path $configDir | Out-Null
}

if (-not (Test-Path $configFile)) {
    if (Test-Path $rootConfigFile) {
        Write-Host "Copying appsettings.json to config folder..."
        Copy-Item $rootConfigFile $configFile
        Write-Host "✓ Config file initialized: $configFile"
    } else {
        Write-Host "Warning: appsettings.json not found in root directory!"
        Write-Host "Please ensure appsettings.json exists in the project root."
    }
} else {
    Write-Host "✓ Config file already exists: $configFile"
}

