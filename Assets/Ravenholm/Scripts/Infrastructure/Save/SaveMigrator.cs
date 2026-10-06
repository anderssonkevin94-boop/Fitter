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
