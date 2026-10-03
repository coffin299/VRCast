@echo off
rem Package VRCast\Builds\Windows into dist\VRCast-<version>-win64.zip for distribution.
rem Build the app in Unity first (VRCast > Build > Windows x64).

setlocal

rem Run package.ps1 next to this file without changing the system execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1" %*
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
    echo Packaging succeeded.
) else (
    echo Packaging failed with exit code %RESULT%.
)

rem Keep the window open when started by double-click
pause
endlocal & exit /b %RESULT%
