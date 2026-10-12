---
name: wec-ui-overlay
description: >-
  Maintains the carcass temperature overlay and dashboard layout notes. Use when
  editing OverlayWindow, ConversationOverlayWindow, OverlayService, or overlay
  ViewModel. For brand tokens use wec-design; for general WPF windows use wec-ui;
  for flows use wec-ux.
---

# WEC UI / Overlay Agent

Cross-cut: **wec-design** (look) · **wec-ui** (WPF) · **wec-ux** (flows).

## Visual system (dashboard)

- Background `#0A0A0A`, panels `#121212` / `#000000`, text `#F2F2F2`
- Accent `#E10600` (WEC red bars / primary actions)
- Font: Bahnschrift, Segoe UI fallback
- UI copy in **English**; keep WEC timing-board density (section bars, not generic cards clutter)
- Dashboard placement (Left/Top/Width/Height/Maximized) persisted via `UserPreferencesStore`; default first launch is `Maximized`; tray restore uses last saved state
- Window chrome: minimize (−) → taskbar; close (X) → WEC `ConfirmExitWindow` then `Shutdown` (`OnExplicitShutdown`); tray “Hide to tray” still hides without quitting
- ComboBoxes / buttons use dark templates in `MainWindow.xaml` resources

## Overlay

- Files: `OverlayWindow.xaml(.cs)`, `ConversationOverlayWindow.xaml(.cs)`, `Services/OverlayService.cs`, `ViewModels/OverlayViewModel.cs`
- Topmost borderless windows; recommend borderless windowed LMU
- Dark plate (`#121212` / `#0A0A0A`) matching dashboard — not the old white timing card
- Visibility (tyres): hide in pit **box** and pause; show in **pitlane**
- Carcass temps; green **PUSH OK** when warm enough; minimal FUEL + Δ (last−best)
- Position + scale persisted (`OverlayLeft`/`Top`/`Scale`); wheel to scale when click-through is off
- Radio toast (`ConversationOverlayWindow`): DRIVER/ENGINEER for 5–10 s via `ExchangeUpdated`
- Toggle **Hide/Show overlays** gates both tyre card and radio toast (`_userEnabled`); Force show still bypasses for layout
- Optional click-through via binding (applies to both overlays)

## Layout notes

- RADIO vs RADIO LOG: keep log slightly smaller than radio controls (`1.55*` / `0.75*`)
- Restart lives in the championship header (must stay visible)
- Prefer styles already in `MainWindow.xaml` over new card chrome
- Config UI lives in `SettingsWindow` (not the radio panel): PTT, TTS rate, overlay prefs, Gemini key
- Left column: TELEMETRY → **TRACK MAP** → **TYRE CALL**
- Track outlines: learned into repo `Tracks/*.json` via `TrackLayoutLearner`; if missing → oval + banner for developers to drive laps
- Tyre call: Soft/Medium/Hard/Wet from weather + wetness (`TireCompoundAdvisor`)
- Session report window: FIA WEC timing-board chrome (`SessionReportWindow` + `SessionReportViewModel`) — not Markdown UI
- Reports browser: header **REPORTS** → `ReportsWindow` / `ReportsBrowserViewModel` → open board

## Verify

- Build, relaunch, check maximized dark UI
- Toggle overlay; confirm hide/show with box / pause / pitlane
