using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// A station drawn at its level (Astra's modular Blacksmith, 2026-10-17): each level its own sprite on the same canvas,
    /// pivot and spot, and a matching fire overlay that burns while the station works and is out when idle: the furnace
    /// while its smelting line runs a batch, the forge's coals while a craft is on it (and a moment after, so an instant
    /// craft still shows a flare). Presentation only: the levels and the work live in the production state.
    /// </summary>
    public sealed class StationArt : MonoBehaviour
    {
        [SerializeField] private string stationId = "";
        [SerializeField] private SpriteRenderer body;
        [SerializeField] private SpriteRenderer glow;
        [SerializeField] private List<Sprite> levels = new List<Sprite>();
        [SerializeField] private List<Sprite> glows = new List<Sprite>();
        [Tooltip("A crafting station's fire: how long it keeps burning after a craft (real seconds).")]
        [SerializeField] private float linger = 2.5f;

        private int _shown = -1;
        private float _lastWork = -100f;

        public string StationId => stationId;
        public int ShownLevel => _shown;
        public bool Burning => glow != null && glow.enabled;

        public void EditorSetup(string station, SpriteRenderer bodyRenderer, SpriteRenderer glowRenderer, List<Sprite> levelSprites, List<Sprite> glowSprites)
        {
            stationId = station;
            body = bodyRenderer;
            glow = glowRenderer;
            levels = levelSprites;
            glows = glowSprites;
        }

        private void Update()
        {
            var root = GameRoot.Instance;
            if (root == null || body == null) return;
            var prod = root.Services.Production;
            var level = Mathf.Clamp(prod.System.Level(stationId), 1, Mathf.Max(1, levels.Count));
            if (level != _shown)
            {
                _shown = level;
                if (levels.Count >= level && levels[level - 1] != null) body.sprite = levels[level - 1];
                if (glow != null && glows.Count >= level) glow.sprite = glows[level - 1];
            }
            if (glow == null) return;
            if (Working(prod.System)) _lastWork = Time.time;
            var on = Time.time - _lastWork < linger;
            glow.enabled = on;
            if (on)
            {
                // A gentle flicker (steady with reduced motion).
                var a = Feel.ReducedMotion ? 1f : 0.82f + 0.18f * Mathf.PerlinNoise(Time.time * 3.1f, stationId.Length * 0.37f);
                glow.color = new Color(1f, 1f, 1f, a);
            }
        }

        /// <summary>The station is at work now: a smelting batch in its line, or a craft on it.</summary>
        private bool Working(Domain.Production.ProductionSystem sys)
        {
            if (sys.State.Processors.TryGetValue(stationId, out var p) && !string.IsNullOrEmpty(p.InFlight)) return true;
            foreach (var c in sys.State.Crafts) if (c.Station == stationId && !c.Waiting) return true;
            return false;
        }
    }
}
