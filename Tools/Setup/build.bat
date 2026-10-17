@echo off
rem Build the setup EXE (VRCastSetup.exe) into %TEMP%\VRCastSetup (for testing; downloads from GitHub when run).
rem The distributable dist\<version>\VRCast-setup.exe is made by Tools\Package\package.bat.
rem Needs Visual Studio 2022.

setlocal

rem Run build.ps1 next to this file without changing the system execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
set "RESULT=%ERRORLEVEL%"

echo.
if "%RESULT%"=="0" (
    echo Build succeeded.
) else (
    echo Build failed with exit code %RESULT%.
)

rem Keep the window open when started by double-click
pause
endlocal & exit /b %RESULT%
