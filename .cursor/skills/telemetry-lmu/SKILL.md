---
name: telemetry-lmu
description: >-
  Works on Le Mans Ultimate / rF2 shared-memory telemetry, REST fallback, and
  TelemetrySnapshot mapping. Use when editing Telemetry/*, tire carcass temps,
  pit flags, wear fields, or poll timing.
---

# Telemetry (LMU / rF2) Agent

## Sources

- Primary: `SharedMemoryTelemetrySource` + `MappedBuffer` + `rF2Data.cs` layouts
- Fallback: `LmuRestClient` (`LmuRestBaseUrl`, default `http://localhost:6397`)
- Hosted poll: `TelemetryHostedService` (`TelemetryPollMs`)

Canonical model for the rest of the app: `Models/TelemetrySnapshot.cs`.

## Domain facts

- Tire **wear** in rF2: `mWear` ≈ 1.0 new → remaining fraction; low remaining = worn
- Tire **puncture**: `mFlat` → `WheelTemps.*Flat` / `AnyFlat` (deflated — not lock-up flat spot)
- Prefer **carcass** temperatures (Kelvin in shared memory → Celsius in snapshot)
- Thresholds: `TireColdBelowC` / `TireWarmReadyC` in `appsettings.json`
- Expose clear flags: `InPitBox`, pitlane, `IsPaused` — UI/radio/overlay depend on them
- Field list for track map: `TelemetrySnapshot.Field` (`TrackCar` from scoring `mLapDist` / `mPlace` / player / pits)
- Track learning: `PlayerWorldX` / `PlayerWorldZ` + lap fraction → `TrackLayoutStore` (`Tracks/*.json`)
- Weather on scoring: `mRaining` / `mTrackTemp` / `mAmbientTemp` / path wetness → tyre call (`TireCompoundAdvisor`)
- Session end reports: `SessionReportService` (live start → GamePhase 8; if on track, wait for finish line / `mFinishStatus` / pits before save) → `Reports/*.md` + `.json`
- Finish status: scoring `mFinishStatus` → `TelemetrySnapshot.FinishStatus` (0 none, 1 finished, 2 dnf, 3 dq)
- Hypercar Virtual Energy: REST `rest/garage/UIScreen/RepairAndRefuel` → `fuelInfo.currentVirtualEnergy` / `maxVirtualEnergy` as 0–100 % (`RestCache.VirtualEnergy`); not in shared memory
- Do not invent fields missing from rF2 layouts; map carefully from `rF2Data.cs`

## Rules when changing mappings

1. Update snapshot properties and all consumers (prompt builder, rules, overlay VM)
2. Keep Celsius in app-facing code; convert once at the telemetry boundary
3. Avoid heavy allocations on the poll path
4. If REST and shared memory disagree, document which wins in code comments only when non-obvious

## Verify

- Build Debug
- With LMU running: confirm dashboard session / tire values update
- Pit box vs pitlane flags behave differently for overlay and spotter suppression
