@echo off
setlocal
cd /d "%~dp0"
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=1"
set "AC_DEBUG_LAB_TOOLS=%~dp0.."
dotnet run --project "%~dp0FeatureDebugLab.Mcp.csproj" -c Release
exit /b %ERRORLEVEL%
