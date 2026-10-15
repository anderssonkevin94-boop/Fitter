using System.Collections.Generic;
using System.IO;
using Ravenholm.Content;
using UnityEditor;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    public static partial class ProjectSetup
    {
        /// <summary>
        /// The journey, step 7 (2026-10-16): the road to the castle, six camps along it (the outline's table), a blocker
        /// before each: old trees (iron tool), a rockslide (tempered tool), a ravine to bridge and a flood to cross (build
        /// projects), an ancient sealed gate (the moonsilver tool). Each camp's land has its own tint; one band of wood and
        /// ore per camp. Laid out again when an older road has fewer camps. Balance starting points.
        /// </summary>
        private static void EnsureRoad(ContentCatalogue catalogue)
        {
            var camp = catalogue.CurrentCamp;
            if (camp == null) return;
            ResourceDefinition R(string key) => catalogue.Get<ResourceDefinition>("resource." + key);
            List<ResourceCost> M(params (string r, int q)[] c)
            {
                var l = new List<ResourceCost>();
                foreach (var (r, qty) in c) l.Add(new ResourceCost { resource = R(r), quantity = qty });
                return l;
            }

            // Rewritten while missing or when the smoothed land ends short of Castle Approach (+30 past it).
            if (camp.Stops.Count < 5 || camp.Ridge.Length - camp.Ridge.CampS < camp.CampAhead(camp.CampCount - 1) + 30)
            {
                CampDefinition.Stretch S(bool upRight, int length) => new CampDefinition.Stretch { upRight = upRight, length = length };
                CampDefinition.StopSpec Stop(string name, int ahead, float radius, Color tint) =>
                    new CampDefinition.StopSpec { name = name, ahead = ahead, radius = radius, groundTint = tint };
                CampDefinition.GateSpec Gate(int ahead, int tier, bool rock, string project = null, List<ResourceCost> cost = null) =>
                    new CampDefinition.GateSpec { ahead = ahead, thickness = 4, tier = tier, rock = rock, project = project, projectCost = cost };
                camp.EditorSetStops(
                    new List<CampDefinition.StopSpec>
                    {
                        Stop("River Bend", 95, 13f, new Color(0.93f, 1f, 0.95f)),
                        Stop("Hollow Wood", 190, 13f, new Color(0.74f, 0.84f, 0.72f)),
                        Stop("Witchlight Fen", 285, 13f, new Color(0.78f, 0.88f, 0.96f)),
                        Stop("Crown Highlands", 380, 13f, new Color(0.92f, 0.9f, 0.84f)),
                        Stop("Castle Approach", 475, 14f, new Color(1f, 0.95f, 0.82f)),
                    },
                    new List<CampDefinition.Stretch>
                    {
                        S(false, 16), S(true, 34), S(false, 38), S(true, 30), S(false, 34), S(true, 30), S(false, 36), S(true, 34),
                        S(false, 36), S(true, 34), S(false, 32), S(true, 36), S(false, 34), S(true, 32), S(false, 34), S(true, 30), S(false, 34),
                        S(true, 34), S(false, 34), S(true, 34), S(false, 30),
                    },
                    new List<CampDefinition.GateSpec>
                    {
                        Gate(55, 2, false),                                                            // old trees: the iron tool
                        Gate(150, 3, true),                                                            // a rockslide: the tempered tool
                        Gate(245, 0, false, "Bridge", M(("ironwood_lumber", 12), ("rope", 8))),     // a ravine: build a bridge
                        Gate(340, 0, false, "Causeway", M(("maple_lumber", 12), ("steel_ingot", 6))), // a flood: build a causeway
                        Gate(435, 4, true),                                                            // the sealed gate: the moonsilver tool
                    },
                    80);   // one band of wood and ore per camp
                EditorUtility.SetDirty(camp);
            }

            // The moonsilver tool (tier 4) and the site equipment that unlocks it.
            var toolPath = WorkerDir + "equipment_tool_moonsilver.asset";
            var toolCreated = !File.Exists(toolPath);
            var tool = EnsureAsset<WorkerEquipmentDefinition>(toolPath);
            if (toolCreated)
            {
                tool.EditorInitialize("equipment.tool_moonsilver", "Moonsilver tool");
                tool.EditorSetupEquipment(EquipmentSlot.Tool, 4, 32, 160, M(("moonsilver_ingot", 6)), "Site equipment level 4");
                EditorUtility.SetDirty(tool);
            }
            if (!new List<WorkerEquipmentDefinition>(catalogue.Equipment).Contains(tool))
            {
                var equipment = new List<WorkerEquipmentDefinition>(catalogue.Equipment) { tool };
                catalogue.EditorSetSites(new List<SiteDefinition>(catalogue.Sites), new List<SiteBandDefinition>(catalogue.Bands),
                    new List<WorkerDefinition>(catalogue.Workers), equipment);
                EditorUtility.SetDirty(catalogue);
            }
            var upPath = EconomyDir + "upgrade_mine_equipment_4.asset";
            var upCreated = !File.Exists(upPath);
            var up = EnsureAsset<UpgradeDefinition>(upPath);
            if (upCreated)
            {
                up.EditorInitialize("upgrade.mine_equipment_4", "Mine Equipment 4");
                up.EditorSetupUpgrade(UpgradeKind.SiteEquipment, null, catalogue.Get<SiteDefinition>("site.mine"), 4, 150,
                    M(("moonsilver_ingot", 6), ("steel_ingot", 6)), "Unlocks the moonsilver tool for purchase",
                    catalogue.Get<UpgradeDefinition>("upgrade.mine_equipment_3"), null, 0, null, 8);
                EditorUtility.SetDirty(up);
            }
            var ups = new List<UpgradeDefinition>(catalogue.Upgrades);
            if (!ups.Contains(up)) { ups.Add(up); catalogue.EditorSetEconomy(catalogue.TraderStock, ups); EditorUtility.SetDirty(catalogue); }
        }
    }
}
