using System.Collections.Generic;

namespace Elderholt
{
    // ============================================================================
    //  GameClient — the local player's client. A renderer with opinions: it holds
    //  no authority, it just files intents and reacts to snapshots. It keeps the
    //  last two snapshots so the render loop can interpolate across the 600 ms
    //  grid, and it fans snapshot events out to the HUD/chat/xp-float systems.
    // ============================================================================
    public class GameClient
    {
        readonly IClientHost host;
        public readonly string id;

        public bool HasPrev;
        public TimedSnap Prev;
        public TimedSnap Next;

        public long tick;
        public int rtt;
        public string status = "Connecting to Thornmere Reach…";
        public string savedAt = "—";

        // XP events awaiting a floating "+28 xp" sprite, drained by the renderer
        // so the float spawns at the interpolated (not snapshot) position.
        public readonly Queue<GameEvent> PendingXpFloats = new Queue<GameEvent>();

        public GameClient(IClientHost host, string id)
        {
            this.host = host;
            this.id = id;
        }

        public void OnSnapshot(Snapshot snap)
        {
            if (HasPrev || Next.snap != null) { Prev = Next; HasPrev = true; }
            Next = new TimedSnap { at = host.NowMs, snap = snap };

            foreach (GameEvent ev in snap.events)
            {
                switch (ev.type)
                {
                    case EventType.Chat:
                        host.PushChat(ev.from, ev.text, ev.who == id);
                        break;
                    case EventType.Xp:
                        PendingXpFloats.Enqueue(ev);
                        break;
                    case EventType.LevelUp:
                        status = ev.name + " — Mining level " + ev.lvl + "!";
                        break;
                    case EventType.Saved:
                        savedAt = ev.at;
                        break;
                    case EventType.Pong:
                        if (ev.who == id) rtt = (int)System.Math.Round(host.NowMs - ev.t);
                        break;
                    case EventType.Depleted:
                        status = ev.name + " cleared the vein.";
                        break;
                }
            }

            tick = snap.tick;
        }
    }
}
