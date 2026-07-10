using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  ZoneServer — the authoritative zone. This is the Unity stand-in for the
    //  Cloudflare Durable Object in the design doc: it owns live entities, the
    //  resource nodes, the tick loop, chat, and the "sockets" (send callbacks).
    //  Clients never mutate it; they submit intents and receive snapshots.
    //
    //  Phase 1 behaviour is a direct port of the prototype's makeServer():
    //  600 ms ticks, one pending intent per player, server-side shared-rock
    //  arbitration, debounced saves. Phase 2 layers on the mining depth systems
    //  (bands, instability, grades, strikes, seams, wedge), smithing, and the
    //  stall/contract economy — split across the partial files in this folder.
    //  Nothing in the server touches UnityEngine rendering.
    // ============================================================================

    public class OreNode
    {
        public string id;
        public float x, z;
        public int band;
        public string metal;      // copper | tin | iron
        public int ore;
        public int respawn;
        public float richness;    // hidden 0..1 density, read by prospecting
        public int phase;         // weak-point tick: tick % tempo == phase
        public long seamUntil;    // tick until which this node carries a seam hint
    }

    public class CharacterRecord
    {
        public int xp;            // Mining
        public int smithXp;       // Smithing
        public int gold;
        public int energy = 100;  // run energy 0..100
        public string pickaxe = "pickaxe.worn";
        public float x, z;
        public int band;
        public Dictionary<string, int> bag = new Dictionary<string, int>();
        public Dictionary<string, int> vault = new Dictionary<string, int>();   // banked at the Vault
        public ContractSnap contract;   // null = none offered
    }

    class PlayerState
    {
        public string id;
        public string name;
        public float x, z, dir;
        public int band;
        public string anim = "idle";
        public readonly List<Vector2Int> path = new List<Vector2Int>();   // tiles ahead
        public bool runOn;         // the persistent run toggle
        public bool forcedWalk;    // energy hit 0; walk until it recovers to 15
        public string mineId;
        public bool wedgeMode;     // heading to / holding the wedge, not swinging
        public string wedgeAt;     // node id currently wedged (in range, holding)
        public bool strikeArmed;   // next swing is an aimed weak-point strike
        public SmeltJob smelt;     // active smelting (null = none)
        public ForgeJob forge;     // active forging (null = none)
    }

    class SmeltJob
    {
        public SmeltRecipe recipe;
        public int gradeIn;        // min grade of consumed ores = bar grade
        public int ticksLeft;
        public List<ItemStack> consumed = new List<ItemStack>();   // for refunds
    }

    class ForgeJob
    {
        public ForgeRecipe recipe;
        public string metal;
        public int gradeIn;        // bar grade going in (quality ceiling)
        public int heat;
        public int strikes;
        public int flaws;
        public bool awaitingQuench;
        public long quenchBy;
        public List<ItemStack> consumed = new List<ItemStack>();   // for refunds
    }

    public partial class ZoneServer
    {
        public const float TickMs = 600f;

        // Gaits (movement doc): walk 1 tile/tick, run 2. Energy 0..100; run drain
        // scales with carried weight; at 0 you're forced to walk until 15.
        public const int CarryCapacity = 30;
        const int RunRecoverAt = 15;

        // Bracken Cross station positions (surface, band 0). The world builder
        // mirrors these; the server owns them because reach checks are
        // server-side. Trade post in Market Square, forge/anvil on Smithy Row,
        // work orders at the Wayfarers' Guildhall, the Vault bank to the NE,
        // and the shaft mouth out east in Grey Quarry.
        public static readonly Vector2 StallPos = new Vector2(4f, -2f);
        public static readonly Vector2 FurnacePos = new Vector2(1f, -9.5f);
        public static readonly Vector2 AnvilPos = new Vector2(4.5f, -9.5f);
        public static readonly Vector2 BoardPos = new Vector2(-10f, 5.2f);
        public static readonly Vector2 VaultPos = new Vector2(10f, 5.5f);
        public static readonly Vector2 EntrancePos = new Vector2(36f, 0f);
        public const float StationReach = 5f;
        public const float NodeReach = 2.6f;

        public long tick;

        readonly Dictionary<string, Action<Snapshot>> sockets = new Dictionary<string, Action<Snapshot>>();
        readonly Dictionary<string, PlayerState> players = new Dictionary<string, PlayerState>();
        readonly Dictionary<string, Intent> pending = new Dictionary<string, Intent>();
        readonly List<KeyValuePair<string, Intent>> actions = new List<KeyValuePair<string, Intent>>();
        readonly Dictionary<string, CharacterRecord> chr = new Dictionary<string, CharacterRecord>();
        readonly List<OreNode> nodes = new List<OreNode>();
        readonly float[] instability = new float[Bands.Count];
        List<GameEvent> events = new List<GameEvent>();

        // Read-only view for clients that legitimately need node positions
        // (e.g. the bot picking the nearest rock — the prototype reads server.nodes).
        public IReadOnlyList<OreNode> Nodes => nodes;

        // (x, z, band, metal) for every node in the zone.
        static readonly (float x, float z, int band, string metal)[] NodeDefs =
        {
            // Band 0 — Grey Quarry surface veins (east of the city, per the map)
            (29, -5, 0, "copper"), (31, 3, 0, "copper"), (33, -7, 0, "copper"),
            (38, 4, 0, "copper"), (30, -1, 0, "copper"), (39, -4, 0, "copper"),
            // Band 1 — Greyroot Gallery
            (212, -6, 1, "copper"), (228, -4, 1, "tin"), (214, 7, 1, "tin"),
            (226, 8, 1, "copper"), (220, -10, 1, "tin"),
            // Band 2 — Deepseam Hollow
            (434, -5, 2, "iron"), (446, -4, 2, "iron"), (436, 6, 2, "iron"), (445, 6, 2, "iron"),
        };

        static string SavePath => Path.Combine(Application.persistentDataPath, "elderholt_server_v3.json");

        public ZoneServer()
        {
            SaveData saved = Load();

            for (int i = 0; i < NodeDefs.Length; i++)
            {
                NodeSave ns = (saved != null && saved.nodes != null && i < saved.nodes.Count) ? saved.nodes[i] : null;
                nodes.Add(new OreNode
                {
                    id = "n" + i,
                    x = NodeDefs[i].x,
                    z = NodeDefs[i].z,
                    band = NodeDefs[i].band,
                    metal = NodeDefs[i].metal,
                    ore = ns != null ? ns.ore : 4 + (i % 3),
                    respawn = ns != null ? ns.respawn : 0,
                    richness = ns != null ? ns.richness : UnityEngine.Random.value,
                    phase = i % Bands.All[NodeDefs[i].band].tempo,
                });
            }

            tick = saved != null ? saved.tick : 0;
            if (saved != null && saved.instability != null)
                for (int b = 0; b < Bands.Count && b < saved.instability.Length; b++)
                    instability[b] = saved.instability[b];

            // Seed the two characters — every new character wakes in Bracken Cross.
            SeedChar("you", 0, 2);
            SeedChar("fenn", -3, 4);
            if (saved != null && saved.chr != null)
            {
                foreach (CharSave cs in saved.chr)
                {
                    CharacterRecord r = new CharacterRecord
                    {
                        xp = cs.xp,
                        smithXp = cs.smithXp,
                        gold = cs.gold,
                        energy = cs.energy,
                        pickaxe = string.IsNullOrEmpty(cs.pickaxe) ? "pickaxe.worn" : cs.pickaxe,
                        x = cs.x,
                        z = cs.z,
                        band = cs.band,
                    };
                    if (cs.bag != null)
                        foreach (ItemSave it in cs.bag)
                            if (it.qty > 0) r.bag[it.item] = it.qty;
                    if (cs.vault != null)
                        foreach (ItemSave it in cs.vault)
                            if (it.qty > 0) r.vault[it.item] = it.qty;
                    if (!string.IsNullOrEmpty(cs.cItem))
                        r.contract = new ContractSnap { item = cs.cItem, qty = cs.cQty, gold = cs.cGold, deadline = cs.cDeadline, accepted = cs.cAccepted };
                    chr[cs.id] = r;
                }
            }
        }

        void SeedChar(string id, float x, float z)
        {
            if (chr.ContainsKey(id)) return;
            CharacterRecord r = new CharacterRecord { xp = 0, gold = 30, x = x, z = z };
            r.bag[Items.Timber] = 2;   // starter kit: enough to learn shoring + smelting
            r.bag[Items.Coal] = 2;
            chr[id] = r;
        }

        // A client "opens a socket": we remember its send callback and spawn its
        // avatar from the persisted character record.
        public void Connect(string id, string name, Action<Snapshot> send)
        {
            if (!chr.ContainsKey(id)) SeedChar(id, 0, 0);
            CharacterRecord c = chr[id];
            players[id] = new PlayerState { id = id, name = name, x = c.x, z = c.z, band = c.band, dir = 0, anim = "idle" };
            sockets[id] = send;
        }

        public void Disconnect(string id)
        {
            players.Remove(id);
            sockets.Remove(id);
            pending.Remove(id);
        }

        // Intent handling. chat/ping resolve immediately; movement-class intents
        // replace any older pending intent; action intents queue for next tick.
        public void SubmitIntent(string id, Intent msg)
        {
            if (!players.ContainsKey(id)) return;

            switch (msg.type)
            {
                case IntentType.Chat:
                {
                    string text = msg.text ?? "";
                    if (text.Length > 120) text = text.Substring(0, 120);
                    events.Add(new GameEvent { type = EventType.Chat, from = players[id].name, who = id, text = text });
                    return;
                }
                case IntentType.Ping:
                    events.Add(new GameEvent { type = EventType.Pong, who = id, t = msg.t });
                    return;
                case IntentType.Move:
                case IntentType.Interact:
                case IntentType.Abandon:
                case IntentType.Wedge:
                    pending[id] = msg;
                    return;
                default:
                    // Rate limit: at most 4 queued actions per player per tick.
                    int mine = 0;
                    foreach (KeyValuePair<string, Intent> a in actions) if (a.Key == id) mine++;
                    if (mine < 4) actions.Add(new KeyValuePair<string, Intent>(id, msg));
                    return;
            }
        }

        // One 600 ms tick:
        // intents -> move -> mine -> hazards -> crafts -> respawns -> save -> broadcast.
        public void Step()
        {
            tick++;

            ResolveMovementIntents();
            ResolveActionIntents();
            MoveAndMine();
            TickInstability();
            TickCrafting();
            TickContracts();
            TickRespawns();

            if (tick % 8 == 0) Save();

            Broadcast();
        }

        void ResolveMovementIntents()
        {
            foreach (KeyValuePair<string, Intent> kv in pending)
            {
                Intent m = kv.Value;
                if (!players.TryGetValue(kv.Key, out PlayerState p)) continue;

                if (m.type == IntentType.Move)
                {
                    Vector2 c = ClampToBand(p.band, m.x, m.z);
                    p.mineId = null;
                    p.wedgeMode = false;
                    ClearHold(p);
                    SetPath(p, c.x, c.y);
                }
                else if (m.type == IntentType.Interact || m.type == IntentType.Wedge)
                {
                    OreNode n = FindNode(m.id);
                    if (n != null && n.band == p.band)
                    {
                        p.mineId = n.id;
                        p.wedgeMode = m.type == IntentType.Wedge;
                        ClearHold(p);
                        // Path to the node's reach tile, not the node itself.
                        SetPath(p, n.x, n.z);
                        TrimPathToReach(p, n);
                    }
                }
                else if (m.type == IntentType.Abandon)
                {
                    p.mineId = null;
                    p.wedgeMode = false;
                    p.path.Clear();
                    ClearHold(p);
                }
            }
            pending.Clear();
        }

        // Server-side A* over the tile grid; the client's marker is optimistic.
        void SetPath(PlayerState p, float wx, float wz)
        {
            p.path.Clear();
            Vector2Int start = TileMap.ToTile(p.band, p.x, p.z);
            Vector2Int goal = TileMap.ToTile(p.band, wx, wz);
            List<Vector2Int> found = TileMap.FindPath(p.band, start, goal, out bool truncated);
            p.path.AddRange(found);
            if (truncated && found.Count == 0)
                Fail(p.id, "You can't reach that.");
        }

        // For interacts, stop at the first tile already within reach of the node.
        void TrimPathToReach(PlayerState p, OreNode n)
        {
            for (int i = 0; i < p.path.Count; i++)
            {
                Vector2 w = TileMap.ToWorld(p.band, p.path[i]);
                float dx = w.x - n.x, dz = w.y - n.z;
                if (dx * dx + dz * dz <= (NodeReach - 0.4f) * (NodeReach - 0.4f))
                {
                    p.path.RemoveRange(i + 1, p.path.Count - i - 1);
                    return;
                }
            }
        }

        // Anything that must stop when the player re-tasks: wedge hold, crafts.
        void ClearHold(PlayerState p)
        {
            p.wedgeAt = null;
            CancelCrafts(p, refund: true);
        }

        void ResolveActionIntents()
        {
            foreach (KeyValuePair<string, Intent> kv in actions)
            {
                if (!players.TryGetValue(kv.Key, out PlayerState p)) continue;
                CharacterRecord c = chr[kv.Key];
                Intent m = kv.Value;

                switch (m.type)
                {
                    case IntentType.Prospect: DoProspect(p, c, m.id); break;
                    case IntentType.Strike: p.strikeArmed = true; break;
                    case IntentType.Shore: DoShore(p, c); break;
                    case IntentType.Descend: DoBandMove(p, c, +1); break;
                    case IntentType.Ascend: DoBandMove(p, c, -1); break;
                    case IntentType.Smelt: DoSmelt(p, c, m.item); break;
                    case IntentType.Forge: DoForge(p, c, m.item); break;
                    case IntentType.Pump: DoPump(p); break;
                    case IntentType.Hammer: DoHammer(p, c); break;
                    case IntentType.Quench: DoQuench(p, c); break;
                    case IntentType.Sell: DoSell(p, c, m.item, m.qty); break;
                    case IntentType.Buy: DoBuy(p, c, m.item); break;
                    case IntentType.AcceptContract: DoAcceptContract(p, c); break;
                    case IntentType.DeliverContract: DoDeliverContract(p, c); break;
                    case IntentType.VaultDeposit: DoVaultDeposit(p, c); break;
                    case IntentType.VaultWithdraw: DoVaultWithdraw(p, c); break;
                    case IntentType.SetRun: p.runOn = m.qty > 0; break;
                }
            }
            actions.Clear();
        }

        void MoveAndMine()
        {
            foreach (KeyValuePair<string, PlayerState> kv in players)
            {
                PlayerState p = kv.Value;
                CharacterRecord rec = chr[kv.Key];
                OreNode node = p.mineId != null ? FindNode(p.mineId) : null;
                if (node != null && node.ore <= 0) { p.mineId = null; p.wedgeMode = false; p.wedgeAt = null; p.path.Clear(); }

                if (p.path.Count > 0)
                {
                    // Gait: walk 1 tile per tick, run 2 (if the toggle is on and
                    // energy holds). Drain scales with carried weight; walking
                    // and standing regenerate.
                    bool running = p.runOn && !p.forcedWalk && rec.energy > 0;
                    int steps = Mathf.Min(running ? 2 : 1, p.path.Count);

                    float fromX = p.x, fromZ = p.z;
                    Vector2 w = Vector2.zero;
                    for (int s = 0; s < steps; s++)
                    {
                        w = TileMap.ToWorld(p.band, p.path[0]);
                        p.path.RemoveAt(0);
                    }
                    p.x = w.x; p.z = w.y;
                    p.dir = Mathf.Atan2(p.x - fromX, p.z - fromZ);

                    float weightRatio = Mathf.Clamp01((float)CarryWeight(rec) / CarryCapacity);
                    if (running && steps == 2)
                    {
                        rec.energy -= 1 + Mathf.RoundToInt(2f * weightRatio);
                        if (rec.energy <= 0) { rec.energy = 0; p.forcedWalk = true; }
                        p.anim = "run";
                    }
                    else
                    {
                        rec.energy = Mathf.Min(100, rec.energy + 2);
                        p.anim = weightRatio > 0.85f ? "trudge" : "walk";
                    }

                }
                else
                {
                    rec.energy = Mathf.Min(100, rec.energy + 2);

                    if (p.mineId != null && node != null)
                    {
                        float ddx = node.x - p.x, ddz = node.z - p.z;
                        if (ddx * ddx + ddz * ddz > NodeReach * NodeReach)
                        {
                            // Path ended short of reach (blocked ring, cap).
                            p.mineId = null;
                            p.wedgeMode = false;
                            p.anim = "idle";
                            Fail(kv.Key, "You can't reach that.");
                        }
                        else if (p.wedgeMode)
                        {
                            p.wedgeAt = p.mineId;
                            p.anim = "wedge";
                            p.dir = Mathf.Atan2(ddx, ddz);
                        }
                        else
                        {
                            p.anim = "mine";
                            p.dir = Mathf.Atan2(ddx, ddz);
                            // One swing per tick; the server arbitrates the shared
                            // rock — two miners each take a swing until it runs out.
                            if (node.ore > 0) DoSwing(kv.Key, p, rec, node);
                        }
                    }
                    else if (p.smelt == null && p.forge == null)
                    {
                        p.anim = "idle";
                    }
                }

                if (p.forcedWalk && rec.energy >= RunRecoverAt) p.forcedWalk = false;

                rec.x = p.x;
                rec.z = p.z;
                rec.band = p.band;
            }
        }

        static int CarryWeight(CharacterRecord rec)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> it in rec.bag) total += it.Value;
            return total;
        }

        void TickRespawns()
        {
            foreach (OreNode n in nodes)
            {
                if (n.ore <= 0 && n.respawn > 0)
                {
                    n.respawn--;
                    if (n.respawn <= 0)
                    {
                        n.ore = 4 + Mathf.FloorToInt(UnityEngine.Random.value * 3f);
                        n.richness = UnityEngine.Random.value;
                    }
                }
                if (n.seamUntil > 0 && tick > n.seamUntil) n.seamUntil = 0;
            }
        }

        Vector2 ClampToBand(int band, float x, float z)
        {
            BandDef b = Bands.All[band];
            if (b.radius <= 0f)
                return new Vector2(Mathf.Clamp(x, -55f, 55f), Mathf.Clamp(z, -55f, 55f));
            Vector2 o = new Vector2(b.originX, b.originZ);
            Vector2 v = new Vector2(x, z) - o;
            if (v.magnitude > b.radius) v = v.normalized * b.radius;
            return o + v;
        }

        void Broadcast()
        {
            Snapshot snap = new Snapshot { tick = tick, events = events };
            foreach (PlayerState p in players.Values)
            {
                string anim = (p.smelt != null || p.forge != null) ? "smith" : p.anim;
                snap.players.Add(new PlayerSnap
                {
                    id = p.id, name = p.name, x = p.x, z = p.z, dir = p.dir, band = p.band,
                    anim = anim, wedgeAt = p.wedgeAt,
                    energy = chr[p.id].energy, running = p.runOn && !p.forcedWalk,
                });
            }
            foreach (OreNode n in nodes)
            {
                snap.nodes.Add(new NodeSnap
                {
                    id = n.id, ore = n.ore, band = n.band, metal = n.metal,
                    tempo = Bands.All[n.band].tempo, phase = n.phase,
                    seamHint = n.seamUntil > tick,
                });
            }
            foreach (KeyValuePair<string, CharacterRecord> kv in chr)
            {
                snap.xp[kv.Key] = kv.Value.xp;
                snap.smithXp[kv.Key] = kv.Value.smithXp;

                BagSnap bag = new BagSnap { gold = kv.Value.gold, pickaxe = kv.Value.pickaxe };
                foreach (KeyValuePair<string, int> it in kv.Value.bag)
                    if (it.Value > 0) bag.items.Add(new ItemStack { item = it.Key, qty = it.Value });
                foreach (KeyValuePair<string, int> it in kv.Value.vault)
                    if (it.Value > 0) bag.vault.Add(new ItemStack { item = it.Key, qty = it.Value });
                snap.bags[kv.Key] = bag;

                if (kv.Value.contract != null) snap.contracts[kv.Key] = kv.Value.contract;
            }
            foreach (KeyValuePair<string, PlayerState> kv in players)
            {
                if (kv.Value.forge != null)
                {
                    ForgeJob f = kv.Value.forge;
                    snap.forges[kv.Key] = new ForgeSnap
                    {
                        recipe = f.recipe.id, heat = f.heat, strikes = f.strikes,
                        flaws = f.flaws, awaitingQuench = f.awaitingQuench, quenchBy = f.quenchBy,
                    };
                }
            }
            for (int b = 0; b < Bands.Count; b++) snap.instability[b] = Mathf.RoundToInt(instability[b]);

            events = new List<GameEvent>();
            foreach (Action<Snapshot> send in sockets.Values) send(snap);
        }

        OreNode FindNode(string id)
        {
            foreach (OreNode n in nodes) if (n.id == id) return n;
            return null;
        }

        bool Near(PlayerState p, Vector2 pos, float reach)
        {
            float dx = p.x - pos.x, dz = p.z - pos.y;
            return dx * dx + dz * dz <= reach * reach;
        }

        // Generic action-failed notice; the client surfaces `text` as status.
        void Fail(string who, string text)
        {
            events.Add(new GameEvent { type = EventType.ForgeFail, who = who, text = text });
        }

        void GrantMiningXp(string id, PlayerState p, CharacterRecord rec, int amount)
        {
            int before = XpCurve.Level(rec.xp);
            rec.xp += amount;
            int after = XpCurve.Level(rec.xp);
            events.Add(new GameEvent { type = EventType.Xp, who = id, amount = amount, x = p.x, z = p.z });
            if (after > before)
            {
                events.Add(new GameEvent { type = EventType.LevelUp, who = id, name = p.name, lvl = after });
                AnnouncePerks(id, before, after);
            }
        }

        void GrantSmithXp(string id, PlayerState p, CharacterRecord rec, int amount)
        {
            int before = XpCurve.Level(rec.smithXp);
            rec.smithXp += amount;
            int after = XpCurve.Level(rec.smithXp);
            events.Add(new GameEvent { type = EventType.Xp, who = id, amount = amount, x = p.x, z = p.z });
            if (after > before)
                events.Add(new GameEvent { type = EventType.LevelUp, who = id, name = p.name + " (Smithing)", lvl = after });
        }

        public void Save()
        {
            try
            {
                SaveData data = new SaveData { tick = tick };
                foreach (KeyValuePair<string, CharacterRecord> kv in chr)
                {
                    CharacterRecord r = kv.Value;
                    CharSave cs = new CharSave
                    {
                        id = kv.Key, xp = r.xp, smithXp = r.smithXp, gold = r.gold,
                        energy = r.energy, pickaxe = r.pickaxe, x = r.x, z = r.z, band = r.band,
                    };
                    foreach (KeyValuePair<string, int> it in r.bag)
                        if (it.Value > 0) cs.bag.Add(new ItemSave { item = it.Key, qty = it.Value });
                    foreach (KeyValuePair<string, int> it in r.vault)
                        if (it.Value > 0) cs.vault.Add(new ItemSave { item = it.Key, qty = it.Value });
                    if (r.contract != null)
                    {
                        cs.cItem = r.contract.item; cs.cQty = r.contract.qty;
                        cs.cGold = r.contract.gold; cs.cDeadline = r.contract.deadline;
                        cs.cAccepted = r.contract.accepted;
                    }
                    data.chr.Add(cs);
                }
                foreach (OreNode n in nodes)
                    data.nodes.Add(new NodeSave { ore = n.ore, respawn = n.respawn, richness = n.richness });
                for (int b = 0; b < Bands.Count; b++) data.instability[b] = Mathf.RoundToInt(instability[b]);

                File.WriteAllText(SavePath, JsonUtility.ToJson(data));
                events.Add(new GameEvent { type = EventType.Saved, at = DateTime.Now.ToLongTimeString() });
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Elderholt] save failed: " + e.Message);
            }
        }

        static SaveData Load()
        {
            try
            {
                if (File.Exists(SavePath))
                    return JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Elderholt] load failed: " + e.Message);
            }
            return null;
        }
    }
}
