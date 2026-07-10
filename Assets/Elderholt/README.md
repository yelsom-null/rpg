# Elderholt — Phases 1 & 2 + Bracken Cross (Unity adaptation)

A Unity port of the **Elderholt** vertical slices from the design handoff.
The original design targets Cloudflare Workers + Durable Objects + three.js; this
module reinterprets the same **behavioural spec** inside Unity while keeping the
architecture honest: an authoritative "server" the clients never mutate, driven by
600 ms ticks, with players submitting **intents** and receiving **snapshots**.

- **Phase 1 — Foundations:** one zone, two clients, chat, the shared mining rock,
  persistence across a server restart.
- **Phase 2 — Mining end to end:** the Depth Matrix's Mining MVP row (instability &
  shoring, prospect-tap, seam-following, weak-point strikes, named depth bands, ore
  grades, caravan contracts, the two-player wedge), Smithing as the ore consumer
  (smelt → forge → quench, quality tiers, maker's mark), and a first-pass market
  stall so **ore → gold → gear** closes.
- **Bracken Cross — the first city** (from the City Map): a walled safe town where
  every new character wakes. Market Square day stalls with the trade post, the
  **Wayfarers' Guildhall** (work orders — the contract board), **The Vault** (a
  working bank: banked ores/bars/blades/gems can't be lost to cave-ins), High
  Street shopfront lots, Smithy Row (the public forge & anvil), Clan Quarter and
  Warehouse Row plots. Every gate points at a skill: **Bracken Grove** (N),
  **Mirror Pond** (W), **Grey Quarry** (E — surface veins, deep shafts below),
  the **Caravan Field** (S) and **Redbriar March** (SE, bounty zone) staked out
  for their phases.

## How to run

1. Open the `rpg` project in Unity (6000.0.x).
2. Press **Play**. That's it — `ElderholtBootstrap` auto-boots via
   `[RuntimeInitializeOnLoadMethod]`, so no scene wiring is required. It builds the
   Thornmere Reach world (surface + two underground chambers), starts the zone
   server, and connects two clients: **You** and the bot **Fenn**.

## Controls

Movement (per the Movement & Camera doc — the server thinks in 1 m tiles,
A*-pathing every step; the client renders continuous motion on top):

- **Left-click ground** → walk there (gold marker). Clicks through walls path
  around them; unreachable spots walk as close as possible ("You can't reach
  that."). Paths cap at 25 tiles — long journeys are re-clicks.
- **WASD** → camera-relative movement, sugar over the same move intents (the
  server can't tell keyboard from mouse). Release to stop.
- **X** → toggle **Run**: 2 tiles/tick instead of 1, drains the energy orb —
  faster the heavier your bag. At 0 you're forced to walk until it recovers.
  Walking and standing regenerate.
- **Left-click an ore rock** → path into reach and mine (red marker).
- **Right-click an ore rock** → brace the **wedge** there (your partner mines
  faster and safer; you earn support XP per partner swing).
- **Space** while mining → **aimed strike** at the weak point. Watch the indicator:
  hit the window for pristine ore; miss and the ore crumbles to rubble (until the
  Steady Hands perk at Mining 5).
- **T** underground → **shore** the gallery with a timber (buy timber at the stall).
- **E / R** at the shaft mouth or a ladder → descend / climb up.
- **B** → bag. **Enter** → chat.
- **Camera:** drag to orbit, **←/→ ↑/↓** yaw/pitch (pitch clamped 9°–69°),
  scroll to zoom (6–24 m, tighter in the shafts), **N compass button** eases back
  to north. The rig follows with a soft ease; anything between the camera and
  you fades translucent rather than the boom jumping.
- At the **anvil** while forging: **F** pump the bellows, **Space** hammer,
  **Q** quench. Hammer in the orange; white heat burns the billet; quench the
  instant it's done or the piece cracks.
- Walk near the **trade post / furnace / anvil / guildhall board / Vault** and
  its panel appears bottom-right.
- **Restart server** (top-left) → reboots the zone from its last save: XP, gold,
  energy, bag, vault, pickaxe, band, rocks and instability all come back.

## The Phase 2 loop

1. Mine copper on the hillside; prospect-tap rocks to find the rich ones.
2. Sell ore at the stall → buy **timber** (shoring) and **coal** (smelting).
3. Descend at the mine entrance: **Greyroot Gallery** (copper/tin), then
   **Deepseam Hollow** (iron). Deeper = better ore, richer grades — and the
   gallery creaks. Read the tells; shore or run before the cave-in.
4. Smelt bars at the furnace (ore grade sets the bar's ceiling), forge a better
   **pickaxe** at the anvil (faster grades, wider strike window) or **blades**
   that carry your maker's mark.
5. Sign a caravan order at the notice board and deliver for a premium.

## Where the design maps

| Handoff concept                         | This project |
|-----------------------------------------|--------------|
| Durable Object (authoritative zone)     | `Server/ZoneServer.cs` (+ partials below) |
| `makeServer()` behavioural spec         | `ZoneServer.Step()` (tick order, arbitration, respawn) |
| Mining MVP row (Depth Matrix)           | `Server/MiningSystems.cs` |
| Smithing MVP row (Depth Matrix)         | `Server/SmithingSystems.cs` |
| Market stall + spec contracts           | `Server/EconomySystems.cs` |
| Item/recipe/band catalogue              | `Server/Items.cs` |
| D1 character store / localStorage save  | `Server/SaveData.cs` + JSON under `Application.persistentDataPath` |
| Intents / snapshots protocol            | `Server/Protocol.cs` |
| Client (renderer with opinions)         | `Client/GameClient.cs` |
| Second player on the shared rock        | `Client/BotClient.cs` (Fenn: mines, sells, wedges) |
| Simulated latency pipe                  | `ElderholtBootstrap` scheduler + `LatSeconds()` |
| three.js Thornmere scene (style only)   | `World/ThornmereWorld.cs`, `World/Avatar.cs`, `World/Geo.cs` |

## Faithful behaviour (from the spec)

- **600 ms ticks.** Per-tick order: intents → move (4.2 u/s, stop at 1.9 u for a
  rock, 0.2 otherwise) → mining swings → hazards (instability, cave-ins) → crafts
  (furnace, anvil) → contracts → respawns (20 ticks, refill 4–6) → debounced save
  every 8 ticks → broadcast snapshot.
- **Everything consequential is computed server-side:** grades, strike windows,
  instability, cave-ins, trades, contracts. The client renders and files intents.
- **Economic write-through:** every stall trade and contract payout saves in the
  same tick it is acknowledged — gold duplication is the one unforgivable bug.
- **Shared-rock arbitration is server-side only.** Two miners on one node each
  swing per tick; the wedge partnership modifies yield/safety on the server.
- **XP curve:** XP to complete level L = `floor(80 × 1.09^L)`, capped at 90.
  Mining and Smithing track separately.
- **Client interpolates** between the last two snapshots (band teleports excepted).

## Faithful adaptation notes (what differs, and why)

- **Networking.** The design's two-browser test becomes two in-process clients
  sharing one `ZoneServer`, exactly as the browser prototype models it. Swapping
  the in-process pipe for a real transport is a later-phase change, not a rewrite.
- **Timing minigames on a 600 ms grid.** The weak-point strike and quench windows
  are judged in ticks (the weak tick ± one tick of grace ≈ the design's ±150 ms
  server-side forgiveness, scaled to the tick model).
- **Instability is read, not shown** — the HUD gives the tunnel's tells ("the
  timbers creak overhead"), with the raw number only as a debug bracket.
- **Visuals are low-fidelity by design** — flat-shaded low-poly. Terrain, water,
  avatars and stations are procedural; trees, boulders/ore rocks and camp props
  use bundled **KayKit** CC0 models (`Resources/KayKit/`, see
  `ThirdParty/ATTRIBUTION.md`), each with a procedural fallback if missing.
  Depth bands are chambers at world offsets with their own fog/lighting.
  Recreate the feel, not the vertices.

## Using your own assets (Unity editor)

Every model the world spawns can be replaced with **your own prefab** from the
Unity editor — no code, no file renaming:

1. In the Project window, right-click inside `Assets/Elderholt/Resources` →
   **Create → Elderholt → Asset Set**. Keep the default name **`ElderholtAssets`**
   and make sure it stays inside a **`Resources`** folder.
2. Select it. Drag your prefabs/models onto the labelled slots (Tree, Ore Rock
   Copper/Tin/Iron, Boulder, Barrel, Crate, Torch, …). Anything a named slot
   doesn't cover, add under **Extra Overrides** keyed by the model's path
   (e.g. `KayKit/Props/keg`) or just its name (`keg`).
3. Press Play. Your assets appear wherever the defaults used to; empty slots keep
   the bundled KayKit model, and a missing model still falls back to procedural
   geometry — so partial sets are fine and nothing breaks.

### Big local-only packs (e.g. Quaternius Medieval Village MegaKit)

Large third-party packs (Asset Store, or a big itch.io kit) shouldn't go in the
shared repo — clones stay small and you avoid the file-size limits. Import them
**per-machine** and keep them out of git:

1. Download the pack and use its **FBX** files (Unity imports FBX natively; glTF
   needs the extra *glTFast* package, so prefer FBX where the pack offers it).
2. Drop the unzipped folder into **`Assets/ThirdPartyLocal/`** — that path is
   gitignored, so it stays local to your machine.
3. On the `ElderholtAssets` set, the **Medieval village** slots hook a few pieces
   straight into Bracken Cross: **House** (the two built High Street lots),
   **Market Stall** (replaces the procedural day stalls), **Well** (Market Square
   centrepiece), **Cart** + **Fence** (Caravan Field), **Lantern** (gates &
   square). Anything else goes in **Extra Overrides** by path/name as above.
4. Modular kits (walls/roofs you assemble into houses) are best hand-placed in a
   scene rather than dropped in one piece at a time — ask and I'll set up a scene
   + placement helper that keeps the procedural world underneath.

(New to imported models looking grey/untextured? Select the `.fbx`, open the
**Materials** tab in the Inspector, and **Extract Textures / Extract Materials** —
FBX packs import geometry first and need the texture atlas pointed at once.)
- **UI** is IMGUI (`OnGUI`) so the slice runs with zero asset setup.

## File layout

```
Assets/Elderholt/Scripts/
  ElderholtBootstrap.cs      host: tick loop, camera, input, panels, wiring
  Server/
    ZoneServer.cs            authoritative zone core (the "Durable Object")
    MiningSystems.cs         swings/grades, strikes, seams, prospect, instability,
                             cave-ins, shoring, bands, wedge, perks
    SmithingSystems.cs       furnace smelting; anvil heat/hammer/quench sessions
    EconomySystems.cs        market stall (buyer of last resort) + caravan contracts
    Items.cs                 item ids, prices, recipes, depth-band definitions
    Protocol.cs              intents, snapshots, events
    SaveData.cs              persistence model + XP curve
  Client/
    ClientHost.cs            host interface + timed-snapshot struct
    GameClient.cs            local player: snapshots -> HUD/chat/floats/notes
    BotClient.cs             Fenn: mines, hauls ore to the stall, braces the wedge
  World/
    ThornmereWorld.cs        terrain, river, birches, camp, mine, band chambers
    Avatar.cs                blocky figure + walk/mine/wedge/smith animation
    Geo.cs                   low-poly mesh + palette helpers, click-ref components
```
