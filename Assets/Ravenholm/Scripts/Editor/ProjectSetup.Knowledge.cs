using System.Collections.Generic;
using Ravenholm.Content;
using UnityEditor;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        /// <summary>
        /// Knowledge stages per location (GDD 13): second success gives a reward clue, third a secret route
        /// (small quest-specific readiness bonus), and a failure can teach one basic observation. Not every
        /// location uses every stage. Only fills quests that have none authored yet.
        /// </summary>
        private static void EnsureKnowledgeContent(ContentCatalogue catalogue)
        {
            void K(string quest, string clue, string route, int bonus, string observation)
            {
                var q = catalogue.Get<QuestDefinition>("quest." + quest);
                if (q.Knowledge.Count > 0 || !string.IsNullOrEmpty(q.FailureObservation)) return;
                var steps = new List<KnowledgeStep>();
                if (clue != null) steps.Add(new KnowledgeStep { successes = 2, fact = clue });
                if (route != null) steps.Add(new KnowledgeStep { successes = 3, fact = route, readinessBonus = bonus });
                q.EditorSetKnowledge(steps, observation ?? "");
                EditorUtility.SetDirty(q);
            }
            K("roadside_patrol", "Travellers tip well: a great success here often turns up a bundle of herbs.",
                "The carters' short cut avoids the worst stretch of road.", 1, null);
            K("briar_track", "The briars hide gem-bearing scree: a great success may bring a gem home.",
                "A ranger's blaze marks the dry path through the thicket.", 2, "The trail forks constantly; someone who knows the woods would not get lost.");
            K("old_shrine", "Arcane dust gathers in the shrine's cracked basin.",
                "The quiet side entrance avoids the brightest lights.", 2, "Whatever is in there reacts to wards and mages.");
            K("quarry_road", "Abandoned carts still hold usable iron.",
                "A collapsed cutting gives cover past the quarry crew.", 2, "The quarry crew hit hard; light armor buckles.");
            K("fen_path", "Fen herbs grow thick where the path is worst.",
                "Dry tussocks mark a safer line through the water.", 2, "The water itself makes people sick; bring something against poison.");
            K("fallen_watchtower", "The tower's armory was never fully looted: steel turns up.",
                "A drain under the tower bypasses the trapped stair.", 2, "Tripwires and loose stones: someone who spots traps would help.");
            K("shrine_chamber", "Copper offerings lie scattered in the chamber.",
                null, 0, "The deeper wards are stronger than the shrine above.");
            K("fur_trappers_camp", "The trappers trade spare rope with anyone who helps them.",
                "The trappers' own trail is quicker than the map's.", 2, null);
            K("mire_crossing", "Smugglers' caches along the causeway hold herbs and rope.",
                "A pole-marked line avoids the deepest mud.", 2, "The causeway is rigged as well as poisonous.");
            K("bandit_ford", "The bandits hoard steel taken from caravans.",
                "Upstream shallows let you cross out of sight.", 2, "The bandits wear heavy armor and hit harder still.");
            K("hollow_warren", "Old miners' steel is buried in the side passages.",
                "Chalk marks from the last survivors show the safe tunnels.", 2, "Snares everywhere, and the tunnels twist: traps and the way both matter.");
            K("witchlight_fen", "Where the lights gather, arcane dust settles on the reeds.",
                null, 0, "The lights are arcane; the water is poisoned. Both need answers.");
            K("sunken_archive", "Moonsilver fittings survive on the drowned shelves.",
                "A dry gallery above the flooded stacks avoids most wards.", 2, null);
            K("crownward_gate", "The gate's armory still holds moonsilver.",
                "A breach in the north wall skips the gatehouse.", 2, null);
            K("ironwood_hunt", "The beasts nest among the best ironwood.",
                null, 0, "Something big hunts in the ironwood; armor matters.");
            K("ashen_barracks", "The barracks' quartermaster kept moonsilver back.",
                "The burned chapel's crypt leads under the guard posts.", 2, null);
            AssetDatabase.SaveAssets();
        }
    }
}
