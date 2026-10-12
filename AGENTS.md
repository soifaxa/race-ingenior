# Race Ingenior — Dev Agents

Specialized Cursor agents for this repo. Invoke by naming the skill (e.g. “use radio-pipeline”) or by working in the matching area — skills auto-apply from context.

| Agent | Skill | Owns |
|-------|--------|------|
| **Radio** | `.cursor/skills/radio-pipeline` | PTT → Whisper → Gemini → TTS, rules, memory |
| **Telemetry** | `.cursor/skills/telemetry-lmu` | Shared memory rF2/LMU, REST, snapshot model |
| **UI / Overlay** | `.cursor/skills/wec-ui-overlay` | WPF dashboard, WEC dark theme, carcass overlay |
| **Build / Release** | `.cursor/skills/build-release` | Build, version, changelog, exe path |

Core project conventions always apply via `.cursor/rules/race-ingenior-core.mdc`.

## Quick commands

```powershell
dotnet build src/RaceIngenior/RaceIngenior.csproj -c Debug
& "src\RaceIngenior\bin\Debug\net8.0-windows\RaceIngenior.exe"
```

Stop a running instance before rebuild if the exe is locked.
