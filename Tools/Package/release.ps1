<#
.SYNOPSIS
    Build everything for a release in one go (except the Unity app build):
      1. Build the MediaPipe tracker (Tools/MediaPipeTracker/build.ps1)
      2. Copy the new tracker into the existing Windows build (VRCast/Builds/Windows)
      3. Pack the exporter into dist/VRCast-Converter-<version>.unitypackage
      4. Package the distribution zip dist/VRCast-<version>-win64.zip
    Build the app in Unity first (VRCast > Build > Windows x64).
.PARAMETER PythonVersion
    Python version used to build the tracker (MediaPipe supports 3.9 - 3.12).
.PARAMETER SkipTracker
    Skip step 1 and package the tracker that is already in VRCast/Trackers.
.PARAMETER BuildPath
    Folder that contains VRCast.exe. Defaults to VRCast/Builds/Windows in this repository.
#>
param(
    [string]$PythonVersion = "3.12",
    [switch]$SkipTracker,
    [string]$BuildPath = ""
)

$ErrorActionPreference = "Stop"

# Repository paths
$repository = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $BuildPath) { $BuildPath = Join-Path $repository "VRCast\Builds\Windows" }
$trackerSource = Join-Path $repository "VRCast\Trackers\MediaPipeTracker"
$trackerTarget = Join-Path $BuildPath "VRCast_Data\StreamingAssets\MediaPipeTracker"

function Write-Step([string]$message) {
    # Make each step easy to find in the console output
    Write-Host ""
    Write-Host "==== $message ====" -ForegroundColor Cyan
}

# Stop before the long tracker build if the Unity build is missing
if (-not (Test-Path (Join-Path $BuildPath "VRCast.exe"))) {
    throw "VRCast.exe was not found in $BuildPath. Build the app in Unity first (VRCast > Build > Windows x64)."
}

# Warn when the app build and the exporter have different versions (the zip name uses the app version)
$settings = Join-Path $repository "VRCast\ProjectSettings\ProjectSettings.asset"
$line = Select-String -Path $settings -Pattern "^\s*bundleVersion:\s*(.+)$" | Select-Object -First 1
$appVersion = if ($line) { $line.Matches[0].Groups[1].Value.Trim() } else { "unknown" }
$converterVersion = (Get-Content -Raw (Join-Path $repository "Packages\com.vrcast.converter\package.json") | ConvertFrom-Json).version
Write-Host "App version (last Unity build): $appVersion / Exporter version: $converterVersion"
if ($appVersion -ne $converterVersion) {
    Write-Warning "The versions differ. Rebuild the app in Unity after changing AppVersion in VRCastBuild.cs."
}

# 1. Tracker
if ($SkipTracker) {
    Write-Step "1/4 Tracker build skipped"
}
else {
    Write-Step "1/4 Building the MediaPipe tracker (Python $PythonVersion)"
    if (-not (Get-Command py -ErrorAction SilentlyContinue)) {
        throw "The 'py' launcher was not found. Install Python $PythonVersion.x from https://www.python.org/"
    }
    & (Join-Path $repository "Tools\MediaPipeTracker\build.ps1") -PythonVersion $PythonVersion
}

# 2. Replace the tracker inside the Unity build so the zip gets the new one without rebuilding in Unity
Write-Step "2/4 Copying the tracker into the Windows build"
if (Test-Path (Join-Path $trackerSource "vrcast_tracker.exe")) {
    if (Test-Path $trackerTarget) { Remove-Item $trackerTarget -Recurse -Force }
    Copy-Item $trackerSource $trackerTarget -Recurse
    Write-Host "Copied to $trackerTarget"
}
else {
    Write-Warning "vrcast_tracker.exe was not found in $trackerSource. The zip keeps the tracker from the Unity build (if any)."
}

# 3. Standalone exporter package (also put into the zip by step 4)
Write-Step "3/4 Packing the exporter unitypackage"
& (Join-Path $PSScriptRoot "unitypackage.ps1") -Version $converterVersion

# 4. Distribution zip
Write-Step "4/4 Packaging the distribution zip"
& (Join-Path $PSScriptRoot "package.ps1") -BuildPath $BuildPath

Write-Step "Release build finished"
Write-Host "Output: $(Join-Path $repository 'dist')"
