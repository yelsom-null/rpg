using System;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  BotClient — "Fenn", a second headless client sharing the same authoritative
    //  server. Phase 1: he walks to the nearest live rock and mines, so shared-
    //  rock arbitration is demonstrable in one session. Phase 2 adds a small
    //  routine: when his bag fills with ore he walks to the stall and sells;
    //  when you're mining a rock near him, he sometimes braces the wedge on it
    //  (the two-player technique); and he still trades banter.
    // ============================================================================
    public class BotClient
    {
        readonly IClientHost host;
        readonly ZoneServer server;      // read node positions, as the prototype does
        readonly Action<Intent> send;    // latency-piped intent sink
        readonly string id;

        bool actCd;      // general decision cooldown
        bool chatCd;
        bool wedging;    // currently committed to holding a wedge
        bool selling;    // on a stall run

        static readonly string[] Lines =
        {
            "aye.",
            "deeper veins past the hill, they say.",
            "watch the ceiling in there.",
            "ha!",
            "sell before the caravan leaves.",
            "pristine or it's not worth hauling.",
            "hold — I'll brace the wedge.",
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
            PlayerSnap you = snap.players.Find(p => p.id == "you");

            int oreCarried = 0;
            if (snap.bags.TryGetValue(id, out BagSnap bag))
                foreach (ItemStack it in bag.items)
                    if (it.item.StartsWith("ore.")) oreCarried += it.qty;

            if (!actCd && me.anim == "idle")
            {
                actCd = true;
                host.Schedule(2f, () => actCd = false);

                // Full bag: haul it to the stall (surface camp) and sell.
                if (selling || (oreCarried >= 12 && me.band == 0))
                {
                    selling = true;
                    float dx = me.x - ZoneServer.StallPos.x, dz = me.z - ZoneServer.StallPos.y;
                    if (dx * dx + dz * dz > (ZoneServer.StationReach - 1f) * (ZoneServer.StationReach - 1f))
                    {
                        send(Intent.Move(ZoneServer.StallPos.x + 1.2f, ZoneServer.StallPos.y - 1.2f));
                    }
                    else
                    {
                        send(Intent.Sell("ALL", 0));
                        selling = false;
                    }
                    return;
                }

                // The two-player technique: if you're mining a rock in his band
                // and he's free, sometimes he braces the wedge instead of racing you.
                if (!wedging && you != null && you.band == me.band && you.anim == "mine" && UnityEngine.Random.value < 0.3f)
                {
                    OreNode yours = NearestNode(you.x, you.z, you.band);
                    if (yours != null)
                    {
                        wedging = true;
                        send(Intent.Wedge(yours.id));
                        if (!chatCd) send(Intent.Chat("hold — I'll brace the wedge."));
                        host.Schedule(10f, () =>
                        {
                            wedging = false;
                            send(Intent.Abandon());
                        });
                        return;
                    }
                }

                // Otherwise: mine the nearest live rock in his band.
                if (!wedging)
                {
                    NodeSnap best = null;
                    float bd = float.MaxValue;
                    foreach (NodeSnap r in snap.nodes)
                    {
                        if (r.ore <= 0 || r.band != me.band) continue;
                        OreNode def = FindDef(r.id);
                        if (def == null) continue;
                        float dist = (def.x - me.x) * (def.x - me.x) + (def.z - me.z) * (def.z - me.z);
                        if (dist < bd) { bd = dist; best = r; }
                    }
                    if (best != null) send(Intent.Interact(best.id));
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
                if (ev.type == EventType.CaveIn && !chatCd)
                {
                    chatCd = true;
                    host.Schedule(1.2f, () => send(Intent.Chat("told you to watch the ceiling.")));
                    host.Schedule(6f, () => chatCd = false);
                }
            }
        }

        OreNode NearestNode(float x, float z, int band)
        {
            OreNode best = null;
            float bd = float.MaxValue;
            foreach (OreNode n in server.Nodes)
            {
                if (n.band != band || n.ore <= 0) continue;
                float d = (n.x - x) * (n.x - x) + (n.z - z) * (n.z - z);
                if (d < bd) { bd = d; best = n; }
            }
            return best;
        }

        OreNode FindDef(string nodeId)
        {
            foreach (OreNode n in server.Nodes) if (n.id == nodeId) return n;
            return null;
        }
    }
}
