@echo off
setlocal
cd /d "%~dp0"
if "%~1"=="" (
  echo Usage: push-update.cmd 0.2.1 ["release notes"]
  exit /b 1
)
set "NOTES=%~2"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0push-update.ps1" -Version "%~1" -Notes "%NOTES%"
exit /b %ERRORLEVEL%
