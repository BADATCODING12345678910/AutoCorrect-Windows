@echo off
setlocal
rem Self-contained Windows x64 publish for Inno Setup / zip / in-app updates.
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_ROOT=%ProgramFiles%\dotnet"
set "DOTNET_MULTILEVEL_LOOKUP=1"

cd /d "%~dp0"

echo Stopping running AutoCorrect if any...
taskkill /IM AutoCorrect.App.exe /F >nul 2>&1

set "OUT=%~dp0artifacts\win-x64"
if exist "%OUT%" rmdir /s /q "%OUT%"
mkdir "%OUT%" 2>nul

dotnet publish "src\AutoCorrect.App\AutoCorrect.App.csproj" ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=false ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "%OUT%"
if errorlevel 1 exit /b 1

rem Ship AHK Data/ + Core/ next to the exe (required for curated/import/wordlist).
set "REPO=%~dp0.."
mkdir "%OUT%\Data" 2>nul
mkdir "%OUT%\Data\shipped" 2>nul
mkdir "%OUT%\Core" 2>nul

copy /Y "%REPO%\Data\AutocorrectCurated.txt" "%OUT%\Data\" >nul
copy /Y "%REPO%\Data\AutocorrectImported.json" "%OUT%\Data\" >nul
copy /Y "%REPO%\Data\unigram_freq_list_filtered_89k.csv" "%OUT%\Data\" >nul
copy /Y "%REPO%\Data\DictationLocalRules.txt" "%OUT%\Data\" >nul
if exist "%REPO%\Data\PersonalDictionary.txt" copy /Y "%REPO%\Data\PersonalDictionary.txt" "%OUT%\Data\" >nul

rem Mirror shipped defaults for post-update EnsureShippedDataPresent refresh.
copy /Y "%OUT%\Data\AutocorrectCurated.txt" "%OUT%\Data\shipped\" >nul
copy /Y "%OUT%\Data\AutocorrectImported.json" "%OUT%\Data\shipped\" >nul
copy /Y "%OUT%\Data\unigram_freq_list_filtered_89k.csv" "%OUT%\Data\shipped\" >nul
copy /Y "%OUT%\Data\DictationLocalRules.txt" "%OUT%\Data\shipped\" >nul

copy /Y "%REPO%\Core\AutoCorrectHotstrings.ahk" "%OUT%\Core\" >nul
if not exist "%OUT%\Data\AutocorrectCurated.txt" (
  echo ERROR: failed to copy Data assets from "%REPO%\Data"
  exit /b 1
)
if not exist "%OUT%\Core\AutoCorrectHotstrings.ahk" (
  echo ERROR: failed to copy Core\AutoCorrectHotstrings.ahk
  exit /b 1
)
if not exist "%OUT%\Data\shipped\AutocorrectCurated.txt" (
  echo ERROR: failed to seed Data\shipped
  exit /b 1
)

echo.
echo Published to: %OUT%
echo Included Data\ + Data\shipped\ + Core\AutoCorrectHotstrings.ahk
echo Next: push-update.cmd for installed users, or Inno Setup for new machines.
exit /b 0
