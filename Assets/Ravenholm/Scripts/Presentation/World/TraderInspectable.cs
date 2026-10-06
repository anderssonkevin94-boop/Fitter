using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// The Trader Pad. While the Trader is in town its stall appears here and the context button
    /// opens the trade panel; otherwise the panel says when the Trader returns (GDD 16).
    /// </summary>
    public sealed class TraderInspectable : Inspectable
    {
        [SerializeField] private GameObject stall;

        public override string ContextVerb => Present ? "Trade at" : "Check";
        public override bool OpensOwnPanel => true;

        public void EditorSetupTrader(GameObject stallRoot) => stall = stallRoot;

        private static bool Present
        {
            get
            {
                var root = GameRoot.Instance;
                var eco = root != null ? root.Services.Economy : null;
                return eco?.Trader != null && eco.Trader.IsPresent(eco.Now);
            }
        }

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root != null) root.Hud.ShowTrader();
        }

        private void Update()
        {
            if (stall != null && stall.activeSelf != Present) stall.SetActive(Present);
        }
    }
}
