using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Persistence model. In the browser prototype this was localStorage; here it
    //  is a JSON file under Application.persistentDataPath. Character truth (xp,
    //  position) and disposable zone state (node ore/respawn) both round-trip so
    //  the "state survives a redeploy" exit test holds after a server restart.
    //  JsonUtility-friendly: [Serializable] classes, no dictionaries.
    // ============================================================================

    [Serializable]
    public class CharSave
    {
        public string id;
        public int xp;
        public float x;
        public float z;
    }

    [Serializable]
    public class NodeSave
    {
        public int ore;
        public int respawn;
    }

    [Serializable]
    public class SaveData
    {
        public long tick;
        public List<CharSave> chr = new List<CharSave>();
        public List<NodeSave> nodes = new List<NodeSave>();
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
