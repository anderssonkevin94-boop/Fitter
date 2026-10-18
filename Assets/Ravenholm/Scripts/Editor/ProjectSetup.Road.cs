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
            // The camp's group sets the Trader's pad down below and right of its middle (world (3.0, -2.1) at camp 1, the
            // anchor at cell (5, 2)): the land's edge stays clear of it at every camp (2026-10-16).
            if (camp.GroupSpotCount == 0)
            {
                camp.EditorSetGroupSpots(new List<Vector2Int> { new Vector2Int(-7, -10) });
                EditorUtility.SetDirty(camp);
            }

            if (camp.Stops.Count < 5 || !camp.Switchback && camp.Ridge.Length - camp.Ridge.CampS < camp.CampAhead(camp.CampCount - 1) + 30)
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

            // The switchback pass (Kevin, 2026-10-16; Astra's pass pack): legs of 40 cells (Kevin: medium, maybe longer later),
            // 16 wide with straight edges like the paintings, joined by her turns (road radius 16). The first leg runs from
            // 12 behind camp 1's town through it and 40 on. Camps sit mid-leg, the gates at the turns (their authored
            // "ahead" no longer used); one band per leg.
            // Gated levels (Kevin, 2026-10-16): the pass gives way to one organic plateau of about 60 x 60 per camp, the camp
            // toward its front, the gate on its north rim (toward the castle). Step 1: level 1 only.
            if (!camp.Plateau)
            {
                camp.EditorSetPlateau(true, 60);
                EditorUtility.SetDirty(camp);
            }

            // The horseshoe levels (Kevin, 2026-10-19: "we are unlocking the old path to the castle, so we're fixing
            // something at each stage"): every level's gate is a project in its north-west pass, never a tool gate. The
            // pass's ridges are unbreakable, so the project is the way on. Costs grow along the road.
            if (camp.Gates.Count == 0 || camp.Gates[0].project != CampDefinition.GateProject)
            {
                CampDefinition.GateSpec P(string project, List<ResourceCost> cost) =>
                    new CampDefinition.GateSpec { ahead = 0, thickness = 4, tier = 0, rock = false, project = project, projectCost = cost };
                camp.EditorSetGates(new List<CampDefinition.GateSpec>
                {
                    P(CampDefinition.GateProject, M(("cedar_lumber", 8), ("iron_ingot", 4))),       // the old gate, repaired
                    P("Bridge", M(("maple_lumber", 12), ("rope", 6))),                               // a broken bridge over a ravine
                    P("Causeway", M(("ironwood_lumber", 12), ("steel_ingot", 6))),                   // a flooded pass
                    P(CampDefinition.GateProject, M(("steel_ingot", 8), ("ironwood_lumber", 8))),    // a sealed gate
                    P("Bridge", M(("moonsilver_ingot", 6), ("rope", 10))),                           // the last bridge to the castle
                });
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
