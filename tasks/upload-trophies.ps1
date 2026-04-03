<#
.SYNOPSIS
    Uploads PS3 trophy ZIP files to GitHub Releases on The-Miso-PlayStation-Database repo.

.DESCRIPTION
    For each PS3_NPWR*_00.zip file in the source folder:
    1. Extracts the NPWR ID from the filename
    2. Creates a GitHub Release with the NPWR ID as the tag
    3. Uploads the ZIP as a release asset
    Skips files that already have a release.

.PARAMETER SourceFolder
    Folder containing PS3_NPWR*_00.zip files. Default: D:\Workbench\PlayStation Trophies\Upload

.PARAMETER Repo
    GitHub repo in owner/name format. Default: Miso-Solutions/The-Miso-PlayStation-Database

.EXAMPLE
    .\upload-trophies.ps1
    .\upload-trophies.ps1 -SourceFolder "C:\MyZips"
#>

param(
    [string]$SourceFolder = "D:\Workbench\PlayStation Trophies\Upload",
    [string]$Repo = "Miso-Solutions/The-Miso-PlayStation-Database"
)

# Verify gh CLI is available
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    Write-Error "GitHub CLI (gh) is required. Install from https://cli.github.com"
    exit 1
}

# Verify authenticated
$authStatus = gh auth status 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Error "Not authenticated. Run 'gh auth login' first."
    exit 1
}

# Get all ZIP files
$zips = Get-ChildItem -Path $SourceFolder -Filter "PS3_NPWR*_00.zip" | Sort-Object Name
if ($zips.Count -eq 0) {
    Write-Warning "No PS3_NPWR*_00.zip files found in $SourceFolder"
    exit 0
}

Write-Host "Found $($zips.Count) ZIP files to upload" -ForegroundColor Cyan
Write-Host "Repo: $Repo" -ForegroundColor Cyan
Write-Host ""

# Get existing releases to skip duplicates
Write-Host "Fetching existing releases..." -ForegroundColor Yellow
$existing = @{}
try {
    $releases = gh release list --repo $Repo --limit 5000 2>&1
    foreach ($line in $releases) {
        if ($line -match "^(\S+)") {
            $existing[$Matches[1]] = $true
        }
    }
    Write-Host "Found $($existing.Count) existing releases" -ForegroundColor Yellow
} catch {
    Write-Host "Could not fetch releases (new repo?). Proceeding..." -ForegroundColor Yellow
}

# Load catalog for game name lookup
$catalogPath = Join-Path $PSScriptRoot "..\src\Trophic\Data\ps3_catalog.json"
$catalog = @{}
if (Test-Path $catalogPath) {
    $entries = Get-Content $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    foreach ($entry in $entries) {
        $catalog[$entry.id] = $entry.name
    }
    Write-Host "Loaded $($catalog.Count) game names from catalog" -ForegroundColor Yellow
}

$uploaded = 0
$skipped = 0
$failed = 0

foreach ($zip in $zips) {
    # Extract NPWR ID: PS3_NPWR04936_00.zip → NPWR04936_00
    if ($zip.Name -match "PS3_(NPWR\d+_\d+)\.zip") {
        $npwrId = $Matches[1]
    } else {
        Write-Host "  SKIP: $($zip.Name) (unexpected filename pattern)" -ForegroundColor DarkYellow
        $skipped++
        continue
    }

    # Skip if release already exists
    if ($existing.ContainsKey($npwrId)) {
        Write-Host "  SKIP: $npwrId (release exists)" -ForegroundColor DarkGray
        $skipped++
        continue
    }

    # Look up game name
    $gameName = if ($catalog.ContainsKey("${npwrId}")) { $catalog["${npwrId}"] } else { $npwrId }
    $notes = "PS3 Trophy Set for the game: $gameName"

    # Create release and upload
    Write-Host "  UPLOAD: $npwrId - $gameName ($([Math]::Round($zip.Length / 1MB, 1)) MB)..." -ForegroundColor White -NoNewline
    try {
        gh release create $npwrId $zip.FullName --repo $Repo --title $npwrId --notes $notes 2>&1 | Out-Null
        if ($LASTEXITCODE -eq 0) {
            Write-Host " OK" -ForegroundColor Green
            $uploaded++
        } else {
            Write-Host " FAILED" -ForegroundColor Red
            $failed++
        }
    } catch {
        Write-Host " ERROR: $_" -ForegroundColor Red
        $failed++
    }

    # Small delay to avoid GitHub rate limiting
    Start-Sleep -Milliseconds 500
}

Write-Host ""
Write-Host "=== DONE ===" -ForegroundColor Cyan
Write-Host "  Uploaded: $uploaded" -ForegroundColor Green
Write-Host "  Skipped:  $skipped" -ForegroundColor Yellow
Write-Host "  Failed:   $failed" -ForegroundColor $(if ($failed -gt 0) { "Red" } else { "Green" })
