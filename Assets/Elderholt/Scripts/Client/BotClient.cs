using System;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  BotClient — "Fenn", a second headless client sharing the same authoritative
    //  server. It exists so Phase 1's shared-rock arbitration is demonstrable in a
    //  single session (the prototype does exactly this): Fenn walks to the nearest
    //  live rock and mines, and both miners' swings are resolved server-side.
    //  Fenn also trades a little chat banter with you.
    // ============================================================================
    public class BotClient
    {
        readonly IClientHost host;
        readonly ZoneServer server;      // read node positions, as the prototype does
        readonly Action<Intent> send;    // latency-piped intent sink
        readonly string id;

        bool mineCd;
        bool chatCd;

        static readonly string[] Lines =
        {
            "aye.",
            "deeper veins past the hill, they say.",
            "watch the ceiling in there.",
            "ha!",
            "sell before the caravan leaves.",
        };

        public BotClient(IClientHost host, ZoneServer server, Action<Intent> send, string id)
        {
            this.host = host;
            this.server = server;
            this.send = send;
            this.id = id;
        }

        public void Greet()
        {
            host.Schedule(2.5f, () => send(Intent.Chat("evening. these rocks are mine, mind.")));
        }

        public void OnSnapshot(Snapshot snap)
        {
            PlayerSnap me = snap.players.Find(p => p.id == id);
            if (me == null) return;

            if (me.anim == "idle" && !mineCd)
            {
                NodeSnap best = null;
                float bd = float.MaxValue;
                foreach (NodeSnap r in snap.nodes)
                {
                    if (r.ore <= 0) continue;
                    OreNode def = FindDef(r.id);
                    if (def == null) continue;
                    float dist = Mathf.Sqrt((def.x - me.x) * (def.x - me.x) + (def.z - me.z) * (def.z - me.z));
                    if (dist < bd) { bd = dist; best = r; }
                }
                if (best != null)
                {
                    mineCd = true;
                    send(Intent.Interact(best.id));
                    host.Schedule(2f, () => mineCd = false);
                }
            }

            foreach (GameEvent ev in snap.events)
            {
                if (ev.type == EventType.Chat && ev.who == "you" && UnityEngine.Random.value < 0.5f && !chatCd)
                {
                    chatCd = true;
                    string line = Lines[UnityEngine.Random.Range(0, Lines.Length)];
                    host.Schedule(1.4f, () => send(Intent.Chat(line)));
                    host.Schedule(6f, () => chatCd = false);
                }
                if (ev.type == EventType.Depleted && ev.who == id && UnityEngine.Random.value < 0.3f)
                {
                    host.Schedule(0.9f, () => send(Intent.Chat("that one’s spent.")));
                }
            }
        }

        OreNode FindDef(string nodeId)
        {
            foreach (OreNode n in server.Nodes) if (n.id == nodeId) return n;
            return null;
        }
    }
}
