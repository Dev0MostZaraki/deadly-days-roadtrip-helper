# Data notes

The helper is an **evidence-aware decision engine**, not a static tier list.

## Modeled item fields

Each item can carry:

- backpack polyomino shape
- rotation
- semantic tags (`throwable`, `triggerable`, `inflatable`, `ranged`, `melee`, `armor`, ...)
- target tags for support items
- exact marker pattern (once verified)
- hand requirement where known
- recipe components
- confidence (`high`, `medium`, `low`)
- source / patch provenance
- future rarity / upgrade-tier values

## Recommendation score

The current score combines:

- character fit
- player goal (balanced / horde / boss / survival)
- current run stage
- item base value
- best feasible backpack placement
- active support-target synergies
- signature-item protection
- opportunity cost when an existing item must be removed
- remaining free space
- backpack-expansion future value
- evidence confidence (shown in UI; exact numeric confidence penalty is intentionally conservative for now)

The score is a **decision/layout score**, not an exact DPS simulation yet.

## Layout optimizer

Items are represented as individual instances, so duplicate items are valid. The optimizer enumerates rotations and legal placements, then uses beam search to keep the irregular-backpack problem responsive in-browser.

Airdrop items are evaluated by re-running the optimizer with that candidate forced into the solution. Existing non-signature items may be removed if the candidate creates a stronger total build.

Backpack expansions are handled differently: the expansion polyomino is attached to the existing bag in all legal adjacent locations, then the current items are re-optimized on every resulting bag shape.

## Marker geometry

If a support item has a verified relative marker mask, the engine can use that mask. If no exact mask exists yet, the UI shows a warning and the engine uses an 8-neighbour approximation.

This is the biggest remaining source of layout uncertainty.

## Patch 0.22 rule

Official patch notes state that throwables can now be affected by most items that affect weapons (the developers explicitly give Firework as an example). This is reflected in the tags/scoring. Patch 0.22.1 also fixed Snake so that it affects throwables as described.

## Deliberately not guessed yet

- exact support marker masks
- complete rarity / item-level numeric scaling
- all Character Item Sticker values
- every current character (the 0.22 surfer still needs reliable name/item details)
- exact hand/powerup-slot interactions across all special cases
- all crafting recipes
- exact Scythe polyomino geometry

Unknown data should stay visible as unknown rather than silently becoming a fact.
