# Publish website/*.html (+ version.json + language-packs) to:
#   1) Cloudflare Pages (advertised primary) — autocorrect-windows-site.pages.dev
#   2) GitHub Pages (fallback mirror) — AutoCorrect-Windows gh-pages
#   3) GitHub Pages backup — autocorrect-site (still github.io)
#
# Future: custom domain on Cloudflare; keep GitHub as optional mirror only.
#
# Each publish stamps a new version.json so browsers that already have the
# site-refresh script can auto-reload once without a manual hard refresh.
#
# Requires: gh auth + write access to both public site repos
#           wrangler logged in (Cloudflare) for the primary site
#
# Usage:
#   .\publish-site.ps1
#   .\publish-site.ps1 -SkipCloudflare

param(
    [switch]$SkipCloudflare
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $PSScriptRoot "GitHubAuthor.ps1")

$siteDir = Join-Path $Root "website"
if (-not (Test-Path $siteDir)) { throw "Missing $siteDir" }
$htmlFiles = @(Get-ChildItem $siteDir -Filter "*.html")
if (-not $htmlFiles) { throw "No HTML files in $siteDir" }

$PrimarySiteUrl = "https://autocorrect-windows-site.pages.dev/"
$AlternateSiteUrl = "https://badatcoding12345678910.github.io/AutoCorrect-Windows/"
$CloudflareProject = "autocorrect-windows-site"

$publishedAt = (Get-Date).ToUniversalTime()
$siteVersion = $publishedAt.ToString("yyyyMMdd-HHmmss")
$versionPath = Join-Path $siteDir "version.json"
@{
    version     = $siteVersion
    publishedAt = $publishedAt.ToString("o")
} | ConvertTo-Json | Set-Content -Path $versionPath -Encoding UTF8
Write-Host "Site version → $siteVersion"

function Publish-StaticSite([string]$Repo, [string]$Branch) {
    $tmp = Join-Path $env:TEMP ("ac-pages-" + [guid]::NewGuid().ToString("n"))
    try {
        Write-Host "Publishing $Repo ($Branch)…"
        New-Item -ItemType Directory -Path $tmp | Out-Null
        Push-Location $tmp
        git init -b $Branch | Out-Null
        foreach ($f in $htmlFiles) {
            Copy-Item $f.FullName (Join-Path $tmp $f.Name) -Force
            Write-Host "  + $($f.Name)"
        }
        Copy-Item $versionPath (Join-Path $tmp "version.json") -Force
        Write-Host "  + version.json ($siteVersion)"

        $packsSrc = Join-Path $siteDir "language-packs"
        if (Test-Path $packsSrc) {
            $packsDest = Join-Path $tmp "language-packs"
            New-Item -ItemType Directory -Path $packsDest | Out-Null
            Get-ChildItem $packsSrc -File | ForEach-Object {
                Copy-Item $_.FullName (Join-Path $packsDest $_.Name) -Force
                Write-Host "  + language-packs/$($_.Name)"
            }
        }

        New-Item -ItemType File -Path (Join-Path $tmp ".nojekyll") -Force | Out-Null
        git add .
        git -c "user.name=$AcGitHubAuthorName" -c "user.email=$AcGitHubAuthorEmail" commit -m "Update website $siteVersion"
        git remote add origin "https://github.com/$Repo.git"
        git push -f origin "HEAD:refs/heads/$Branch"
        Write-Host "  pushed (author: $AcGitHubAuthorName)"
        Pop-Location
    } finally {
        if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

function Test-WranglerLoggedIn {
    # Prefer real API token if user set one. Do NOT promote wrangler OAuth into
    # CLOUDFLARE_API_TOKEN — that causes Cloudflare API error 10000.
    if ($env:CLOUDFLARE_API_TOKEN) { return $true }
    $tomlCandidates = @(
        (Join-Path $env:APPDATA "xdg.config\.wrangler\config\default.toml"),
        (Join-Path $env:USERPROFILE ".wrangler\config\default.toml"),
        (Join-Path $env:USERPROFILE ".config\.wrangler\config\default.toml")
    )
    foreach ($toml in $tomlCandidates) {
        if (-not (Test-Path $toml)) { continue }
        $raw = Get-Content $toml -Raw
        if ($raw -match 'oauth_token\s*=\s*"[^"]+"') { return $true }
    }
    return $false
}

function Publish-CloudflarePages {
    $wrangler = Get-Command wrangler -ErrorAction SilentlyContinue
    if (-not $wrangler) {
        Write-Warning "wrangler not on PATH — skipped Lightspeed mirror. Install: npm i -g wrangler && wrangler login"
        return
    }
    if (-not (Test-WranglerLoggedIn)) {
        Write-Warning "Not logged in to Cloudflare — run 'wrangler login' or set CLOUDFLARE_API_TOKEN. Skipped Lightspeed mirror."
        return
    }
    # Clear CI so wrangler uses OAuth session instead of requiring an API token.
    Remove-Item Env:CI -ErrorAction SilentlyContinue

    # Stage a clean publish dir (omit _src, firebase tooling, node_modules).
    $stage = Join-Path $env:TEMP ("ac-cf-pages-" + [guid]::NewGuid().ToString("n"))
    try {
        Write-Host "Publishing Cloudflare Pages ($CloudflareProject)…"
        New-Item -ItemType Directory -Path $stage | Out-Null
        foreach ($f in $htmlFiles) {
            Copy-Item $f.FullName (Join-Path $stage $f.Name) -Force
        }
        Copy-Item $versionPath (Join-Path $stage "version.json") -Force
        $packsSrc = Join-Path $siteDir "language-packs"
        if (Test-Path $packsSrc) {
            $packsDest = Join-Path $stage "language-packs"
            New-Item -ItemType Directory -Path $packsDest | Out-Null
            Get-ChildItem $packsSrc -File | ForEach-Object {
                Copy-Item $_.FullName (Join-Path $packsDest $_.Name) -Force
            }
        }

        Push-Location $stage
        & wrangler pages deploy . --project-name=$CloudflareProject --commit-dirty=true
        if ($LASTEXITCODE -ne 0) {
            throw "wrangler pages deploy failed (exit $LASTEXITCODE)."
        }
        Write-Host "  Cloudflare Pages OK → $PrimarySiteUrl"
        Pop-Location
    } finally {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }
        if ((Get-Location).Path -eq $stage) { Pop-Location -ErrorAction SilentlyContinue }
    }
}

Publish-StaticSite -Repo "BADATCODING12345678910/AutoCorrect-Windows" -Branch "gh-pages"
Publish-StaticSite -Repo "BADATCODING12345678910/autocorrect-site" -Branch "main"

if (-not $SkipCloudflare) {
    Publish-CloudflarePages
} else {
    Write-Host "Skipped Cloudflare (-SkipCloudflare)."
}

Write-Host ""
Write-Host "Public site (primary):  $PrimarySiteUrl"
Write-Host "Support (primary):      ${PrimarySiteUrl}support"
Write-Host "GitHub mirror:          $AlternateSiteUrl"
Write-Host "Support (mirror):       ${AlternateSiteUrl}support.html"
Write-Host "Setup:                  https://github.com/BADATCODING12345678910/AutoCorrect-Windows/releases/download/v0.2.0/AutoCorrectSetup.exe"
Write-Host "Version:                $siteVersion (browsers with the refresh script auto-reload once)"
Write-Host "Note: Setup.exe stays on GitHub Releases for now — future: non-GitHub Setup mirror + custom domain on Cloudflare."
exit 0
