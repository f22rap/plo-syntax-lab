@echo off
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Setup-Codex.ps1"
set "PLO_SETUP_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %PLO_SETUP_EXIT%
