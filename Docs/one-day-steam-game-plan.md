# DEEPSEAM — a one-day Steam game plan

**Pitch:** *Dig deep. Sell everything. Don't get buried.* A single-player,
run-based mining game: descend a creaking mine, fill your bag, gamble on going
one band deeper, and get back to the surface before the gallery comes down on
you. Between runs, smelt and forge better gear at the camp, then dive again.
Reach the **Heart of the Mountain** (band 10) to win a run; permanent forge
upgrades carry across runs.

**Inspirations (popular games):**

- **SteamWorld Dig / Motherload** — the core loop: dig down, bag fills up,
  return to sell, upgrade, dig deeper.
- **Dome Keeper** — short runs, push-your-luck depth pressure, the tension of
  "one more vein" vs. "get out now."
- **Dave the Diver / Moonlighter** — the two-halves rhythm: risky expedition,
  then a cozy surface economy (smelt, forge, sell, contract).
- **Vampire Survivors** — scope discipline: one mechanic polished, 15-minute
  sessions, launch small and cheap.

**Format:** 3D low-poly (flat-shaded KayKit CC0 art, already in repo),
10–20 minute runs, single-player, offline, Windows + Linux builds.
**Price target:** $3.99. **Session length:** first win in ~2 hours of play.

---

## Why one day is realistic: the reuse map

This is not a from-scratch build. ~80% of the game already runs in this repo
today. The day is spent *refocusing*, not building.

| Deepseam feature | Already built in this repo | Day-of work |
|---|---|---|
| Digging, ore grades, prospect-tap, weak-point strikes | `Server/MiningSystems.cs` | none — it's the core loop |
| Cave-in tension (the run timer) | Instability + shoring + tells in `MiningSystems.cs` | tune curves so pressure ramps per band |
| Depth bands (levels) | Band chambers in `ThornmereWorld.cs` + `Items.cs` band defs | extend 3 bands → 10, procedural variation per run |
| Between-run economy | Stall + contracts in `EconomySystems.cs` | reskin contracts as "orders" that gate upgrades |
| Forge upgrades (meta-progression) | Full smelt/forge/quench minigame in `SmithingSystems.cs` | mark forged gear as permanent across runs |
| Death = lose bag, keep bank | The Vault (bank) + `SaveData.cs` | wire death → drop bag, respawn at camp |
| Movement, camera, click-to-move | Movement & Camera doc implementation | none |
| Persistence | `SaveData.cs` JSON saves | none |
| Game world & art | `ThornmereWorld.cs`, KayKit packs, `ElderholtAssets` overrides | none |
| Deterministic sim (easy to balance/test) | 600 ms authoritative tick in `ZoneServer.cs` | none |

**What gets cut (the MMO chaff):** the second client/bot Fenn, zone chat, the
simulated-latency pipe, Bracken Cross city, the multiplayer wedge mechanic
(becomes a single-player "brace" self-buff or is cut outright). The
server/client split *stays* — it costs nothing and keeps the sim testable —
but it stops pretending to be networked.

---

## The core loop (design)

1. **Camp (surface).** Bank, furnace, anvil, order board. Safe. Cozy music.
2. **Descend.** Enter the shaft. Each band is a seeded chamber (the *mountain
   seed* is rolled per save file, so every new game is a new mountain):
   richer ore, tighter tunnels, faster instability growth, thicker fog.
3. **Push your luck.** Bag weight slows you and drains run energy (already
   implemented). Instability tells escalate: dust → creaks → cracks → cave-in.
   Shoring timbers buy time but cost bag space. Climb out with your haul, or
   gamble one band deeper.
4. **Cash out or die.** Your bag is only at risk underground — surfacing *is*
   the cash-out. Die to a cave-in and the whole bag is gone; gold, XP, the
   pickaxe on your belt, and anything stashed in the Vault before the dive
   survive. The Vault is the deliberate pre-dive stash, Dome Keeper-style.
5. **Forge up.** Smelt banked ore, hammer a better pickaxe (wider strike
   window, faster swings — already implemented as quality tiers), fill orders
   for gold, buy timbers/coal. Dive again.
6. **Win state:** reach band 10, mine the Heart. Post-win: endless mode
   (deeper bands, leaderboard-ready score = gold banked).

One mechanic (mine under pressure), one minigame (forge), one currency loop.
Nothing else ships day one.

---

## Hour-by-hour build schedule

Assumes one focused 10-hour day in the Unity Editor (MCP-driven or manual),
with the existing project compiling green at hour zero.

| Hours | Work | Done means |
|---|---|---|
| 0–1 | **Strip to single-player.** Remove Fenn/bot boot path, chat UI, latency sim. Boot straight into camp. | Play mode = one player at camp, no MMO UI |
| 1–3 | **Run structure.** Band count 3 → 10 with seeded per-save layout variation; death drops the whole bag & wakes you at camp (gold/XP/gear/Vault survive); the Heart of the Mountain win object at band 10. | Full run loop playable: descend → die/escape → camp |
| 3–4 | **Meta loop.** Forged gear persists across runs; order board gates pickaxe tiers; win condition at band 10 + victory screen. | A player can lose runs and still progress |
| 4–5 | **Balance pass.** Tune instability/energy/prices so band 3 is reachable run one, band 10 needs ~5 upgrade cycles. Playtest 3 full runs. | First win takes ~2 hours, deaths feel fair |
| 5–6 | **Game feel.** Cave-in screenshake, dust particles, ore-pop sounds, heartbeat at high instability. CC0 audio (Kenney SFX, Kevin MacLeod music) — no licensing risk. | The mine is scary, the camp is cozy |
| 6–7 | **Shipping shell.** Title screen, pause menu, settings (volume, fullscreen, sensitivity), quit. Rebind the IMGUI HUD into a clean minimal skin. | Behaves like a product, not a prototype |
| 7–8 | **Builds.** Windows + Linux standalone builds via `manage_build`; test the Windows build outside the editor; fix build-only bugs (paths, `persistentDataPath`, resolution). | Both builds boot and complete a run |
| 8–9 | **Steam integration.** Steamworks.NET package, App ID init, 5 achievements (First Ore, First Blade, Band 5, Buried Alive, The Heart). Overlay verified. | Achievements pop in the Steam client |
| 9–10 | **Store assets + upload.** Screenshots from best angles; capsule art from a staged in-game render + title text; write store copy; upload depot via SteamPipe (`steamcmd`); fill store page draft. | Build live on a Steam branch, page in review |

**Scope law for the day:** anything not on this table is written down for
post-launch and not built. If a slot overruns, the cut order is: achievements
→ Linux build → settings menu polish. The core loop and the Windows build are
never cut.

---

## Steam shipping checklist & honest timeline

Building the game in a day is realistic; **being live on Steam the same day is
not** — Valve's process has fixed delays. Plan: build day → launch ~2–3 weeks
later.

- [ ] **Steamworks account + $100 app fee** (per app; recoupable at $1,000
      revenue). If not already paid, do this the *morning of* — identity/tax
      verification can take days and everything else blocks on the App ID.
- [ ] **Store page assets:** header capsule 460×215, small 231×87, main
      616×353, vertical 374×448, library 600×900 + hero 3840×1240 + logo;
      5+ screenshots at 1920×1080; short (<300 chars) + long description.
      A trailer is optional but strongly recommended — 30s of raw gameplay cut
      to music is enough at this price point.
- [ ] **Store page review:** 2–5 business days for Valve to approve the page.
- [ ] **"Coming Soon" minimum:** the approved page must be public for at least
      **2 weeks** before release — this is the hard floor on launch date.
- [ ] **Build review:** first build review is separate from page review
      (typically 1–3 days). Upload early, iterate on a beta branch freely.
- [ ] **Launch requirements:** launch discount (10%), release date set,
      supported OS checkboxes matching actual builds, content survey filled.
- [ ] **Use the 2-week window:** the game is done, so spend it on wishlists —
      post the trailer to r/incremental_games / r/roguelites / itch.io devlog,
      and playtest via Steam beta-branch keys; ship day-one patch from
      feedback.

---

## Risks & fallbacks

| Risk | Fallback |
|---|---|
| Editor/build issues eat hours (Unity's classic tax) | Core loop runs in Play mode by hour 5 regardless; builds get the whole 7–8 slot with achievements as the sacrificial buffer |
| Run loop isn't fun with 10 bands | Ship 6 bands; depth count is data in `Items.cs`, not code |
| Balance can't be tuned in one hour | The deterministic 600 ms tick sim allows headless auto-play balance scripts (bot code exists — repurpose Fenn as a balance tester, not a feature) |
| Steamworks.NET friction | Ship v1.0 with no achievements; add in week-one patch during the Coming Soon window |
| Capsule art looks amateur | Staged in-game screenshot + heavy vignette + bold title type is the accepted style for $3.99 games; do not hand-draw anything |

## Post-launch (not for build day)

Endless mode leaderboards (Steam Leaderboards), daily seeded run, the wedge
mechanic returning as local co-op, Steam Cloud saves, workshop-free content
patches (new ores/bands are pure data), Mac build.

---

## Alternatives considered (and why not)

- **Forge Rush** — arcade smithing-only game (the anvil minigame as a
  WarioWare-style score attack). Smallest scope, but shallow: minigame depth
  can't carry a Steam page. Kept as a *mode* idea for Deepseam later.
- **Elderholt: Prospector** — cozy shop-keeper angle (Moonlighter-style,
  economy-first). Needs NPC customers and dialogue that don't exist; the
  mine-pressure loop is the part that's already fun and already built.

Deepseam wins because the tension mechanic (instability + push-your-luck
depth) is the repo's most complete, most *game-feeling* system, and it maps
1:1 onto a proven, popular genre shape.
