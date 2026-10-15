using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ravenholm.Infrastructure.Save
{
    /// <summary>
    /// Versioned save DTO. Plain data only: never serialize ScriptableObjects or scene objects.
    /// Later phases add sections (inventories, sites, contractors...) through migrations.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SaveEnvelope
    {
        /// <summary>Current schema written by this build. Bump it and add a migration when the shape changes.</summary>
        public const int CurrentSchemaVersion = 9;

        [JsonProperty("schemaVersion", Required = Required.Always)] public int SchemaVersion = CurrentSchemaVersion;
        [JsonProperty("gameBuild")] public string GameBuild = "";
        [JsonProperty("contentVersion")] public int ContentVersion;
        [JsonProperty("profile", Required = Required.Always)] public string Profile = "";

        /// <summary>Stored as a decimal string so 64-bit seeds survive any JSON tooling.</summary>
        [JsonProperty("worldSeed", Required = Required.Always)] public string WorldSeed = "0";

        [JsonProperty("saveGeneration", Required = Required.Always)] public long SaveGeneration;
        [JsonProperty("lastAcceptedUtc", Required = Required.Always)] public DateTime LastAcceptedUtc;

        /// <summary>PHASE 0 FIXTURE section for the debug sandbox.</summary>
        [JsonProperty("sandbox")] public SandboxSaveData Sandbox = new SandboxSaveData();

        /// <summary>Physical goods ownership (schema 3+). Null means "never saved": fixtures are seeded fresh.</summary>
        [JsonProperty("logistics")] public LogisticsSaveData Logistics;

        /// <summary>Gathering sites and their workers (schema 4+). Null means "never saved": sites start fresh.</summary>
        [JsonProperty("sites")] public List<SiteSaveData> Sites;

        /// <summary>Processing lines, facility levels and owned blueprints (schema 5+). Null = fresh start (starting stock granted).</summary>
        [JsonProperty("production")] public ProductionSaveData Production;

        /// <summary>Company gold and reputation, contractors, the open posting and expeditions (schema 6+). Null = fresh.</summary>
        [JsonProperty("jobs")] public JobsSaveData Jobs;

        /// <summary>Trader schedule and stock, purchased upgrades (schema 8+). Null = nothing bought yet.</summary>
        [JsonProperty("economy")] public EconomySaveData Economy;

        /// <summary>First-hour prompts finished or hidden (schema 9+). Null = new player (or derived on migration).</summary>
        [JsonProperty("onboarding")] public OnboardingSaveData Onboarding;

        /// <summary>The supplier's own tool and pack (solo play). Null = starting gear.</summary>
        [JsonProperty("playerGear")] public PlayerGearData PlayerGear;

        /// <summary>The shop's blocks and furniture (2026-10-15). Optional: older saves start with the default shop.</summary>
        [JsonProperty("shop")] public ShopSaveData Shop;
        /// <summary>Stations moved or packed at the camp (optional; null = every station where the camp was laid out).</summary>
        [JsonProperty("stations")] public StationsSaveData Stations;
        /// <summary>Which camp on the road the company is at (optional; null = camp 1).</summary>
        [JsonProperty("camp")] public CampSaveData Camp;

        public ulong GetWorldSeed() => ulong.Parse(WorldSeed, System.Globalization.CultureInfo.InvariantCulture);

        public void SetWorldSeed(ulong seed) => WorldSeed = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SandboxSaveData
    {
        [JsonProperty("bellRingCount")] public int BellRingCount;
        [JsonProperty("sessionCount")] public int SessionCount;
        [JsonProperty("playerX")] public float PlayerX;
        [JsonProperty("playerY")] public float PlayerY;
        /// <summary>Debug commands ever used on this profile (optional; absent in older saves = none).</summary>
        [JsonProperty("assisted")] public List<string> Assisted = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class LogisticsSaveData
    {
        [JsonProperty("carry")] public List<CarryUnitData> Carry = new List<CarryUnitData>();
        [JsonProperty("containers")] public List<ContainerData> Containers = new List<ContainerData>();
        [JsonProperty("shared")] public SortedDictionary<string, int> SharedMaterials = new SortedDictionary<string, int>();
        [JsonProperty("store")] public List<string> StoreItems = new List<string>();
        [JsonProperty("itemDefinitions")] public SortedDictionary<string, string> ItemDefinitions = new SortedDictionary<string, string>();
        [JsonProperty("materialsCreated")] public SortedDictionary<string, long> MaterialsCreated = new SortedDictionary<string, long>();
        [JsonProperty("materialsConsumed")] public SortedDictionary<string, long> MaterialsConsumed = new SortedDictionary<string, long>();
        [JsonProperty("nextInstance")] public long NextInstanceNumber = 1;
        [JsonProperty("pinned")] public List<string> Pinned = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CarryUnitData
    {
        /// <summary>"m" = material (id is a resource ID), "i" = item (id is an instance ID).</summary>
        [JsonProperty("k")] public string Kind;
        [JsonProperty("id")] public string Id;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ContainerData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("materials")] public SortedDictionary<string, int> Materials = new SortedDictionary<string, int>();
        [JsonProperty("items")] public List<string> Items = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SiteSaveData
    {
        [JsonProperty("siteId")] public string SiteId;
        [JsonProperty("depth")] public int Depth;
        [JsonProperty("k")] public int K;
        [JsonProperty("sectionsCompleted")] public int SectionsCompleted;
        /// <summary>Depth of each completed section (optional; older saves are inferred on load).</summary>
        [JsonProperty("sectionDepths")] public List<int> SectionDepths;
        [JsonProperty("status")] public string Status;
        [JsonProperty("simMs")] public long SimMs;
        [JsonProperty("rngState")] public string RngState;
        [JsonProperty("rngInc")] public string RngIncrement;
        [JsonProperty("composition")] public CompositionData Composition;
        [JsonProperty("revealed")] public SortedDictionary<int, CompositionData> Revealed = new SortedDictionary<int, CompositionData>();
        [JsonProperty("sectionDurabilityMicro")] public long SectionDurabilityMicro;
        [JsonProperty("sectionDoneMicro")] public long SectionDoneMicro;
        [JsonProperty("sectionEmitted")] public int SectionEmitted;
        [JsonProperty("facePending")] public List<string> FacePending = new List<string>();
        [JsonProperty("nextRecipient")] public int NextRecipient;
        [JsonProperty("trees")] public List<TreeSaveData> Trees = new List<TreeSaveData>();
        [JsonProperty("workers")] public List<WorkerSaveData> Workers = new List<WorkerSaveData>();
        [JsonProperty("depositedTotal")] public long DepositedTotal;
        [JsonProperty("measureStartMs")] public long MeasureStartMs;
        [JsonProperty("depositedSinceMeasure")] public long DepositedSinceMeasure;
        /// <summary>Mine sideways tunnels (optional; absent in older saves).</summary>
        [JsonProperty("tunnels")] public List<TunnelSaveData> Tunnels;
        /// <summary>Mine layout version: 2 = layers of levels, Continue digs left; 3 = the dig slab; 4 = the cliff-face slice (optional; absent = older).</summary>
        [JsonProperty("layout")] public int Layout;
        /// <summary>Mine dig slab: dug blocks as x, u, row triples (optional).</summary>
        [JsonProperty("dug")] public List<int> Dug;
        /// <summary>Mine dig slab: blocks dug straight down (ladders), as x, u, row triples (optional).</summary>
        [JsonProperty("dugDown")] public List<int> DugDown;
        /// <summary>Mine dig slab: the block being broken by hand (x, u, row) and its progress.</summary>
        [JsonProperty("digTarget")] public List<int> DigTarget;
        [JsonProperty("digDoneMicro")] public long DigDoneMicro;
        /// <summary>Mine dig slab: where the supplier last dug, and a routine's tunnel (x, u, feet, dx, du, doneMicro).</summary>
        [JsonProperty("lastDig")] public List<long> LastDig;
        [JsonProperty("routineFace")] public List<long> RoutineFace;
        /// <summary>Forest grid: trees started, as area, slot, micro-damage triples (optional), and the tree last cut.</summary>
        [JsonProperty("chopped")] public List<long> Chopped;
        /// <summary>Mine: scaffolds as x, u, row triples, and the material each used (optional).</summary>
        [JsonProperty("scaffolds")] public List<int> Scaffolds;
        /// <summary>Mine: surface outcrop blocks broken, as cell x, cell y, −level triples (optional).</summary>
        [JsonProperty("surfaceBroken")] public List<int> SurfaceBroken;
        [JsonProperty("scaffoldMaterials")] public List<string> ScaffoldMaterials;
        [JsonProperty("lastChop")] public List<int> LastChop;
        /// <summary>Forest grid: stumps as area, slot, felled-at ms triples, and stumps cleared as area, slot pairs (optional).</summary>
        [JsonProperty("stumps")] public List<long> Stumps;
        [JsonProperty("cleared")] public List<int> Cleared;
        /// <summary>The camp whose map the cells above belong to (2026-10-15; null = the old town and frontier).</summary>
        [JsonProperty("camp")] public string Camp;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlayerGearData
    {
        [JsonProperty("tool")] public string ToolId;
        [JsonProperty("pack")] public string PackId;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TunnelSaveData
    {
        [JsonProperty("level")] public int Level;
        [JsonProperty("side")] public string Side;
        [JsonProperty("depth")] public int Depth;
        [JsonProperty("k")] public int K;
        [JsonProperty("stretches")] public int Stretches;
        [JsonProperty("status")] public string Status;
        [JsonProperty("durabilityMicro")] public long DurabilityMicro;
        [JsonProperty("doneMicro")] public long DoneMicro;
        [JsonProperty("emitted")] public int Emitted;
        [JsonProperty("nextRecipient")] public int NextRecipient;
        [JsonProperty("pending")] public List<string> Pending = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CompositionData
    {
        [JsonProperty("weights")] public List<WeightData> Weights = new List<WeightData>();
        [JsonProperty("rare")] public string RareResourceId;
        [JsonProperty("rareChance")] public double RareChance;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WeightData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("w")] public double Weight;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TreeSaveData
    {
        [JsonProperty("index")] public int Index;
        [JsonProperty("species")] public string Species;
        [JsonProperty("primary")] public List<string> Primary = new List<string>();
        [JsonProperty("bonus")] public List<string> Bonus = new List<string>();
        [JsonProperty("durabilityMicro")] public long DurabilityMicro;
        [JsonProperty("doneMicro")] public long DoneMicro;
        [JsonProperty("emitted")] public int Emitted;
        [JsonProperty("claimedBy")] public string ClaimedBy;
        [JsonProperty("pending")] public List<string> Pending = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class WorkerSaveData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("tool")] public string ToolId;
        [JsonProperty("pack")] public string PackId;
        [JsonProperty("state")] public string State;
        [JsonProperty("phaseStartMs")] public long PhaseStartMs;
        [JsonProperty("phaseEndMs")] public long PhaseEndMs;
        [JsonProperty("targetTree")] public int TargetTree = -1;
        /// <summary>Mine face (0 = shaft, n = tunnel n); optional.</summary>
        [JsonProperty("face")] public int Face;
        [JsonProperty("carried")] public List<string> Carried = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProductionSaveData
    {
        [JsonProperty("simMs")] public long SimMs;
        [JsonProperty("processors")] public List<ProcessorSaveData> Processors = new List<ProcessorSaveData>();
        [JsonProperty("levels")] public SortedDictionary<string, int> FacilityLevels = new SortedDictionary<string, int>();
        [JsonProperty("blueprints")] public List<string> Blueprints = new List<string>();
        /// <summary>Items being made (the crafting expansion, 2026-10-16; optional).</summary>
        [JsonProperty("crafts")] public List<CraftSaveData> Crafts = new List<CraftSaveData>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CraftSaveData
    {
        [JsonProperty("station")] public string Station;
        [JsonProperty("recipe")] public string Recipe;
        [JsonProperty("start")] public long StartMs;
        [JsonProperty("end")] public long EndMs;
        [JsonProperty("waiting")] public bool Waiting;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProcessorSaveData
    {
        [JsonProperty("facility")] public string Facility;
        [JsonProperty("selected")] public string Selected;
        [JsonProperty("queued")] public string Queued;
        [JsonProperty("inFlight")] public string InFlight;
        [JsonProperty("startMs")] public long StartMs;
        [JsonProperty("endMs")] public long EndMs;
        [JsonProperty("reserves")] public SortedDictionary<string, int> Reserves = new SortedDictionary<string, int>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class JobsSaveData
    {
        [JsonProperty("simMs")] public long SimMs;
        [JsonProperty("gold")] public int Gold;
        [JsonProperty("reputationTenths")] public int ReputationTenths;
        [JsonProperty("contractors")] public List<ContractorSaveData> Contractors = new List<ContractorSaveData>();
        [JsonProperty("posting")] public PostingSaveData Posting;
        [JsonProperty("expeditions")] public List<ExpeditionSaveData> Expeditions = new List<ExpeditionSaveData>();
        [JsonProperty("windowIndex")] public SortedDictionary<string, int> WindowIndex = new SortedDictionary<string, int>();
        [JsonProperty("nextExpedition")] public long NextExpeditionNumber = 1;
        [JsonProperty("generated")] public int GeneratedContractors;
        [JsonProperty("firstDispatchDone")] public bool FirstDispatchDone;
        /// <summary>The castle's job offers (2026-10-15). Optional: older saves schedule the first offer on load.</summary>
        [JsonProperty("nextCastleOfferMs")] public long NextCastleOfferMs = -1;
        [JsonProperty("castleOffers")] public int CastleOffers;
        [JsonProperty("castleSkipOnce")] public string CastleSkipOnce = "";
        [JsonProperty("progress")] public SortedDictionary<string, QuestProgressData> Progress = new SortedDictionary<string, QuestProgressData>();
        [JsonProperty("results")] public List<ResultSaveData> Results = new List<ResultSaveData>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class QuestProgressData
    {
        [JsonProperty("attempts")] public int Attempts;
        [JsonProperty("successes")] public int Successes;
        [JsonProperty("unlocked")] public bool Unlocked;
        [JsonProperty("revealed")] public List<string> RevealedThreats = new List<string>();
        [JsonProperty("observed")] public bool Observed;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ThreatSpecData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("tag")] public string CounterTag;
        [JsonProperty("required")] public int Required;
        [JsonProperty("unmetReadiness")] public int UnmetReadiness;
        [JsonProperty("unmetSurvival")] public int UnmetSurvival;
        [JsonProperty("armorThreshold")] public int ArmorThreshold;
        [JsonProperty("armorReadiness")] public int ArmorReadiness;
        [JsonProperty("armorSurvival")] public int ArmorSurvival;
        [JsonProperty("known")] public bool Known;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CarriedPotionData
    {
        [JsonProperty("instance")] public string InstanceId;
        [JsonProperty("definition")] public string DefinitionId;
        [JsonProperty("survivalBonus")] public int SurvivalBonus;
        [JsonProperty("tag")] public string CounterTag;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ResultSaveData
    {
        [JsonProperty("expedition")] public string ExpeditionId;
        [JsonProperty("quest")] public string QuestId;
        [JsonProperty("contractor")] public string ContractorId;
        [JsonProperty("contractorName")] public string ContractorName;
        [JsonProperty("outcome")] public string Outcome;
        [JsonProperty("forced")] public bool Forced;
        [JsonProperty("sponsored")] public bool Sponsored;
        [JsonProperty("completedMs")] public long CompletedMs;
        [JsonProperty("gold")] public int GoldEarned;
        [JsonProperty("fee")] public int FeePaid;
        [JsonProperty("materials")] public List<string> Materials = new List<string>();
        [JsonProperty("returned")] public List<string> ReturnedItems = new List<string>();
        [JsonProperty("consumed")] public List<string> ConsumedItems = new List<string>();
        [JsonProperty("lost")] public List<string> LostItems = new List<string>();
        [JsonProperty("blueprint")] public string BlueprintGranted;
        [JsonProperty("substitute")] public bool SubstituteGranted;
        [JsonProperty("repTenths")] public int ReputationDeltaTenths;
        [JsonProperty("leveledUp")] public bool LeveledUp;
        [JsonProperty("newLevel")] public int NewLevel;
        [JsonProperty("injured")] public bool Injured;
        [JsonProperty("died")] public bool Died;
        [JsonProperty("revealed")] public List<string> RevealedThreats = new List<string>();
        [JsonProperty("unlocked")] public List<string> QuestsUnlocked = new List<string>();
        [JsonProperty("contributors")] public List<string> Contributors = new List<string>();
        [JsonProperty("j")] public double J;
        [JsonProperty("k")] public double K;
        [JsonProperty("a")] public double A;
        [JsonProperty("e")] public double E;
        [JsonProperty("v")] public double V;
        [JsonProperty("acknowledged")] public bool Acknowledged;
        [JsonProperty("facts")] public List<string> Facts = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ContractorSaveData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("class")] public string ClassId;
        [JsonProperty("level")] public int Level;
        [JsonProperty("xp")] public int Xp;
        [JsonProperty("traits")] public List<string> Traits = new List<string>();
        [JsonProperty("personalityPrice")] public double PersonalityPrice;
        [JsonProperty("status")] public string Status;
        [JsonProperty("injuredUntilMs")] public long InjuredUntilMs;
        [JsonProperty("sponsored")] public bool Sponsored;
        [JsonProperty("skin")] public int Skin;
        [JsonProperty("jobs")] public int Jobs;
        [JsonProperty("successes")] public int Successes;
        [JsonProperty("lastNote")] public string LastNote;
        // Optional history fields (absent in older saves = empty history).
        [JsonProperty("injuries")] public int Injuries;
        [JsonProperty("highestDifficulty")] public int HighestDifficulty;
        [JsonProperty("discoveries")] public List<string> Discoveries = new List<string>();
        [JsonProperty("deathNote")] public string DeathNote;
        [JsonProperty("recent")] public List<string> Recent = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PostingSaveData
    {
        [JsonProperty("quest")] public string QuestId;
        [JsonProperty("createdMs")] public long CreatedMs;
        [JsonProperty("windowSeed")] public string WindowSeed;
        [JsonProperty("applicants")] public List<ApplicantSaveData> Applicants = new List<ApplicantSaveData>();
        [JsonProperty("noApplicantReason")] public string NoApplicantReason;
        [JsonProperty("byCastle")] public bool ByCastle;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ApplicantSaveData
    {
        [JsonProperty("contractor")] public string ContractorId;
        [JsonProperty("fee")] public int Fee;
        [JsonProperty("arriveMs")] public long ArriveMs;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ExpeditionSaveData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("quest")] public string QuestId;
        [JsonProperty("contractor")] public string ContractorId;
        [JsonProperty("feeQuoted")] public int FeeQuoted;
        [JsonProperty("feePaid")] public int FeePaid;
        [JsonProperty("sponsored")] public bool Sponsored;
        /// <summary>The shop's share of the reward (a castle job), or -1 (older saves: the old Job Board).</summary>
        [JsonProperty("shopShare")] public int ShopSharePercent = -1;
        [JsonProperty("items")] public List<string> Items = new List<string>();
        [JsonProperty("startMs")] public long StartMs;
        [JsonProperty("endMs")] public long EndMs;
        [JsonProperty("seed")] public string Seed;
        [JsonProperty("might")] public int Might;
        [JsonProperty("survival")] public int Survival;
        [JsonProperty("insight")] public int Insight;
        [JsonProperty("weaponPower")] public int WeaponPower;
        [JsonProperty("armorProtection")] public int ArmorProtection;
        [JsonProperty("survivalPotionBonus")] public int SurvivalPotionBonus;
        /// <summary>A shield's block chance in percent (optional; 0 = no shield).</summary>
        [JsonProperty("block")] public int BlockChance;
        [JsonProperty("counters")] public SortedDictionary<string, int> Counters = new SortedDictionary<string, int>();
        [JsonProperty("threats")] public List<string> ThreatIds = new List<string>();
        [JsonProperty("forecast")] public string ForecastBand;
        [JsonProperty("testDeparture")] public bool TestDeparture;
        [JsonProperty("difficulty")] public int Difficulty;
        [JsonProperty("resolverVersion")] public int ResolverVersion;
        [JsonProperty("threatSpecs")] public List<ThreatSpecData> ThreatSpecs = new List<ThreatSpecData>();
        [JsonProperty("potions")] public List<CarriedPotionData> Potions = new List<CarriedPotionData>();
        [JsonProperty("routeBonus")] public int RouteBonus;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class EconomySaveData
    {
        [JsonProperty("traderAnchorMs")] public long TraderAnchorMs = -1;
        [JsonProperty("traderVisitIndex")] public long TraderVisitIndex = -1;
        [JsonProperty("traderRemaining")] public SortedDictionary<string, int> TraderRemaining = new SortedDictionary<string, int>();
        [JsonProperty("upgrades")] public List<string> Upgrades = new List<string>();
        [JsonProperty("lastCatchUpId")] public string LastCatchUpId = "";
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class CampSaveData
    {
        [JsonProperty("road")] public string Road;
        [JsonProperty("index")] public int Index;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StationsSaveData
    {
        [JsonProperty("camp")] public string Camp;
        [JsonProperty("sites")] public List<StationSiteSaveData> Sites = new List<StationSiteSaveData>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StationSiteSaveData
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("packed")] public bool Packed;
        [JsonProperty("x")] public int X;
        [JsonProperty("y")] public int Y;
        /// <summary>When its build finishes (UTC ticks; 0 = built).</summary>
        [JsonProperty("readyAt")] public long ReadyAt;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ShopSaveData
    {
        [JsonProperty("blocks")] public List<int[]> Blocks = new List<int[]>();
        [JsonProperty("furniture")] public List<ShopFurnitureSaveData> Furniture = new List<ShopFurnitureSaveData>();
        [JsonProperty("nextId")] public long NextId = 1;
        [JsonProperty("expansions")] public int Expansions;
        [JsonProperty("layout")] public int Layout = 1;
        /// <summary>The wagon's level (0: a walled shop, from before the journey).</summary>
        [JsonProperty("wagon")] public int Wagon;
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ShopFurnitureSaveData
    {
        [JsonProperty("id")] public string Id = "";
        [JsonProperty("furniture")] public string Furniture = "";
        [JsonProperty("level")] public int Level = 1;
        [JsonProperty("placed")] public bool Placed;
        [JsonProperty("a")] public int A;
        [JsonProperty("b")] public int B;
        [JsonProperty("facing")] public string Facing = "A";
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class OnboardingSaveData
    {
        [JsonProperty("done")] public List<string> Done = new List<string>();
        [JsonProperty("hidden")] public bool Hidden;
    }
}
