using System.Collections.Generic;

namespace Elderholt
{
    // ============================================================================
    //  GameClient — the local player's client. A renderer with opinions: it holds
    //  no authority, it just files intents and reacts to snapshots. It keeps the
    //  last two snapshots so the render loop can interpolate across the 600 ms
    //  grid, and it fans snapshot events out to the HUD/chat/float systems.
    //  Phase 2: it also tracks prospect readings, the latest creak tell, and
    //  queues notable events (ore gains, forge results, trades) for the HUD.
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

        // Floating text awaiting a world-space sprite, drained by the renderer
        // so it spawns at the interpolated (not snapshot) position.
        public readonly Queue<GameEvent> PendingXpFloats = new Queue<GameEvent>();
        public readonly Queue<GameEvent> PendingNoteFloats = new Queue<GameEvent>();

        // Latest prospect reading per node id (client-side knowledge — "your
        // private read of the rock is the asset").
        public readonly Dictionary<string, string> ProspectNotes = new Dictionary<string, string>();

        // The current band hazard tell, shown on the HUD while underground.
        public string creakTell = "";

        // Camera shake seconds remaining (cave-ins).
        public float shake;

        // Set when the Victory event lands this session; the host shows the win
        // screen once and clears it. (The persistent flag lives in PlayerSnap.won.)
        public bool justWon;

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
                    case EventType.Xp:
                        PendingXpFloats.Enqueue(ev);
                        break;
                    case EventType.LevelUp:
                        status = ev.name + " — level " + ev.lvl + "!";
                        if (ev.who == id) Sfx.Play("level");
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

                    case EventType.OreGained:
                        if (ev.who == id)
                        {
                            PendingNoteFloats.Enqueue(ev);
                            if (ev.text != null) status = ev.text;
                            Sfx.Play(ev.text != null ? "pristine" : "ore");
                        }
                        break;
                    case EventType.Prospected:
                        if (ev.who == id) { ProspectNotes[ev.id] = ev.text; status = "prospect: " + ev.text; }
                        break;
                    case EventType.Seam:
                        status = ev.text;
                        break;
                    case EventType.Creak:
                        creakTell = ev.text;
                        status = ev.text;
                        Sfx.Play("creak");
                        break;
                    case EventType.CaveIn:
                        status = "CAVE-IN! " + ev.text;
                        shake = 1.2f;
                        Sfx.Play("cavein");
                        break;
                    case EventType.Died:
                        if (ev.who == id) { status = ev.text; shake = 1.6f; Sfx.Play("died"); }
                        break;
                    case EventType.Victory:
                        if (ev.who == id) { status = ev.text; justWon = true; Sfx.Play("victory"); }
                        break;
                    case EventType.Shored:
                        if (ev.who == id) { status = "shored up — the timbers hold (instability " + ev.amount + ")"; Sfx.Play("shore"); }
                        break;
                    case EventType.BandMoved:
                        if (ev.who == id)
                        {
                            status = "— " + Bands.All[ev.band].name + ": " + Bands.All[ev.band].ambience + " —";
                            creakTell = "";
                            Sfx.Play("descend");
                        }
                        break;
                    case EventType.Smelted:
                        if (ev.who == id) { status = "the furnace yields a " + Items.Pretty(ev.item); PendingNoteFloats.Enqueue(ev); Sfx.Play("smelt"); }
                        break;
                    case EventType.ForgeTick:
                        break;   // read from snapshot.forges, not events
                    case EventType.Forged:
                        if (ev.who == id) { status = ev.text + "  (" + Items.Pretty(ev.item) + ")"; PendingNoteFloats.Enqueue(ev); Sfx.Play("pristine"); }
                        break;
                    case EventType.ForgeFail:
                        if (ev.who == id) { status = ev.text; Sfx.Play("fail", 0.7f); }
                        break;
                    case EventType.Traded:
                        if (ev.who == id) { status = ev.text; Sfx.Play("coin"); }
                        break;
                    case EventType.Contract:
                        if (ev.who == id) status = ev.text;
                        break;
                    case EventType.Perk:
                        if (ev.who == id) status = "PERK — " + ev.text;
                        break;
                }
            }

            tick = snap.tick;
        }
    }
}
