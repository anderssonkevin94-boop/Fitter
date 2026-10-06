using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Tunables for the Phase 0 sandbox. Movement values are GDD starting points; the
    /// Phase 1 BalanceProfile will take ownership of the gameplay ones.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Sandbox Settings", fileName = "SandboxSettings")]
    public sealed class SandboxSettings : ScriptableObject
    {
        [Header("Movement (GDD 3 starting points)")]
        [Tooltip("World units per second. GDD default 3.5.")]
        [Min(0.1f)] public float walkSpeed = 3.5f;

        [Tooltip("Walkable town footprint in world units (GDD: about 24 x 18).")]
        public Rect plotBounds = new Rect(-12f, -9f, 24f, 18f);

        [Header("Camera")]
        [Tooltip("Visible world width in units. The debug panel cycles through these to compare framing; the game itself has no zoom (locked).")]
        public float[] viewWidthOptions = { 7f, 8.5f, 10f };
        [Min(0)] public int defaultViewWidthIndex = 1;
        [Tooltip("Shifts the camera up so the player sits slightly below centre, leaving thumb room.")]
        public float cameraLookAheadY = 0.6f;

        [Header("Touch (screen points)")]
        [Tooltip("Drag distance before movement starts.")]
        [Min(0)] public float dragDeadZonePoints = 8f;
        [Tooltip("Drag distance that maps to full speed.")]
        [Min(1)] public float dragRadiusPoints = 60f;
        [Tooltip("A touch shorter than this, that barely moved, counts as a tap.")]
        [Min(0.05f)] public float tapMaxSeconds = 0.3f;
        [Min(1)] public float tapMaxMovePoints = 12f;

        [Header("Saving")]
        [Tooltip("Seconds after a cheap change (player position) before an autosave.")]
        [Min(0.5f)] public float autosaveDebounceSeconds = 3f;
    }
}
