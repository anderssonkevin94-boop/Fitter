using System.Collections.Generic;
using System.Text;
using Ravenholm.Content;
using Ravenholm.Domain.Logistics;
using Ravenholm.Services;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Station interaction for receiver buildings (Phase 1): the context button "Opens" a panel that
    /// shows what the receiver accepts and what is now in shared inventory or the Store.
    /// Processing and crafting arrive in Phase 3.
    /// </summary>
    public sealed class StationInspectable : Inspectable
    {
        [SerializeField] private ReceiverKind receiverKind;

        public override string ContextVerb => "Open";

        public void EditorSetupStation(ReceiverKind kind) => receiverKind = kind;

        public override string BuildDetail()
        {
            var root = GameRoot.Instance;
            if (root == null) return base.BuildDetail();
            var cat = root.Services.Catalogue;
            var state = root.Services.Logistics.System.State;
            var sb = new StringBuilder(base.BuildDetail()).Append("\n\n");

            if (receiverKind == ReceiverKind.Store)
            {
                sb.Append("<b>Accepts:</b> finished weapons, armor and potions.\n<b>In the Store:</b> ");
                var counts = new SortedDictionary<string, int>();
                foreach (var inst in state.StoreItems)
                    if (state.ItemDefinitions.TryGetValue(inst, out var def))
                        counts[def] = counts.TryGetValue(def, out var n) ? n + 1 : 1;
                AppendCounts(sb, counts);
                return sb.ToString();
            }

            var accepted = new List<ResourceDefinition>();
            foreach (var r in cat.Resources)
                if (r != null && CatalogueAcceptanceRules.Map(r.AcceptedReceiver) == receiverKind) accepted.Add(r);
            sb.Append("<b>Accepts:</b> ");
            for (var i = 0; i < accepted.Count; i++) sb.Append(i > 0 ? ", " : "").Append(accepted[i].DisplayName);
            sb.Append("\n<b>In shared inventory:</b> ");
            var have = new SortedDictionary<string, int>();
            foreach (var r in accepted)
                if (state.SharedMaterials.TryGetValue(r.Id, out var q)) have[r.Id] = q;
            AppendCounts(sb, have);
            return sb.ToString();
        }

        private static void AppendCounts(StringBuilder sb, SortedDictionary<string, int> counts)
        {
            if (counts.Count == 0) { sb.Append("nothing yet"); return; }
            var first = true;
            foreach (var kv in counts)
            {
                sb.Append(first ? "" : ", ").Append(UnitVisuals.DisplayName(kv.Key)).Append(" ×").Append(kv.Value);
                first = false;
            }
        }
    }
}
