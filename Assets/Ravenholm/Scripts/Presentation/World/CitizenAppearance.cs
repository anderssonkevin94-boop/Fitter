using Ravenholm.Domain.Jobs;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// How a contractor looks (Astra's head library and civilian outfits, 2026-10-19): their saved head identity and
    /// everyday clothes, or, until the player chooses, one picked from their id: the same on every load, varied across the
    /// crowd.
    /// </summary>
    public static class CitizenAppearance
    {
        public static (string head, string outfit) For(HoseCharacterDefinition def, ContractorRecord c)
        {
            if (def == null || c == null) return ("", "");
            var heads = def.HeadIds();
            var head = !string.IsNullOrEmpty(c.HeadId) ? c.HeadId : heads[(int)(Hash(c.Id, 1) % (uint)heads.Count)];
            var outfit = !string.IsNullOrEmpty(c.OutfitId) ? c.OutfitId
                : def.civilianOutfits.Count > 0 ? def.civilianOutfits[(int)(Hash(c.Id, 2) % (uint)def.civilianOutfits.Count)] : "";
            return (head, outfit);
        }

        /// <summary>Puts a contractor's look on a figure's rig (head, clothes; their skin comes with the head).</summary>
        public static void Apply(HoseRig rig, ContractorRecord c)
        {
            if (rig == null || c == null) return;
            var (head, outfit) = For(rig.Definition, c);
            rig.HeadId = head;
            rig.CivilianOutfit = outfit;
        }

        /// <summary>A stable hash (FNV-1a): the same pick on every device and every load.</summary>
        private static uint Hash(string s, uint salt)
        {
            var h = 2166136261u ^ salt * 16777619u;
            foreach (var ch in s ?? "") { h ^= ch; h *= 16777619u; }
            return h;
        }
    }
}
