using System;
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
        public const int CurrentSchemaVersion = 2;

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
}
