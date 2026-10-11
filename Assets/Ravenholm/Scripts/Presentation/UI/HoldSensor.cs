using UnityEngine;
using UnityEngine.EventSystems;

namespace Ravenholm.Presentation
{
    /// <summary>Reports whether a UI element is held down (for hold-to-work on the context button).</summary>
    public sealed class HoldSensor : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }

        public void OnPointerDown(PointerEventData e) => Held = true;
        public void OnPointerUp(PointerEventData e) => Held = false;
        public void OnPointerExit(PointerEventData e) => Held = false;
        private void OnDisable() => Held = false;
    }
}
