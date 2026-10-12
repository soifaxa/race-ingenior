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
- Prefer **carcass** temperatures (Kelvin in shared memory → Celsius in snapshot)
- Thresholds: `TireColdBelowC` / `TireWarmReadyC` in `appsettings.json`
- Expose clear flags: `InPitBox`, pitlane, `IsPaused` — UI/radio/overlay depend on them
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
