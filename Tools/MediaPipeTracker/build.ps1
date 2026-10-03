<#
.SYNOPSIS
    Build vrcast_tracker.exe and place it with the models into VRCast/Assets/StreamingAssets/MediaPipeTracker.
.PARAMETER PythonVersion
    Python version passed to the py launcher (MediaPipe supports 3.9 - 3.12).
#>
param(
    [string]$PythonVersion = "3.12"
)

$ErrorActionPreference = "Stop"

# __pycache__ / .pyc を作らない
$env:PYTHONDONTWRITEBYTECODE = "1"

# 入力（このフォルダ）と配置先（StreamingAssets）
$source = $PSScriptRoot
$repository = (Resolve-Path (Join-Path $source "..\..")).Path
$output = Join-Path $repository "VRCast\Assets\StreamingAssets\MediaPipeTracker"

# 仮想環境と PyInstaller の中間ファイルはリポジトリの外に置く
$work = Join-Path $env:LOCALAPPDATA "VRCast\tracker-build"
$venv = Join-Path $work "venv"
$python = Join-Path $venv "Scripts\python.exe"

# 仮想環境が無ければ作る
if (-not (Test-Path $python)) {
    New-Item -ItemType Directory -Force $work | Out-Null
    py "-$PythonVersion" -m venv $venv
    if ($LASTEXITCODE -ne 0) { throw "Failed to create venv with Python $PythonVersion" }
}

# 依存パッケージを入れる（.pyc を作らない）
& $python -m pip install --disable-pip-version-check --no-compile -r (Join-Path $source "requirements.txt")
if ($LASTEXITCODE -ne 0) { throw "pip install failed" }

# フォルダ形式で exe 化（MediaPipe のデータ・DLL を同梱、標準出力を VRCast が読むためコンソール版）
& $python -m PyInstaller --noconfirm --clean --onedir --console --name vrcast_tracker `
    --collect-all mediapipe `
    --distpath (Join-Path $work "dist") --workpath (Join-Path $work "build") --specpath $work `
    (Join-Path $source "vrcast_tracker.py")
if ($LASTEXITCODE -ne 0) { throw "PyInstaller failed" }

# 古い配置を消してから配置し直す
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
Copy-Item (Join-Path $work "dist\vrcast_tracker") $output -Recurse

# モデルをダウンロードして models フォルダへ
$models = Join-Path $output "models"
New-Item -ItemType Directory -Force $models | Out-Null
$base = "https://storage.googleapis.com/mediapipe-models"
$downloads = [ordered]@{
    "face_landmarker.task"      = "$base/face_landmarker/face_landmarker/float16/latest/face_landmarker.task"
    "hand_landmarker.task"      = "$base/hand_landmarker/hand_landmarker/float16/latest/hand_landmarker.task"
    "pose_landmarker_lite.task" = "$base/pose_landmarker/pose_landmarker_lite/float16/latest/pose_landmarker_lite.task"
}
foreach ($name in $downloads.Keys) {
    Write-Host "Downloading $name"
    Invoke-WebRequest -Uri $downloads[$name] -OutFile (Join-Path $models $name)
}

Write-Host "Done: $output"
