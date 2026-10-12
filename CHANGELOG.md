# Changelog

All notable changes to Race Ingenior are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/).
Versioning uses `Major.Minor.Patch.Build` with an optional `-dev` prerelease label on the informational version.

## [0.0.1.3-dev] — 2026-10-12

### Added

- Whisper model install script: `scripts/install-whisper-model.ps1` (repo `Models/` + `%LocalAppData%\RaceIngenior\Models\`)
- Tyre overlay: dark WEC theme aligned with dashboard, fuel + last-vs-best Δ strip, remembered position and mouse-wheel scale
- Radio conversation toast overlay (DRIVER / ENGINEER) for 5 / 8 / 10 s after each exchange
- Overlay **Force show** checkbox to keep tyre + radio overlays visible for testing / placement
- Dashboard window position, size, and maximized state remembered across launches
- **COPY** button for the last DRIVER / ENGINEER exchange
- Application version (`InformationalVersion`) shown in the championship header
- WEC-styled exit confirmation when closing the dashboard (X / tray Exit)
- Puncture detection via rF2/LMU `mFlat`: offline radio alert, Gemini `;flat=` context, dashboard / overlay **FLAT** markers

### Fixed

- Minimize (−) keeps the window in the taskbar; tray hide remains via “Hide to tray”
- **Hide overlays** also suppresses the radio toast (not only the tyre card)
- Whisper model path no longer resolves under `bin/`; looks up existing `Models/ggml-tiny.bin` in LocalAppData / repo, otherwise downloads to LocalAppData
- Offline rule engine same-alert cooldown extended to 1 minute (was 8 s)
- Rule alerts suppressed until a live session (no tyre-wear callouts at game launch / menus)

### Notes

- Assembly / file version: `0.0.1.3`. Informational version: `0.0.1.3-dev`.
- `Models/*.bin` stays gitignored — run the install script (or let the app download on first STT use).
- Agents docs updated (`AGENTS.md`, radio / overlay skills) for Whisper install and overlay toast behaviour.

## [0.0.1.2-dev] — 2026-10-12

### Fixed

- PC freeze when launching without a wheel: DirectInput device enumeration no longer runs every 15 ms; re-scan at most every 2 s while searching

### Notes

- Assembly / file version: `0.0.1.2`. Informational version: `0.0.1.2-dev`.

## [0.0.1.1-dev] — 2026-10-12

### Added

- Cursor dev agents: `radio-pipeline`, `telemetry-lmu`, `wec-ui-overlay`, `build-release`
- Project rules (`.cursor/rules/race-ingenior-core.mdc`) and `AGENTS.md` index

### Notes

- Assembly / file version: `0.0.1.1`. Informational version: `0.0.1.1-dev`.

## [0.0.1.0-dev] — 2026-10-11

First development milestone: Windows race-engineer companion for Le Mans Ultimate.

### Added

- WPF dashboard with WEC-inspired dark UI (black plates, red accent `#E10600`)
- System tray icon with show / hide / overlay / restart / exit
- Radio pipeline: wheel PTT (SharpDX DirectInput) → Whisper STT → Gemini engineer → Windows TTS
- In-app PTT device / button binding, learn mode, and speech-rate slider
- Gemini API key via `secrets/gemini_api_key.txt`, token-efficient prompts, session memory
- Offline rule engine for tire / status callouts when Gemini is unavailable
- rF2 / LMU shared-memory telemetry (+ REST fallback path)
- Carcass tire temperature overlay (WEC timing look): hide in pit box / pause, show in pitlane
- Pit-exit carcass warmup alerts (`TireColdBelowC` / `TireWarmReadyC`) and green **PUSH OK**
- Restart button to relaunch the app process
- Dashboard opens maximized; radio log panel sized below the radio controls

### Fixed

- Gemini empty replies when thinking tokens consumed the budget (`thinkingBudget` 0)
- Tire-wear alerts inverted (now remaining wear under threshold)
- Spotter “nearby” callouts suppressed while in stands / pit box
- ComboBox contrast on dark theme
- Restart control clipped / hard to reach in earlier layouts

### Notes

- Overlay is a topmost borderless window (borderless windowed LMU recommended), not an in-game HUD injection.
- Assembly / file version: `0.0.1.0`. Informational version: `0.0.1.0-dev`.
