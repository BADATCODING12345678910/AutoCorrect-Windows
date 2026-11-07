# Rebuild language-pack zips + manifest.json from website/language-packs/_src.
# Usage: .\rebuild-language-packs.ps1
#
# Output:
#   website/language-packs/<code>.zip
#   website/language-packs/manifest.json  (Cloudflare Pages primary URLs)
# Prints a summary table (code, curated, wordlist, sha, size).

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$PacksDir = Join-Path $Root "website\language-packs"
$SrcRoot = Join-Path $PacksDir "_src"
$PrimaryRoot = "https://autocorrect-windows-site.pages.dev/language-packs/"

if (-not (Test-Path $SrcRoot)) { throw "Missing $SrcRoot" }

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-Sha256Hex([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $fs = [System.IO.File]::OpenRead($Path)
        try {
            $hash = $sha.ComputeHash($fs)
            return ([BitConverter]::ToString($hash) -replace "-", "").ToLowerInvariant()
        }
        finally { $fs.Dispose() }
    }
    finally { $sha.Dispose() }
}

function Count-NonCommentLines([string]$Path) {
    if (-not (Test-Path $Path)) { return 0 }
    $n = 0
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $t = $line.Trim()
        if ($t -eq "" -or $t.StartsWith("#") -or $t.StartsWith(";")) { continue }
        $n++
    }
    return $n
}

$displayNames = @{
    "en-US" = "English (United States)"
    "en-GB" = "English (United Kingdom)"
    "es-ES" = "Spanish (Spain)"
    "es-AR" = "Spanish (Argentina)"
    "fr-FR" = "French (France)"
    "de-DE" = "German (Germany)"
    "pt-BR" = "Portuguese (Brazil)"
    "it-IT" = "Italian"
    "nl-NL" = "Dutch (Netherlands)"
    "pl-PL" = "Polish"
    "ru-RU" = "Russian"
    "ja-JP" = "Japanese"
    "zh-CN" = "Chinese (Simplified)"
    "ar"    = "Arabic"
}

$fallbacks = @{
    "es-AR" = @("es-ES")
}

$packs = New-Object System.Collections.Generic.List[object]
$packs.Add([ordered]@{
        code        = "en-US"
        name        = $displayNames["en-US"]
        packVersion = "1.0.0"
        bundled     = $true
    }) | Out-Null

$rows = @()

Get-ChildItem $SrcRoot -Directory | Sort-Object Name | ForEach-Object {
    $code = $_.Name
    $src = $_.FullName
    $curated = Join-Path $src "AutocorrectCurated.txt"
    $wordlist = Join-Path $src "wordlist.txt"
    $metaPath = Join-Path $src "pack.json"
    if (-not (Test-Path $curated)) { throw "Missing curated for $code" }
    if (-not (Test-Path $metaPath)) { throw "Missing pack.json for $code" }

    $meta = Get-Content $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $packVersion = [string]$meta.packVersion
    if ([string]::IsNullOrWhiteSpace($packVersion)) { $packVersion = "1.1.0" }
    $name = [string]$meta.name
    if ([string]::IsNullOrWhiteSpace($name) -and $displayNames.ContainsKey($code)) {
        $name = $displayNames[$code]
    }
    if ([string]::IsNullOrWhiteSpace($name)) { $name = $code }

    $zipPath = Join-Path $PacksDir ($code + ".zip")
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($fileName in @("AutocorrectCurated.txt", "wordlist.txt", "pack.json")) {
            $full = Join-Path $src $fileName
            if (-not (Test-Path $full)) { continue }
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $full, $fileName, [System.IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally { $zip.Dispose() }

    $sha = Get-Sha256Hex $zipPath
    $size = (Get-Item $zipPath).Length
    $curatedCount = Count-NonCommentLines $curated
    $wordlistCount = Count-NonCommentLines $wordlist

    $entry = [ordered]@{
        code        = $code
        name        = $name
        packVersion = $packVersion
        bundled     = $false
        url         = $PrimaryRoot + $code + ".zip"
        sha256      = $sha
        sizeBytes   = [int64]$size
    }
    if ($fallbacks.ContainsKey($code)) {
        $entry["fallbacks"] = $fallbacks[$code]
    }
    $packs.Add($entry) | Out-Null

    $rows += [pscustomobject]@{
        Code     = $code
        Curated  = $curatedCount
        Wordlist = $wordlistCount
        Version  = $packVersion
        Size     = $size
        Sha256   = $sha
    }
}

$manifest = [ordered]@{
    version = 1
    packs   = $packs
}

$manifestPath = Join-Path $PacksDir "manifest.json"
$json = $manifest | ConvertTo-Json -Depth 6
# PowerShell ConvertTo-Json can emit awkward spacing; keep UTF-8 no BOM
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($manifestPath, $json + "`n", $utf8)

Write-Host ""
Write-Host ("{0,-8} {1,8} {2,9} {3,8} {4,7} {5}" -f "Code", "Curated", "Wordlist", "Version", "Bytes", "SHA256")
Write-Host ("-" * 100)
foreach ($r in $rows) {
    Write-Host ("{0,-8} {1,8} {2,9} {3,8} {4,7} {5}" -f $r.Code, $r.Curated, $r.Wordlist, $r.Version, $r.Size, $r.Sha256)
}
Write-Host ""
Write-Host "Wrote $($rows.Count) zips + manifest → $manifestPath"
Write-Host "Primary URL root: $PrimaryRoot"
