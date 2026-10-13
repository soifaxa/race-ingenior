# Race Ingenior

Your **Le Mans Ultimate** race engineer — radio, live telemetry, and pit-wall overlays in a FIA WEC timing-board style.

Talk to the engineer on Push-to-Talk. Get tyre, flag, and strategy callouts. Track the field on a live map. Review the session when the chequered flag falls.

**Latest release:** [v0.1.0](https://github.com/soifaxa/race-ingenior/releases/tag/v0.1.0)

---

## Download & install

1. Open **[Releases](https://github.com/soifaxa/race-ingenior/releases/latest)**
2. Download **`RaceIngenior-win-Setup.exe`**
3. Run the installer (Windows 10/11, 64-bit)

The app checks for updates on startup and from **Settings → CHECK FOR UPDATES**.

> Prefer a zip? Use **`RaceIngenior-win-Portable.zip`** from the same release page. Auto-update works best with the Setup installer.

---

## What you get

| Area | Highlights |
|------|------------|
| **Radio** | Wheel / joystick PTT → speech-to-text → Gemini engineer → neural voice reply (Windows voice as fallback) |
| **Telemetry** | Live session, fuel / virtual energy, tyre temps & wear, weather, flags |
| **Overlays** | Always-on-top carcass temps + short radio toast (not an in-game HUD inject) |
| **Track map** | Class-coloured field; outline learns as you drive full laps |
| **Standings** | Position, brand logo, driver, class, last / best, gap, pit |
| **Tyre call** | Soft / Medium / Hard / Wet from track temp and wetness |
| **Reports** | End-of-session WEC-style board (also saved for later under **REPORTS**) |
| **Records** | Personal best lap per track × car |

---

## First launch

1. Start **Le Mans Ultimate**, then start **Race Ingenior**.
2. Open **Settings**:
   - Bind your **PTT** device and button.
   - Paste a **Gemini API key** (from [Google AI Studio](https://aistudio.google.com/apikey)) → **SAVE KEY** → **TEST GEMINI**.
   - Pick a **RADIO VOICE** (neural Gemini voices need the key; Windows voices work offline).
3. Join a session and hold PTT to talk to the engineer (French radio is supported).

The Whisper speech model downloads automatically on first use if needed.

---

## Requirements

- **Windows 10 / 11** (64-bit)
- **Le Mans Ultimate** running (shared memory / local telemetry)
- **Microphone** for PTT
- **Internet** for Gemini (engineer + neural TTS) and for updates
- A free **Gemini API key** for the AI engineer (optional for overlays / telemetry-only use)

---

## Privacy

- Your Gemini API key is stored only on your PC (`%LocalAppData%\RaceIngenior`).
- It is never bundled in the installer or committed to this repository.

---

## Changelog

Release notes live on each [GitHub Release](https://github.com/soifaxa/race-ingenior/releases). Full history: [CHANGELOG.md](CHANGELOG.md).

---

*Unofficial companion app — not affiliated with Le Mans Ultimate, Motorsport Games, or the FIA WEC.*
