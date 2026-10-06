using System.Collections.Generic;
using Ravenholm.Content;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        /// <summary>
        /// Versioned balance revisions (GDD 28 Phase 9: "keep balance changes versioned"). Each revision is applied
        /// once, bumps the content version of what it touches, and is described in Docs/DECISIONS.md. Inspector
        /// edits made after a revision are never overwritten.
        /// </summary>
        private static void ApplyBalanceRevisions(ContentCatalogue catalogue)
        {
            var changed = new List<string>();
            void Difficulty(string quest, int from, int to)
            {
                var q = catalogue.Get<QuestDefinition>("quest." + quest);
                if (q.Difficulty != from) return;   // already edited by hand: leave it
                q.EditorSetDifficulty(to);
                q.EditorBumpVersion();
                EditorUtility.SetDirty(q);
                changed.Add($"{q.Id} D{from}→{to}");
            }

            if (catalogue.BalanceRevision < 1)
            {
                // Revision 1 (Phase 9 harness): first attempts at tier 2-3 were walls (Dangerous or Suicidal at the
                // expected level) because each job's counter is its own reward.
                Difficulty("fen_path", 23, 21);
                Difficulty("fallen_watchtower", 24, 22);
                Difficulty("hollow_warren", 26, 23);
                catalogue.EditorSetBalanceRevision(1);
                EditorUtility.SetDirty(catalogue);
            }
            if (catalogue.BalanceRevision < 2)
            {
                // Revision 2: the Rogue (traps specialist) had no weapon to carry trap jobs until Hollow Warren's own
                // reward; the starter dagger matches the starter sword, and the two trap jobs ease one more step.
                var dagger = catalogue.Get<ItemDefinition>("item.iron_dagger");
                if (dagger.Power == 7)
                {
                    dagger.EditorSetPower(8);
                    dagger.EditorBumpVersion();
                    EditorUtility.SetDirty(dagger);
                    changed.Add("item.iron_dagger Power 7→8");
                }
                Difficulty("fallen_watchtower", 22, 21);
                Difficulty("hollow_warren", 23, 22);
                catalogue.EditorSetBalanceRevision(2);
                EditorUtility.SetDirty(catalogue);
            }
            if (catalogue.BalanceRevision < 3)
            {
                // Revision 3: postgame challenge contracts were Good/Excellent with endgame gear (counters cap at +8);
                // a challenge should be Risky for a typical level-6 contractor and reach Good around level 9-10.
                Difficulty("challenge_long_hunt", 30, 36);
                Difficulty("challenge_fen_gauntlet", 31, 40);
                Difficulty("challenge_quarry_siege", 31, 37);
                Difficulty("challenge_shrine_vigil", 32, 38);
                catalogue.EditorSetBalanceRevision(3);
                EditorUtility.SetDirty(catalogue);
            }
            if (catalogue.BalanceRevision < 4)
            {
                // Revision 4: harness follow-up to revision 3: two challenges never reached Good by level 10, one was
                // still Good at level 6.
                Difficulty("challenge_long_hunt", 36, 34);
                Difficulty("challenge_quarry_siege", 37, 35);
                Difficulty("challenge_shrine_vigil", 38, 40);
                catalogue.EditorSetBalanceRevision(4);
                EditorUtility.SetDirty(catalogue);
            }
            if (changed.Count > 0) Debug.Log("[Setup] Balance revision applied: " + string.Join(", ", changed));
            AssetDatabase.SaveAssets();
        }
    }
}
