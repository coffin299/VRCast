@echo off
rem Build vrcast_tracker.exe and place it into VRCast\Assets\StreamingAssets\MediaPipeTracker.
rem Usage: build.bat [PythonVersion]   (default: 3.12, requires the "py" launcher from python.org)

setlocal

rem Do not write __pycache__ / .pyc files
set PYTHONDONTWRITEBYTECODE=1

rem Optional Python version argument
set "PYVER=%~1"
if "%PYVER%"=="" set "PYVER=3.12"

rem Run build.ps1 next to this file without changing the system execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -PythonVersion %PYVER%
set "RESULT=%ERRORLEVEL%"

if not "%RESULT%"=="0" (
    echo.
    echo Build failed with exit code %RESULT%.
) else (
    echo.
    echo Build succeeded.
)

rem Keep the window open when started by double-click
pause
endlocal & exit /b %RESULT%
