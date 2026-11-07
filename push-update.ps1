# Publish win-x64, zip, host zip on GitHub Releases (Supabase Storage ~50MB cap is too small),
# then update Supabase app_update_feed + latest.json.
# Usage:
#   .\push-update.ps1 -Version 0.2.1
#   .\push-update.ps1 -Version 0.2.1 -Notes "Icon + updater"
#   .\push-update.ps1 -Version 0.2.1 -SkipPublish   # zip+upload existing artifacts\win-x64
#
# Requires: gh auth (repo write to AutoCorrect-Windows)
# Credentials (never commit): %LocalAppData%\AutoCorrect\SupabaseCredentials.ini
#   [Supabase]
#   Url=https://arsfrcafaoagrjbnjydx.supabase.co
#   UpdatePushSecret=...   (preferred — one POST to update-feed; no PostgREST)
#   ServiceRoleKey=eyJ...  (optional legacy JWT; sb_secret_ cannot hit PostgREST)

param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Notes = "",
    # Leave empty so installed users almost never hit RequiresFullSetup.
    [string]$MinVersion = "",
    [switch]$SkipPublish,
    [switch]$SkipUpload
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must look like 0.2.1 (got '$Version')"
}

function Set-CsprojVersion([string]$csproj, [string]$ver) {
    $xml = Get-Content -Raw $csproj
    foreach ($tag in @('Version', 'InformationalVersion')) {
        if ($xml -match "<$tag>[^<]*</$tag>") {
            $xml = [regex]::Replace($xml, "<$tag>[^<]*</$tag>", "<$tag>$ver</$tag>")
        }
        else {
            $xml = $xml -replace '(<PropertyGroup>\s*)', "`$1`r`n    <$tag>$ver</$tag>"
        }
    }
    foreach ($tag in @('AssemblyVersion', 'FileVersion')) {
        $full = "$ver.0"
        if ($xml -match "<$tag>[^<]*</$tag>") {
            $xml = [regex]::Replace($xml, "<$tag>[^<]*</$tag>", "<$tag>$full</$tag>")
        }
    }
    Set-Content -Path $csproj -Value $xml -Encoding UTF8 -NoNewline
}

function Set-IssVersion([string]$iss, [string]$ver) {
    $text = Get-Content -Raw $iss
    $text = [regex]::Replace($text, '#define MyAppVersion "[^"]*"', "#define MyAppVersion `"$ver`"")
    Set-Content -Path $iss -Value $text -Encoding UTF8 -NoNewline
}

function Read-IniValue([string]$path, [string]$section, [string]$key) {
    if (-not (Test-Path $path)) { return $null }
    $inSection = $false
    foreach ($line in Get-Content $path) {
        $t = $line.Trim()
        if ($t -match '^\[(.+)\]$') {
            $inSection = ($Matches[1] -eq $section)
            continue
        }
        if (-not $inSection -or $t.StartsWith(';') -or $t -eq '') { continue }
        if ($t -match "^\s*$([regex]::Escape($key))\s*=\s*(.*)$") {
            return $Matches[1].Trim()
        }
    }
    return $null
}

function Upload-StorageObject(
    [string]$baseUrl,
    [string]$serviceKey,
    [string]$bucket,
    [string]$objectPath,
    [string]$filePath,
    [string]$contentType
) {
    $url = "$baseUrl/storage/v1/object/$bucket/$objectPath"
    $bytes = [System.IO.File]::ReadAllBytes($filePath)
    $headers = @{
        apikey         = $serviceKey
        Authorization  = "Bearer $serviceKey"
        "x-upsert"     = "true"
        "Content-Type" = $contentType
    }
    Invoke-RestMethod -Method Post -Uri $url -Headers $headers -Body $bytes | Out-Null
}

$csproj = Join-Path $Root "src\AutoCorrect.App\AutoCorrect.App.csproj"
$iss = Join-Path $Root "installer\AutoCorrect.iss"
Write-Host "Bumping version → $Version"
Set-CsprojVersion $csproj $Version
Set-IssVersion $iss $Version

if (-not $SkipPublish) {
    Write-Host "Publishing win-x64…"
    & cmd /c "publish-win.cmd"
    if ($LASTEXITCODE -ne 0) { throw "publish-win.cmd failed ($LASTEXITCODE)" }
}

$publishDir = Join-Path $Root "artifacts\win-x64"
if (-not (Test-Path (Join-Path $publishDir "AutoCorrect.App.exe"))) {
    throw "Missing $publishDir\AutoCorrect.App.exe — run publish-win.cmd first."
}

$artifacts = Join-Path $Root "artifacts"
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$zipName = "AutoCorrect-$Version-win-x64.zip"
$zipPath = Join-Path $artifacts $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "Zipping $publishDir → $zipName"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$sha = (Get-FileHash -Algorithm SHA256 -Path $zipPath).Hash.ToLowerInvariant()
$sizeBytes = (Get-Item $zipPath).Length
$projectUrl = "https://arsfrcafaoagrjbnjydx.supabase.co"
$ghRepo = "BADATCODING12345678910/AutoCorrect-Windows"
$tag = "v$Version"
$publicZipUrl = "https://github.com/$ghRepo/releases/download/$tag/$zipName"
$feedUrl = "$projectUrl/storage/v1/object/public/app-updates/latest.json"
$edgeFeed = "$projectUrl/functions/v1/update-feed"

$manifest = [ordered]@{
    version     = $Version
    url         = $publicZipUrl
    sha256      = $sha
    sizeBytes   = $sizeBytes
    releasedAt  = (Get-Date).ToUniversalTime().ToString("o")
    notes       = $Notes
}
if (-not [string]::IsNullOrWhiteSpace($MinVersion)) {
    $manifest.minVersion = $MinVersion
}
$latestPath = Join-Path $artifacts "latest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $latestPath -Encoding UTF8
Write-Host "Wrote $latestPath"
Write-Host "SHA256 $sha"

if ($SkipUpload) {
    Write-Host "SkipUpload set — local artifacts only."
    Write-Host "Feed would be: $edgeFeed"
    Write-Host "Zip would be:  $publicZipUrl"
    exit 0
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "gh CLI required to host update zips on GitHub Releases (Supabase Storage max file size is too small)."
}

Write-Host "Publishing zip to GitHub Releases $ghRepo $tag …"
$prevError = $ErrorActionPreference
$ErrorActionPreference = "Continue"
gh release view $tag -R $ghRepo 2>$null | Out-Null
$viewExit = $LASTEXITCODE
$ErrorActionPreference = $prevError
if ($viewExit -ne 0) {
    gh release create $tag -R $ghRepo --title "AutoCorrect $Version" --notes $Notes --latest
    if ($LASTEXITCODE -ne 0) { throw "gh release create failed ($LASTEXITCODE)" }
}
gh release upload $tag $zipPath -R $ghRepo --clobber
if ($LASTEXITCODE -ne 0) { throw "gh release upload failed ($LASTEXITCODE)" }

$credPath = Join-Path $env:LOCALAPPDATA "AutoCorrect\SupabaseCredentials.ini"
$serviceKey = Read-IniValue $credPath "Supabase" "ServiceRoleKey"
$pushSecret = Read-IniValue $credPath "Supabase" "UpdatePushSecret"
$urlOverride = Read-IniValue $credPath "Supabase" "Url"
if ($urlOverride) { $projectUrl = $urlOverride.TrimEnd('/') }
$edgeFeed = "$projectUrl/functions/v1/update-feed"

$feedUpdated = $false

# Preferred: one POST to Edge update-feed (works with UpdatePushSecret; no PostgREST/JWT).
if ($pushSecret) {
    try {
        Write-Host "Updating feed via update-feed POST…"
        $body = @{
            version    = $Version
            minVersion = $MinVersion
            url        = $publicZipUrl
            sha256     = $sha
            notes      = $Notes
            secret     = $pushSecret
        } | ConvertTo-Json
        $resp = Invoke-RestMethod -Method Post -Uri $edgeFeed -ContentType "application/json" -Body $body
        if ($resp.version -eq $Version) {
            $feedUpdated = $true
            Write-Host "Feed POST OK → $($resp.version)"
        }
        else {
            Write-Warning "Feed POST returned unexpected version: $($resp.version)"
        }
    } catch {
        Write-Warning "Feed POST failed: $($_.Exception.Message)"
    }
}

# Fallback: PostgREST upsert (legacy JWT service_role only — sb_secret_ is rejected).
if (-not $feedUpdated -and $serviceKey -and $serviceKey -notlike "sb_secret_*") {
    try {
        Write-Host "Updating Storage latest.json + app_update_feed via PostgREST…"
        Upload-StorageObject $projectUrl $serviceKey "app-updates" "latest.json" $latestPath "application/json"
        $feedRow = @{
            id           = 1
            version      = $Version
            min_version  = $MinVersion
            url          = $publicZipUrl
            sha256       = $sha
            notes        = $Notes
            released_at  = (Get-Date).ToUniversalTime().ToString("o")
        } | ConvertTo-Json
        $pgHeaders = @{
            apikey          = $serviceKey
            Authorization   = "Bearer $serviceKey"
            "Content-Type"  = "application/json"
            Prefer          = "resolution=merge-duplicates"
            "User-Agent"    = "AutoCorrect-push-update/1.0"
        }
        Invoke-RestMethod -Method Post -Uri "$projectUrl/rest/v1/app_update_feed" -Headers $pgHeaders -Body $feedRow | Out-Null
        $feedUpdated = $true
    } catch {
        Write-Warning "REST feed update failed: $($_.Exception.Message)"
    }
} elseif (-not $feedUpdated -and $serviceKey -like "sb_secret_*") {
    Write-Warning "ServiceRoleKey is sb_secret_ (PostgREST blocked). Add UpdatePushSecret to SupabaseCredentials.ini."
}

if (-not $feedUpdated) {
    Write-Host ""
    Write-Host "Zip is on GitHub, but the live feed was NOT updated."
    Write-Host "Fix: set UpdatePushSecret in %LocalAppData%\AutoCorrect\SupabaseCredentials.ini"
    Write-Host "     (same value as edge_secrets.UPDATE_PUSH_SECRET), then re-run with -SkipPublish."
    Write-Host "Emergency SQL:"
    Write-Host @"
update app_update_feed set
  version = '$Version',
  min_version = '$MinVersion',
  url = '$publicZipUrl',
  sha256 = '$sha',
  notes = '$($Notes.Replace("'","''"))',
  released_at = now()
where id = 1;
"@
}

try {
    $warm = Invoke-RestMethod -Method Get -Uri $edgeFeed
    Write-Host "Live feed version: $($warm.version)"
    if ($feedUpdated -and $warm.version -ne $Version) {
        Write-Warning "Feed write claimed OK but GET still shows $($warm.version)"
    }
} catch {
    Write-Warning "update-feed read failed: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "Pushed $Version"
Write-Host "Feed (app): $edgeFeed"
Write-Host "Zip:        $publicZipUrl"
Write-Host "Users pick this up via tray Check for updates / startup check."
exit 0
