using System.Collections.Generic;

namespace Elderholt
{
    // ============================================================================
    //  Wire protocol — mirrors the Phase 1 prototype's client<->server messages,
    //  extended for Phase 2 (mining depth, smithing, market, contracts).
    //  In this Unity adaptation the "wire" is an in-process latency pipe, so these
    //  are plain C# objects rather than JSON. Shapes match the design handoff so
    //  the split stays honest: clients send intents, the server sends snapshots.
    // ============================================================================

    public enum IntentType
    {
        Move, Interact, Chat, Ping, Abandon,
        // Phase 2 — mining depth
        Prospect,   // tap a node to hear its density (id)
        Strike,     // arm a weak-point strike for the next swing
        Wedge,      // hold the wedge at a node so a partner mines faster (id)
        Shore,      // spend 1 timber to prop the current band
        Descend,    // climb down at the mine entrance / shaft
        Ascend,     // climb up a band
        // Phase 2 — smithing
        Smelt,      // start smelting a recipe (item = recipe id)
        Forge,      // put bars on the anvil for a recipe (item = recipe id)
        Pump,       // pump the bellows (+heat)
        Hammer,     // hammer the billet
        Quench,     // quench the finished piece
        // Phase 2 — economy
        Sell,       // sell items at the stall (item id, qty; qty 0 = all)
        Buy,        // buy one of item at the stall (item id)
        AcceptContract,
        DeliverContract,
        // Bracken Cross — the Vault (bank; banked goods survive cave-ins)
        VaultDeposit,   // move all valuables from bag to vault
        VaultWithdraw,  // move everything from vault back to bag
        // Movement doc — run is a persistent server-side toggle (qty 1/0)
        SetRun,
    }

    // Client -> server. One pending movement-class intent per player; a newer
    // intent replaces the older. Action intents (prospect/strike/hammer/trade/…)
    // queue separately and all resolve on the next tick; chat/ping immediately.
    public class Intent
    {
        public IntentType type;
        public float x, z;    // move
        public string id;     // interact/prospect/wedge: node id
        public string item;   // recipe or item id (smelt/forge/sell/buy)
        public int qty;       // sell quantity (0 = all)
        public string text;   // chat
        public double t;      // ping: client timestamp (ms)

        public static Intent Move(float x, float z) => new Intent { type = IntentType.Move, x = x, z = z };
        public static Intent Interact(string id) => new Intent { type = IntentType.Interact, id = id };
        public static Intent Chat(string text) => new Intent { type = IntentType.Chat, text = text };
        public static Intent Ping(double t) => new Intent { type = IntentType.Ping, t = t };
        public static Intent Abandon() => new Intent { type = IntentType.Abandon };
        public static Intent Prospect(string id) => new Intent { type = IntentType.Prospect, id = id };
        public static Intent Strike() => new Intent { type = IntentType.Strike };
        public static Intent Wedge(string id) => new Intent { type = IntentType.Wedge, id = id };
        public static Intent Shore() => new Intent { type = IntentType.Shore };
        public static Intent Descend() => new Intent { type = IntentType.Descend };
        public static Intent Ascend() => new Intent { type = IntentType.Ascend };
        public static Intent Smelt(string recipe) => new Intent { type = IntentType.Smelt, item = recipe };
        public static Intent Forge(string recipe) => new Intent { type = IntentType.Forge, item = recipe };
        public static Intent Pump() => new Intent { type = IntentType.Pump };
        public static Intent Hammer() => new Intent { type = IntentType.Hammer };
        public static Intent Quench() => new Intent { type = IntentType.Quench };
        public static Intent Sell(string item, int qty) => new Intent { type = IntentType.Sell, item = item, qty = qty };
        public static Intent Buy(string item) => new Intent { type = IntentType.Buy, item = item };
        public static Intent AcceptContract() => new Intent { type = IntentType.AcceptContract };
        public static Intent DeliverContract() => new Intent { type = IntentType.DeliverContract };
        public static Intent VaultDeposit() => new Intent { type = IntentType.VaultDeposit };
        public static Intent VaultWithdraw() => new Intent { type = IntentType.VaultWithdraw };
        public static Intent SetRun(bool on) => new Intent { type = IntentType.SetRun, qty = on ? 1 : 0 };
    }

    public enum EventType
    {
        Xp, LevelUp, Chat, Depleted, Saved, Pong,
        // Phase 2
        OreGained,     // who, item, text = grade flavour
        Prospected,    // who, id, text = density reading
        Seam,          // id = revealed node, text = flavour
        Creak,         // band, amount = instability, text = the tell
        CaveIn,        // band, text = flavour
        Shored,        // who, band, amount = new instability
        BandMoved,     // who, band (arrival)
        Smelted,       // who, item = bar id produced
        ForgeTick,     // who, amount = heat, lvl = strikes done, qty = flaws
        Forged,        // who, item = finished item id, text = flavour
        ForgeFail,     // who, text = what went wrong
        Traded,        // who, gold = delta, text = summary
        Contract,      // who, text = description, gold = reward (offer/complete)
        Perk,          // who, text = perk unlocked
        // Deepseam — the run loop
        Died,          // who, band = where it happened, text = flavour (bag lost)
        Victory,       // who, item = the Heart, text = flavour
    }

    // Server -> client, batched inside each snapshot's events list.
    public class GameEvent
    {
        public EventType type;
        public string who;    // player id the event concerns
        public string name;   // player display name (levelup / depleted)
        public string from;   // chat sender name
        public string text;   // chat text / flavour / grade
        public int amount;    // xp gained / instability / heat
        public int lvl;       // new level / forge strikes
        public int qty;       // item qty / forge flaws
        public int gold;      // gold delta / contract reward
        public int band;      // band index for band-scoped events
        public float x, z;    // xp float world position
        public string id;     // node id
        public string item;   // item id
        public string at;     // saved wall-clock time
        public double t;      // pong echoed timestamp
    }

    public class PlayerSnap
    {
        public string id;
        public string name;
        public float x, z, dir;
        public int band;
        public string anim;    // idle | walk | run | trudge | mine | wedge | smith
        public string wedgeAt; // node id the player is holding a wedge at (or null)
        public int energy;     // run energy 0..100
        public bool running;   // the persistent run toggle, as the server sees it
        public bool won;       // has mined the Heart of the Mountain
    }

    public class NodeSnap
    {
        public string id;
        public int ore;
        public int band;
        public string metal;   // copper | tin | iron
        public int tempo;      // weak-point rhythm: weak tick every `tempo` ticks
        public int phase;      // tick % tempo == phase -> weak-point tick
        public bool seamHint;  // revealed as a seam continuation
    }

    public class ItemStack
    {
        public string item;
        public int qty;
    }

    // Per-player public economy state, sent inside the snapshot.
    public class BagSnap
    {
        public int gold;
        public string pickaxe;                     // equipped pickaxe item id
        public List<ItemStack> items = new List<ItemStack>();
        public List<ItemStack> vault = new List<ItemStack>();   // banked at the Vault
    }

    public class ContractSnap
    {
        public string item;     // required item id
        public int qty;         // required quantity
        public int gold;        // reward
        public long deadline;   // tick it expires
        public bool accepted;
    }

    // A player's live forge session (present in snapshot only while forging).
    public class ForgeSnap
    {
        public string recipe;
        public int heat;
        public int strikes;    // good strikes so far (Smithing.StrikesNeeded completes)
        public int flaws;
        public bool awaitingQuench;
        public long quenchBy;  // tick the quench window closes
    }

    // Full snapshot per tick (fine at this scale; deltas are a later phase).
    public class Snapshot
    {
        public long tick;
        public List<PlayerSnap> players = new List<PlayerSnap>();
        public List<NodeSnap> nodes = new List<NodeSnap>();
        public Dictionary<string, int> xp = new Dictionary<string, int>();          // Mining
        public Dictionary<string, int> smithXp = new Dictionary<string, int>();     // Smithing
        public Dictionary<string, BagSnap> bags = new Dictionary<string, BagSnap>();
        public Dictionary<string, ContractSnap> contracts = new Dictionary<string, ContractSnap>();
        public Dictionary<string, ForgeSnap> forges = new Dictionary<string, ForgeSnap>();
        public int[] instability = new int[Bands.Count];   // per band; [0] unused (surface)
        public List<GameEvent> events = new List<GameEvent>();
    }
}
