@echo off
rem Build vrcast_tracker.exe and place it into VRCast\Assets\StreamingAssets\MediaPipeTracker.
rem Uses Python 3.12.x through the "py" launcher (install Python 3.12 from python.org).
rem The virtual environment is created in .venv next to this file.

setlocal

rem Python version used for .venv (any 3.12.x patch release)
set "PYVER=3.12"

rem Do not write __pycache__ / .pyc files
set PYTHONDONTWRITEBYTECODE=1

rem Make sure the py launcher and Python 3.12 are installed
where py >nul 2>nul
if errorlevel 1 (
    echo The "py" launcher was not found. Install Python %PYVER%.x from https://www.python.org/
    set "RESULT=1"
    goto :finish
)
py -%PYVER% --version >nul 2>nul
if errorlevel 1 (
    echo Python %PYVER%.x was not found. Install Python %PYVER%.x from https://www.python.org/
    set "RESULT=1"
    goto :finish
)
for /f "delims=" %%v in ('py -%PYVER% --version') do echo Using %%v

rem Run build.ps1 next to this file without changing the system execution policy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -PythonVersion %PYVER%
set "RESULT=%ERRORLEVEL%"

:finish
echo.
if "%RESULT%"=="0" (
    echo Build succeeded.
) else (
    echo Build failed with exit code %RESULT%.
)

rem Keep the window open when started by double-click
pause
endlocal & exit /b %RESULT%
