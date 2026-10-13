---
name: build-release
description: >-
  Builds, versions, and documents Race Ingenior releases. Use when bumping
  version, updating CHANGELOG, producing Debug/Release exe, or fixing locked-exe
  build failures.
---

# Build / Release Agent

## Build

```powershell
Stop-Process -Name RaceIngenior -ErrorAction SilentlyContinue
dotnet build src/RaceIngenior/RaceIngenior.csproj -c Debug
# or -c Release
```

Exe paths:

- Debug: `src/RaceIngenior/bin/Debug/net8.0-windows10.0.19041.0/RaceIngenior.exe`
- Release: `src/RaceIngenior/bin/Release/net8.0-windows10.0.19041.0/RaceIngenior.exe`

Keep the exe next to its output folder (deps, `appsettings.json`). Gemini key is set in-app, not via shipped secrets files.

## Versioning

In `RaceIngenior.csproj`:

- `Version` / `AssemblyVersion` / `FileVersion`: numeric `Major.Minor.Patch.Build`
- `InformationalVersion`: may include `-dev` (e.g. `0.0.1.0-dev`)

Update `CHANGELOG.md` (Keep a Changelog) in the same change as a version bump.

## Checklist for a release bump

1. Bump csproj versions (`Version` / `AssemblyVersion` / `FileVersion` / `InformationalVersion`)
2. Add CHANGELOG section (Added / Fixed / Notes)
3. Build Release (or pack — see below)
4. Smoke: launch exe, tray, radio test, overlay toggle
5. For a published release: pack + upload installer to GitHub Releases

## Installer + auto-update (Velopack)

Stack: **Velopack** NuGet in-app + `vpk` CLI. Updates come from **public** GitHub Releases (`UpdateGithubRepoUrl` in `appsettings.json`).

```powershell
dotnet tool install -g vpk   # once; match Velopack package major
.\scripts\pack-release.ps1              # → artifacts/releases/*-Setup.exe
.\scripts\pack-release.ps1 -Upload      # needs $env:GITHUB_TOKEN or gh auth
```

In-app:

- Startup: silent check; MessageBox if a newer release exists (Yes = download + restart)
- Settings → **CHECK FOR UPDATES**
- Dev/`dotnet run` builds are not installed → updates disabled (status explains why)

Do not commit `artifacts/`. Whisper models stay out of the pack (auto-download / install script).

## Do not

- Commit real API keys
- Force-push or amend unless the user explicitly asks
- Change git config
