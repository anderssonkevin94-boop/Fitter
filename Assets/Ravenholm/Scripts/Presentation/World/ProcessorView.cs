using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>World progress bar and output icon over a processing building while a batch runs (GDD 8).</summary>
    public sealed class ProcessorView : MonoBehaviour
    {
        [SerializeField] private string stationId = "";
        [SerializeField] private GameObject root;
        [SerializeField] private SpriteRenderer fill;
        [SerializeField] private SpriteRenderer outputIcon;
        [SerializeField] private float fillWidth = 1.0f;
        [Tooltip("Local x of the bar's left edge; the fill grows rightwards from here.")]
        [SerializeField] private float fillLeft = -0.32f;

        private GameRoot _game;

        public void EditorSetup(string station, GameObject barRoot, SpriteRenderer fillRenderer, SpriteRenderer icon)
        {
            stationId = station;
            root = barRoot;
            fill = fillRenderer;
            outputIcon = icon;
        }

        private void Start()
        {
            _game = GameRoot.Instance;
            if (_game == null) enabled = false;
        }

        private void Update()
        {
            var prod = _game.Services.Production.System;
            if (!prod.State.Processors.TryGetValue(stationId, out var p) || p.InFlight == null)
            {
                root.SetActive(false);
                return;
            }
            root.SetActive(true);
            var span = Mathf.Max(1f, p.EndMs - p.StartMs);
            var t = Mathf.Clamp01((prod.State.SimMs - p.StartMs) / span);
            var w = Mathf.Max(0.08f, fillWidth * t);
            fill.size = new Vector2(w, fill.size.y);
            fill.transform.localPosition = new Vector3(fillLeft + w * 0.5f, 0f, 0f);
            var r = prod.Process(p.InFlight);
            if (r != null && _game.Services.Catalogue.TryGet<Content.ResourceDefinition>(r.Output, out var res)) outputIcon.sprite = res.Icon;
        }
    }
}
