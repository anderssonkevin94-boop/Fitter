using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Shows a building's upgrade level in the town (GDD 16/20): one pennant per level above 1, raised along
    /// the roofline. Purely visual; the level lives in the production state.
    /// </summary>
    public sealed class StationLevelView : MonoBehaviour
    {
        [SerializeField] private string stationId = "";
        [SerializeField] private List<SpriteRenderer> pennants = new List<SpriteRenderer>();
        private int _shown = -1;

        public int ShownLevel => _shown;

        public void EditorSetup(string station, List<SpriteRenderer> flags)
        {
            stationId = station;
            pennants = flags;
        }

        private void Update()
        {
            var root = GameRoot.Instance;
            if (root == null) return;
            var level = root.Services.Production.System.Level(stationId);
            if (level == _shown) return;
            _shown = level;
            for (var i = 0; i < pennants.Count; i++) pennants[i].enabled = i < level - 1;
        }
    }
}
