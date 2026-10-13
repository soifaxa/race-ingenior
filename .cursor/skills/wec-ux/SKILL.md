---
name: wec-ux
description: >-
  Defines Race Ingenior interaction and information architecture (when to show
  windows, report lifecycle, settings vs dashboard, overlay visibility rules).
  Use when designing flows, empty states, confirmations, or deciding what the
  driver sees while racing — not colours (wec-design) or XAML wiring (wec-ui).
---

# WEC UX Agent

Owns **flows and IA**. Look → `wec-design`. Controls → `wec-ui` / `wec-ui-overlay`.

## Driver context

- Primary user is **in-car or pit wall** under time pressure
- Prefer glanceable status on the dashboard; deep config in Settings
- Do not require reading long prose mid-session; reports are post-session

## Information architecture

| Need | Where |
|------|--------|
| Live telemetry / flags / map / standings / tyre call / radio | Dashboard |
| Enlarged track map + standings | Click TRACK MAP panel → `TrackMapWindow` (Esc closes) |
| PTT, TTS rate, overlay prefs, Gemini key, auto-open report | Settings |
| Browse past sessions | **REPORTS** (header + tray) |
| Personal bests by track + car | **RECORDS** (header + tray) |
| One session detail | Session report board (auto or from list) |
| Tyre carcass while driving | Overlay (hide in pit box / pause) |

## Session report flow

1. Live session starts → accumulate silently  
2. GamePhase **8** (Session over) → if still on track, **wait for finish line** (or pits / DNF / disconnect)  
3. Save `.md` + `.json` (LocalAppData + app/repo `Reports/`)  
4. If “Automatically open end-of-session report” → open board  
5. Anytime: REPORTS → list (newest first) → OPEN / double-click → board  

Empty list copy: explain that finishing a live session creates a report.

## Interaction principles

- One job per window; Esc closes secondary windows
- Confirm before exit (`ConfirmExitWindow`); Hide to tray ≠ quit
- Destructive / rare actions (Clear Gemini, Clear memory) stay in Settings with clear labels
- Auto-open report is opt-out (default on) — never force mid-lap popups for other features
- Tray: Show, Records, Reports, Settings, Hide, Overlays, Restart, Exit
- Track records: best lap per track×car in `%LocalAppData%\RaceIngenior\track_records.json`; seeded from session reports; updated on session end

## Copy tone

- English, short, timing-board labels (`SESSION`, `TYRES`, `VIRTUAL ENERGY`)
- Status line explains connection / Gemini / radio without alarm spam

## Handoff

- Spec flows here; implement UI with **wec-ui**; match visuals with **wec-design**
- Overlay show/hide edge cases: coordinate with **wec-ui-overlay**
