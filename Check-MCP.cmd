@echo off
where node >nul 2>nul
if errorlevel 1 (
  echo Node.js 20 or newer is required for generated research through MCP.
  echo See README-FIRST.txt. The desktop application can run without Node.js.
  pause
  exit /b 1
)
if not exist "%~dp0mcp.local.json" (
  echo Configure your CSV folder first. See README-FIRST.txt, step 3.
  pause
  exit /b 1
)
node "%~dp0mcp\check-connection.mjs" "%~dp0mcp.local.json"
set "PLO_CHECK_EXIT=%ERRORLEVEL%"
echo.
pause
exit /b %PLO_CHECK_EXIT%
