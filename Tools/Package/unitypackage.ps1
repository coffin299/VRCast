<#
.SYNOPSIS
    Pack the exporter (Packages/com.vrcast.converter) into a .unitypackage that installs to Assets/VRCast/Converter.
    Unity is not required. Uses the .meta files in the package so the GUIDs stay the same across versions.
.PARAMETER Version
    Version in the file name. Defaults to "version" in Packages/com.vrcast.converter/package.json.
.PARAMETER OutputPath
    Output file. Defaults to dist/VRCast-Converter-<version>.unitypackage in this repository.
#>
param(
    [string]$Version = "",
    [string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

# 取り込み元・取り込み先（利用者のプロジェクト内のパス）
$repository = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$source = Join-Path $repository "Packages\com.vrcast.converter"
$installPath = "Assets/VRCast/Converter"

# バージョン: 指定 > package.json の version
if (-not $Version) {
    $Version = (Get-Content -Raw (Join-Path $source "package.json") | ConvertFrom-Json).version
}
if (-not $OutputPath) { $OutputPath = Join-Path $repository "dist\VRCast-Converter-$Version.unitypackage" }
Write-Host "Packing VRCast-Converter-$Version from $source"

# 作業フォルダはリポジトリの外（完了後に削除）
$work = Join-Path $env:LOCALAPPDATA "VRCast\unitypackage-build"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null

try {
    # .meta を持つファイル・フォルダを 1 件ずつ「<GUID>/asset, asset.meta, pathname」の形で並べる（package.json は UPM 用なので除く）
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    foreach ($meta in Get-ChildItem $source -Recurse -File -Filter *.meta) {
        $assetPath = $meta.FullName.Substring(0, $meta.FullName.Length - 5)
        if ((Split-Path $assetPath -Leaf) -eq "package.json") { continue }

        # GUID は .meta の guid 行から取る
        $guidLine = Select-String -Path $meta.FullName -Pattern "^guid:\s*([0-9a-f]{32})" | Select-Object -First 1
        if (-not $guidLine) { throw "GUID not found in $($meta.FullName)" }
        $entry = Join-Path $work $guidLine.Matches[0].Groups[1].Value
        New-Item -ItemType Directory -Force $entry | Out-Null

        # 取り込み先のパス（区切りは "/"）と .meta。フォルダは中身を持たない
        $relative = $assetPath.Substring($source.Length + 1).Replace('\', '/')
        [System.IO.File]::WriteAllText((Join-Path $entry "pathname"), "$installPath/$relative", $utf8)
        Copy-Item $meta.FullName (Join-Path $entry "asset.meta")
        if (Test-Path $assetPath -PathType Leaf) {
            Copy-Item $assetPath (Join-Path $entry "asset")
        }
    }

    # .meta の無いファイルは取り込まれないため中止（Unity で一度開くと作られる）
    $missing = Get-ChildItem $source -Recurse |
        Where-Object { $_.Extension -ne ".meta" -and -not (Test-Path "$($_.FullName).meta") }
    if ($missing) {
        throw "Missing .meta files (open the VRCast project in Unity once, then commit them): $($missing.FullName -join ', ')"
    }

    # GUID のフォルダを gzip 圧縮の tar にまとめる（Windows 10 以降の tar.exe。Unity が読める ustar 形式）
    New-Item -ItemType Directory -Force (Split-Path $OutputPath -Parent) | Out-Null
    if (Test-Path $OutputPath) { Remove-Item $OutputPath -Force }
    $entries = Get-ChildItem $work -Directory -Name
    & tar.exe --format ustar -czf $OutputPath -C $work @entries
    if ($LASTEXITCODE -ne 0) { throw "tar.exe failed with exit code $LASTEXITCODE." }

    Write-Host "Done: $OutputPath ($($entries.Count) assets)"
}
finally {
    # 作業フォルダは成否に関わらず削除
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
