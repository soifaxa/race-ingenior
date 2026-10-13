# Changelog

All notable changes to Race Ingenior are documented in this file.

Format based on [Keep a Changelog](https://keepachangelog.com/).
Versioning uses `Major.Minor.Patch.Build` with an optional `-dev` prerelease label on the informational version.

## [0.0.1.5-dev] — 2026-10-13

### Added

- Manufacturer SVG logos from LMU UI pack under `Assets/Brands` (standings + **RECORDS**)
- Brand resolve: REST **`vehicleFilename`** over opaque **`carId`** hashes; GT3 team liveries (e.g. AF Corse → Ferrari); single-make **LMP2 → Oreca**, **LMP3 → Ligier**

### Changed

- Telemetry CAR, records, session reports, and REPORTS list show **brand + class** (e.g. `Ferrari LMGT3`, `Aston Martin HYPER`)
- Class label **LMGT3** (not GT3) everywhere

### Notes

- Assembly / file version: `0.0.1.5`. Informational version: `0.0.1.5-dev`.

## [0.0.1.4-dev] — 2026-10-12

### Added

- Dedicated **Settings** window (header + tray): PTT binding, speech rate, overlay options, Gemini API key (Save / Clear / Test), clear session memory
- Gemini API key persisted in `%LocalAppData%\RaceIngenior\user_settings.json`
- **Track map** panel under telemetry: class colours (Hypercar red / LMP2 blue / LMGT3 green; solid white = you)
- Track outline **learning**: drive full on-track laps to record world XZ into `Tracks/*.json` (repo + output); missing layout falls back to oval with an explanation banner
- **Tyre call** panel: Soft / Medium / Hard / Wet from track temp + rain / path wetness (scoring weather), shows fitted compound when available
- Flag warnings (radio + dashboard): FCY / safety car, yellow state (pits closed/open, resume), local sector yellow, blue flag, session stop, green after FCY
- **Session reports** at end of live session: Markdown + JSON under `Reports/` (and `%LocalAppData%\RaceIngenior\Reports`) with track/car, tyre warmup, wear, fuel/VE, track+air temps (start/end/min/max/avg), weather, flags
- Telemetry panel shows live **track** and **air** temperatures
- End-of-session report opens in-app (Settings: “Automatically open end-of-session report”, on by default)
- Session report saves only on GamePhase **8** (Session over) — not pit entry or phase 7
- If still on track at session over, report waits until the **finish line** (or pits / DNF / disconnect)
- Click the dashboard **TRACK MAP** to open an enlarged map window with **STANDINGS** on the right (thicker outline/markers, maximize / F11, Esc / CLOSE to dismiss)
- Track map car markers ease smoothly between telemetry samples (~60 fps)
- Live **STANDINGS** panel (P / manufacturer logo / driver / class / last / best / gap / pit; player row highlighted; refresh 5 s)

### Changed

- Offline tyre-wear callouts: info below 70% remaining, warning below 50%, critical below 30% (was critical only at 15%)
- Gemini key is configured in the dashboard only; users no longer edit `secrets/` under the app folder
- Session panel shows the real game-phase name (e.g. “Green flag (5)”) instead of “Phase X”
- Hypercar **Virtual Energy** from LMU REST `RepairAndRefuel` (`fuelInfo` current/max → %), not the old strategy/usage-only path
- In-app session report is a **FIA WEC timing-board** layout (section bars, cells) instead of raw Markdown; `.md` files still saved
- **REPORTS** browser (header + tray): list past sessions and open any saved report in-app
- Lap times shown as motorsport **`m:ss.mmm`** (e.g. `1:40.737`) on dashboard, reports, and Markdown export
- **RECORDS** board: personal best lap per track and car (seeded from session reports, updated on session end)

### Notes

- Assembly / file version: `0.0.1.4`. Informational version: `0.0.1.4-dev`.
- Repo may still keep `secrets/gemini_api_key.example.txt` for developers; it is not required for end users.

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
