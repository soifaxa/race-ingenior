# Pack Race Ingenior with Velopack (Setup.exe + delta packages for GitHub Releases).
# Prerequisites: .NET 8 SDK, `dotnet tool install -g vpk` (same major as Velopack NuGet).
#
# Usage:
#   .\scripts\pack-release.ps1
#   .\scripts\pack-release.ps1 -Version 0.1.0
#   .\scripts\pack-release.ps1 -Upload   # needs GITHUB_TOKEN or gh auth
#
param(
    [string]$Version = "",
    [switch]$Upload,
    [switch]$Clean,
    [string]$RepoUrl = "https://github.com/soifaxa/race-ingenior",
    [string]$PackId = "RaceIngenior"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$csproj = Join-Path $root "src\RaceIngenior\RaceIngenior.csproj"
if (-not $Version) {
    [xml]$proj = Get-Content $csproj
    $Version = $proj.Project.PropertyGroup.InformationalVersion |
        Where-Object { $_ -and $_ -notmatch '-' } |
        Select-Object -First 1
    if (-not $Version) {
        $raw = ($proj.Project.PropertyGroup.Version | Select-Object -First 1)
        # 0.1.0.0 -> 0.1.0
        $Version = ($raw -split '\.')[0..2] -join '.'
    }
}

$publishDir = Join-Path $root "artifacts\publish"
$releasesDir = Join-Path $root "artifacts\releases"
Stop-Process -Name RaceIngenior -ErrorAction SilentlyContinue

Write-Host "Publishing $Version (self-contained win-x64)..." -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir `
    -p:Version=$Version `
    -p:InformationalVersion=$Version
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Packing Velopack release..." -ForegroundColor Cyan
# Always start from a fresh local releases folder, then pull remote history for deltas.
if (Test-Path $releasesDir) {
    Remove-Item $releasesDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $releasesDir | Out-Null

# Pull previous RELEASES so deltas work when publishing updates.
$ErrorActionPreference = "Continue"
vpk download github --repoUrl $RepoUrl --outputDir $releasesDir 2>&1 | Out-Null
$downloadOk = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = "Stop"
if (-not $downloadOk) {
    Write-Host "No existing GitHub releases yet (first pack) - continuing." -ForegroundColor DarkYellow
} elseif ($Clean) {
    Write-Host "Note: -Clean cleared local artifacts; remote packages were re-downloaded for deltas." -ForegroundColor DarkYellow
}

vpk pack `
    --packId $PackId `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe RaceIngenior.exe `
    --outputDir $releasesDir `
    --runtime win-x64 `
    --packTitle "Race Ingenior"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Artifacts in $releasesDir" -ForegroundColor Green
Get-ChildItem $releasesDir | Select-Object Name, Length | Format-Table -AutoSize
$setup = Join-Path $releasesDir ($PackId + "-win-Setup.exe")
Write-Host "Installer: $setup" -ForegroundColor Green

if ($Upload) {
    Write-Host "Uploading to GitHub Releases..." -ForegroundColor Cyan
    $token = $env:GITHUB_TOKEN
    if (-not $token) {
        throw "Set GITHUB_TOKEN (repo scope) or run: `$env:GITHUB_TOKEN = (gh auth token)"
    }
    vpk upload github `
        --repoUrl $RepoUrl `
        --token $token `
        --outputDir $releasesDir `
        --publish `
        --releaseName ("Race Ingenior " + $Version) `
        --tag ("v" + $Version)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host ("Published https://github.com/soifaxa/race-ingenior/releases/tag/v" + $Version) -ForegroundColor Green
}
