using System;
using System.IO;
using Newtonsoft.Json;

namespace Ravenholm.Services
{
    public enum JoystickSide { Either, Left, Right }

    /// <summary>Device-level preferences (GDD 19): kept outside the game save so a profile reset never touches them.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PlayerSettings
    {
        [JsonProperty("musicVolume")] public float MusicVolume = 0.4f;
        [JsonProperty("effectsVolume")] public float EffectsVolume = 0.8f;
        [JsonProperty("haptics")] public bool Haptics = true;
        [JsonProperty("reducedMotion")] public bool ReducedMotion;
        [JsonProperty("textScale")] public float TextScale = 1f;
        [JsonProperty("joystickSide")] public JoystickSide JoystickSide = JoystickSide.Either;
        /// <summary>Developer tools visible (null = default: on in development builds, off in release builds).</summary>
        [JsonProperty("developerMode")] public bool? DeveloperMode;

        public PlayerSettings Clone() => (PlayerSettings)MemberwiseClone();
    }

    /// <summary>Loads and saves <see cref="PlayerSettings"/> as a small JSON file (temp file, then replace).</summary>
    public sealed class PlayerSettingsService
    {
        public static readonly float[] TextScales = { 1f, 1.15f, 1.3f };
        private readonly string _path;

        public PlayerSettingsService(string directory)
        {
            _path = directory != null ? Path.Combine(directory, "settings.json") : null;
            Current = Load();
        }

        public PlayerSettings Current { get; private set; }
        public event Action<PlayerSettings> Changed;

        private PlayerSettings Load()
        {
            try
            {
                if (_path != null && File.Exists(_path))
                {
                    var s = JsonConvert.DeserializeObject<PlayerSettings>(File.ReadAllText(_path));
                    if (s != null) return Sanitise(s);
                }
            }
            catch (Exception)
            {
                // A damaged settings file only costs preferences: fall back to defaults (game saves are separate).
            }
            return new PlayerSettings();
        }

        private static PlayerSettings Sanitise(PlayerSettings s)
        {
            s.MusicVolume = Clamp01(s.MusicVolume);
            s.EffectsVolume = Clamp01(s.EffectsVolume);
            if (Array.IndexOf(TextScales, s.TextScale) < 0) s.TextScale = 1f;
            return s;
        }

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Applies a change and writes it at once (settings changes are rare and explicit).</summary>
        public void Update(Action<PlayerSettings> change)
        {
            var next = Current.Clone();
            change(next);
            Current = Sanitise(next);
            Write();
            Changed?.Invoke(Current);
        }

        private void Write()
        {
            if (_path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonConvert.SerializeObject(Current, Formatting.Indented));
                if (File.Exists(_path)) File.Replace(tmp, _path, null);
                else File.Move(tmp, _path);
            }
            catch (Exception)
            {
                // Preferences stay in memory for this session.
            }
        }
    }
}
