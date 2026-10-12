# Changelog

All notable changes to Race Ingenior are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/).
Versioning uses `Major.Minor.Patch.Build` with an optional `-dev` prerelease label on the informational version.

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
