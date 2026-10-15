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
        [Header("World grid (cells: x runs right-up, y runs left-up)")]
        [Tooltip("The town plot as a rectangle of grid cells, so every edge is a straight cliff face along the grid.")]
        public RectInt townCells = new RectInt(-20, -16, 48, 37);
        [Tooltip("The Mine's corner: the first cell of the cliff-face slice the supplier digs. It sits on the town's left edge, so " +
                 "the cliff is one flush face from the town along the frontier.")]
        public Vector2Int mineCell = new Vector2Int(-20, 17);
        [Tooltip("The frontier beyond the town's upper-left edge: how far it runs left (cells along y) and back (cells along x). " +
                 "Tunnels run along its front edge and the Forest grows on it; raise these as more is charted.")]
        [Min(4)] public int frontierLengthCells = 100;
        [Min(4)] public int frontierWidthCells = 30;
        [Tooltip("The camp this scene shows (the journey, 2026-10-15): its clearing is the town plot, wrapped by forest. " +
                 "When set, the frontier above is not used.")]
        public Content.CampDefinition camp;

        /// <summary>World position of a cell's centre on the town grid (cell size 1 × 0.5, isometric).</summary>
        public static Vector2 CellCentre(int x, int y) => new Vector2((x - y) * 0.5f, (x + y) * 0.25f + 0.25f);

        /// <summary>Walkable world, as a bounding rectangle: the town plot plus the frontier (tiles decide the exact shape).</summary>
        public Rect WorldBounds
        {
            get
            {
                if (camp != null)
                {
                    var cb = camp.Bounds;
                    var a = CellCentre(cb.xMin, cb.yMin);
                    var r = CellCentre(cb.xMax - 1, cb.yMin);
                    var l = CellCentre(cb.xMin, cb.yMax - 1);
                    var top = CellCentre(cb.xMax - 1, cb.yMax - 1);
                    return Rect.MinMaxRect(l.x - 0.5f, a.y - 0.5f, r.x + 0.5f, top.y + 0.5f);
                }
                var t = townCells;
                var fy0 = t.yMax;
                var corners = new[]
                {
                    CellCentre(t.xMin, t.yMin), CellCentre(t.xMax - 1, t.yMin), CellCentre(t.xMin, t.yMax - 1), CellCentre(t.xMax - 1, t.yMax - 1),
                    CellCentre(mineCell.x, mineCell.y), CellCentre(mineCell.x, fy0 + frontierLengthCells),
                    CellCentre(mineCell.x + frontierWidthCells, fy0), CellCentre(mineCell.x + frontierWidthCells, fy0 + frontierLengthCells),
                };
                var min = corners[0];
                var max = corners[0];
                foreach (var c in corners) { min = Vector2.Min(min, c); max = Vector2.Max(max, c); }
                return Rect.MinMaxRect(min.x - 0.5f, min.y - 0.5f, max.x + 0.5f, max.y + 0.5f);
            }
        }

        [Header("Camera")]
        [Tooltip("Visible world width in units. The debug panel cycles through these to compare framing; the game itself has no zoom (locked).")]
        public float[] viewWidthOptions = { 6f, 7f, 8.5f };
        [Min(0)] public int defaultViewWidthIndex = 1;
        [Tooltip("Shifts the camera up so the player sits slightly below centre, leaving thumb room.")]
        public float cameraLookAheadY = 2.4f;   // the journey: more of the road ahead on screen, under the horizon
        [Tooltip("The game-drawn outline: thickness, colour and which sprites get it.")]
        public OutlineSettings outlines;
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
