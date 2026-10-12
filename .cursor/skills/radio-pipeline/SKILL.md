---
name: radio-pipeline
description: >-
  Develops and debugs the Race Ingenior radio chain (wheel PTT, Whisper STT,
  Gemini engineer, Windows TTS, offline rules, session memory). Use when editing
  Radio/*, Gemini prompts, PTT binding, speech rate, or callout logic.
---

# Radio Pipeline Agent

## Pipeline order

1. `WheelPttService` — SharpDX DirectInput, learn/save binding via `UserPreferencesStore`
2. `WhisperSttService` — model path from `appsettings` (`WhisperModelPath`); resolves outside `bin/` (LocalAppData / repo `Models/`). Install: `scripts/install-whisper-model.ps1`
3. `GeminiEngineerService` — key from in-app Save (`UserPreferencesStore` / LocalAppData)
4. `WindowsTtsService` — French voice preferred; respect speech rate setting
5. Fallback: `RuleEngineService` when Gemini unavailable or empty

Orchestrator: `RadioPipelineService`. Context: `TelemetryPromptBuilder` + `EngineerMemory`.

## Hard constraints

- Keep prompts short / token-efficient (`MaxOutputTokens` in settings)
- Gemini thinking budget must stay at `0` (empty replies if thinking eats the budget)
- Wear alerts use **remaining** wear (alert when remaining is low), not “worn %” inverted
- Puncture alerts from `WheelTemps.*Flat` (`mFlat`) — priority callout to box; prompt key `;flat=FL,…`
- Flag alerts: FCY (phase 6), yellow states, local `SectorFlags`, blue (`PrimaryFlag` 6), green after FCY, session stop (phase 7)
- No rule alerts until `IsInLiveSession` (Connected + InRealtime + GamePhase 1–6)
- No spotter / nearby callouts while in pit box or stands (`InPitBox` / garage)
- Offline rules must stay FR WEC engineer tone, concise for TTS
- Never log or commit the real API key

## Where to edit

| Concern | Files |
|---------|--------|
| End-to-end flow | `Radio/RadioPipelineService.cs` |
| LLM | `Radio/GeminiEngineerService.cs`, `Radio/TelemetryPromptBuilder.cs` |
| Offline callouts | `Radio/RuleEngineService.cs` |
| Memory | `Radio/EngineerMemory.cs` |
| PTT | `Radio/WheelPttService.cs`, prefs store |
| STT / TTS | `WhisperSttService.cs`, `WindowsTtsService.cs` |
| UI bindings | `ViewModels/MainViewModel.cs` |

## Verify

- Build Debug, relaunch exe
- Test Gemini / Test Radio buttons from dashboard
- Confirm PTT still fires after preference save
