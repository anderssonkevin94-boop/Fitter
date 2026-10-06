using Ravenholm.Domain.Logistics;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Production building: the context button "Opens" its station panel (receiving, processing line,
    /// crafting), or the Store inventory for the Store.
    /// </summary>
    public sealed class StationInspectable : Inspectable
    {
        [Tooltip("StationDefinition ID, e.g. 'station.smelter'. Empty for the Store.")]
        [SerializeField] private string stationId = "";
        [SerializeField] private bool hasReceiver;
        [SerializeField] private ReceiverKind receiverKind;

        public override string ContextVerb => "Open";
        public override bool OpensOwnPanel => true;
        public string StationId => stationId;
        public bool HasReceiver => hasReceiver;
        public ReceiverKind ReceiverKind => receiverKind;
        public bool IsStore => hasReceiver && receiverKind == ReceiverKind.Store;

        public void EditorSetupStation(string station, bool receiver, ReceiverKind kind)
        {
            stationId = station;
            hasReceiver = receiver;
            receiverKind = kind;
        }

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            if (IsStore) root.Hud.ShowStore();
            else root.Hud.ShowStation(this);
        }
    }
}
