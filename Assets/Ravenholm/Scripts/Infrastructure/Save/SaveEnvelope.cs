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
        public const int CurrentSchemaVersion = 4;

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
}
