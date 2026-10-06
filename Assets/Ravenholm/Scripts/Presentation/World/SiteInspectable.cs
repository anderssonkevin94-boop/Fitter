using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Mine or Forest entrance: the context button opens the site panel (strata or trail, decisions).</summary>
    public sealed class SiteInspectable : Inspectable
    {
        [SerializeField] private string siteId = "";

        public override string ContextVerb => "Open";
        public override bool OpensOwnPanel => true;
        public string SiteId => siteId;

        public void EditorSetupSite(string id) => siteId = id;

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root != null) root.Hud.ShowSite(siteId);
        }
    }
}
