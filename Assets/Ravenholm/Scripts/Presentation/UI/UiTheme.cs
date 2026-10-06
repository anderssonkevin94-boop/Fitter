using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Placeholder UI kit references and type sizes. Swap sprites here, not in code.</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/UI Theme", fileName = "UiTheme")]
    public sealed class UiTheme : ScriptableObject
    {
        [Header("Panels (9-slice)")]
        public Sprite panel;
        public Sprite panelDark;
        public Sprite button;
        public Sprite chip;
        public Sprite roundButton;
        public Sprite joystickRing;
        public Sprite joystickKnob;

        [Header("Status icons")]
        public Sprite coin;
        public Sprite reputation;
        public Sprite carry;
        public Sprite check;
        public Sprite warning;
        public Sprite locked;
        public Sprite timer;
        public Sprite unknown;
        public Sprite interact;
        public Sprite settings;

        [Header("Colours")]
        public Color ink = new Color32(59, 42, 32, 255);
        public Color inkOnDark = new Color32(250, 242, 226, 255);
        public Color accent = new Color32(70, 136, 152, 255);
        public Color danger = new Color32(180, 64, 52, 255);

        [Header("Type (reference pixels at 1179 px wide)")]
        [Min(10)] public float bodySize = 40f;
        [Min(10)] public float smallSize = 32f;
        [Min(10)] public float titleSize = 54f;

        [Header("Touch")]
        [Tooltip("Minimum touch target in reference pixels (44 pt at 3x = 132).")]
        [Min(44)] public float minTouchTarget = 132f;

        [Header("Fixture HUD values (layout only, not systems yet)")]
        public int fixtureGold = 60;
        public int fixtureReputation = 20;
    }
}
