using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Items — the Phase 2 catalogue: ores by metal and grade, bars, tools, and
    //  the depth-band definitions. Item ids are flat strings ("ore.copper.pure")
    //  so inventories are simple string->count maps and saves stay JsonUtility-
    //  friendly. Prices live here too: the stall is the NPC buyer of last resort
    //  (the design's "degrade gracefully to low population" rule).
    // ============================================================================

    public static class Items
    {
        public const string Timber = "timber";
        public const string Coal = "coal";
        public const string Gem = "gem";

        public static readonly string[] Grades = { "poor", "pure", "pristine" };
        public static readonly string[] GradeLabel = { "poor", "pure", "PRISTINE" };

        public static string Ore(string metal, int grade) => "ore." + metal + "." + Grades[grade];
        public static string Bar(string metal, int grade) => "bar." + metal + "." + Grades[grade];

        // Pickaxe tiers. Higher tier = wider strike window + better grade luck.
        public static readonly string[] PickaxeTiers = { "pickaxe.worn", "pickaxe.sound", "pickaxe.fine", "pickaxe.masterwork" };

        public static int PickaxeTier(string id)
        {
            for (int i = 0; i < PickaxeTiers.Length; i++) if (PickaxeTiers[i] == id) return i;
            return 0;
        }

        // Stall sell prices (gold per unit). Anything not listed can't be sold.
        static readonly Dictionary<string, int> SellPrice = new Dictionary<string, int>
        {
            { "ore.copper.poor", 3 },  { "ore.copper.pure", 6 },  { "ore.copper.pristine", 14 },
            { "ore.tin.poor", 4 },     { "ore.tin.pure", 8 },     { "ore.tin.pristine", 18 },
            { "ore.iron.poor", 6 },    { "ore.iron.pure", 12 },   { "ore.iron.pristine", 26 },
            { "bar.copper.poor", 14 }, { "bar.copper.pure", 24 }, { "bar.copper.pristine", 46 },
            { "bar.bronze.poor", 20 }, { "bar.bronze.pure", 34 }, { "bar.bronze.pristine", 62 },
            { "bar.iron.poor", 28 },   { "bar.iron.pure", 46 },   { "bar.iron.pristine", 80 },
            { "blade.crude", 30 },     { "blade.sound", 70 },     { "blade.fine", 140 },      { "blade.masterwork", 260 },
            { "pickaxe.sound", 60 },   { "pickaxe.fine", 130 },   { "pickaxe.masterwork", 240 },
            { Gem, 120 },
        };

        // Stall buy prices.
        static readonly Dictionary<string, int> BuyPrice = new Dictionary<string, int>
        {
            { Timber, 12 },
            { Coal, 8 },
            { "pickaxe.sound", 120 },
        };

        public static int PriceToSell(string item)
        {
            // Maker-marked pieces ("blade.fine#You") price by their base id.
            int hash = item.IndexOf('#');
            string key = hash >= 0 ? item.Substring(0, hash) : item;
            return SellPrice.TryGetValue(key, out int p) ? p : 0;
        }

        public static int PriceToBuy(string item) => BuyPrice.TryGetValue(item, out int p) ? p : 0;

        public static readonly string[] StallStock = { Timber, Coal, "pickaxe.sound" };

        // Human-readable name for the HUD ("ore.copper.pristine" -> "pristine copper ore").
        public static string Pretty(string item)
        {
            int hash = item.IndexOf('#');
            string maker = hash >= 0 ? item.Substring(hash + 1) : null;
            string key = hash >= 0 ? item.Substring(0, hash) : item;
            string[] parts = key.Split('.');
            string s;
            if (parts[0] == "ore" && parts.Length == 3) s = parts[2] + " " + parts[1] + " ore";
            else if (parts[0] == "bar" && parts.Length == 3) s = parts[2] + " " + parts[1] + " bar";
            else if (parts[0] == "pickaxe" && parts.Length == 2) s = parts[1] + " pickaxe";
            else if (parts[0] == "blade" && parts.Length == 2) s = parts[1] + " blade";
            else s = key;
            return maker != null ? s + " ⚔ " + maker : s;
        }
    }

    // ---------------------------------------------------------------------------
    //  Smelting and forging recipes.
    // ---------------------------------------------------------------------------
    public class SmeltRecipe
    {
        public string id;            // "smelt.copper" …
        public string label;
        public string metalOut;      // bar metal produced
        public (string metal, int qty)[] ores;
        public int coal;
        public int ticks;            // smelting duration
    }

    public class ForgeRecipe
    {
        public string id;            // "forge.pickaxe" | "forge.blade"
        public string label;
        public string outBase;       // "pickaxe" | "blade"
        public int bars;             // bars of any one metal consumed
    }

    public static class Recipes
    {
        public static readonly SmeltRecipe[] Smelting =
        {
            new SmeltRecipe { id = "smelt.copper", label = "Copper bar  (2 copper ore + 1 coal)",
                metalOut = "copper", ores = new[] { ("copper", 2) }, coal = 1, ticks = 4 },
            new SmeltRecipe { id = "smelt.bronze", label = "Bronze bar  (1 copper + 1 tin + 1 coal)",
                metalOut = "bronze", ores = new[] { ("copper", 1), ("tin", 1) }, coal = 1, ticks = 5 },
            new SmeltRecipe { id = "smelt.iron", label = "Iron bar  (2 iron ore + 2 coal)",
                metalOut = "iron", ores = new[] { ("iron", 2) }, coal = 2, ticks = 6 },
        };

        public static readonly ForgeRecipe[] Forging =
        {
            new ForgeRecipe { id = "forge.pickaxe", label = "Pickaxe  (2 bars)", outBase = "pickaxe", bars = 2 },
            new ForgeRecipe { id = "forge.blade", label = "Blade  (3 bars)", outBase = "blade", bars = 3 },
        };

        public static SmeltRecipe FindSmelt(string id)
        {
            foreach (SmeltRecipe r in Smelting) if (r.id == id) return r;
            return null;
        }

        public static ForgeRecipe FindForge(string id)
        {
            foreach (ForgeRecipe r in Forging) if (r.id == id) return r;
            return null;
        }
    }

    // ---------------------------------------------------------------------------
    //  Depth bands — "named depth bands, each with own ores, hazards, ambience".
    //  Band 0 is the surface hillside; bands 1-2 are underground chambers the
    //  renderer builds at a world offset so one coordinate space serves all.
    // ---------------------------------------------------------------------------
    public class BandDef
    {
        public string name;
        public float originX, originZ;   // chamber centre in world space
        public float radius;             // walkable clamp radius (0 = surface rect)
        public int tempo;                // strike rhythm (weak tick every N)
        public float swingInstability;   // instability added per swing
        public string ambience;          // HUD line
    }

    public static class Bands
    {
        public static readonly BandDef[] All =
        {
            new BandDef { name = "Grey Quarry", originX = 0, originZ = 0, radius = 0,
                          tempo = 4, swingInstability = 0f, ambience = "pick-song and open sky at the quarry rim" },
            new BandDef { name = "Greyroot Gallery", originX = 220, originZ = 0, radius = 16f,
                          tempo = 5, swingInstability = 1.3f, ambience = "drips echo off grey stone" },
            new BandDef { name = "Deepseam Hollow", originX = 440, originZ = 0, radius = 14f,
                          tempo = 6, swingInstability = 2.2f, ambience = "the dark presses in; timber groans" },
        };

        public const int Count = 3;

        // Where a player stands right after moving between bands.
        public static (float x, float z) Spawn(int band)
        {
            if (band == 0) return (34f, 0f); // beside the quarry shaft mouth
            BandDef b = All[band];
            return (b.originX, b.originZ + 6f);
        }

        // Instability tells, worst first. amount thresholds match MiningSystems.
        public static string Tell(int inst)
        {
            if (inst >= 90) return "the whole gallery RUMBLES — get out or shore it";
            if (inst >= 70) return "cracks race along the ceiling";
            if (inst >= 40) return "the timbers creak overhead";
            return "the rock is quiet";
        }
    }

    // ---------------------------------------------------------------------------
    //  Surface areas of the Bracken Cross map ("every gate points at a skill").
    //  Purely presentational: the HUD names where you're standing.
    // ---------------------------------------------------------------------------
    public static class Areas
    {
        public static string Name(float x, float z, int band)
        {
            if (band > 0) return Bands.All[band].name;
            if (Mathf.Abs(x) < 18f && Mathf.Abs(z) < 14f) return "Bracken Cross";
            if (x > 24f && x < 44f && z > -12f && z < 10f) return "Grey Quarry";
            if (z > 17f && x > -14f && x < 12f) return "Bracken Grove";
            if (x < -22f && z > -8f && z < 12f) return "Mirror Pond";
            if (z < -18f && Mathf.Abs(x) < 12f) return "Caravan Field";
            if (x > 16f && z < -18f) return "Redbriar March";
            return "Thornmere Reach";
        }
    }
}
