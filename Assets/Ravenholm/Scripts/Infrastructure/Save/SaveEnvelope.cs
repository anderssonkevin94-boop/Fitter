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
        public const int CurrentSchemaVersion = 6;

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
        [JsonProperty("carried")] public List<string> Carried = new List<string>();
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ProductionSaveData
    {
        [JsonProperty("simMs")] public long SimMs;
        [JsonProperty("processors")] public List<ProcessorSaveData> Processors = new List<ProcessorSaveData>();
        [JsonProperty("levels")] public SortedDictionary<string, int> FacilityLevels = new SortedDictionary<string, int>();
        [JsonProperty("blueprints")] public List<string> Blueprints = new List<string>();
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
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PostingSaveData
    {
        [JsonProperty("quest")] public string QuestId;
        [JsonProperty("createdMs")] public long CreatedMs;
        [JsonProperty("windowSeed")] public string WindowSeed;
        [JsonProperty("applicants")] public List<ApplicantSaveData> Applicants = new List<ApplicantSaveData>();
        [JsonProperty("noApplicantReason")] public string NoApplicantReason;
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
        [JsonProperty("counters")] public SortedDictionary<string, int> Counters = new SortedDictionary<string, int>();
        [JsonProperty("threats")] public List<string> ThreatIds = new List<string>();
        [JsonProperty("forecast")] public string ForecastBand;
        [JsonProperty("testDeparture")] public bool TestDeparture;
    }
}
