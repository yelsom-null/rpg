using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Persistence model. In the browser prototype this was localStorage; here it
    //  is a JSON file under Application.persistentDataPath. Character truth (xp,
    //  gold, inventory, position) and disposable zone state (node ore/respawn,
    //  band instability) both round-trip so the "state survives a redeploy" exit
    //  test holds after a server restart.
    //  JsonUtility-friendly: [Serializable] classes, lists instead of dicts.
    // ============================================================================

    [Serializable]
    public class ItemSave
    {
        public string item;
        public int qty;
    }

    [Serializable]
    public class CharSave
    {
        public string id;
        public int xp;         // Mining
        public int smithXp;    // Smithing
        public int gold;
        public int energy = 100;
        public bool won;       // has mined the Heart of the Mountain
        public string pickaxe;
        public float x;
        public float z;
        public int band;
        public List<ItemSave> bag = new List<ItemSave>();
        public List<ItemSave> vault = new List<ItemSave>();

        // Active/offered contract (empty item = none).
        public string cItem;
        public int cQty;
        public int cGold;
        public long cDeadline;
        public bool cAccepted;
    }

    [Serializable]
    public class NodeSave
    {
        public int ore;
        public int respawn;
        public float richness;   // hidden density read by prospecting
    }

    [Serializable]
    public class SaveData
    {
        public long tick;
        public int seed;   // the mountain seed: underground layout regenerates from it
        public List<CharSave> chr = new List<CharSave>();
        public List<NodeSave> nodes = new List<NodeSave>();
        public int[] instability = new int[Bands.Count];
    }

    // XP curve from the handoff: XP to complete level L = floor(80 * 1.09^L),
    // capped at level 90. Shared by server (authoritative) and HUD (display).
    public static class XpCurve
    {
        public const int MaxLevel = 90;

        public static int Level(int xp)
        {
            int L = 1;
            int rem = xp;
            while (L < MaxLevel)
            {
                int n = Mathf.FloorToInt(80f * Mathf.Pow(1.09f, L));
                if (rem < n) break;
                rem -= n;
                L++;
            }
            return L;
        }
    }
}
