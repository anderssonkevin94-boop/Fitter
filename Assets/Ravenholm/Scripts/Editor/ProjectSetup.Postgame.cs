using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        /// <summary>
        /// Postgame challenge contracts (GDD 14: "optional challenge variants using existing systems"). They unlock
        /// after the Beacon, reuse existing threats in harder combinations, pay well and stay repeatable. Pure data:
        /// no new rules. Balance starting points.
        /// </summary>
        /// <summary>
        /// The journey, step 5 (2026-10-15): each camp's castle posts its own tier of jobs, and one of them is its summons.
        /// Frontier Paths covers camps 1-2, the Hollow March 3-4, the Crownward Ruins 5-6 (the outline); until those camps
        /// exist their jobs are posted at the last camp. Set every run (content authoring, not player data).
        /// </summary>
        private static void EnsureJourneyJobs(ContentCatalogue catalogue)
        {
            var camps = new (string id, int camp, bool summons)[]
            {
                ("local_supply_survey", 1, false), ("roadside_patrol", 1, false), ("briar_track", 1, false), ("old_shrine", 1, true),
                ("quarry_road", 2, false), ("fen_path", 2, false), ("fallen_watchtower", 2, false), ("shrine_chamber", 2, true),
                // Step 7: a summons at each of camps 3-5 (camp 6's last job is the finale itself).
                ("fur_trappers_camp", 3, false), ("bandit_ford", 3, true), ("hollow_warren", 3, false), ("mire_crossing", 3, false),
                ("witchlight_fen", 4, false), ("sunken_archive", 4, true),
                ("crownward_gate", 5, true), ("ironwood_hunt", 5, false),
                ("ashen_barracks", 6, false), ("beacon_road", 6, false), ("beacon_pass", 6, false), ("beacon_expedition", 6, false),
                ("challenge_fen_gauntlet", 6, false), ("challenge_quarry_siege", 6, false), ("challenge_long_hunt", 6, false), ("challenge_shrine_vigil", 6, false),
            };
            foreach (var (id, camp, summons) in camps)
            {
                if (!catalogue.TryGet<QuestDefinition>("quest." + id, out var q) || q.Camp == camp && q.Summons == summons) continue;
                q.EditorSetCamp(camp, summons);
                EditorUtility.SetDirty(q);
            }
            // The finale is the castle's last, greatest job (the Beacon campaign's wording retired, its jobs kept).
            void Rename(string id, string name, string brief)
            {
                if (!catalogue.TryGet<QuestDefinition>("quest." + id, out var q) || q.DisplayName == name) return;
                q.EditorInitialize(q.Id, name);
                q.EditorSetBrief(brief);
                EditorUtility.SetDirty(q);
            }
            Rename("beacon_road", "Chart the Castle Road", "A capable explorer must find a way up the last climb to the castle.");
            Rename("beacon_pass", "Hold the Castle Pass", "A seasoned fighter must clear the pass below the castle's walls.");
            Rename("beacon_expedition", "The Castle's Last Great Job",
                "The castle's last, greatest job: send an elite contractor with advanced gear and a counter to the Pale Flame at its gates.");
            if (catalogue.TryGet<RegionDefinition>("region.crownward_ruins", out var crown) && crown.Rumour.Contains("Beacon"))
            {
                crown.EditorSetupRegion(crown.Order, crown.Theme, "Beyond the March, old ruins guard the last road to the castle.");
                EditorUtility.SetDirty(crown);
            }
        }

        private static void EnsurePostgameContent(ContentCatalogue catalogue)
        {
            ThreatDefinition T(string key) => catalogue.Get<ThreatDefinition>("threat." + key);
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = R(r), quantity = qty });
                return l;
            }
            var finale = catalogue.Get<QuestDefinition>("quest.beacon_expedition");
            var crown = catalogue.Get<RegionDefinition>("region.crownward_ruins");

            var created = new List<QuestDefinition>();
            QuestDefinition Challenge(string key, string name, int d, float seconds, int gold, string brief, List<ResourceCost> success,
                params (ThreatDefinition t, bool known)[] threats)
            {
                var path = JobsDir + "quest_" + key + ".asset";
                var isNew = !File.Exists(path);
                var q = EnsureAsset<QuestDefinition>(path);
                if (isNew)
                {
                    q.EditorInitialize("quest." + key, name);
                    var list = new List<QuestThreat>();
                    foreach (var (t, known) in threats) list.Add(new QuestThreat { threat = t, knownAtStart = known });
                    q.EditorSetupQuest(d, seconds, list, gold, true, false, 90, brief);
                    q.EditorSetRegion(crown);
                    q.EditorSetupProgression(new List<QuestDefinition> { finale }, 0, 9, success, M(("gem", 1)), new List<ResourceCost>(), null, M(), R("gem"), 0.6f);
                    q.EditorSetChallenge(true);
                    EditorUtility.SetDirty(q);
                }
                created.Add(q);
                return q;
            }
            Challenge("challenge_fen_gauntlet", "Challenge: Fen Gauntlet", 31, 300f, 120,
                "Poison, witch-lights and snares on one road. Bring every counter you own.",
                M(("arcane_dust", 3), ("herb", 4)), (T("poison"), true), (T("arcane"), true), (T("traps"), true));
            Challenge("challenge_quarry_siege", "Challenge: Quarry Siege", 31, 300f, 125,
                "The quarry crew has dug in behind traps. Heavy armor and a sharp eye.",
                M(("steel_ingot", 4), ("moonsilver_ingot", 2)), (T("heavy_blows"), true), (T("traps"), true));
            Challenge("challenge_long_hunt", "Challenge: The Long Hunt", 30, 330f, 115,
                "Track the old beast through country nobody has mapped.",
                M(("ironwood_lumber", 3), ("fur", 3)), (T("exploration"), true), (T("heavy_blows"), true));
            Challenge("challenge_shrine_vigil", "Challenge: Shrine Vigil", 32, 330f, 130,
                "Keep watch in the deepest shrine for a night. The wards fight back.",
                M(("moonsilver_ingot", 3), ("arcane_dust", 2)), (T("arcane"), true), (T("exploration"), false));

            var quests = new List<QuestDefinition>(catalogue.Quests);
            var changed = false;
            foreach (var q in created) if (!quests.Contains(q)) { quests.Add(q); changed = true; }
            if (changed) { catalogue.EditorSetQuests(quests, new List<ThreatDefinition>(catalogue.Threats)); EditorUtility.SetDirty(catalogue); }
            AssetDatabase.SaveAssets();
        }
    }
}
