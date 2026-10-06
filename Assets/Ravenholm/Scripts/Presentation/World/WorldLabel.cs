using System;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Optional floating name over a building. Off by default so silhouettes are judged on
    /// their own first; the debug panel toggles all labels together.
    /// </summary>
    public sealed class WorldLabel : MonoBehaviour
    {
        private static bool _visible;
        private static event Action<bool> VisibilityChanged;

        [SerializeField] private GameObject labelObject;

        public static bool Visible
        {
            get => _visible;
            set
            {
                _visible = value;
                VisibilityChanged?.Invoke(value);
            }
        }

        public void EditorSetup(GameObject label) => labelObject = label;

        private void OnEnable()
        {
            VisibilityChanged += Apply;
            Apply(_visible);
        }

        private void OnDisable() => VisibilityChanged -= Apply;

        private void Apply(bool visible)
        {
            if (labelObject != null) labelObject.SetActive(visible);
        }
    }
}
