# Downloads ggml-tiny.bin for Race Ingenior Whisper STT.
# Default targets (outside bin/):
#   1) <repo>/Models/ggml-tiny.bin
#   2) %LocalAppData%\RaceIngenior\Models\ggml-tiny.bin
#
# Usage:
#   .\scripts\install-whisper-model.ps1
#   .\scripts\install-whisper-model.ps1 -Force
#   .\scripts\install-whisper-model.ps1 -LocalAppDataOnly

[CmdletBinding()]
param(
    [switch]$Force,
    [switch]$LocalAppDataOnly,
    [switch]$RepoOnly
)

$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$RelativeModel = Join-Path "Models" "ggml-tiny.bin"
$Url = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin"

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
    Write-Host "Downloading Whisper tiny model..."
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
Write-Host "Whisper model ready. App resolves Models/ggml-tiny.bin from LocalAppData or repo Models/ (not bin/)."
)
