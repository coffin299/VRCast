<#
.SYNOPSIS
    Package the Windows build (VRCast/Builds/Windows) and the exporter .unitypackage
    into dist/VRCast-<version>-win64.zip for distribution.
    The zip contains VRCast/ (the app) and VRCast-Converter/ (the exporter for the avatar project).
.PARAMETER Version
    Version in the zip name. Defaults to bundleVersion in ProjectSettings (Player > Version).
.PARAMETER BuildPath
    Folder that contains VRCast.exe. Defaults to VRCast/Builds/Windows in this repository.
#>
param(
    [string]$Version = "",
    [string]$BuildPath = ""
)

$ErrorActionPreference = "Stop"

# リポジトリ直下・ビルドの場所・出力先
$repository = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $BuildPath) { $BuildPath = Join-Path $repository "VRCast\Builds\Windows" }
$dist = Join-Path $repository "dist"

# ビルドが無ければ中止（先に Unity で VRCast > Build > Windows x64）
if (-not (Test-Path (Join-Path $BuildPath "VRCast.exe"))) {
    throw "VRCast.exe was not found in $BuildPath. Build it first (Unity: VRCast > Build > Windows x64)."
}

# バージョン: 指定 > ProjectSettings の bundleVersion > dev
if (-not $Version) {
    $settings = Join-Path $repository "VRCast\ProjectSettings\ProjectSettings.asset"
    $line = if (Test-Path $settings) { Select-String -Path $settings -Pattern "^\s*bundleVersion:\s*(.+)$" | Select-Object -First 1 }
    $Version = if ($line) { $line.Matches[0].Groups[1].Value.Trim() } else { "dev" }
}
$name = "VRCast-$Version-win64"
Write-Host "Packaging $name from $BuildPath"

# 同梱物が欠けていれば警告（無くても続行。その機能が使えないだけ）
$streaming = Join-Path $BuildPath "VRCast_Data\StreamingAssets"
$optional = [ordered]@{
    "MediaPipeTracker\vrcast_tracker.exe" = "MediaPipe tracker (run Tools\MediaPipeTracker\build.bat, then rebuild in Unity)"
    "UnityCapture\UnityCaptureFilter64.dll" = "Virtual camera driver (run Tools\UnityCapture\fetch.ps1, then rebuild in Unity)"
}
foreach ($path in $optional.Keys) {
    if (-not (Test-Path (Join-Path $streaming $path))) {
        Write-Warning "Missing: $($optional[$path])"
    }
}

# Spout2 の送信プラグインは Unity のビルド時にしか入らないので、欠けていれば中止（Spout2 出力が使えない zip を配らない）
if (-not (Test-Path (Join-Path $BuildPath "VRCast_Data\Plugins\x86_64\KlakSpout.dll"))) {
    throw "KlakSpout.dll was not found in the build. Run Tools\Spout\fetch.ps1, then rebuild in Unity (VRCast > Build > Windows x64)."
}

# 書き出しツールのバージョン（ファイル名に入れる）: package.json の version
$converterVersion = (Get-Content -Raw (Join-Path $repository "Packages\com.vrcast.converter\package.json") | ConvertFrom-Json).version

# 作業フォルダはリポジトリの外（完了後に削除）。zip を展開すると、本体の VRCast フォルダと
# アバターのプロジェクトに入れる書き出しツールの VRCast-Converter フォルダが並ぶ（取り違えないよう分ける）
$work = Join-Path $env:LOCALAPPDATA "VRCast\package-build"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
$staging = Join-Path $work "VRCast"
$converter = Join-Path $work "VRCast-Converter"
New-Item -ItemType Directory -Force $staging | Out-Null
New-Item -ItemType Directory -Force $converter | Out-Null

try {
    # ビルド一式をコピー（Unity が「配布しない」と名付けるデバッグ用フォルダは除く）
    Get-ChildItem -Force $BuildPath |
        Where-Object { $_.Name -notlike "*_DoNotShip" -and $_.Name -notlike "*_ButDontShipItWithYourGame" } |
        Copy-Item -Destination $staging -Recurse -Force

    # ライセンス・サードパーティ表記・はじめにお読みください
    Copy-Item (Join-Path $repository "LICENSE") (Join-Path $staging "LICENSE.txt")
    Copy-Item (Join-Path $repository "NOTICE") (Join-Path $staging "NOTICE.txt")
    Copy-Item (Join-Path $repository "CHANGELOG.txt") (Join-Path $staging "CHANGELOG.txt")
    Copy-Item (Join-Path $PSScriptRoot "README.txt") (Join-Path $staging "README.txt")

    # アバターのプロジェクトに入れる書き出しツール（バージョン付きの名前）と、
    # 「アバターのプロジェクトにインポートする」と名前で伝える説明（日本語のファイル名はスクリプトに書かず Converter フォルダから複製）
    & (Join-Path $PSScriptRoot "unitypackage.ps1") -Version $converterVersion `
        -OutputPath (Join-Path $converter "VRCast-Converter-$converterVersion.unitypackage")
    Copy-Item (Join-Path $PSScriptRoot "Converter\*") $converter -Recurse -Force

    # 既存の同名 zip を消してから作成
    New-Item -ItemType Directory -Force $dist | Out-Null
    $zip = Join-Path $dist "$name.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # 作業フォルダからの相対パス（VRCast/... と VRCast-Converter/...）をエントリ名にする
    $root = $work
    $archive = [System.IO.Compression.ZipFile]::Open(
        $zip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem $work -Recurse -File) {
            # Windows PowerShell 5.1 は "\" 区切りのエントリを作るため "/" に揃える
            $entry = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive, $file.FullName, $entry,
                [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        # zip を確実に閉じてファイルロックを解放
        $archive.Dispose()
    }

    $size = [Math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host "Done: $zip ($size MB)"
}
finally {
    # 作業フォルダは成否に関わらず削除
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
