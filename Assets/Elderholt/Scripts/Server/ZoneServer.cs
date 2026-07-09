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
    //  Behaviour is a direct port of the Phase 1 prototype's makeServer():
    //  600 ms ticks, one pending intent per player, one mining swing per tick,
    //  server-side shared-rock arbitration, 28 XP/swing, 20-tick respawn, and a
    //  debounced save every 8 ticks. Nothing here touches UnityEngine rendering.
    // ============================================================================

    public class OreNode
    {
        public string id;
        public float x, z;
        public int ore;
        public int respawn;
    }

    public class CharacterRecord
    {
        public int xp;
        public float x, z;
    }

    class PlayerState
    {
        public string id;
        public string name;
        public float x, z, dir;
        public string anim = "idle";
        public bool hasTarget;
        public float tx, tz;
        public string mineId;
    }

    public class ZoneServer
    {
        public const float TickMs = 600f;
        static readonly float Speed = 4.2f * TickMs / 1000f; // units per tick

        public long tick;

        readonly Dictionary<string, Action<Snapshot>> sockets = new Dictionary<string, Action<Snapshot>>();
        readonly Dictionary<string, PlayerState> players = new Dictionary<string, PlayerState>();
        readonly Dictionary<string, Intent> pending = new Dictionary<string, Intent>();
        readonly Dictionary<string, CharacterRecord> chr = new Dictionary<string, CharacterRecord>();
        readonly List<OreNode> nodes = new List<OreNode>();
        List<GameEvent> events = new List<GameEvent>();

        // Read-only view for clients that legitimately need node positions
        // (e.g. the bot picking the nearest rock — the prototype reads server.nodes).
        public IReadOnlyList<OreNode> Nodes => nodes;

        static readonly (float x, float z)[] NodeDefs =
        {
            (20, -12), (23, -9), (18, -16), (25, -14), (21, -19), (27, -10),
        };

        static string SavePath => Path.Combine(Application.persistentDataPath, "elderholt_p1_server_v1.json");

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
                    ore = ns != null ? ns.ore : 4 + (i % 3),
                    respawn = ns != null ? ns.respawn : 0,
                });
            }

            tick = saved != null ? saved.tick : 0;

            // Seed the two Phase 1 characters (matches the prototype's chr map).
            SeedChar("you", 2, 4);
            SeedChar("fenn", -5, 8);
            if (saved != null && saved.chr != null)
            {
                foreach (CharSave cs in saved.chr)
                {
                    chr[cs.id] = new CharacterRecord { xp = cs.xp, x = cs.x, z = cs.z };
                }
            }
        }

        void SeedChar(string id, float x, float z)
        {
            if (!chr.ContainsKey(id)) chr[id] = new CharacterRecord { xp = 0, x = x, z = z };
        }

        // A client "opens a socket": we remember its send callback and spawn its
        // avatar from the persisted character record.
        public void Connect(string id, string name, Action<Snapshot> send)
        {
            if (!chr.ContainsKey(id)) SeedChar(id, 0, 0);
            CharacterRecord c = chr[id];
            players[id] = new PlayerState { id = id, name = name, x = c.x, z = c.z, dir = 0, anim = "idle" };
            sockets[id] = send;
        }

        public void Disconnect(string id)
        {
            players.Remove(id);
            sockets.Remove(id);
            pending.Remove(id);
        }

        // Intent handling. chat/ping resolve immediately; movement-class intents
        // are queued and replace any older pending intent for that player.
        public void SubmitIntent(string id, Intent msg)
        {
            if (!players.ContainsKey(id)) return;

            if (msg.type == IntentType.Chat)
            {
                string text = msg.text ?? "";
                if (text.Length > 120) text = text.Substring(0, 120);
                events.Add(new GameEvent { type = EventType.Chat, from = players[id].name, who = id, text = text });
                return;
            }
            if (msg.type == IntentType.Ping)
            {
                events.Add(new GameEvent { type = EventType.Pong, who = id, t = msg.t });
                return;
            }
            pending[id] = msg;
        }

        // One 600 ms tick: resolve intents -> move+mine -> respawns -> save -> broadcast.
        public void Step()
        {
            tick++;

            // --- resolve intents (in tick order) ---
            foreach (KeyValuePair<string, Intent> kv in pending)
            {
                Intent m = kv.Value;
                if (!players.TryGetValue(kv.Key, out PlayerState p)) continue;

                if (m.type == IntentType.Move)
                {
                    p.tx = Mathf.Clamp(m.x, -55f, 55f);
                    p.tz = Mathf.Clamp(m.z, -55f, 55f);
                    p.hasTarget = true;
                    p.mineId = null;
                }
                else if (m.type == IntentType.Interact)
                {
                    OreNode n = FindNode(m.id);
                    if (n != null)
                    {
                        p.mineId = n.id;
                        p.tx = n.x; p.tz = n.z;
                        p.hasTarget = true;
                    }
                }
                else if (m.type == IntentType.Abandon)
                {
                    p.mineId = null;
                    p.hasTarget = false;
                }
            }
            pending.Clear();

            // --- move + mine ---
            foreach (KeyValuePair<string, PlayerState> kv in players)
            {
                PlayerState p = kv.Value;
                OreNode node = p.mineId != null ? FindNode(p.mineId) : null;
                if (node != null && node.ore <= 0) { p.mineId = null; p.hasTarget = false; }

                if (p.hasTarget)
                {
                    float dx = p.tx - p.x, dz = p.tz - p.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    float stopAt = p.mineId != null ? 1.9f : 0.2f;
                    if (d > stopAt)
                    {
                        float stepLen = Mathf.Min(Speed, d - stopAt * 0.5f);
                        p.x += (dx / d) * stepLen;
                        p.z += (dz / d) * stepLen;
                        p.dir = Mathf.Atan2(dx, dz);
                        p.anim = "walk";
                    }
                    else
                    {
                        p.hasTarget = false;
                        p.anim = p.mineId != null ? "mine" : "idle";
                    }
                }
                else if (p.mineId != null && node != null)
                {
                    p.anim = "mine";
                    p.dir = Mathf.Atan2(node.x - p.x, node.z - p.z);
                    // One swing per tick; the server arbitrates the shared rock —
                    // two miners on one node each take a swing until it runs out.
                    if (node.ore > 0)
                    {
                        node.ore--;
                        CharacterRecord rec = chr[kv.Key];
                        int before = XpCurve.Level(rec.xp);
                        rec.xp += 28;
                        int after = XpCurve.Level(rec.xp);
                        events.Add(new GameEvent { type = EventType.Xp, who = kv.Key, amount = 28, x = p.x, z = p.z });
                        if (after > before)
                            events.Add(new GameEvent { type = EventType.LevelUp, who = kv.Key, name = p.name, lvl = after });
                        if (node.ore <= 0)
                        {
                            node.respawn = 20;
                            events.Add(new GameEvent { type = EventType.Depleted, id = node.id, who = kv.Key, name = p.name });
                            p.mineId = null;
                            p.anim = "idle";
                        }
                    }
                }
                else
                {
                    p.anim = "idle";
                }

                chr[kv.Key].x = p.x;
                chr[kv.Key].z = p.z;
            }

            // --- respawns ---
            foreach (OreNode n in nodes)
            {
                if (n.ore <= 0 && n.respawn > 0)
                {
                    n.respawn--;
                    if (n.respawn <= 0) n.ore = 4 + Mathf.FloorToInt(UnityEngine.Random.value * 3f);
                }
            }

            // --- debounced persistence ---
            if (tick % 8 == 0) Save();

            // --- broadcast snapshot ---
            Snapshot snap = new Snapshot { tick = tick, events = events };
            foreach (PlayerState p in players.Values)
                snap.players.Add(new PlayerSnap { id = p.id, name = p.name, x = p.x, z = p.z, dir = p.dir, anim = p.anim });
            foreach (OreNode n in nodes)
                snap.nodes.Add(new NodeSnap { id = n.id, ore = n.ore });
            foreach (KeyValuePair<string, CharacterRecord> kv in chr)
                snap.xp[kv.Key] = kv.Value.xp;

            events = new List<GameEvent>();
            foreach (Action<Snapshot> send in sockets.Values) send(snap);
        }

        OreNode FindNode(string id)
        {
            foreach (OreNode n in nodes) if (n.id == id) return n;
            return null;
        }

        public void Save()
        {
            try
            {
                SaveData data = new SaveData { tick = tick };
                foreach (KeyValuePair<string, CharacterRecord> kv in chr)
                    data.chr.Add(new CharSave { id = kv.Key, xp = kv.Value.xp, x = kv.Value.x, z = kv.Value.z });
                foreach (OreNode n in nodes)
                    data.nodes.Add(new NodeSave { ore = n.ore, respawn = n.respawn });

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
