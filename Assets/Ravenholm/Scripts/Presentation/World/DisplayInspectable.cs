namespace Ravenholm.Presentation
{
    /// <summary>A display piece (table, stand, rack, shelf): "Arrange" opens the display picker (Kevin, 2026-10-18).</summary>
    public sealed class DisplayInspectable : Inspectable
    {
        public string PlacementId => GetComponent<FurnitureView>()?.PlacementId;

        public override string ContextVerb => "Arrange";
        public override bool OpensOwnPanel => true;

        public void Bind(string name) => EditorSetup(name, "Choose what it shows.", "");

        public override void OnInspected()
        {
            var root = GameRoot.Instance;
            if (root != null) root.Hud.ShowDisplay(PlacementId);
        }
    }
}
