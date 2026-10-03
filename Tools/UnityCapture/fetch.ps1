# UnityCapture (zlib License) の仮想カメラ用 DLL を取得して VRCast プロジェクトへ配置する。
#   - ドライバー (DirectShow フィルター 32 / 64 bit): Assets/StreamingAssets/UnityCapture/
#   - 送信プラグイン (64 bit):                       Assets/Plugins/UnityCapture/x86_64/
# 取得物はリポジトリに含めない (.gitignore 済み)。

$ErrorActionPreference = 'Stop'

# 取得元 (再現性のためコミットを固定)
$commit = '3ed54c325e0ad71afcf4f246c07e5e17b3d7f2d2'
$baseUrl = "https://raw.githubusercontent.com/schellingb/UnityCapture/$commit"

# 配置先の Assets フォルダ
$assets = Join-Path $PSScriptRoot '..\..\VRCast\Assets'

# 取得元パスと配置先パスの組
$files = @(
    @{ Source = 'Install/UnityCaptureFilter64.dll'; Target = 'StreamingAssets/UnityCapture/UnityCaptureFilter64.dll' },
    @{ Source = 'Install/UnityCaptureFilter32.dll'; Target = 'StreamingAssets/UnityCapture/UnityCaptureFilter32.dll' },
    @{ Source = 'UnityCaptureSample/Assets/UnityCapture/Plugins/x86_64/UnityCapturePlugin.dll';
       Target = 'Plugins/UnityCapture/x86_64/UnityCapturePlugin.dll' }
)

foreach ($file in $files) {
    # 配置先フォルダを作ってからダウンロード
    $target = Join-Path $assets $file.Target
    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/$($file.Source)" -OutFile $target
    Write-Host "Downloaded $($file.Target)"
}

# ドライバーと一緒に配布されるライセンス表記
$license = @'
Unity Capture
Copyright (c) 2018 Bernhard Schelling
https://github.com/schellingb/UnityCapture

Based on UnityCam
https://github.com/mrayy/UnityCam
Copyright (c) 2016 MHD Yamen Saraiji

This software is provided 'as-is', without any express or implied
warranty. In no event will the authors be held liable for any damages
arising from the use of this software.

Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:

1. The origin of this software must not be misrepresented; you must not
   claim that you wrote the original software. If you use this software
   in a product, an acknowledgment in the product documentation would be
   appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
   misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
'@
Set-Content -Path (Join-Path $assets 'StreamingAssets/UnityCapture/LICENSE.txt') -Value $license -Encoding UTF8
Write-Host 'Done.'
