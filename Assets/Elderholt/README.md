# Elderholt — Phase 1 Foundations (Unity adaptation)

A Unity port of the **Elderholt** Phase 1 vertical slice from the design handoff.
The original design targets Cloudflare Workers + Durable Objects + three.js; this
module reinterprets the same **behavioural spec** inside Unity while keeping the
architecture honest: an authoritative "server" the clients never mutate, driven by
600 ms ticks, with players submitting **intents** and receiving **snapshots**.

## How to run

1. Open the `rpg` project in Unity (6000.0.x).
2. Press **Play**. That's it — `ElderholtBootstrap` auto-boots via
   `[RuntimeInitializeOnLoadMethod]`, so no scene wiring is required. It builds the
   Thornmere Reach world, starts the zone server, and connects two clients:
   **You** and the bot **Fenn**, both sharing one authoritative world.

## Controls

- **Left-click ground** → walk there (a gold marker pulses at the target).
- **Left-click an ore rock** → walk into range and mine it (arm swings while mining).
- **Drag** → orbit the camera (pitch clamped 0.15–1.2 rad). **Scroll** → zoom (6–34).
- **Chat** → type in the bottom-left field, press **Enter** (your name in gold,
  others in green).
- **Restart server** (top-left button) → reboots the zone from its last save, proving
  characters, XP and rock state persist. This is the Phase 1 exit test, adapted:
  mine some ore, watch XP rise, hit Restart, and the state comes back.

## Where the design maps

| Handoff concept                         | This project |
|-----------------------------------------|--------------|
| Durable Object (authoritative zone)     | `Server/ZoneServer.cs` |
| `makeServer()` behavioural spec         | `ZoneServer.Step()` (tick order, arbitration, respawn) |
| D1 character store / localStorage save  | `Server/SaveData.cs` + JSON under `Application.persistentDataPath` |
| Intents / snapshots protocol            | `Server/Protocol.cs` |
| Client (renderer with opinions)         | `Client/GameClient.cs` |
| Second player on the shared rock        | `Client/BotClient.cs` (Fenn) |
| Simulated latency pipe                  | `ElderholtBootstrap` scheduler + `LatSeconds()` |
| three.js Thornmere scene (style only)   | `World/ThornmereWorld.cs`, `World/Avatar.cs`, `World/Geo.cs` |

## Faithful behaviour (from the spec)

- **600 ms ticks.** Resolution order per tick: apply intents → move (4.2 u/s, stop at
  1.9 u from a mining target, 0.2 otherwise) → one mining swing per player in range
  (`ore--`, +28 Mining XP) → depletion sets a 20-tick respawn (refills to 4–6 ore) →
  debounced save every 8 ticks → broadcast snapshot.
- **Shared-rock arbitration is server-side only.** Two miners on one node each swing
  per tick until the ore runs out; no client-side yield prediction.
- **XP curve:** XP to complete level L = `floor(80 × 1.09^L)`, capped at 90.
- **Client interpolates** between the last two snapshots so the 600 ms grid is smooth
  but the server stays authoritative.

## Faithful adaptation notes (what differs, and why)

- **Networking.** The design's two-browser test becomes two in-process clients (You +
  Fenn) sharing one `ZoneServer`, exactly as the browser prototype models it. The
  server/client split, intent/snapshot protocol, and per-socket latency are all real,
  so swapping the in-process pipe for a real transport (Unity Netcode / a headless
  server) is a later-phase change, not a rewrite.
- **Visuals are low-fidelity by design** — flat-shaded low-poly, procedurally built,
  no imported art. Recreate the feel, not the vertices.
- **UI** is drawn with IMGUI (`OnGUI`) so the slice runs with zero asset setup.

## File layout

```
Assets/Elderholt/Scripts/
  ElderholtBootstrap.cs      host: tick loop, camera, input, UI, wiring
  Server/
    ZoneServer.cs            authoritative zone (the "Durable Object")
    Protocol.cs              intents, snapshots, events
    SaveData.cs              persistence model + XP curve
  Client/
    ClientHost.cs            host interface + timed-snapshot struct
    GameClient.cs            local player: snapshots -> HUD/chat/interp
    BotClient.cs             Fenn: mines nearest rock, banters
  World/
    ThornmereWorld.cs        terrain, river, birches, mine hill, ore rocks, avatars
    Avatar.cs                blocky figure + walk/mine animation
    Geo.cs                   low-poly mesh + palette helpers
```
