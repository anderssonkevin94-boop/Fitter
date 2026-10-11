using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Presentation and control-feel tunables (camera, joystick, saving cadence). Gameplay
    /// numbers such as walk speed and carry capacity live in <see cref="Content.BalanceProfile"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Sandbox Settings", fileName = "SandboxSettings")]
    public sealed class SandboxSettings : ScriptableObject
    {
        [Header("Town")]
        [Tooltip("Walkable town footprint in world units (GDD: about 24 x 18).")]
        public Rect plotBounds = new Rect(-12f, -9f, 24f, 18f);
        [Tooltip("The frontier: the town's ground continues this far left of the town (world units). The Forest grows on it " +
                 "and the Mine's tunnels run under it; both progress left, so raise this as more is charted.")]
        [Min(0f)] public float frontierLength = 48f;

        /// <summary>Walkable world: the town plus the frontier strip on its left.</summary>
        public Rect WorldBounds => new Rect(plotBounds.xMin - frontierLength, plotBounds.yMin, plotBounds.width + frontierLength, plotBounds.height);

        [Header("Camera")]
        [Tooltip("Visible world width in units. The debug panel cycles through these to compare framing; the game itself has no zoom (locked).")]
        public float[] viewWidthOptions = { 7f, 8.5f, 10f };
        [Min(0)] public int defaultViewWidthIndex = 1;
        [Tooltip("Shifts the camera up so the player sits slightly below centre, leaving thumb room.")]
        public float cameraLookAheadY = 0.6f;
        [Tooltip("Half-size (units) of the box the player can move in before the camera follows.")]
        public Vector2 cameraDeadZone = new Vector2(0.45f, 0.7f);
        [Tooltip("Camera smoothing time in seconds (gentle damping, GDD 3).")]
        [Min(0f)] public float cameraSmoothTime = 0.18f;

        [Header("Floating joystick (screen points)")]
        [Tooltip("Joystick touches must start in this lower fraction of the screen (GDD 3: lower movement area). Taps work anywhere.")]
        [Range(0.3f, 1f)] public float movementAreaHeight = 0.72f;
        [Tooltip("Drag distance before movement starts.")]
        [Min(0)] public float dragDeadZonePoints = 8f;
        [Tooltip("Drag distance that maps to full speed.")]
        [Min(1)] public float dragRadiusPoints = 60f;
        [Tooltip("A touch shorter than this, that barely moved, counts as a tap.")]
        [Min(0.05f)] public float tapMaxSeconds = 0.3f;
        [Min(1)] public float tapMaxMovePoints = 12f;

        [Header("Saving")]
        [Tooltip("Seconds after a cheap change (movement, hauling) before an autosave. Critical commands save at once.")]
        [Min(0.5f)] public float autosaveDebounceSeconds = 1.5f;
    }
}
