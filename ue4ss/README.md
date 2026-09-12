# Deadly Days: Roadtrip — UE4SS Companion Bridge

This folder contains **our own read-only Lua bridge**, not UE4SS itself.

The goal is to replace fragile image guessing with game-native telemetry while keeping the decision engine and overlay in the external Windows companion.

## Current stage

`DDRCompanionBridge` is intentionally a discovery probe first:

- emits a 1-second heartbeat to `%LOCALAPPDATA%\DeadlyDaysCompanion\Bridge\telemetry.jsonl`;
- observes newly created `UserWidget` objects and records only names that look relevant to inventory / rewards / items / characters;
- pressing **F7** performs a single capped `GUObjectArray` discovery pass and writes matching object full names;
- emits map-load synchronization events;
- does **not** set Unreal properties, call gameplay functions, spawn items, alter money/stats, automate input, or write Deadly Days saves/configs.

The F7 scan is intentionally manual because `ForEachUObject` is expensive. We use its result to identify the exact Deadly Days classes and UFunctions that should later receive narrow `RegisterHook`/`FindAllOf` read-only telemetry.

## Why this approach

The final architecture is:

```text
Deadly Days / Unreal runtime
        |
        v
DDRCompanionBridge (small, read-only)
        |
        v
local telemetry
        |
        v
DeadlyDaysCompanion.exe
        |
        +--> state fusion
        +--> build / airdrop decision engine
        +--> layout optimizer
        +--> overlay / later native-looking HUD
```

The bridge should expose facts. The external companion decides what those facts mean.

## Installation test

1. Install a **current compatible UE4SS build** for the game and verify UE4SS starts by itself first.
2. Copy the folder `ue4ss/Mods/DDRCompanionBridge` into UE4SS' `Mods` directory.
3. `enabled.txt` is already included, so UE4SS can load the mod without editing `mods.txt`.
4. Start the Windows companion before or shortly after the game. It creates the local bridge output directory.
5. Start Deadly Days.
6. Verify the UE4SS console/log contains `DDRCompanionBridge Loaded`.
7. The Windows companion should show the bridge as live once heartbeats arrive.
8. Open an inventory / reward screen and press **F7 once**. The discovery event is local and is used to determine game-specific class names.

## Deadly Days compatibility note

A public custom UE4SS config exists for **Deadly Days: Roadtrip Early Access v0.14.7** and states that build used Unreal Engine **5.6.1** with an engine-version override. That is useful evidence that UE4SS has been made to work with the game, but it is **not proof that the old config is correct for 0.22.1**. We therefore do not ship or force that override until the current build is tested.

## Safety / scope

This project does not bundle UE4SS binaries or original Deadly Days assets. The bridge is read-only by design. If the game later adds anti-cheat or integrity protection that rejects in-process modding, the project should fall back to the external capture/save/log path instead of attempting to bypass it.
