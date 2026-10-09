# VRCast のアップデーター（VRCastUpdater.exe）をビルドして VRCast プロジェクトへ配置する。
#   - 出力: VRCast/Assets/StreamingAssets/Updater/VRCastUpdater.exe
#     （ビルドの StreamingAssets に入り、VRCast が自動更新のときに LocalAppData へコピーして起動する）
# 必要なもの: Visual Studio 2022 の「C++ によるデスクトップ開発」
# 出力物はリポジトリに含めない (.gitignore 済み)。

$ErrorActionPreference = 'Stop'

# Visual Studio の C++ ツールを探す
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path $vswhere)) {
    throw 'Visual Studio not found. Install Visual Studio 2022 with "Desktop development with C++".'
}
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) {
    throw 'Visual Studio with C++ tools not found.'
}
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

# 入出力（中間ファイルは一時フォルダへ）
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot '..\..\VRCast\Assets\StreamingAssets\Updater'
$obj = Join-Path ([IO.Path]::GetTempPath()) 'VRCastUpdater-obj'
New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null
$out = (Resolve-Path $out).Path

# VC++ ランタイムが無い PC でも動くよう CRT は静的リンク。コンソールを出さない GUI アプリにし、
# 名前に "Update" を含む exe を Windows が勝手に管理者権限で起動しないよう asInvoker のマニフェストを埋め込む
$sources = (Get-ChildItem $src -Filter *.cpp | ForEach-Object { "`"$($_.FullName)`"" }) -join ' '
$cl = "cl /nologo /EHsc /O2 /MT /std:c++17 /utf-8 /W4 /permissive- /DUNICODE /D_UNICODE " +
      "/Fo`"$obj\\`" $sources /Fe`"$out\VRCastUpdater.exe`" " +
      "/link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED `"/MANIFESTUAC:level='asInvoker' uiAccess='false'`" " +
      "user32.lib shell32.lib"
cmd /c "`"$vcvars`" >nul && $cl"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed ($LASTEXITCODE)."
}

Write-Host "Built $out\VRCastUpdater.exe"
