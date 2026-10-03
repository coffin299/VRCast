@echo off
rem Pack the exporter (Packages\com.vrcast.converter) into dist\VRCast-Converter-<version>.unitypackage.
rem Unity is not required.

setlocal

rem Run unitypackage.ps1 next to this file without changing the system execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0unitypackage.ps1" %*
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
    echo Packing succeeded.
) else (
    echo Packing failed with exit code %RESULT%.
)

rem Keep the window open when started by double-click
pause
endlocal & exit /b %RESULT%
