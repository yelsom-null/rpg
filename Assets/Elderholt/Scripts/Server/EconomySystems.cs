using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Economy (Phase 2) — the first-pass market stall and the NPC contract
    //  board, closing the ore → gold → gear loop. The stall is the buyer of last
    //  resort (markets must degrade gracefully to low population); contracts are
    //  the Depth Matrix's "40 pristine copper by Friday's caravan" spec orders.
    //  Per the tech doc, economic actions write through: every trade triggers an
    //  immediate save in the same tick it is acknowledged.
    // ============================================================================
    public partial class ZoneServer
    {
        const int ContractOfferEvery = 30;    // ticks between offer rolls
        const int ContractDuration = 400;     // ticks from offer to caravan

        // ------------------------------------------------------------------ stall
        void DoSell(PlayerState p, CharacterRecord rec, string item, int qty)
        {
            if (!Near(p, StallPos, StationReach)) { Fail(p.id, "the stall is at the camp"); return; }

            int earned = 0, sold = 0;
            if (item == "ALL")
            {
                List<string> keys = new List<string>(rec.bag.Keys);
                foreach (string k in keys)
                {
                    int price = Items.PriceToSell(k);
                    if (price <= 0) continue;
                    int n = rec.bag[k];
                    Take(rec, k, n);
                    earned += price * n;
                    sold += n;
                }
            }
            else
            {
                int price = Items.PriceToSell(item);
                if (price <= 0) { Fail(p.id, "the keeper shrugs — no market for that"); return; }
                int have = Count(rec, item);
                int n = qty <= 0 ? have : Mathf.Min(qty, have);
                if (n <= 0) { Fail(p.id, "nothing of that to sell"); return; }
                Take(rec, item, n);
                earned = price * n;
                sold = n;
            }

            if (sold == 0) { Fail(p.id, "nothing the stall wants"); return; }
            rec.gold += earned;
            events.Add(new GameEvent { type = EventType.Traded, who = p.id, gold = earned, text = "sold " + sold + " for " + earned + "g" });
            Save();   // economic write-through
        }

        void DoBuy(PlayerState p, CharacterRecord rec, string item)
        {
            if (!Near(p, StallPos, StationReach)) { Fail(p.id, "the stall is at the camp"); return; }
            int price = Items.PriceToBuy(item);
            if (price <= 0) { Fail(p.id, "not in stock"); return; }
            if (rec.gold < price) { Fail(p.id, "not enough gold (" + price + "g)"); return; }

            if (item.StartsWith("pickaxe."))
            {
                if (Items.PickaxeTier(item) <= Items.PickaxeTier(rec.pickaxe))
                {
                    Fail(p.id, "the keeper eyes your pickaxe: \"yours is as good.\"");
                    return;
                }
                rec.gold -= price;
                rec.pickaxe = item;
            }
            else
            {
                rec.gold -= price;
                Give(rec, item, 1);
            }
            events.Add(new GameEvent { type = EventType.Traded, who = p.id, gold = -price, text = "bought " + Items.Pretty(item) + " for " + price + "g" });
            Save();   // economic write-through
        }

        // ------------------------------------------------------------------ vault
        // The Vault: Bracken Cross's bank. Banked goods are character truth, not
        // carried inventory — cave-ins can't touch them ("walls = safety").
        static readonly string[] Bankable = { "ore.", "bar.", "blade.", Items.Gem };

        static bool IsBankable(string item)
        {
            foreach (string prefix in Bankable)
                if (item == prefix || item.StartsWith(prefix)) return true;
            return false;
        }

        void DoVaultDeposit(PlayerState p, CharacterRecord rec)
        {
            if (!Near(p, VaultPos, StationReach)) { Fail(p.id, "the Vault is in the city's north-east"); return; }
            int moved = 0;
            List<string> keys = new List<string>(rec.bag.Keys);
            foreach (string k in keys)
            {
                if (!IsBankable(k)) continue;
                int n = rec.bag[k];
                rec.bag.Remove(k);
                rec.vault.TryGetValue(k, out int have);
                rec.vault[k] = have + n;
                moved += n;
            }
            if (moved == 0) { Fail(p.id, "nothing the Vault will take — ores, bars, blades and gems only"); return; }
            events.Add(new GameEvent { type = EventType.Traded, who = p.id, gold = 0, text = "banked " + moved + " items — safe from cave-ins now" });
            Save();   // the ledger is the one thing that must not lie
        }

        void DoVaultWithdraw(PlayerState p, CharacterRecord rec)
        {
            if (!Near(p, VaultPos, StationReach)) { Fail(p.id, "the Vault is in the city's north-east"); return; }
            if (rec.vault.Count == 0) { Fail(p.id, "your vault box is empty"); return; }
            int moved = 0;
            foreach (KeyValuePair<string, int> it in rec.vault)
            {
                rec.bag.TryGetValue(it.Key, out int have);
                rec.bag[it.Key] = have + it.Value;
                moved += it.Value;
            }
            rec.vault.Clear();
            events.Add(new GameEvent { type = EventType.Traded, who = p.id, gold = 0, text = "withdrew " + moved + " items from the Vault" });
            Save();
        }

        // -------------------------------------------------------------- contracts
        void TickContracts()
        {
            foreach (KeyValuePair<string, PlayerState> kv in players)
            {
                CharacterRecord rec = chr[kv.Key];

                if (rec.contract != null && tick > rec.contract.deadline)
                {
                    if (rec.contract.accepted)
                        events.Add(new GameEvent { type = EventType.Contract, who = kv.Key, gold = 0, text = "the caravan left without your order" });
                    rec.contract = null;
                }

                if (rec.contract == null && tick % ContractOfferEvery == 0)
                    OfferContract(kv.Key, rec);
            }
        }

        void OfferContract(string id, CharacterRecord rec)
        {
            float mroll = Random.value;
            string metal = mroll < 0.5f ? "copper" : mroll < 0.8f ? "tin" : "iron";
            int grade = Random.value < 0.3f ? 2 : 1;
            int qty = 4 + Mathf.FloorToInt(Random.value * 4f);   // 4–7
            string item = Items.Ore(metal, grade);
            int gold = Mathf.CeilToInt(Items.PriceToSell(item) * qty * 1.7f);

            rec.contract = new ContractSnap
            {
                item = item, qty = qty, gold = gold,
                deadline = tick + ContractDuration, accepted = false,
            };
            events.Add(new GameEvent
            {
                type = EventType.Contract, who = id, gold = gold,
                text = "caravan order: " + qty + "× " + Items.Pretty(item) + " — " + gold + "g",
            });
        }

        void DoAcceptContract(PlayerState p, CharacterRecord rec)
        {
            if (!Near(p, BoardPos, StationReach)) { Fail(p.id, "the notice board is at the camp"); return; }
            if (rec.contract == null || rec.contract.accepted) return;
            rec.contract.accepted = true;
            events.Add(new GameEvent { type = EventType.Contract, who = p.id, gold = rec.contract.gold, text = "contract signed — the caravan waits for no one" });
        }

        void DoDeliverContract(PlayerState p, CharacterRecord rec)
        {
            if (!Near(p, BoardPos, StationReach)) { Fail(p.id, "the notice board is at the camp"); return; }
            ContractSnap c = rec.contract;
            if (c == null || !c.accepted) { Fail(p.id, "no signed contract to deliver on"); return; }
            if (Count(rec, c.item) < c.qty) { Fail(p.id, "short of the order: needs " + c.qty + "× " + Items.Pretty(c.item)); return; }

            Take(rec, c.item, c.qty);
            rec.gold += c.gold;
            rec.contract = null;
            GrantMiningXp(p.id, p, rec, 60);
            events.Add(new GameEvent { type = EventType.Contract, who = p.id, gold = c.gold, text = "delivered — the caravan master pays " + c.gold + "g" });
            Save();   // economic write-through
        }
    }
}
