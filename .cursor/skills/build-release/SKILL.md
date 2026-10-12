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

- Debug: `src/RaceIngenior/bin/Debug/net8.0-windows/RaceIngenior.exe`
- Release: `src/RaceIngenior/bin/Release/net8.0-windows/RaceIngenior.exe`

Keep the exe next to its output folder (deps, `appsettings.json`, secrets copy).

## Versioning

In `RaceIngenior.csproj`:

- `Version` / `AssemblyVersion` / `FileVersion`: numeric `Major.Minor.Patch.Build`
- `InformationalVersion`: may include `-dev` (e.g. `0.0.1.0-dev`)

Update `CHANGELOG.md` (Keep a Changelog) in the same change as a version bump.

## Checklist for a release bump

1. Bump csproj versions
2. Add CHANGELOG section (Added / Fixed / Notes)
3. Build Release
4. Smoke: launch exe, tray, radio test, overlay toggle

## Do not

- Commit real API keys
- Force-push or amend unless the user explicitly asks
- Change git config
