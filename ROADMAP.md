# Product roadmap – Deadly Days: Roadtrip Helper

## Product rule

This project must answer a live run question, not merely reproduce a wiki:

> Given my character, signature item/stickers, current backpack shape, current item instances, run stage and three offered items: what should I take, what should I drop/craft, and what exact layout should I use next?

Every recommendation must be explainable and carry a data-confidence level.

## P0 – trustworthy core (in progress)

Already implemented:

- irregular backpack editor
- 14×12 maximum backpack canvas
- rotatable polyomino items
- duplicate instances
- whole-bag optimizer
- character / goal / stage-aware scoring
- airdrop re-optimization
- spatial backpack-expansion evaluation
- visible data confidence
- 0.22 throwable compatibility rule
- Steam/game installation discovery and build fingerprinting
- local read-only game-file indexer
- screenshot fallback with multi-frame consensus
- UE4SS read-only bridge probe + external telemetry consumer

Still required:

- exact item polyomino shapes for the full 0.22.1 item pool
- exact marker masks for every support item (rotation-aware)
- complete rarity / upgrade-tier values and stacking rules
- Character Item Stickers / modifiers
- hands / weapon-slot / powerup-slot constraints and special cases
- complete crafting recipe/effect data
- exact current character roster, including the 0.22 surf character
- patch provenance per individual mechanic / item

No unknown value should silently become a fact.

## P0.5 – game-native telemetry / true companion

Goal: make screen recognition a fallback rather than the main source of truth.

Implemented foundation:

- `DDRCompanionBridge` UE4SS Lua probe is read-only by design
- 1-second bridge heartbeat through local JSONL telemetry
- on-demand F7 UObject discovery scan, capped to avoid continuous heavy global scans
- filtered `UserWidget` creation observation for inventory/reward UI discovery
- map-load synchronization events
- Windows companion reports bridge live/stale/offline and exposes discovery candidates to its source model

Next steps:

1. test a current UE4SS build against Deadly Days 0.22.1 without forcing the older 0.14.7 config;
2. capture the first F7 discovery result from a live run;
3. identify exact Deadly Days classes/UFunctions for player character, backpack/inventory, reward/airdrop, crafting and support-marker logic;
4. replace global discovery with narrow `FindAllOf` / `RegisterHook` reads for those exact classes/functions;
5. emit stable normalized telemetry IDs instead of raw UObject names;
6. fuse bridge + save + logs + screen with source-specific confidence and conflict handling;
7. add a native-looking in-game HUD path only after telemetry is trustworthy.

The bridge must not modify gameplay values, spawn items, alter money/stats, automate input, or write Deadly Days saves/configs. If a future anti-cheat/integrity system blocks in-process modding, fall back instead of bypassing it.

## P1 – live Run Assistant

- faster item entry (keyboard search + recently used items)
- click-to-place manual mode in addition to auto-layout
- per-instance rarity / upgrade tier
- explicit “take / skip / replace X” recommendation sentence
- exact next-layout visualization with support marker overlays
- side-by-side before/after layout
- Craft-vs-Keep evaluator
- recipe opportunity warnings
- shareable URL state in addition to Run-Code
- run history snapshots

## P2 – character intelligence

For every character:

- Early / Mid / Late priorities
- signature-item build paths
- boss vs horde vs survival variants
- realistic transition items
- target crafts
- anti-synergies / bait items
- banish recommendations
- sticker recommendations
- build-specific scoring coefficients derived from verified mechanics

## P3 – exact combat model

- weapon/item upgrade numbers
- attack speed / reload / magazine calculations
- proc rates
- projectile and throwable interactions
- armor / movement trade-offs
- trigger cooldowns and expected triggers per minute
- boss vs horde expected-value models
- breakpoint detection where the game mechanics make it meaningful

## P4 – maintenance

- patch change log
- stale-data warnings per record
- Weekly Mode profiles
- HQ / endgame weighting
- community-tested presets with evidence labels
- optional screenshot-assisted data entry

## Validation workflow

For support geometry, collect screenshots while the item is being moved/selected and the game visibly highlights its marked cells. Store the relative mask and source screenshot reference, then promote that record from provisional to verified.
