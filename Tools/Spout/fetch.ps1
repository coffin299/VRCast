# Download the KlakSpout (Unlicense) native plugin for the Spout2 output into the VRCast project.
#   - Sender plugin (64 bit): Assets/Plugins/KlakSpout/x86_64/KlakSpout.dll
# KlakSpout.dll contains the Spout SDK (BSD 2-Clause). The files are not committed (.gitignore).
# Keep this file ASCII only: Windows PowerShell 5.1 reads BOM-less UTF-8 as the ANSI code page.

$ErrorActionPreference = 'Stop'

# Source (pinned commit for reproducible builds)
$commit = '2a1186748dcaeff1801fed9f4432c3907b811c50'
$baseUrl = "https://raw.githubusercontent.com/keijiro/KlakSpout/$commit/Packages/jp.keijiro.klak.spout"

# Destination
$folder = Join-Path $PSScriptRoot '..\..\VRCast\Assets\Plugins\KlakSpout\x86_64'
New-Item -ItemType Directory -Force -Path $folder | Out-Null

# Plugin and the KlakSpout license
Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/Plugin/KlakSpout.dll" -OutFile (Join-Path $folder 'KlakSpout.dll')
Write-Host 'Downloaded Plugins/KlakSpout/x86_64/KlakSpout.dll'
Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/LICENSE" -OutFile (Join-Path $folder '..\LICENSE.txt')
Write-Host 'Done.'
