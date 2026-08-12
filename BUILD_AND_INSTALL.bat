@echo off
setlocal
cd /d "%~dp0"

echo Building and installing Erenshor Practice Duels...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0BUILD_AND_INSTALL.ps1"

if errorlevel 1 (
    echo.
    echo Build or installation failed. Copy the message above if you need help.
    pause
    exit /b 1
)

echo.
echo Finished. Press any key to close.
pause >nul
