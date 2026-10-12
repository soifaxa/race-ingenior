---
name: wec-design
description: >-
  Defines Race Ingenior visual identity (FIA WEC timing-board look, colour,
  type, density). Use when choosing look-and-feel, brand signals, report chrome,
  colours, typography, or reviewing whether a screen feels WEC — not for WPF
  control wiring (see wec-ui) or interaction flows (see wec-ux).
---

# WEC Design Agent

Owns **visual identity** only. Implementation lives in `wec-ui`; flows in `wec-ux`.

## Brand

- Product reads as a **FIA WEC pit wall / timing board**, not a generic dark dashboard
- Accent is WEC red `#E10600` — section bars, selection, primary danger actions
- Neutrals: bg `#0A0A0A`, panels `#121212` / `#000000`, text `#F2F2F2`, meta `#888888`
- Type: **Bahnschrift** with Segoe UI fallback; bold values, small caps-style meta labels
- UI copy always **English**; chat with the user may be French
- Lap times: motorsport `m:ss.mmm` via `Models.LapTimeFormat` (e.g. `1:40.737`), never raw seconds like `100.737`

## Composition rules

- Timing-board density: red section bar + title, then flat value cells — avoid card stacks, pill clusters, glow
- One red accent bar per section header (6 px); do not sprinkle red decoration
- Headers: `FIA WEC · …` or championship strip (track / car / meta) before detail grids
- Prefer grids of labelled cells over prose paragraphs in-app (Markdown files may stay for export)
- No purple gradients, cream serif themes, or soft SaaS chrome

## Surfaces in scope

- Dashboard (`MainWindow`), Settings, Session report board, Reports browser, overlays
- Session report = structured WEC board (`SessionReportWindow`), not a document reader
- Reports list = dense selectable rows with red lead bar

## Anti-patterns

- Generic Material/Fluent cards with elevation
- Centred marketing heroes inside the engineer app
- Replacing Bahnschrift with Inter / system UI defaults
- Soft rounding everywhere; keep hard edges consistent with timing screens

## Handoff

- After design decisions: implement with **wec-ui**; validate flows with **wec-ux**
- Overlay behaviour (when shown/hidden) is **wec-ui-overlay** + **wec-ux**
