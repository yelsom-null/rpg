using System.Collections.Generic;

namespace Elderholt
{
    // ============================================================================
    //  Wire protocol — mirrors the Phase 1 prototype's client<->server messages.
    //  In this Unity adaptation the "wire" is an in-process latency pipe, so these
    //  are plain C# objects rather than JSON. Shapes match the design handoff so
    //  the split stays honest: clients send intents, the server sends snapshots.
    // ============================================================================

    public enum IntentType { Move, Interact, Chat, Ping, Abandon }

    // Client -> server. One pending (move/interact/abandon) intent per player;
    // a newer intent replaces the older. chat/ping are handled immediately.
    public class Intent
    {
        public IntentType type;
        public float x, z;    // move
        public string id;     // interact: node id
        public string text;   // chat
        public double t;      // ping: client timestamp (ms)

        public static Intent Move(float x, float z) => new Intent { type = IntentType.Move, x = x, z = z };
        public static Intent Interact(string id) => new Intent { type = IntentType.Interact, id = id };
        public static Intent Chat(string text) => new Intent { type = IntentType.Chat, text = text };
        public static Intent Ping(double t) => new Intent { type = IntentType.Ping, t = t };
        public static Intent Abandon() => new Intent { type = IntentType.Abandon };
    }

    public enum EventType { Xp, LevelUp, Chat, Depleted, Saved, Pong }

    // Server -> client, batched inside each snapshot's events list.
    public class GameEvent
    {
        public EventType type;
        public string who;    // player id the event concerns
        public string name;   // player display name (levelup / depleted)
        public string from;   // chat sender name
        public string text;   // chat text
        public int amount;    // xp gained
        public int lvl;       // new level
        public float x, z;    // xp float world position
        public string id;     // depleted node id
        public string at;     // saved wall-clock time
        public double t;      // pong echoed timestamp
    }

    public class PlayerSnap
    {
        public string id;
        public string name;
        public float x, z, dir;
        public string anim; // idle | walk | mine
    }

    public class NodeSnap
    {
        public string id;
        public int ore;
    }

    // Full snapshot per tick (fine at this scale; deltas are a later phase).
    public class Snapshot
    {
        public long tick;
        public List<PlayerSnap> players = new List<PlayerSnap>();
        public List<NodeSnap> nodes = new List<NodeSnap>();
        public Dictionary<string, int> xp = new Dictionary<string, int>();
        public List<GameEvent> events = new List<GameEvent>();
    }
}
