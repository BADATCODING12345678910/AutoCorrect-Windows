@echo off
rem Launch framework-dependent Debug build without Scoop DOTNET_ROOT breaking shared runtimes.
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=1"
cd /d "%~dp0"
dotnet run --project "%~dp0src\AutoCorrect.App\AutoCorrect.App.csproj" -c Debug %*
