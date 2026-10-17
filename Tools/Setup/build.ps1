<#
.SYNOPSIS
    Build the setup EXE (VRCastSetup.exe) for BOOTH / Jinxxy.
    Tools/Package/package.ps1 copies it to dist/<version>/VRCast-setup.exe.
    With no zip appended, the EXE downloads the latest release from GitHub when run.
    Needs Visual Studio 2022 (its C# compiler) and .NET Framework 4.8 (included in Windows 10 / 11).
.PARAMETER Version
    Version shown in the window title and the file properties. Defaults to "dev".
.PARAMETER OutputPath
    Path of the stub. Defaults to %TEMP%\VRCastSetup\VRCastSetup.exe.
#>
param(
    [string]$Version = "dev",
    [string]$OutputPath = ""
)

$ErrorActionPreference = 'Stop'

# リポジトリ直下・ソース・中間ファイル（一時フォルダ）・出力先
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$src = Join-Path $PSScriptRoot 'src'
$obj = Join-Path ([IO.Path]::GetTempPath()) 'VRCastSetup-obj'
if (-not $OutputPath) { $OutputPath = Join-Path ([IO.Path]::GetTempPath()) 'VRCastSetup\VRCastSetup.exe' }
New-Item -ItemType Directory -Force -Path $obj, (Split-Path $OutputPath) | Out-Null

# Visual Studio に付属の C# コンパイラー（Roslyn）を探す（.NET Framework 4.8 向けにビルドする）
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw 'Visual Studio not found. Install Visual Studio 2022.'
}
$vs = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
$csc = if ($vs) { Join-Path $vs 'MSBuild\Current\Bin\Roslyn\csc.exe' } else { $null }
if (-not $csc -or -not (Test-Path $csc)) {
    throw 'The C# compiler of Visual Studio was not found (MSBuild\Current\Bin\Roslyn\csc.exe).'
}

# バージョンをソースに書き込む（数字だけの版はファイルのプロパティのバージョンにも入れる）
if ($Version -notmatch '^[0-9A-Za-z.+-]+$') {
    throw "Invalid version: $Version"
}
$info = @(
    '[assembly: System.Reflection.AssemblyTitle("VRCast Setup")]'
    '[assembly: System.Reflection.AssemblyProduct("VRCast")]'
    '[assembly: System.Reflection.AssemblyCompany("VRCast")]'
    "[assembly: System.Reflection.AssemblyInformationalVersion(`"$Version`")]"
)
if ($Version -match '^\d+(\.\d+){0,3}$') {
    $info += "[assembly: System.Reflection.AssemblyVersion(`"$Version`")]"
    $info += "[assembly: System.Reflection.AssemblyFileVersion(`"$Version`")]"
}
$assemblyInfo = Join-Path $obj 'AssemblyInfo.cs'
[IO.File]::WriteAllLines($assemblyInfo, $info, (New-Object Text.UTF8Encoding $false))

# アプリと同じアイコン（AppIcon.png を縮小した PNG 形式の .ico。256・48・32・16 ピクセル）
Add-Type -AssemblyName System.Drawing
$icon = Join-Path $obj 'VRCast.ico'
$sizes = 256, 48, 32, 16
$source = [Drawing.Image]::FromFile((Join-Path $repository 'VRCast\Assets\VRCast\Branding\AppIcon.png'))
try {
    $images = foreach ($size in $sizes) {
        # 透過を保ったまま高品質に縮小して PNG のバイト列にする
        $bitmap = New-Object Drawing.Bitmap $size, $size
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($source, 0, 0, $size, $size)
        $graphics.Dispose()
        $memory = New-Object IO.MemoryStream
        $bitmap.Save($memory, [Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        , $memory.ToArray()
    }
}
finally {
    $source.Dispose()
}
$writer = New-Object IO.BinaryWriter ([IO.File]::Create($icon))
try {
    # ヘッダー（予約 0・種類 1 = アイコン・枚数）と、各画像の項目（幅・高さ（256 は 0）・色数・予約・面・ビット数・大きさ・位置）
    $writer.Write([UInt16]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$edge)
        $writer.Write([byte]$edge)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]32)
        $writer.Write([UInt32]$images[$i].Length)
        $writer.Write([UInt32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($image in $images) { $writer.Write($image) }
}
finally {
    $writer.Dispose()
}

# 展開の検証・SHA-256 は VRCast 本体と同じソースを使う（Unity に依存しない 2 ファイル）
$sources = @(Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName }) + @(
    (Join-Path $repository 'VRCast\Assets\VRCast\Runtime\Platform\UpdatePackage.cs')
    (Join-Path $repository 'Packages\com.vrcast.converter\Runtime\HashUtility.cs')
    $assemblyInfo
)

# 64 bit で動く GUI アプリ（他のプロセスの実行ファイルの場所を調べるため）。言語は Unity と同じ C# 9 にそろえる
$arguments = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/langversion:9.0', '/utf8output', '/warnaserror+',
    "/win32manifest:$(Join-Path $src 'app.manifest')", "/win32icon:$icon", "/out:$OutputPath",
    '/reference:System.IO.Compression.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Runtime.Serialization.dll'
) + $sources
& $csc @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Build failed ($LASTEXITCODE)."
}

Write-Host "Built $OutputPath"
