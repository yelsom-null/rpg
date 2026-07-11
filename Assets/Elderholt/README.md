# DEEPSEAM — a run-based mining game (Unity)

*Dig deep. Sell everything. Don't get buried.*

Deepseam is a single-player push-your-luck miner carved from the earlier
**Elderholt** MMO vertical slice. The architecture it inherited stays honest:
an authoritative in-process "server" driven by 600 ms ticks that the client
never mutates — the client files **intents** and renders **snapshots**. That
split is why the sim is deterministic, testable, and cheap to balance.

Inspirations: SteamWorld Dig / Motherload (the dig-sell-upgrade loop),
Dome Keeper (short runs, depth pressure), Dave the Diver (risky dive, cozy
surface economy).

## The loop

1. **Camp (Bracken Cross).** Safe. Stall, furnace, anvil, work-order board,
   and the Vault. Sell your haul, buy timber and coal, smelt bars, forge a
   better pickaxe, sign caravan orders.
2. **Descend.** The shaft in Grey Quarry (east gate) drops into a 10-band
   mine. Each band is a seeded chamber — richer metals, faster instability
   growth, tighter strike rhythm, thicker fog. The *mountain seed* is rolled
   per save: every new game digs different.
3. **Push your luck.** Mining raises the band's instability. Read the tells —
   quiet → creaking timbers → racing cracks → RUMBLE — and shore with timber
   (T) or climb out. Bag weight slows you and drains run energy.
4. **Cash out or die.** Your bag is only at risk underground; surfacing is the
   cash-out. A cave-in takes **everything you carry** — gold, XP, the pickaxe
   on your belt and anything stashed in the Vault survive. The Vault is the
   deliberate pre-dive stash.
5. **Win.** Band 10, the Heart of the Mountain: one glowing rock at the bottom
   of the world. Mine it and the run is won — then keep digging (endless) or
   roll a new mountain.

## How to run

1. Open the `rpg` project in Unity (6000.0.x).
2. Press **Play**. `ElderholtBootstrap` auto-boots via
   `[RuntimeInitializeOnLoadMethod]` — no scene wiring. The title screen
   fronts the sim; **Continue** resumes your save, **New mountain** rolls a
   fresh seed.

## Controls

- **Left-click ground** → walk (gold marker). Paths cap at 25 tiles.
- **WASD** → camera-relative movement (sugar over the same move intents).
- **X** → toggle **Run**: 2 tiles/tick, drains the energy orb — faster the
  heavier your bag. At 0 you're forced to walk until it recovers.
- **Left-click an ore rock** → path into reach and mine (red marker).
- **Right-click an ore rock** → **prospect-tap**: hear its density (rich rocks
  ring full). Walks you closer first if you're out of arm's reach.
- **Space** while mining → **aimed strike** at the weak point. Hit the window
  for pristine ore; miss and it crumbles (until Steady Hands at Mining 5).
- **T** underground → **shore** the gallery with a timber.
- **E / R** at the shaft mouth or ladder → descend / climb.
- **B** → bag. **Esc** → pause (settings, save & quit).
- At the **anvil**: **F** pump the bellows, **Space** hammer, **Q** quench.
  Hammer in the orange; white heat burns the billet; quench the instant it's
  done or the piece cracks.
- **Camera:** drag to orbit, arrows yaw/pitch, scroll to zoom, **N** eases
  back to north. Occluders fade translucent rather than the boom jumping.

## Faithful behaviour (inherited from the Elderholt spec)

- **600 ms ticks.** Per-tick order: intents → move → mining swings → hazards
  (instability, cave-ins) → crafts → contracts → respawns → debounced save →
  broadcast snapshot.
- **Everything consequential is server-side:** grades, strike windows,
  instability, cave-ins, trades, contracts, death, victory.
- **Write-through economy:** trades, contract payouts, deaths and the victory
  all save in the tick they happen — no reload-scumming.
- **XP curve:** XP to complete level L = `floor(80 × 1.09^L)`, capped at 90.
  Mining and Smithing track separately. Perks at Mining 5 (Steady Hands),
  8 (Seam Sense) and 12 (Gem Eye).
- **Client interpolates** between the last two snapshots (band teleports
  excepted).

## Visuals & audio

Flat-shaded low-poly. Terrain, water, avatars and stations are procedural;
trees, boulders/ore rocks and props use bundled **KayKit** CC0 models
(`Resources/KayKit/`, see `ThirdParty/ATTRIBUTION.md`), each with a
procedural fallback if missing. Sound is **fully procedural** (`Sfx.cs`) —
every clip synthesized from sines and noise at first play; no audio assets.

## Using your own assets (Unity editor)

Every model the world spawns can be replaced with **your own prefab** — no
code, no file renaming:

1. In the Project window, right-click inside `Assets/Elderholt/Resources` →
   **Create → Elderholt → Asset Set**. Keep the default name **`ElderholtAssets`**
   and make sure it stays inside a **`Resources`** folder.
2. Select it. Drag your prefabs/models onto the labelled slots (Tree, Ore Rock
   Copper/Tin/Iron, Boulder, Barrel, Crate, Torch, …). Anything a named slot
   doesn't cover, add under **Extra Overrides** keyed by the model's path
   (e.g. `KayKit/Props/keg`) or just its name (`keg`).
3. Press Play. Your assets appear wherever the defaults used to; empty slots
   keep the bundled KayKit model, and a missing model still falls back to
   procedural geometry.

Large third-party packs (Asset Store, itch.io kits) go in
**`Assets/ThirdPartyLocal/`** — that path is gitignored, so clones stay small.
Prefer FBX (Unity imports it natively). If imports look grey, select the
`.fbx` → **Materials** tab → **Extract Textures / Extract Materials**.

## File layout

```
Assets/Elderholt/Scripts/
  ElderholtBootstrap.cs      host: title/pause shell, tick loop, camera, input, HUD
  Server/
    ZoneServer.cs            authoritative zone core; mountain-seeded node layout
    MiningSystems.cs         swings/grades, strikes, seams, prospect, instability,
                             cave-ins (death), shoring, bands, the Heart, perks
    SmithingSystems.cs       furnace smelting; anvil heat/hammer/quench sessions
    EconomySystems.cs        market stall, the Vault, level-scaled caravan orders
    Items.cs                 item ids, prices, recipes, the 11 depth bands
    Protocol.cs              intents, snapshots, events
    SaveData.cs              persistence model (incl. mountain seed) + XP curve
    TileMap.cs               per-band walkability grids + A* pathing
  Client/
    ClientHost.cs            host interface + timed-snapshot struct
    GameClient.cs            snapshots -> HUD/floats/notes/SFX; win state
    BotClient.cs             headless client (kept as a future balance tester)
    Sfx.cs                   procedural sound synthesis, no audio assets
  World/
    ThornmereWorld.cs        camp city, quarry, 10 band chambers, the Heart
    Avatar.cs                blocky figure + walk/mine/smith animation
    Geo.cs                   low-poly mesh + palette helpers, click-ref components
```
