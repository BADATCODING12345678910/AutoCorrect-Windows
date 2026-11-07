@echo off
setlocal
cd /d "%~dp0"
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=1"
echo Building FeatureDebugLab (offline runner)…
dotnet build "%~dp0..\FeatureDebugLab\FeatureDebugLab.csproj" -c Debug --nologo
if errorlevel 1 exit /b 1
echo Building FeatureDebugLab.Mcp…
dotnet build "%~dp0FeatureDebugLab.Mcp.csproj" -c Release --nologo
if errorlevel 1 exit /b 1
echo OK. Restart Cursor MCP / reload tools to pick up autocorrect-debug-lab.
exit /b 0
