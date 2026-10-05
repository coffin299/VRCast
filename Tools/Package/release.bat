@echo off
rem Build the release files in one go (the Unity app build is done manually beforehand):
rem   1. MediaPipe tracker  2. copy it into VRCast\Builds\Windows  3. exporter unitypackage  4. distribution zip
rem Output goes to dist\. Options are passed to release.ps1 (for example: release.bat -SkipTracker).

setlocal

rem Run release.ps1 next to this file without changing the system execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
    echo Release build succeeded.
) else (
    echo Release build failed with exit code %RESULT%.
)

rem Keep the window open when started by double-click
pause
endlocal & exit /b %RESULT%
