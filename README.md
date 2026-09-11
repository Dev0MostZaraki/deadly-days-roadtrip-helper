# Deadly Days: Roadtrip – Run Helper

A dependency-free, GitHub-Pages-ready decision helper for **Deadly Days: Roadtrip**, currently calibrated for **patch 0.22.1**.

The goal is not to reproduce a wiki or publish a static tier list. The helper tries to answer the live-run question:

> Given my character, exact backpack shape, current items, run stage and three airdrop choices: what should I take, what would it replace, and how should the backpack be rearranged?

## What works now

- Character-aware scoring for Survivor, Mr. Präsi Sir, Bananenperson, Huhn, Astronaut, Der Tod, Sheriff and Penny
- Current **Mr. Präsi Sir** preset matching the live run used while building the tool
- Editable irregular 6×6 backpack canvas
- Polyomino item shapes + rotation
- Duplicate item instances
- Whole-bag layout optimizer (beam search)
- Character / goal / run-stage weighting
- Synergy graph for triggerable, inflatable, ranged, melee, weapon and footwear support
- Patch 0.22 throwable compatibility reflected in weapon-support scoring
- Airdrop comparison: each offered item is tested by re-optimizing the complete backpack
- Existing items may be replaced when an offered item is strong enough; the character signature item is protected
- Backpack expansions (+2 / +3 L / +4) are spatially tested on the current backpack instead of receiving only a static score
- Run-Code export/import
- localStorage persistence
- Data confidence labels and visible warnings for provisional mechanics
- Item database with recipes / roles for the currently modeled items

## Important limitation: marker geometry

The exact highlighted/marked-cell geometry of support items (for example Fahrradpumpe, Feuerwerk, Laufschuhe, Werkzeuggürtel) still needs to be verified in-game.

Until a support item's exact mask has been recorded, the optimizer uses a clearly marked **8-neighbour approximation**. The engine already supports exact marker masks and can be upgraded item-by-item as screenshots are collected.

Therefore:

- item/category relationships can already be useful;
- exact field-perfect support layouts are still provisional for unmeasured support items;
- no provisional layout is presented as exact game truth.

## Data priority

1. Official Pixelsplit Steam patch notes
2. In-game verified observations/screenshots
3. Deadly Days: Roadtrip Community Wiki
4. Community discussions / build experience

The Community Wiki states that it is created in cooperation with Pixelsplit, but also notes that it may not always be completely current.

## Sources

- Steam news / patch notes: https://steamcommunity.com/app/3026450/allnews/
- Steam store: https://store.steampowered.com/app/3026450/Deadly_Days_Roadtrip/
- Community Wiki items: https://deadly-days-roadtrip.fandom.com/de/wiki/Items
- Community Wiki characters: https://deadly-days-roadtrip.fandom.com/de/wiki/Charaktere

## Run locally

No dependencies or build tools are required.

```bash
python -m http.server 8080
```

Then open `http://localhost:8080`.

## GitHub Pages

The site is fully static (`index.html`, `styles.css`, `data.js`, `engine.js`, `ui.js`). It can be served directly from the repository root. No npm install and no build step are needed.

To publish it through GitHub Pages, open **Settings → Pages**, choose **Deploy from a branch**, then select **main** and **/(root)**. If Pages is unavailable for this private repository on the current GitHub plan, the repository must either be made public or hosted with another supported option.

## Architecture

- `data.js` – patch-aware characters, items, tags, shapes, sources and confidence
- `engine.js` – legal placements, rotations, synergy scoring, backpack expansion search and whole-bag optimization
- `ui.js` – run state, local persistence, Airdrop comparison and rendering
- `DATA_NOTES.md` – evidence and scoring rules
- `ROADMAP.md` – planned work toward exact combat and crafting decisions

## Product rule

A recommendation must remain explainable. The helper should always be able to show **why** an item is ranked above another: character fit, active synergy, layout cost, displaced items, run stage, free space and confidence.

See `ROADMAP.md` and `DATA_NOTES.md` for the next steps.
