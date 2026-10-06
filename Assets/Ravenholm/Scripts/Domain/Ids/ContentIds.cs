namespace Ravenholm.Domain.Ids
{
    /// <summary>
    /// Rules for stable content IDs. An ID is "&lt;kind&gt;.&lt;name&gt;", lowercase ASCII,
    /// e.g. "resource.iron_ore" or "item.iron_sword". IDs are saved in player files, so
    /// they must never change once shipped; rename the display name instead.
    /// </summary>
    public static class ContentIds
    {
        public const int MaxLength = 64;

        public static bool IsValid(string id, out string reason)
        {
            if (string.IsNullOrEmpty(id)) { reason = "ID is empty"; return false; }
            if (id.Length > MaxLength) { reason = $"ID is longer than {MaxLength} characters"; return false; }
            if (id[0] < 'a' || id[0] > 'z') { reason = "ID must start with a lowercase letter"; return false; }

            var dots = 0;
            for (var i = 0; i < id.Length; i++)
            {
                var c = id[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '.';
                if (!ok) { reason = $"ID contains invalid character '{c}' (allowed: a-z 0-9 _ .)"; return false; }
                if (c != '.') continue;
                dots++;
                if (i == id.Length - 1 || id[i + 1] == '.') { reason = "ID has an empty segment"; return false; }
            }

            if (dots == 0) { reason = "ID must have a kind prefix, e.g. 'resource.iron_ore'"; return false; }
            reason = null;
            return true;
        }

        /// <summary>True when <paramref name="id"/> begins with "<paramref name="kind"/>.".</summary>
        public static bool HasKind(string id, string kind)
        {
            return id != null && id.Length > kind.Length + 1 && id.StartsWith(kind, System.StringComparison.Ordinal) && id[kind.Length] == '.';
        }
    }
}
