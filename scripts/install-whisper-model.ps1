# Downloads a Whisper ggml model for Race Ingenior STT.
# Default targets (outside bin/):
#   1) <repo>/Models/ggml-<size>.bin
#   2) %LocalAppData%\RaceIngenior\Models\ggml-<size>.bin
#
# Usage:
#   .\scripts\install-whisper-model.ps1              # base (recommended)
#   .\scripts\install-whisper-model.ps1 -Size small  # better FR accuracy, slower
#   .\scripts\install-whisper-model.ps1 -Size tiny   # fastest, weakest
#   .\scripts\install-whisper-model.ps1 -Force
#   .\scripts\install-whisper-model.ps1 -LocalAppDataOnly

[CmdletBinding()]
param(
    [ValidateSet("tiny", "base", "small")]
    [string]$Size = "base",
    [switch]$Force,
    [switch]$LocalAppDataOnly,
    [switch]$RepoOnly
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$FileName = "ggml-$Size.bin"
$RelativeModel = Join-Path "Models" $FileName
$Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$FileName"

$targets = [System.Collections.Generic.List[string]]::new()
if (-not $LocalAppDataOnly) {
    $targets.Add((Join-Path $RepoRoot $RelativeModel))
}
if (-not $RepoOnly) {
    $localRoot = Join-Path $env:LOCALAPPDATA "RaceIngenior"
    $targets.Add((Join-Path $localRoot $RelativeModel))
}

if ($targets.Count -eq 0) {
    throw "No install target selected (do not combine -LocalAppDataOnly and -RepoOnly)."
}

function Install-Model([string]$Destination) {
    $dir = Split-Path -Parent $Destination
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }

    if ((Test-Path $Destination) -and -not $Force) {
        $sizeMb = [math]::Round((Get-Item $Destination).Length / 1MB, 1)
        Write-Host "Already present ($sizeMb MB): $Destination"
        return
    }

    $tmp = "$Destination.download"
    Write-Host "Downloading Whisper $Size model..."
    Write-Host "  from: $Url"
    Write-Host "  to:   $Destination"

    try {
        Invoke-WebRequest -Uri $Url -OutFile $tmp -UseBasicParsing
        Move-Item -Force -Path $tmp -Destination $Destination
    }
    finally {
        if (Test-Path $tmp) { Remove-Item -Force $tmp }
    }

    $sizeMb = [math]::Round((Get-Item $Destination).Length / 1MB, 1)
    Write-Host "Installed ($sizeMb MB): $Destination"
}

foreach ($path in $targets) {
    Install-Model $path
}

Write-Host ""
Write-Host "Whisper $Size ready. Set RaceIngenior:WhisperModelPath to Models/$FileName in appsettings.json."
Write-Host "App resolves the file from LocalAppData or repo Models/ (not bin/)."
