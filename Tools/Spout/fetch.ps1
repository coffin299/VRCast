# KlakSpout (Unlicense) の Spout2 送信用ネイティブプラグインを取得して VRCast プロジェクトへ配置する。
#   - 送信プラグイン (64 bit): Assets/Plugins/KlakSpout/x86_64/KlakSpout.dll
# KlakSpout.dll は Spout SDK (BSD 2-Clause) を含む。取得物はリポジトリに含めない (.gitignore 済み)。

$ErrorActionPreference = 'Stop'

# 取得元 (再現性のためコミットを固定)
$commit = '2a1186748dcaeff1801fed9f4432c3907b811c50'
$baseUrl = "https://raw.githubusercontent.com/keijiro/KlakSpout/$commit/Packages/jp.keijiro.klak.spout"

# 配置先
$folder = Join-Path $PSScriptRoot '..\..\VRCast\Assets\Plugins\KlakSpout\x86_64'
New-Item -ItemType Directory -Force -Path $folder | Out-Null

# プラグイン本体と KlakSpout のライセンス
Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/Plugin/KlakSpout.dll" -OutFile (Join-Path $folder 'KlakSpout.dll')
Write-Host 'Downloaded Plugins/KlakSpout/x86_64/KlakSpout.dll'
Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/LICENSE" -OutFile (Join-Path $folder '..\LICENSE.txt')
Write-Host 'Done.'
