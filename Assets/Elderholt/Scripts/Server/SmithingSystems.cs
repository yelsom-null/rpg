using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Smithing MVP (Phase 2) — the consumer for ore, per the Depth Matrix:
    //    Risk     — overheat burns the billet; quench timing can crack the piece
    //    Reading  — heat judged by colour (the client shows colour, not numbers);
    //               ore grade sets the quality ceiling before you start
    //    The verb — pump / hammer rhythm in the heat window; misses add flaws
    //    Economy  — named quality tiers; masterwork blades carry the maker's mark
    //  Smelting turns graded ore + coal into graded bars at the furnace; forging
    //  turns bars into pickaxes (gear that feeds back into mining) or blades.
    // ============================================================================
    public partial class ZoneServer
    {
        public const int ForgeStrikesNeeded = 5;
        const int HeatSweetLo = 55, HeatSweetHi = 85, HeatBurn = 95;

        // ---------------------------------------------------------------- smelting
        void DoSmelt(PlayerState p, CharacterRecord rec, string recipeId)
        {
            SmeltRecipe r = Recipes.FindSmelt(recipeId);
            if (r == null) return;
            if (!Near(p, FurnacePos, StationReach)) { Fail(p.id, "the furnace is at the camp"); return; }

            CancelCrafts(p, refund: true);

            // Check everything first, then consume: ores best-grade-first (the
            // bar's grade is the worst ore that went in — grade sets the ceiling).
            foreach ((string metal, int qty) in r.ores)
                if (CountOres(rec, metal) < qty) { Fail(p.id, "not enough " + metal + " ore"); return; }
            if (Count(rec, Items.Coal) < r.coal) { Fail(p.id, "no coal for the fire — the stall sells it"); return; }

            SmeltJob job = new SmeltJob { recipe = r, gradeIn = 2, ticksLeft = r.ticks };
            foreach ((string metal, int qty) in r.ores)
                for (int i = 0; i < qty; i++)
                {
                    int g = TakeBestOre(rec, metal, job.consumed);
                    job.gradeIn = Mathf.Min(job.gradeIn, g);
                }
            Take(rec, Items.Coal, r.coal);
            job.consumed.Add(new ItemStack { item = Items.Coal, qty = r.coal });

            p.smelt = job;
            p.mineId = null; p.path.Clear(); p.wedgeAt = null;
        }

        int CountOres(CharacterRecord rec, string metal)
        {
            int total = 0;
            for (int g = 0; g < 3; g++) total += Count(rec, Items.Ore(metal, g));
            return total;
        }

        // Takes one ore of the highest grade available; records it for refunds.
        int TakeBestOre(CharacterRecord rec, string metal, List<ItemStack> consumed)
        {
            for (int g = 2; g >= 0; g--)
            {
                string item = Items.Ore(metal, g);
                if (Take(rec, item, 1))
                {
                    consumed.Add(new ItemStack { item = item, qty = 1 });
                    return g;
                }
            }
            return 0;
        }

        // ----------------------------------------------------------------- forging
        void DoForge(PlayerState p, CharacterRecord rec, string recipeId)
        {
            ForgeRecipe r = Recipes.FindForge(recipeId);
            if (r == null) return;
            if (!Near(p, AnvilPos, StationReach)) { Fail(p.id, "the anvil is at the camp"); return; }

            CancelCrafts(p, refund: true);

            // Bars of one metal, best metal first (iron > bronze > copper).
            string metal = null;
            foreach (string m in new[] { "iron", "bronze", "copper" })
                if (CountBars(rec, m) >= r.bars) { metal = m; break; }
            if (metal == null) { Fail(p.id, "needs " + r.bars + " bars of one metal — smelt some first"); return; }

            ForgeJob job = new ForgeJob { recipe = r, metal = metal, gradeIn = 2, heat = 74 };
            for (int i = 0; i < r.bars; i++)
            {
                for (int g = 2; g >= 0; g--)
                {
                    string item = Items.Bar(metal, g);
                    if (Take(rec, item, 1))
                    {
                        job.consumed.Add(new ItemStack { item = item, qty = 1 });
                        job.gradeIn = Mathf.Min(job.gradeIn, g);
                        break;
                    }
                }
            }

            p.forge = job;
            p.mineId = null; p.path.Clear(); p.wedgeAt = null;
        }

        void DoPump(PlayerState p)
        {
            ForgeJob f = p.forge;
            if (f == null || f.awaitingQuench) return;
            f.heat += 18;
            if (f.heat > HeatBurn)
            {
                // Overheat: the billet burns. One bar is ruined; the rest return.
                if (f.consumed.Count > 0) f.consumed.RemoveAt(0);
                foreach (ItemStack it in f.consumed) Give(chr[p.id], it.item, it.qty);
                p.forge = null;
                events.Add(new GameEvent { type = EventType.ForgeFail, who = p.id, text = "the billet flares white and burns — a bar is ruined" });
            }
        }

        void DoHammer(PlayerState p, CharacterRecord rec)
        {
            ForgeJob f = p.forge;
            if (f == null || f.awaitingQuench) return;

            if (f.heat < HeatSweetLo)
            {
                events.Add(new GameEvent { type = EventType.ForgeFail, who = p.id, text = "too cold — the hammer skips off dull metal" });
            }
            else if (f.heat > HeatSweetHi)
            {
                f.flaws++;
                events.Add(new GameEvent { type = EventType.ForgeFail, who = p.id, text = "too hot — the metal splashes; a flaw sets in" });
            }
            else
            {
                f.strikes++;
                GrantSmithXp(p.id, p, rec, 8);
                if (f.strikes >= ForgeStrikesNeeded)
                {
                    f.awaitingQuench = true;
                    f.quenchBy = tick + 2;   // quench window: now — hesitate and it cracks
                }
            }
        }

        void DoQuench(PlayerState p, CharacterRecord rec)
        {
            ForgeJob f = p.forge;
            if (f == null) return;

            if (!f.awaitingQuench)
            {
                // Quenched a half-worked piece: it cracks to crude.
                FinishForge(p, rec, f, 0, "quenched too soon — the piece cracks");
                return;
            }
            // In-window quench (lateness is handled by TickCrafting).
            int q = 1 + f.gradeIn - f.flaws;   // grade sets the ceiling, flaws pull it down
            q = Mathf.Clamp(q, 0, 3);
            FinishForge(p, rec, f, q, null);
        }

        static readonly string[] QualityTiers = { "crude", "sound", "fine", "masterwork" };

        void FinishForge(PlayerState p, CharacterRecord rec, ForgeJob f, int quality, string flavour)
        {
            p.forge = null;
            string tier = QualityTiers[quality];
            string item;
            if (f.recipe.outBase == "pickaxe")
            {
                item = "pickaxe." + (quality == 0 ? "worn" : tier);
                int newTier = Items.PickaxeTier(item);
                if (newTier > Items.PickaxeTier(rec.pickaxe))
                {
                    rec.pickaxe = item;   // straight to the belt
                }
                else Give(rec, item, 1);
            }
            else
            {
                // Blades carry the maker's mark — provenance is the economy hook.
                item = "blade." + tier + "#" + p.name;
                Give(rec, item, 1);
            }

            GrantSmithXp(p.id, p, rec, 40 + quality * 20);
            events.Add(new GameEvent
            {
                type = EventType.Forged, who = p.id, item = item,
                text = flavour ?? (quality == 3 ? "a masterwork — it sings off the anvil" : "the " + tier + " piece cools in your hands"),
            });
        }

        int CountBars(CharacterRecord rec, string metal)
        {
            int total = 0;
            for (int g = 0; g < 3; g++) total += Count(rec, Items.Bar(metal, g));
            return total;
        }

        // ------------------------------------------------------------- tick driver
        void TickCrafting()
        {
            foreach (KeyValuePair<string, PlayerState> kv in players)
            {
                PlayerState p = kv.Value;
                CharacterRecord rec = chr[kv.Key];

                if (p.smelt != null)
                {
                    p.smelt.ticksLeft--;
                    if (p.smelt.ticksLeft <= 0)
                    {
                        string bar = Items.Bar(p.smelt.recipe.metalOut, p.smelt.gradeIn);
                        Give(rec, bar, 1);
                        GrantSmithXp(kv.Key, p, rec, 20);
                        events.Add(new GameEvent { type = EventType.Smelted, who = kv.Key, item = bar });
                        p.smelt = null;
                    }
                }

                if (p.forge != null)
                {
                    ForgeJob f = p.forge;
                    if (f.awaitingQuench)
                    {
                        if (tick > f.quenchBy)
                            FinishForge(p, rec, f, 0, "left glowing too long — the piece cracks in the air");
                    }
                    else
                    {
                        f.heat = Mathf.Max(0, f.heat - 7);
                        events.Add(new GameEvent { type = EventType.ForgeTick, who = kv.Key, amount = f.heat, lvl = f.strikes, qty = f.flaws });
                    }
                }
            }
        }

        // Cancel any live craft (movement, band travel, re-task). Refund what
        // went in — the risk lens lives in heat and timing, not in griefing
        // yourself by clicking the ground.
        void CancelCrafts(PlayerState p, bool refund)
        {
            CharacterRecord rec = chr[p.id];
            if (p.smelt != null)
            {
                if (refund) foreach (ItemStack it in p.smelt.consumed) Give(rec, it.item, it.qty);
                p.smelt = null;
            }
            if (p.forge != null)
            {
                if (refund) foreach (ItemStack it in p.forge.consumed) Give(rec, it.item, it.qty);
                p.forge = null;
            }
        }
    }
}
