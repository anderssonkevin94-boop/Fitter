using System.Runtime.InteropServices;
using Ravenholm.Services;
using UnityEngine;

namespace Ravenholm.Presentation
{
    public enum HapticKind { Light = 0, Medium = 1, Heavy = 2, Success = 3, Warning = 4 }

    /// <summary>
    /// Player feel settings read by views (GDD 19/20): reduced motion, haptics and text scale. Reduced motion
    /// removes large item arcs and pulses but keeps every state change visible.
    /// </summary>
    public static class Feel
    {
        private static PlayerSettings Settings => GameRoot.Instance != null && GameRoot.Instance.Settings != null ? GameRoot.Instance.Settings.Current : null;

        /// <summary>Developer tools and debug readouts (GDD 24: gated). Default on in the editor and development builds.</summary>
        public static bool DeveloperMode => Settings != null ? Settings.DeveloperMode ?? DefaultDeveloperMode : DefaultDeveloperMode;
        private static bool DefaultDeveloperMode => Debug.isDebugBuild || UnityEngine.Application.isEditor;

        public static bool ReducedMotion => Settings != null && Settings.ReducedMotion;
        public static float TextScale => Settings != null ? Settings.TextScale : 1f;
        public static float EffectsVolume => Settings != null ? Settings.EffectsVolume : 0.8f;
        public static float MusicVolume => Settings != null ? Settings.MusicVolume : 0.4f;

        /// <summary>Haptics played so far this session (tests check the toggle).</summary>
        public static int HapticsPlayed { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _RavenholmHaptic(int style);
#endif

        public static void Haptic(HapticKind kind)
        {
            if (Settings == null || !Settings.Haptics) return;
            HapticsPlayed++;
#if UNITY_IOS && !UNITY_EDITOR
            _RavenholmHaptic((int)kind);
#endif
        }
    }
}
