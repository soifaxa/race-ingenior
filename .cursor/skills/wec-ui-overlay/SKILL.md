---
name: wec-ui-overlay
description: >-
  Maintains the WEC dark WPF dashboard and carcass temperature overlay. Use when
  editing MainWindow, OverlayWindow, OverlayService, overlay ViewModel, or visual
  styling of Race Ingenior.
---

# WEC UI / Overlay Agent

## Visual system (dashboard)

- Background `#0A0A0A`, panels `#121212` / `#000000`, text `#F2F2F2`
- Accent `#E10600` (WEC red bars / primary actions)
- Font: Bahnschrift, Segoe UI fallback
- UI copy in **English**; keep WEC timing-board density (section bars, not generic cards clutter)
- Window starts `Maximized`; tray restore also maximized
- ComboBoxes / buttons use dark templates in `MainWindow.xaml` resources

## Overlay

- Files: `OverlayWindow.xaml(.cs)`, `Services/OverlayService.cs`, `ViewModels/OverlayViewModel.cs`
- Topmost borderless window; recommend borderless windowed LMU
- Visibility: hide in pit **box** and pause; show in **pitlane**
- Carcass temps; green **PUSH OK** when warm enough
- Optional click-through via binding

## Layout notes

- RADIO vs RADIO LOG: keep log slightly smaller than radio controls (`1.55*` / `0.75*`)
- Restart lives in the championship header (must stay visible)
- Prefer styles already in `MainWindow.xaml` over new card chrome

## Verify

- Build, relaunch, check maximized dark UI
- Toggle overlay; confirm hide/show with box / pause / pitlane
