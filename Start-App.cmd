@echo off
if not exist "%~dp0dist\FlopCommandApp.exe" (
  echo Application missing. Extract the entire ZIP before starting.
  pause
  exit /b 1
)
start "" "%~dp0dist\FlopCommandApp.exe"
