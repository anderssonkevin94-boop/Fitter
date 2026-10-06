using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Ravenholm.Content;
using Ravenholm.Domain.Jobs;
using Ravenholm.Domain.Time;
using Ravenholm.EditorTools;
using Ravenholm.Services;
using UnityEditor;

namespace Ravenholm.Tests
{
    /// <summary>
    /// Phase 9 balance harness (GDD 15/24/29). Monte Carlo over the real resolver for every quest with the
    /// gear a player would plausibly own by then, plus the three explicit economy states. Writes
    /// Docs/BALANCE_REPORT.md. Values are hypotheses; the asserts guard the GDD's stated rules only.
    /// </summary>
    public sealed class BalanceTests : TempDirFixture
    {
        private const int Seeds = 2000;
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        private static ContentCatalogue Cat => AssetDatabase.LoadAssetAtPath<ContentCatalogue>(ContentValidationMenu.CataloguePath);
        private static BalanceProfile Balance => AssetDatabase.LoadAssetAtPath<BalanceProfile>(ProjectSetup.ConfigDir + "BalanceProfile.asset");

        /// <summary>Shadow prices for replacing materials (Trader prices where sold; assumptions otherwise).</summary>
        private static readonly Dictionary<string, int> Shadow = new Dictionary<string, int>
        {
            { "resource.iron_ingot", 3 }, { "resource.cedar_lumber", 3 }, { "resource.herb", 4 }, { "resource.rope", 8 }, { "resource.fur", 8 },
            { "resource.arcane_dust", 12 }, { "resource.copper_ingot", 3 }, { "resource.steel_ingot", 5 }, { "resource.maple_lumber", 4 },
            { "resource.moonsilver_ingot", 8 }, { "resource.ironwood_lumber", 6 }, { "resource.gem", 10 },
        };

        private static int ReplacementCost(string itemId)
        {
            var r = Cat.Recipes.FirstOrDefault(x => x.OutputItem != null && x.OutputItem.Id == itemId);
            return r == null ? 0 : r.Cost.Sum(c => Shadow.TryGetValue(c.resource.Id, out var p) ? p * c.quantity : 5 * c.quantity);
        }

        /// <summary>Blueprints owned when a quest is first attempted: starters plus its prerequisite chain's first-success rewards.</summary>
        private static HashSet<BlueprintDefinition> BlueprintsBefore(QuestDefinition q, bool includeSelf = false)
        {
            var set = new HashSet<BlueprintDefinition>(Cat.Blueprints.Where(b => b.OwnedAtStart));
            var stack = new Stack<QuestDefinition>(q.Prerequisites);
            if (includeSelf) stack.Push(q);
            var seen = new HashSet<QuestDefinition>();
            while (stack.Count > 0)
            {
                var p = stack.Pop();
                if (p == null || !seen.Add(p)) continue;
                if (p.FirstSuccessBlueprint != null) set.Add(p.FirstSuccessBlueprint);
                foreach (var x in p.Prerequisites) stack.Push(x);
            }
            return set;
        }

        /// <summary>Typical contractor level when a quest first appears (balance assumption, reported).</summary>
        private static int TypicalLevel(QuestDefinition q) => Math.Max(q.MinContractorLevel, Math.Min(6, 1 + q.MapTier * 3 / 4));

        private sealed class Row
        {
            public QuestDefinition Quest;
            public string Class;
            public int Level;
            public RiskBand Band;
            public double Great, Success, Partial, Fail, Death;
            public double Gold, Fee, PotionCost, GearLoss, Net;
            public int LevelForGood = -1;
            public RiskBand FirstBand;
            public int LevelForRiskyFirst = -1;
            public string RepeatClass = "";
            public double RepeatNet;
        }

        private GameServices Boot()
        {
            var g = new GameServices(new ManualClock(T0), Cat, Balance, Dir, "sandbox", "test", 1.5f);
            g.Start();
            return g;
        }

        private Row Simulate(GameServices g, QuestDefinition q, AdventurerClassDefinition cls, int level, IEnumerable<string> storeItems)
        {
            var log = g.Logistics.System;
            foreach (var id in log.StoreItems.ToList()) { log.TakeFromStore(new[] { id }, "balance", "reset"); log.DestroyItems(new[] { id }, "reset"); }
            foreach (var i in storeItems) log.CreateStoreItem(i, "balance");
            var board = g.Jobs.Board;
            var view = board.Quest(q.Id);
            var c = new ContractorRecord { Id = "balance." + cls.Id, Name = cls.DisplayName, ClassId = cls.Id, Level = level, PersonalityPrice = 1.0 };
            var l = board.BestAvailableLoadout(c, null);
            board.AddBestCounterPotions(l, view, null);
            var tonic = log.StoreItems.FirstOrDefault(i => log.DefinitionOfItem(i) == "item.strong_tonic") ?? log.StoreItems.FirstOrDefault(i => log.DefinitionOfItem(i) == "item.recovery_tonic");
            if (l.Potion1 == null) l.Potion1 = tonic; else if (l.Potion2 == null && tonic != null && log.DefinitionOfItem(tonic) != log.DefinitionOfItem(l.Potion1)) l.Potion2 = tonic;

            var snapshot = board.StatsWith(c, l);
            var known = view.Threats.Select(t => new ThreatSpec
            {
                Id = t.Id, CounterTag = t.CounterTag, Required = t.Required, UnmetReadiness = t.UnmetReadiness, UnmetSurvival = t.UnmetSurvival,
                ArmorThreshold = t.ArmorThreshold, ArmorReadiness = t.ArmorReadiness, ArmorSurvival = t.ArmorSurvival, Known = true,
            }).ToList();
            var potions = new List<CarriedPotion>();
            foreach (var pid in new[] { l.Potion1, l.Potion2 })
            {
                var pi = pid != null ? g.Jobs.Content.Item(log.DefinitionOfItem(pid)) : null;
                if (pi != null) potions.Add(new CarriedPotion { InstanceId = pid, DefinitionId = pi.Id, SurvivalBonus = pi.SurvivalBonus, CounterTag = pi.CounterTag });
            }
            var row = new Row { Quest = q, Class = cls.DisplayName, Level = level };
            row.Band = ForecastCalculator.Evaluate(board.Config.Resolver, snapshot, q.Difficulty, known).Band;
            double gold = 0, potionCost = 0, loss = 0;
            var gearValue = l.All().Sum(i => ReplacementCost(log.DefinitionOfItem(i)));
            for (var s = 0; s < Seeds; s++)
            {
                var r = Resolver.Resolve(board.Config.Resolver, snapshot, q.Difficulty, known, potions, (ulong)(s * 7919 + 17));
                switch (r.Outcome)
                {
                    case Outcome.GreatSuccess: row.Great++; break;
                    case Outcome.Success: row.Success++; break;
                    case Outcome.PartialInjury: row.Partial++; break;
                    case Outcome.Failure: row.Fail++; break;
                    default: row.Death++; break;
                }
                gold += Math.Floor(q.BaseGold * Resolver.GoldMultiplier(r.Outcome));
                if (r.Outcome == Outcome.Death) loss += gearValue;
                else foreach (var p in r.ConsumedPotions) potionCost += ReplacementCost(log.DefinitionOfItem(p));
            }
            row.Great /= Seeds; row.Success /= Seeds; row.Partial /= Seeds; row.Fail /= Seeds; row.Death /= Seeds;
            row.Gold = gold / Seeds;
            row.PotionCost = potionCost / Seeds;
            row.GearLoss = loss / Seeds;
            row.Fee = board.Config.Fees.Quote(cls.BaseFee, level, q.Difficulty, 1.0, q.PrestigeTarget);
            row.Net = row.Gold - row.Fee - row.PotionCost - row.GearLoss;
            return row;
        }

        private List<string> ItemsFor(QuestDefinition q, bool afterSuccess = false)
        {
            var bps = BlueprintsBefore(q, afterSuccess);
            var items = new List<string>();
            foreach (var r in Cat.Recipes)
                if (r.RequiredBlueprint != null && bps.Contains(r.RequiredBlueprint)) { items.Add(r.OutputItem.Id); items.Add(r.OutputItem.Id); }
            return items;
        }

        [Test]
        public void RepeatableContractsAtGoodRiskEarnPositiveNetGoldAndTheReportIsWritten()
        {
            var g = Boot();
            var rows = new List<Row>();
            foreach (var q in Cat.Quests.Where(x => !x.RecoveryContract).OrderBy(x => x.MapTier).ThenBy(x => x.Difficulty))
            {
                // First attempt: gear owned before it; the best class by expected net at the typical level.
                var first = ItemsFor(q);
                Row best = null;
                foreach (var cls in Cat.Classes)
                {
                    var row = Simulate(g, q, cls, TypicalLevel(q), first);
                    if (best == null || row.Net > best.Net) best = row;
                }
                best.FirstBand = Cat.Classes.Max(cls => Simulate(g, q, cls, TypicalLevel(q), first).Band);
                for (var lv = 1; lv <= 10 && best.LevelForRiskyFirst < 0; lv++)
                    if (Cat.Classes.Any(cls => Simulate(g, q, cls, lv, first).Band >= RiskBand.Risky)) best.LevelForRiskyFirst = lv;

                // Repeat runs: including the job's own reward; the lowest level where some class reaches Good, and its net.
                var repeat = ItemsFor(q, true);
                for (var lv = 1; lv <= 10 && best.LevelForGood < 0; lv++)
                    foreach (var cls in Cat.Classes)
                    {
                        var r = Simulate(g, q, cls, lv, repeat);
                        if (r.Band < RiskBand.Good) continue;
                        if (best.LevelForGood < 0 || r.Net > best.RepeatNet) { best.RepeatNet = r.Net; best.RepeatClass = $"{r.Class} L{lv}"; }
                        best.LevelForGood = lv;
                    }
                rows.Add(best);
            }
            WriteReport(rows, g);

            var problems = new List<string>();
            foreach (var r in rows)
            {
                // GDD 15: at Good risk, repeatable contracts have positive long-run net gold.
                if (r.Quest.Repeatable && (r.LevelForGood < 0 || r.LevelForGood > 8))
                    problems.Add($"{r.Quest.Id}: no class reaches Good on repeat runs by level 8");
                else if (r.Quest.Repeatable && r.RepeatNet <= 0)
                    problems.Add($"{r.Quest.Id}: Good on repeat but expected net {r.RepeatNet:0.0} ≤ 0");
                // No first-attempt walls: some class reaches Risky within three levels of the typical level.
                if (r.LevelForRiskyFirst < 0 || r.LevelForRiskyFirst > TypicalLevel(r.Quest) + 3)
                    problems.Add($"{r.Quest.Id}: first attempt not Risky until level {r.LevelForRiskyFirst} (typical {TypicalLevel(r.Quest)})");
            }
            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        // ------------------------------------------------------------------ the three economy states (GDD 15)

        /// <summary>Minutes of play needed from a state to afford a viable Roadside Patrol (fee plus starter sword and coat).</summary>
        private static double MinutesToViablePatrol(int gold, bool hasGear, bool hasMaterials)
        {
            const int fee = 13, swordAndCoatMaterials = 6 * 3 + 3 * 3;   // 6 iron, 3 cedar at Trader prices
            var need = fee + (hasGear || hasMaterials ? 0 : swordAndCoatMaterials);
            var survey = Cat.Quests.First(q => q.RecoveryContract);
            var minutes = 0.0;
            while (gold < need) { gold += survey.BaseGold; minutes += survey.DurationSeconds / 60.0 + 0.5; }   // +30 s to post and wait for the sponsor
            return minutes;
        }

        [Test]
        public void NormalStartConsecutiveDeathsAndEmptiedAccountAllRecover()
        {
            var normal = MinutesToViablePatrol(60, false, true);
            var afterDeaths = MinutesToViablePatrol(0, false, true);    // gear lost twice, gold spent; starter stock remains as shared materials
            var emptied = MinutesToViablePatrol(0, false, false);       // nothing at all: Trader emergency bundle for materials
            Assert.AreEqual(0, normal, 0.01);
            Assert.Less(afterDeaths, 15, "recovery after deaths stays short");
            Assert.Less(emptied, 30, "even an emptied account can field a patrol within half an hour (plus Trader timing)");
            Assert.IsTrue(Cat.TraderStock.Entries.Any(e => e.availability == StockAvailability.Guaranteed && e.resource.Id == "resource.iron_ingot"),
                "recovery never waits on a rare Trader roll");
        }

        private static string ReportPath => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Docs", "BALANCE_REPORT.md"));

        private static void WriteReport(List<Row> rows, GameServices g)
        {
            var sb = new StringBuilder();
            sb.Append("# Balance report (generated)\n\n");
            sb.Append($"Generated by `BalanceTests` on {DateTime.UtcNow:yyyy-MM-dd}. All values are balance hypotheses (GDD 30). ");
            sb.Append($"Each row is the best class at the assumed level, {Seeds} resolver seeds, threats treated as known, ");
            sb.Append("gear from blueprints owned by the time the quest first appears (starters plus its prerequisite chain's rewards), ");
            sb.Append("plus counter potions and a tonic. Fee at reputation = the quest's prestige target, neutral personality.\n\n");
            sb.Append("Net = expected gold − fee − expected consumed potions − death rate × carried gear replacement (shadow prices: ");
            sb.Append(string.Join(", ", Shadow.Select(kv => $"{kv.Key.Replace("resource.", "")} {kv.Value}"))).Append(").\n\n");
            sb.Append("First attempt: best class by net at the typical level with gear owned beforehand. Repeat: with the job's own reward too; ");
            sb.Append("the lowest level where some class reaches Good, and the best net there.\n\n");
            sb.Append("| Quest | D | Tier | First: class | Level | Band | Great | Success | Partial | Fail | Death | Gold | Fee | Potions | Gear loss | Net | Risky from level | Repeat: Good from | Repeat net |\n");
            sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
            foreach (var r in rows)
                sb.Append($"| {r.Quest.DisplayName}{(r.Quest.Repeatable ? "" : " (one-time)")} | {r.Quest.Difficulty} | {r.Quest.MapTier} | {r.Class} | {r.Level} | {r.Band} | ")
                  .Append($"{r.Great:P0} | {r.Success:P0} | {r.Partial:P0} | {r.Fail:P0} | {r.Death:P1} | {r.Gold:0.0} | {r.Fee:0} | {r.PotionCost:0.0} | {r.GearLoss:0.0} | **{r.Net:0.0}** | {r.LevelForRiskyFirst} | {(r.LevelForGood > 0 ? r.RepeatClass : "never")} | {(r.LevelForGood > 0 ? r.RepeatNet.ToString("0.0") : "–")} |\n");

            // Calibration: actual outcomes per displayed band across all quest × class × level samples.
            sb.Append("\n## Risk-word calibration\n\nOutcome rates by the forecast word (best rows above; small samples for rare words).\n\n");
            sb.Append("| Band | Rows | Success or better | Death |\n|---|---|---|---|\n");
            foreach (var band in new[] { RiskBand.Excellent, RiskBand.Good, RiskBand.Risky, RiskBand.Dangerous, RiskBand.Suicidal })
            {
                var b = rows.Where(r => r.Band == band).ToList();
                if (b.Count == 0) { sb.Append($"| {band} | 0 | – | – |\n"); continue; }
                sb.Append($"| {band} | {b.Count} | {b.Average(r => r.Great + r.Success):P0} | {b.Average(r => r.Death):P1} |\n");
            }
            sb.Append("\n## Recovery states (GDD 15)\n\n| State | Minutes of recovery surveys until a viable Roadside Patrol (fee + sword + coat) |\n|---|---|\n");
            sb.Append($"| Normal start (60 g, starter stock) | {MinutesToViablePatrol(60, false, true):0.0} |\n");
            sb.Append($"| After consecutive deaths (0 g, gear lost, materials left) | {MinutesToViablePatrol(0, false, true):0.0} |\n");
            sb.Append($"| Emptied account (0 g, 0 gear, 0 materials) | {MinutesToViablePatrol(0, false, false):0.0}, plus up to 20 min for the next Trader visit (its iron and cedar bundle is guaranteed) |\n");
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, sb.ToString());
        }
    }
}
