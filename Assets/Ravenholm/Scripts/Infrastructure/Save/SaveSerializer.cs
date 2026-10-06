using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ravenholm.Infrastructure.Save
{
    /// <summary>JSON (de)serialization of <see cref="SaveEnvelope"/>, including migration.</summary>
    public static class SaveSerializer
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            DateParseHandling = DateParseHandling.DateTime,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public static string ToJson(SaveEnvelope envelope) => JsonConvert.SerializeObject(envelope, Settings);

        public static bool TryFromJson(string json, out SaveEnvelope envelope, out int migratedFrom, out string error)
        {
            envelope = null;
            migratedFrom = 0;
            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(json, Settings);
                if (root == null) { error = "JSON root is not an object"; return false; }
                if (!SaveMigrator.TryMigrate(root, out migratedFrom, out error)) return false;

                envelope = root.ToObject<SaveEnvelope>(JsonSerializer.Create(Settings));
                if (envelope == null) { error = "envelope deserialized to null"; return false; }
                if (string.IsNullOrEmpty(envelope.Profile)) { error = "envelope has no profile"; return false; }
                if (!ulong.TryParse(envelope.WorldSeed, out _)) { error = "world seed is not a number"; return false; }
                envelope.LastAcceptedUtc = DateTime.SpecifyKind(envelope.LastAcceptedUtc, DateTimeKind.Utc);
                return true;
            }
            catch (Exception e)
            {
                error = "invalid JSON: " + e.Message;
                envelope = null;
                return false;
            }
        }
    }
}
