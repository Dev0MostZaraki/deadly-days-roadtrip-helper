# Deadly Days: Roadtrip Windows Companion

This is the native Windows half of the project. The long-term goal is that the user plays normally while the companion reads **only local, read-only signals** and feeds the existing decision engine automatically.

## Architecture

- **Game detection:** finds `DDSurvivors-Win64-Shipping.exe` (fallback: `DDSurvivors.exe`) and reads its client-area bounds.
- **Screen capture:** captures only the visible game client area through Win32/GDI. The overlay is marked `WDA_EXCLUDEFROMCAPTURE` so it does not recursively detect itself.
- **Vision pipeline:** a conservative 16:9 calibration profile extracts the three Airdrop icon regions. It never invents an item. Recognition is accepted only when a locally learned template meets a strict dHash threshold.
- **Self-learning item templates:** once a candidate is labelled, its local visual hash is stored under `%LOCALAPPDATA%\DeadlyDaysCompanion\item-templates.json`. No game assets are committed to this public repository.
- **Save watcher:** observes `%LOCALAPPDATA%\DDSurvivors\Saved\SaveGames\*.sav` read-only. Parsing of Unreal SaveGame contents comes later; no save is modified.
- **WebView2 bridge:** the existing web planner is bundled locally and remains the single source of truth for layout scoring and Airdrop recommendation.
- **Overlay:** click-through, non-activating and excluded from screen capture.

## Current status (v0.1 foundation)

Working foundation:

1. detect the running game process/window,
2. capture the game client region,
3. watch the save directory,
4. detect an Airdrop UI gate on a 16:9 profile,
5. compute stable icon hashes,
6. persist local labelled references,
7. send recognized Airdrop IDs into the web helper,
8. receive the helper's recommendation back for an overlay.

Not yet considered production-accurate:

- exact per-resolution Airdrop calibration beyond the initial 16:9 seed,
- automatic first-time item naming without any local references,
- backpack cell and item recognition,
- current character recognition,
- Unreal `.sav` field parsing,
- exact support-marker geometry for every item.

Those are deliberately explicit instead of being guessed.

## Build

Requires Windows 10/11 and .NET 8 SDK.

```powershell
dotnet restore .\companion\src\DeadlyDays.Companion\DeadlyDays.Companion.csproj
dotnet run --project .\companion\src\DeadlyDays.Companion\DeadlyDays.Companion.csproj
```

GitHub Actions also builds a Windows artifact on changes to the companion or shared decision engine.
