# VRCast の Media Foundation 仮想カメラ（Windows 11 以降）の DLL をビルドして VRCast プロジェクトへ配置する。
#   - 出力: VRCast/Assets/Plugins/VRCastVirtualCamera/x86_64/VRCastVirtualCamera.dll
#     （VRCast 本体が P/Invoke で呼び、ドライバー登録時は Program Files へコピーして Frame Server に読ませる）
# 必要なもの: Visual Studio 2022 の「C++ によるデスクトップ開発」（Windows SDK 10.0.22000 以降）
# 出力物はリポジトリに含めない (.gitignore 済み)。Unity エディターが DLL を読み込み中だと上書きできないため、エディターを閉じてから実行する。

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
$out = Join-Path $PSScriptRoot '..\..\VRCast\Assets\Plugins\VRCastVirtualCamera\x86_64'
$obj = Join-Path ([IO.Path]::GetTempPath()) 'VRCastVirtualCamera-obj'
New-Item -ItemType Directory -Force -Path $out, $obj | Out-Null
$out = (Resolve-Path $out).Path

# Frame Server（サービス）に VC++ ランタイムが無くても読めるよう CRT は静的リンク
$sources = (Get-ChildItem $src -Filter *.cpp | ForEach-Object { "`"$($_.FullName)`"" }) -join ' '
$cl = "cl /nologo /LD /EHsc /O2 /MT /std:c++17 /utf-8 /W4 /wd4324 /permissive- /DUNICODE /D_UNICODE " +
      "/Fo`"$obj\\`" $sources /Fe`"$out\VRCastVirtualCamera.dll`" " +
      "/link /DEF:`"$src\VRCastVirtualCamera.def`" /IMPLIB:`"$obj\VRCastVirtualCamera.lib`" " +
      "mfplat.lib mfuuid.lib mf.lib ole32.lib advapi32.lib runtimeobject.lib"
cmd /c "`"$vcvars`" >nul && $cl"
if ($LASTEXITCODE -ne 0) {
    throw "Build failed ($LASTEXITCODE)."
}

Write-Host "Built $out\VRCastVirtualCamera.dll"
