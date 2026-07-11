using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  ElderholtAssets — an optional, Inspector-assignable override for the
    //  world's models. The world normally loads bundled KayKit models by their
    //  Resources path; if an ElderholtAssets asset exists in a Resources folder
    //  (named "ElderholtAssets"), any slot you fill on it replaces that model
    //  with your own prefab — no code, no file renaming, no scene wiring beyond
    //  creating the asset. Empty slots fall back to the bundled model, and a
    //  missing model still falls back to procedural geometry. Nothing breaks.
    //
    //  HOW TO USE (in the Unity editor):
    //    1. Project window → right-click inside Assets/Elderholt/Resources →
    //       Create → Elderholt → Asset Set. (It MUST live in a Resources folder
    //       and be named "ElderholtAssets".)
    //    2. Select it. Drag your own prefabs/models onto the labelled slots, or
    //       add rows under "Extra Overrides" keyed by the model's path (e.g.
    //       "KayKit/Props/keg") to replace anything the named slots don't cover.
    //    3. Press Play. Your assets appear where the defaults used to.
    // ============================================================================
    [CreateAssetMenu(fileName = "ElderholtAssets", menuName = "Elderholt/Asset Set", order = 0)]
    public class ElderholtAssets : ScriptableObject
    {
        [Header("Nature")]
        [Tooltip("Trees in Bracken Grove. Replaces both bundled tree variants.")]
        public GameObject tree;
        [Tooltip("The ore-rock body for copper veins (Grey Quarry surface).")]
        public GameObject oreRockCopper;
        [Tooltip("The ore-rock body for tin veins (Greyroot Gallery).")]
        public GameObject oreRockTin;
        [Tooltip("The ore-rock body for iron veins (Deepseam Hollow).")]
        public GameObject oreRockIron;
        [Tooltip("Boulders: chamber walls, quarry rim, scenery. Replaces both variants.")]
        public GameObject boulder;

        [Header("Camp props")]
        public GameObject barrel;
        public GameObject crate;
        public GameObject box;
        public GameObject chest;
        public GameObject torch;
        public GameObject table;
        public GameObject pillar;

        [Header("Medieval village (e.g. Quaternius MegaKit, local import)")]
        [Tooltip("A finished house model — placed on the two built High Street lots.")]
        public GameObject house;
        [Tooltip("A finished market-stall model — replaces the procedural day stalls.")]
        public GameObject marketStall;
        [Tooltip("A well — placed as the Market Square centrepiece if assigned.")]
        public GameObject well;
        [Tooltip("A cart/wagon — placed in the Caravan Field.")]
        public GameObject cart;
        [Tooltip("A lantern/lamp post — placed at the gates and around the square.")]
        public GameObject lantern;
        [Tooltip("A fence section — lines the Caravan Field paddock if assigned.")]
        public GameObject fence;

        [System.Serializable]
        public class Override
        {
            [Tooltip("The model's Resources path, e.g. KayKit/Props/keg — or just its last name, e.g. keg.")]
            public string key;
            public GameObject prefab;
        }

        [Header("Extra overrides (by model path or name)")]
        [Tooltip("Replace any other model the named slots above don't cover.")]
        public List<Override> extraOverrides = new List<Override>();

        // path key -> prefab, built once. Named slots expand to the paths the
        // world builder actually requests.
        Dictionary<string, GameObject> map;

        void MapSlot(GameObject go, params string[] keys)
        {
            if (go == null) return;
            foreach (string k in keys) map[k] = go;
        }

        public GameObject Lookup(string path)
        {
            if (map == null)
            {
                map = new Dictionary<string, GameObject>();
                MapSlot(tree, "KayKit/Nature/tree_single_A", "KayKit/Nature/tree_single_B");
                MapSlot(oreRockCopper, "KayKit/Nature/rock_single_C");
                MapSlot(oreRockTin, "KayKit/Nature/rock_single_D");
                MapSlot(oreRockIron, "KayKit/Nature/rock_single_E");
                MapSlot(boulder, "KayKit/Nature/rock_single_A", "KayKit/Nature/rock_single_B");
                MapSlot(barrel, "KayKit/Props/barrel_large");
                MapSlot(crate, "KayKit/Props/crates_stacked");
                MapSlot(box, "KayKit/Props/box_small");
                MapSlot(chest, "KayKit/Props/chest");
                MapSlot(torch, "KayKit/Props/torch_lit");
                MapSlot(table, "KayKit/Props/table_long");
                MapSlot(pillar, "KayKit/Props/pillar");
                MapSlot(house, "Village/House");
                MapSlot(marketStall, "Village/Stall");
                MapSlot(well, "Village/Well");
                MapSlot(cart, "Village/Cart");
                MapSlot(lantern, "Village/Lantern");
                MapSlot(fence, "Village/Fence");

                foreach (Override o in extraOverrides)
                    if (o != null && o.prefab != null && !string.IsNullOrEmpty(o.key))
                        map[o.key.Trim()] = o.prefab;
            }

            if (map.TryGetValue(path, out GameObject byPath)) return byPath;
            int slash = path.LastIndexOf('/');
            if (slash >= 0 && map.TryGetValue(path.Substring(slash + 1), out GameObject byName)) return byName;
            return null;
        }
    }

    // Central resolver: an assigned override wins; otherwise the bundled model.
    public static class AssetLibrary
    {
        static bool loaded;
        static ElderholtAssets set;

        public static ElderholtAssets Set
        {
            get
            {
                if (!loaded) { set = Resources.Load<ElderholtAssets>("ElderholtAssets"); loaded = true; }
                return set;
            }
        }

        // Returns the prefab to instantiate for a given Resources path: the
        // Inspector override if one is assigned, else the bundled model, else null.
        public static GameObject Resolve(string path)
        {
            GameObject over = Set != null ? Set.Lookup(path) : null;
            return over != null ? over : Resources.Load<GameObject>(path);
        }

        // For tests/hot-swaps: forget the cached asset so it reloads next Play.
        public static void Reset() { loaded = false; set = null; }
    }
}
