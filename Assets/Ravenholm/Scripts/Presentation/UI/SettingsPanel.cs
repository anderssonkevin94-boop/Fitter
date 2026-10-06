using System;
using System.Collections.Generic;
using Ravenholm.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Player settings (GDD 19): music and effects volume, haptics, reduced motion, text size, joystick side
    /// and first-hour tips. Developer tools sit behind their own button.
    /// </summary>
    public sealed class SettingsPanel : MonoBehaviour
    {
        private UiFactory _ui;
        private UiTheme _theme;
        private GameRoot _root;
        private RectTransform _content;
        private TextMeshProUGUI _title;
        private readonly List<(TextMeshProUGUI label, Func<string> text)> _live = new List<(TextMeshProUGUI, Func<string>)>();

        public bool IsOpen => gameObject.activeSelf;

        public static SettingsPanel Create(Transform parent, UiFactory ui, GameRoot root)
        {
            var content = ui.ModalScroll(parent, "SettingsLayer", out var layer, out var title);
            var panel = layer.AddComponent<SettingsPanel>();
            panel._ui = ui;
            panel._theme = ui.Theme;
            panel._root = root;
            panel._content = content;
            panel._title = title;
            panel.Build();
            layer.SetActive(false);
            return panel;
        }

        private PlayerSettingsService Settings => _root.Settings;
        private PlayerSettings S => _root.Settings.Current;

        private static readonly float[] Volumes = { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };

        private static float NextVolume(float v)
        {
            for (var i = 0; i < Volumes.Length; i++) if (v < Volumes[i] - 0.01f) return Volumes[i];
            return 0f;
        }

        private static string Percent(float v) => v <= 0.001f ? "off" : $"{Mathf.RoundToInt(v * 100)}%";

        private void Build()
        {
            _title.text = "Settings";
            Row(() => $"Music: {Percent(S.MusicVolume)} · tap to change", () => Settings.Update(s => s.MusicVolume = NextVolume(s.MusicVolume)));
            Row(() => $"Sound effects: {Percent(S.EffectsVolume)} · tap to change", () => Settings.Update(s => s.EffectsVolume = NextVolume(s.EffectsVolume)));
            Row(() => $"Haptics: {(S.Haptics ? "on" : "off")}", () => Settings.Update(s => s.Haptics = !s.Haptics));
            Row(() => $"Reduced motion: {(S.ReducedMotion ? "on" : "off")}", () => Settings.Update(s => s.ReducedMotion = !s.ReducedMotion));
            Row(() => $"Text size: {Mathf.RoundToInt(S.TextScale * 100)}% · tap to change", CycleTextScale);
            Row(() => $"Joystick: {(S.JoystickSide == JoystickSide.Either ? "anywhere low on the screen" : S.JoystickSide == JoystickSide.Left ? "left half" : "right half")}",
                () => Settings.Update(s => s.JoystickSide = (JoystickSide)(((int)s.JoystickSide + 1) % 3)));
            Row(() => $"First-hour tips: {(Tips.Hidden ? "hidden" : "shown")}", ToggleTips);
            Row(() => "Show tips again from the start", () => Tips.Restart());
            var note = _ui.LiveText(_content, _theme.smallSize - 2f);
            note.text = "Settings are kept on this device and never reset with a save profile.";
            _devRow = Row(() => "Developer tools…", () => { Close(); _root.Hud.OpenDebug(); });
            _versionRow = Row(() => $"Version {UnityEngine.Application.version} · balance revision {_root.Services.Catalogue.BalanceRevision}" +
                                    (DeveloperMode ? " · developer mode" : ""), TapVersion);
            Row(() => "Close", Close);
        }

        private TutorialService Tips => _root.Services.Tutorial;

        private void ToggleTips()
        {
            Tips.SetHidden(!Tips.Hidden);
        }

        public void CycleTextScale()
        {
            var scales = PlayerSettingsService.TextScales;
            Settings.Update(s => s.TextScale = scales[(Array.IndexOf(scales, s.TextScale) + 1) % scales.Length]);
        }

        private GameObject _devRow, _versionRow;
        private int _versionTaps;
        private float _lastVersionTap;

        /// <summary>Developer tools are hidden in release builds unless unlocked (five taps on the version line).</summary>
        public bool DeveloperMode => S.DeveloperMode ?? (Debug.isDebugBuild || UnityEngine.Application.isEditor);

        public void TapVersion()
        {
            _versionTaps = Time.unscaledTime - _lastVersionTap < 1.5f ? _versionTaps + 1 : 1;
            _lastVersionTap = Time.unscaledTime;
            if (_versionTaps < 5) return;
            _versionTaps = 0;
            var on = !DeveloperMode;
            Settings.Update(s => s.DeveloperMode = on);
            _root.Hud.ShowToast(on ? "Developer mode on" : "Developer mode off");
        }

        private GameObject Row(Func<string> text, Action onClick)
        {
            var b = _ui.RowButton(_content, () => { onClick(); FeedbackAudio.Play(FeedbackAudio.Cue.Tap); Refresh(); }, out var label);
            _live.Add((label, text));
            return b.gameObject;
        }

        private void Refresh()
        {
            if (_devRow != null) _devRow.SetActive(DeveloperMode);
            foreach (var (label, text) in _live) label.text = text();
        }

        public void Show()
        {
            Refresh();
            if (IsOpen) return;
            gameObject.SetActive(true);
            TouchInputRouter.PushModal();
        }

        public void Close()
        {
            if (!IsOpen) return;
            gameObject.SetActive(false);
            TouchInputRouter.PopModal();
        }

        private void OnDestroy()
        {
            if (IsOpen) TouchInputRouter.PopModal();
        }
    }
}
