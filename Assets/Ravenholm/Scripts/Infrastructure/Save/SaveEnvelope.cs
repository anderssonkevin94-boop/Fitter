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
        public const int CurrentSchemaVersion = 3;

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
}
