using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Ravenholm.Infrastructure.Save
{
    /// <summary>
    /// Sequential N -> N+1 migrations on raw JSON, run before the DTO is deserialized.
    /// Each step must be tested against a real fixture file in Tests/Fixtures.
    /// </summary>
    public static class SaveMigrator
    {
        private static readonly Dictionary<int, Action<JObject>> Steps = new Dictionary<int, Action<JObject>>
        {
            { 1, MigrateV1ToV2 },
            { 2, MigrateV2ToV3 },
            { 3, MigrateV3ToV4 },
            { 4, MigrateV4ToV5 },
            { 5, MigrateV5ToV6 },
            { 6, MigrateV6ToV7 },
            { 7, MigrateV7ToV8 },
        };

        /// <summary>Migrates <paramref name="root"/> in place up to the current schema.</summary>
        public static bool TryMigrate(JObject root, out int fromVersion, out string error)
        {
            fromVersion = ReadVersion(root);
            if (fromVersion <= 0) { error = "save has no schema version"; return false; }
            if (fromVersion > SaveEnvelope.CurrentSchemaVersion)
            {
                error = $"save schema {fromVersion} is newer than this build supports ({SaveEnvelope.CurrentSchemaVersion})";
                return false;
            }

            for (var v = fromVersion; v < SaveEnvelope.CurrentSchemaVersion; v++)
            {
                if (!Steps.TryGetValue(v, out var step)) { error = $"no migration from schema {v}"; return false; }
                try { step(root); }
                catch (Exception e) { error = $"migration {v}->{v + 1} failed: {e.Message}"; return false; }
                root["schemaVersion"] = v + 1;
            }

            error = null;
            return true;
        }

        private static int ReadVersion(JObject root)
        {
            // Schema 1 used "version"; later schemas use "schemaVersion".
            var token = root["schemaVersion"] ?? root["version"];
            return token != null && token.Type == JTokenType.Integer ? token.Value<int>() : 0;
        }

        /// <summary>
        /// Schema 8 (Phase 6) adds "economy": Trader schedule and stock, purchased upgrades.
        /// Absent = none bought; the Trader is scheduled from the earliest ordinary quest result on load.
        /// </summary>
        private static void MigrateV7ToV8(JObject o)
        {
            if (o["economy"] == null) o["economy"] = JValue.CreateNull();
        }

        /// <summary>
        /// Schema 7 (Phase 5): quest progress, results, and frozen resolution inputs on expeditions.
        /// Older expeditions get resolverVersion 0 and are given inputs from content once on load.
        /// </summary>
        private static void MigrateV6ToV7(JObject o)
        {
            if (!(o["jobs"] is JObject j)) return;
            if (j["progress"] == null) j["progress"] = new JObject();
            if (j["results"] == null) j["results"] = new JArray();
            if (j["expeditions"] is JArray ex)
                foreach (var e in ex)
                    if (e is JObject eo && eo["resolverVersion"] == null) eo["resolverVersion"] = 0;
        }

        /// <summary>Schema 6 (Phase 4) adds "jobs": the company starts with the GDD 15 gold and reputation.</summary>
        private static void MigrateV5ToV6(JObject o)
        {
            if (o["jobs"] == null) o["jobs"] = JValue.CreateNull();
        }

        /// <summary>
        /// Schema 5 (Phase 3) adds "production" and the consumption ledger. Older saves receive the
        /// starting stock and starter blueprints on first load, as a new company would.
        /// </summary>
        private static void MigrateV4ToV5(JObject o)
        {
            if (o["production"] == null) o["production"] = JValue.CreateNull();
            if (o["logistics"] is JObject l && l["materialsConsumed"] == null) l["materialsConsumed"] = new JObject();
        }

        /// <summary>Schema 4 (Phase 2) adds "sites". Older saves start every site fresh at band 1.</summary>
        private static void MigrateV3ToV4(JObject o)
        {
            if (o["sites"] == null) o["sites"] = JValue.CreateNull();
        }

        /// <summary>
        /// Schema 3 (Phase 1) adds the "logistics" ownership section. Older saves have no
        /// physical goods yet, so it starts empty (null) and the sandbox seeds its fixtures.
        /// </summary>
        private static void MigrateV2ToV3(JObject o)
        {
            if (o["logistics"] == null) o["logistics"] = JValue.CreateNull();
        }

        /// <summary>
        /// Schema 1 is a FIXTURE format: the flat layout an early prototype could plausibly have
        /// written, kept to prove the migration pipeline end to end. No real player saves use it.
        ///   v1: { version, profile, seed (number), generation, savedAt, bells, sessions, px, py }
        ///   v2: { schemaVersion, profile, worldSeed (string), saveGeneration, lastAcceptedUtc,
        ///         gameBuild, contentVersion, sandbox { bellRingCount, sessionCount, playerX, playerY } }
        /// </summary>
        private static void MigrateV1ToV2(JObject o)
        {
            var seed = o["seed"]?.Value<ulong>() ?? 0UL;
            var sandbox = new JObject
            {
                ["bellRingCount"] = o["bells"]?.Value<int>() ?? 0,
                ["sessionCount"] = o["sessions"]?.Value<int>() ?? 0,
                ["playerX"] = o["px"]?.Value<float>() ?? 0f,
                ["playerY"] = o["py"]?.Value<float>() ?? 0f,
            };

            o["worldSeed"] = seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            o["saveGeneration"] = o["generation"]?.Value<long>() ?? 0L;
            o["lastAcceptedUtc"] = o["savedAt"] ?? new JValue(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
            o["gameBuild"] = "pre-0.0.1";
            o["contentVersion"] = 0;
            o["sandbox"] = sandbox;

            foreach (var old in new[] { "version", "seed", "generation", "savedAt", "bells", "sessions", "px", "py" })
                o.Remove(old);
        }
    }
}
