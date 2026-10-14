# Race Ingenior — Dev Agents

Specialized Cursor agents for this repo. Invoke by naming the skill (e.g. “use radio-pipeline”) or by working in the matching area — skills auto-apply from context.

| Agent | Skill | Owns |
|-------|--------|------|
| **Radio** | `.cursor/skills/radio-pipeline` | PTT → Whisper → Gemini → TTS, rules, memory |
| **Telemetry** | `.cursor/skills/telemetry-lmu` | Shared memory rF2/LMU, REST, snapshot model |
| **Design** | `.cursor/skills/wec-design` | FIA WEC visual identity, colour, type, density |
| **UI** | `.cursor/skills/wec-ui` | WPF windows, controls, MVVM wiring |
| **UX** | `.cursor/skills/wec-ux` | Flows, IA, when to show reports / settings / overlays |
| **UI / Overlay** | `.cursor/skills/wec-ui-overlay` | Carcass overlay + dashboard layout notes |
| **Build / Release** | `.cursor/skills/build-release` | Build, version, changelog, exe path |

Core project conventions always apply via `.cursor/rules/race-ingenior-core.mdc`.

## Quick commands

```powershell
dotnet build src/RaceIngenior/RaceIngenior.csproj -c Debug
& "src\RaceIngenior\bin\Debug\net8.0-windows10.0.19041.0\RaceIngenior.exe"
.\scripts\install-whisper-model.ps1          # ggml-base.bin (default) → Models/ + LocalAppData
.\scripts\install-whisper-model.ps1 -Size small  # better FR PTT, slower
.\scripts\pack-release.ps1                   # Velopack Setup.exe → artifacts/releases/
.\scripts\pack-release.ps1 -Upload           # publish to GitHub Releases (GITHUB_TOKEN)
```

Track maps: drive full on-track laps to learn outlines into `Tracks/*.json` (committed for everyone). Unknown circuits fall back to an oval map with a banner.

Session reports: written when a live session ends (GamePhase 8); if still on track, waits for the finish line → `Reports/*.md` + `.json` (also `%LocalAppData%\RaceIngenior\Reports`). Browse anytime from the dashboard **REPORTS** button.

Track records: personal best lap per track + car → **RECORDS** button; stored in `%LocalAppData%\RaceIngenior\track_records.json` (seeded from past reports). Manufacturer logos: `src/RaceIngenior/Assets/Brands/*.svg` (from LMU `UI.zip` manufacturer images).

Stop a running instance before rebuild if the exe is locked.

## Branching

- **`main`** — releases only; never commit directly
- **`dev`** — integration branch; all feature/fix work branches off `dev`
- Pull requests target `dev`
- Releases: merge `dev` → `main`, tag, and publish
