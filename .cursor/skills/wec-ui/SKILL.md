---
name: wec-ui
description: >-
  Implements Race Ingenior WPF UI (windows, controls, bindings, ViewModels).
  Use when editing MainWindow, SettingsWindow, SessionReportWindow,
  ReportsWindow, shared styles/templates, or MVVM wiring — not pure visual
  tokens (wec-design), not flow/IA decisions (wec-ux), not overlay physics
  (wec-ui-overlay).
---

# WEC UI Agent

Owns **WPF structure and wiring**. Visual tokens → `wec-design`. Flows → `wec-ux`.

## Stack

- .NET 8 WPF, CommunityToolkit.Mvvm, DI via Microsoft.Extensions.Hosting
- Prefer existing styles (`WecButton`, `WecButtonGhost`, `WecButtonDanger`, `Meta`, `Value`, `SectionTitle`)
- Borderless secondary windows: `WindowStyle=None`, `AllowsTransparency`, drag on header, Esc to close
- English UI strings only

## Key surfaces

| Window | Role |
|--------|------|
| `MainWindow` | Dashboard + tray; header actions REPORTS / SETTINGS / overlays / RESTART |
| `SettingsWindow` | PTT, TTS, overlay prefs, Gemini, auto-open report |
| `ReportsWindow` | Browse saved session reports (`ReportsBrowserViewModel`) |
| `RecordsWindow` | Personal bests by track + car (`RecordsBrowserViewModel`) |
| `TrackMapWindow` | Enlarged map + standings (click dashboard TRACK MAP) |
| `SessionReportWindow` | WEC timing-board for one `SessionReport` |
| Overlay windows | Owned with `wec-ui-overlay` |

## Patterns

- Secondary windows: single instance per type; Activate if already open; refresh lists on reopen
- Open report detail via `SessionReportWindow(path, report)`; list via `SessionReportService.ListReports` / `TryLoadReport`
- Persist placement only for main dashboard (`UserPreferencesStore`)
- Match folder layout: `ViewModels/`, `Controls/`, root windows next to `MainWindow`

## Do not

- Invent a second design system — reuse MainWindow resource patterns
- Put Settings / Gemini key chrome back into the Radio panel
- Render in-app reports as raw Markdown (export `.md` only)

## Verify

```powershell
Stop-Process -Name RaceIngenior -ErrorAction SilentlyContinue
dotnet build src/RaceIngenior/RaceIngenior.csproj -c Debug
```

Smoke: REPORTS opens list → OPEN / double-click shows board; SETTINGS still works.
