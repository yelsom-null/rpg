using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Mining depth systems (Phase 2) — the Depth Matrix's MVP row for Mining,
    //  every lens server-side:
    //    Risk     — band instability with audible tells, cave-ins, timber shoring
    //    Reading  — prospect-tap density readings; seams revealed on depletion
    //    The verb — weak-point rhythm strikes; sloppy hits crumble ore to rubble
    //    Place    — three named depth bands with their own ores and hazards
    //    Economy  — poor/pure/pristine grades from strike skill and depth
    //    Social   — the two-player wedge technique; master perks change the verb
    // ============================================================================
    public partial class ZoneServer
    {
        // ------------------------------------------------------------------ swings
        void DoSwing(string id, PlayerState p, CharacterRecord rec, OreNode node)
        {
            int lvl = XpCurve.Level(rec.xp);
            int pick = Items.PickaxeTier(rec.pickaxe);

            // Wedge partnership: someone else holding the wedge on this node
            // makes the swing faster and safer ("faster and safer" — the matrix).
            PlayerState partner = null;
            foreach (PlayerState o in players.Values)
                if (o != p && o.wedgeAt == node.id) { partner = o; break; }

            // --- strike quality -> ore grade ---
            int grade;          // -1 = rubble (no ore)
            bool aimed = p.strikeArmed;
            p.strikeArmed = false;
            if (aimed)
            {
                // Weak-point window: the weak tick itself, one tick early (the
                // ±150 ms grace scaled to the tick grid), and a fine pickaxe
                // widens the window by one more tick.
                int tempo = Bands.All[node.band].tempo;
                bool hit = tick % tempo == node.phase
                        || (tick + 1) % tempo == node.phase
                        || (pick >= 2 && (tick + 2) % tempo == node.phase);
                if (hit) grade = 2; // clean pristine ore
                else if (lvl >= 5) grade = 0;  // Steady Hands: sloppy, not ruined
                else grade = -1;               // crumbled to rubble
            }
            else
            {
                float pristine = 0.05f + 0.03f * node.band + 0.02f * pick + lvl * 0.003f + node.richness * 0.08f;
                float pure = 0.35f + 0.05f * node.band + 0.03f * pick;
                if (partner != null) pristine += 0.10f;
                float roll = Random.value;
                grade = roll < pristine ? 2 : roll < pristine + pure ? 1 : 0;
            }

            node.ore--;
            instability[node.band] += Bands.All[node.band].swingInstability * (partner != null ? 0.5f : 1f);

            if (grade >= 0)
            {
                int qty = 1;
                // Seam-following: mining the revealed continuation runs richer.
                if (node.seamUntil > tick && Random.value < 0.35f) qty = 2;
                Give(rec, Items.Ore(node.metal, grade), qty);
                events.Add(new GameEvent
                {
                    type = EventType.OreGained, who = id, item = Items.Ore(node.metal, grade), qty = qty,
                    text = grade == 2 ? "a clean strike — pristine " + node.metal : null,
                });
                GrantMiningXp(id, p, rec, grade == 2 ? 38 : 28);
                if (partner != null)
                    GrantMiningXp(partner.id, partner, chr[partner.id], 14);
            }
            else
            {
                events.Add(new GameEvent { type = EventType.ForgeFail, who = id, text = "sloppy hit — the ore crumbles to rubble" });
                GrantMiningXp(id, p, rec, 6);
            }

            if (node.ore <= 0)
            {
                node.respawn = 20;
                events.Add(new GameEvent { type = EventType.Depleted, id = node.id, who = id, name = p.name });
                p.mineId = null;
                p.anim = "idle";
                RevealSeam(node);
            }
        }

        // "Veins have direction — follow the seam right and it yields more."
        // When a node pinches out, the nearest live node in the band carries the
        // seam for a while and yields double swings ~35% of the time.
        void RevealSeam(OreNode from)
        {
            OreNode best = null;
            float bd = float.MaxValue;
            foreach (OreNode n in nodes)
            {
                if (n == from || n.band != from.band || n.ore <= 0) continue;
                float dx = n.x - from.x, dz = n.z - from.z;
                float d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = n; }
            }
            if (best == null) return;
            best.seamUntil = tick + 60;
            events.Add(new GameEvent { type = EventType.Seam, id = best.id, band = from.band, text = "the seam runs on — a nearby rock glitters" });
        }

        // ------------------------------------------------------------- prospecting
        void DoProspect(PlayerState p, CharacterRecord rec, string nodeId)
        {
            OreNode n = FindNode(nodeId);
            if (n == null || n.band != p.band) return;
            if (!Near(p, new Vector2(n.x, n.z), NodeReach + 1.5f)) { Fail(p.id, "too far to tap the wall"); return; }

            events.Add(MakeProspect(p.id, n));

            // Master perk (Mining 8): hear ore through walls — the tap reads the
            // whole band.
            if (XpCurve.Level(rec.xp) >= 8)
                foreach (OreNode o in nodes)
                    if (o != n && o.band == p.band && o.ore > 0)
                        events.Add(MakeProspect(p.id, o));
        }

        GameEvent MakeProspect(string who, OreNode n)
        {
            string density = n.ore <= 0 ? "spent — nothing but rubble"
                : n.richness > 0.66f ? "rich — the wall rings full"
                : n.richness > 0.33f ? "fair — a steady seam"
                : "lean — barely a whisper";
            return new GameEvent { type = EventType.Prospected, who = who, id = n.id, text = n.metal + ": " + density };
        }

        // ------------------------------------------------------- instability & risk
        void TickInstability()
        {
            for (int b = 1; b < Bands.Count; b++)
            {
                float before = instability[b];

                // Slow natural settling.
                instability[b] = Mathf.Max(0f, instability[b] - 0.15f);

                // Tells on crossing thresholds upward (read the tunnel, not a number).
                foreach (int th in new[] { 40, 70, 90 })
                    if (before < th && instability[b] >= th)
                        events.Add(new GameEvent { type = EventType.Creak, band = b, amount = Mathf.RoundToInt(instability[b]), text = Bands.Tell(Mathf.RoundToInt(instability[b])) });

                // Cave-in risk grows past 60.
                if (instability[b] > 60f && Random.value < (instability[b] - 60f) / 40f * 0.05f)
                    CaveIn(b);
            }
        }

        void CaveIn(int band)
        {
            events.Add(new GameEvent { type = EventType.CaveIn, band = band, text = Bands.All[band].name + " comes down!" });
            instability[band] = 25f;

            foreach (OreNode n in nodes)
            {
                if (n.band != band) continue;
                n.ore = 0;
                n.respawn = 30;
                n.seamUntil = 0;
            }

            foreach (PlayerState p in players.Values)
            {
                if (p.band != band) continue;
                CharacterRecord rec = chr[p.id];

                // Lose ~30% of each carried ore stack in the scramble out.
                List<string> keys = new List<string>(rec.bag.Keys);
                foreach (string k in keys)
                    if (k.StartsWith("ore.")) rec.bag[k] = Mathf.CeilToInt(rec.bag[k] * 0.7f);

                // Master perk (Mining 12): cave-ins shake a gem loose for you.
                if (XpCurve.Level(rec.xp) >= 12)
                {
                    Give(rec, Items.Gem, 1);
                    events.Add(new GameEvent { type = EventType.Perk, who = p.id, text = "a gem glitters in the rubble" });
                }

                MovePlayerToBand(p, rec, 0);
                events.Add(new GameEvent { type = EventType.BandMoved, who = p.id, band = 0 });
            }
        }

        // Shoring: spend timber to prop the shaft and reset some risk.
        void DoShore(PlayerState p, CharacterRecord rec)
        {
            if (p.band == 0) { Fail(p.id, "nothing to shore up here"); return; }
            if (!Take(rec, Items.Timber, 1)) { Fail(p.id, "no timber to shore with — the stall sells it"); return; }
            instability[p.band] = Mathf.Max(0f, instability[p.band] - 30f);
            events.Add(new GameEvent { type = EventType.Shored, who = p.id, band = p.band, amount = Mathf.RoundToInt(instability[p.band]) });
        }

        // ------------------------------------------------------------- band travel
        void DoBandMove(PlayerState p, CharacterRecord rec, int delta)
        {
            int target = p.band + delta;
            if (target < 0 || target >= Bands.Count) return;

            // Reach: at the surface you descend at the mine entrance; underground
            // the chamber shaft (its centre) serves both directions.
            bool near = p.band == 0
                ? Near(p, EntrancePos, StationReach)
                : Near(p, new Vector2(Bands.All[p.band].originX, Bands.All[p.band].originZ), StationReach + 2f);
            if (!near) { Fail(p.id, p.band == 0 ? "the shaft mouth is in Grey Quarry, east of the city" : "the shaft ladder is at the chamber's heart"); return; }

            MovePlayerToBand(p, rec, target);
            events.Add(new GameEvent { type = EventType.BandMoved, who = p.id, band = target });
        }

        void MovePlayerToBand(PlayerState p, CharacterRecord rec, int band)
        {
            (float x, float z) = Bands.Spawn(band);
            p.band = band;
            p.x = x; p.z = z;
            p.hasTarget = false;
            p.mineId = null;
            p.wedgeMode = false;
            p.wedgeAt = null;
            p.anim = "idle";
            CancelCrafts(p, refund: true);
            rec.band = band;
            rec.x = x; rec.z = z;
        }

        // ------------------------------------------------------------------- perks
        // Announced when a level-up crosses a perk threshold ("master perks
        // change the verb").
        void AnnouncePerks(string id, int before, int after)
        {
            if (before < 5 && after >= 5)
                events.Add(new GameEvent { type = EventType.Perk, who = id, text = "Steady Hands — off-rhythm strikes no longer crumble the ore" });
            if (before < 8 && after >= 8)
                events.Add(new GameEvent { type = EventType.Perk, who = id, text = "Seam Sense — a prospect-tap now reads the whole band" });
            if (before < 12 && after >= 12)
                events.Add(new GameEvent { type = EventType.Perk, who = id, text = "Gem Eye — cave-ins shake a gem loose for you" });
        }

        // -------------------------------------------------------------- bag helpers
        static void Give(CharacterRecord rec, string item, int qty)
        {
            rec.bag.TryGetValue(item, out int have);
            rec.bag[item] = have + qty;
        }

        static bool Take(CharacterRecord rec, string item, int qty)
        {
            if (!rec.bag.TryGetValue(item, out int have) || have < qty) return false;
            if (have == qty) rec.bag.Remove(item);
            else rec.bag[item] = have - qty;
            return true;
        }

        static int Count(CharacterRecord rec, string item)
        {
            return rec.bag.TryGetValue(item, out int have) ? have : 0;
        }
    }
}
