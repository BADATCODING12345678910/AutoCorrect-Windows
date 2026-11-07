@echo off
setlocal
cd /d "%~dp0"
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=1"

echo Building Feature Debug Lab…
dotnet build "%~dp0FeatureDebugLab\FeatureDebugLab.csproj" -c Debug --nologo
if errorlevel 1 exit /b 1
echo.

if /I "%~1"=="--offline" goto offline
if /I "%~1"=="offline" goto offline

echo Tip: live suite can auto-launch AutoCorrect.App from bin\Debug if needed.
echo Prefer Debug tray build so DebugLabInjectGate accepts lab keystrokes.
echo Reports: %%LocalAppData%%\AutoCorrect\debug-lab\
echo MCP:     FeatureDebugLab.Mcp\build-mcp.cmd then enable autocorrect-debug-lab in Cursor
echo.
echo Usage: run-feature-debug-lab.cmd           ^(GUI^)
echo        run-feature-debug-lab.cmd --offline ^(headless bug hunt^)
dotnet run --project "%~dp0FeatureDebugLab\FeatureDebugLab.csproj" -c Debug --no-build
exit /b %ERRORLEVEL%

:offline
echo Running headless offline bug hunt…
dotnet run --project "%~dp0FeatureDebugLab\FeatureDebugLab.csproj" -c Debug --no-build -- --offline
set ERR=%ERRORLEVEL%
echo.
echo Report: %LOCALAPPDATA%\AutoCorrect\debug-lab\last-failures.txt
echo Exit %ERR% ^(0=clean, 1=warn, 2=fail^)
exit /b %ERR%
