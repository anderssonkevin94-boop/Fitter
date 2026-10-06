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
