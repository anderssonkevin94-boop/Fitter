using System;
using System.Text;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Persistent placeholder HUD for the Phase 0 sandbox: representative town HUD layout
    /// (fixture values), context button, inspect card, and the gated debug panel.
    /// It only reads services and submits commands; it never edits state directly.
    /// </summary>
    public sealed class SandboxHud : MonoBehaviour
    {
        public static readonly Vector2 ReferenceResolution = new Vector2(1179f, 2556f);

        private GameRoot _root;
        private UiFactory _ui;
        private UiTheme _theme;
        private Canvas _canvas;

        private TextMeshProUGUI _persistText;
        private Image _persistIcon;
        private Button _contextButton;
        private CanvasGroup _contextGroup;
        private TextMeshProUGUI _contextLabel;
        private GameObject _infoLayer;
        private TextMeshProUGUI _infoTitle;
        private TextMeshProUGUI _infoBody;
        private GameObject _debugLayer;
        private TextMeshProUGUI _debugStatus;
        private TextMeshProUGUI _labelsButtonText;
        private TextMeshProUGUI _stackButtonText;
        private TextMeshProUGUI _viewButtonText;
        private TextMeshProUGUI _resetButtonText;
        private string _debugMessage = "";
        private float _resetArmedUntil = -1f;
        private Inspectable _shownContext;

        public bool IsInfoOpen => _infoLayer != null && _infoLayer.activeSelf;
        public bool IsDebugOpen => _debugLayer != null && _debugLayer.activeSelf;
        public Canvas Canvas => _canvas;
        public string PersistenceText => _persistText != null ? _persistText.text : "";

        public static SandboxHud Create(Transform parent, GameRoot root)
        {
            var go = new GameObject("HudCanvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            var hud = go.AddComponent<SandboxHud>();
            hud.Build(root);
            return hud;
        }

        /// <summary>Renders the HUD through the scene camera so captures and sorting match the world.</summary>
        public void AttachWorldCamera(Camera cam)
        {
            if (cam == null)
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                return;
            }
            _canvas.renderMode = RenderMode.ScreenSpaceCamera;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = 1f;
            _canvas.sortingLayerName = "Overlay";
            _canvas.sortingOrder = 100;
        }

        private void Build(GameRoot root)
        {
            _root = root;
            _theme = root.Config.UiTheme;
            _ui = new UiFactory(_theme);

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;
            gameObject.AddComponent<GraphicRaycaster>();

            var safe = UiFactory.Rect("SafeArea", transform);
            UiFactory.Stretch(safe);
            safe.gameObject.AddComponent<SafeAreaFitter>().Apply();

            BuildTopBar(safe);
            BuildPersistenceCard(safe);
            BuildContextButton(safe);
            BuildInfoLayer(safe);
            BuildDebugLayer(safe);

            _root.Services.Saves.StatusChanged += _ => Refresh();
            _root.Services.Sandbox.StateChanged += _ => Refresh();
            Refresh();
        }

        private void OnDestroy()
        {
            if (IsInfoOpen) TouchInputRouter.PopModal();
            if (IsDebugOpen) TouchInputRouter.PopModal();
        }

        // ------------------------------------------------------------------ layout

        private void BuildTopBar(RectTransform safe)
        {
            var bar = UiFactory.Rect("TopBar", safe);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.sizeDelta = new Vector2(-64f, 140f);
            bar.anchoredPosition = new Vector2(0, -24f);

            var chips = UiFactory.Rect("Chips", bar);
            UiFactory.Place(chips, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(860f, 112f));
            var h = chips.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 18f;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = h.childForceExpandHeight = false;

            Chip(chips, _theme.coin, _theme.fixtureGold.ToString(), 250f);
            Chip(chips, _theme.reputation, _theme.fixtureReputation.ToString(), 220f);
            Chip(chips, _theme.carry, $"0/{_theme.fixtureCarryCapacity}", 270f);

            var gear = _ui.Panel("DebugButton", bar, _theme.roundButton);
            gear.type = Image.Type.Simple;
            UiFactory.Place(gear.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(132f, 132f));
            gear.gameObject.AddComponent<Button>().onClick.AddListener(OpenDebug);
            var gi = _ui.Icon("Icon", gear.transform, _theme.settings, 76f);
            UiFactory.Place(gi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4f), new Vector2(76f, 76f));

            var banner = _ui.Panel("PhaseBanner", safe, _theme.panelDark);
            UiFactory.Place(banner.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -180f), new Vector2(560f, 76f));
            banner.raycastTarget = false;
            var bt = _ui.Text("Text", banner.transform, "DEBUG SANDBOX · PHASE 0", _theme.smallSize - 4f, _theme.inkOnDark, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(bt.rectTransform, 8f);
        }

        private void Chip(Transform parent, Sprite icon, string value, float width)
        {
            var chip = _ui.Panel("Chip", parent, _theme.chip);
            chip.raycastTarget = false;
            var le = chip.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = width;
            le.preferredHeight = 104f;
            var h = chip.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.padding = new RectOffset(14, 24, 12, 16);
            h.spacing = 12f;
            h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = h.childControlHeight = true;
            h.childForceExpandWidth = false;
            _ui.Icon("Icon", chip.transform, icon, 72f);
            var t = _ui.Text("Value", chip.transform, value, _theme.titleSize - 6f, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }

        private void BuildPersistenceCard(RectTransform safe)
        {
            var card = _ui.Panel("PersistenceCard", safe, _theme.panelDark);
            card.raycastTarget = false;
            UiFactory.Place(card.rectTransform, Vector2.zero, Vector2.zero, new Vector2(32f, 40f), new Vector2(700f, 190f));
            _persistIcon = _ui.Icon("Icon", card.transform, _theme.check, 64f);
            UiFactory.Place(_persistIcon.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24f, -24f), new Vector2(64f, 64f));
            _persistText = _ui.Text("Text", card.transform, "", _theme.smallSize, _theme.inkOnDark, TextAlignmentOptions.TopLeft);
            UiFactory.Stretch(_persistText.rectTransform);
            _persistText.rectTransform.offsetMin = new Vector2(104f, 18f);
            _persistText.rectTransform.offsetMax = new Vector2(-20f, -22f);
        }

        private void BuildContextButton(RectTransform safe)
        {
            var holder = UiFactory.Rect("Context", safe);
            UiFactory.Place(holder, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-32f, 40f), new Vector2(300f, 330f));
            _contextGroup = holder.gameObject.AddComponent<CanvasGroup>();

            var btnImg = _ui.Panel("ContextButton", holder, _theme.roundButton);
            btnImg.type = Image.Type.Simple;
            UiFactory.Place(btnImg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(230f, 230f));
            _contextButton = btnImg.gameObject.AddComponent<Button>();
            _contextButton.onClick.AddListener(OnContextPressed);
            var icon = _ui.Icon("Icon", btnImg.transform, _theme.interact, 130f);
            UiFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 6f), new Vector2(130f, 130f));

            var label = _ui.Panel("LabelChip", holder, _theme.chip);
            label.raycastTarget = false;
            UiFactory.Place(label.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(300f, 88f));
            _contextLabel = _ui.Text("Text", label.transform, "", _theme.smallSize - 2f, _theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(_contextLabel.rectTransform, 10f);
            _contextLabel.enableAutoSizing = true;
            _contextLabel.fontSizeMin = 20f;
            _contextLabel.fontSizeMax = _theme.smallSize - 2f;
        }

        private void BuildInfoLayer(RectTransform safe)
        {
            var layer = UiFactory.Rect("InfoLayer", safe);
            UiFactory.Stretch(layer);
            // Backdrop is a sibling (not the parent) so taps on the card itself do not bubble up and close it.
            var backdropRt = UiFactory.Rect("Backdrop", layer);
            UiFactory.Stretch(backdropRt);
            var backdrop = backdropRt.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0.1f, 0.07f, 0.05f, 0.25f);
            backdropRt.gameObject.AddComponent<Button>().onClick.AddListener(HideInfo);

            var card = _ui.Panel("Card", layer, _theme.panel);
            UiFactory.Place(card.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 40f), new Vector2(1100f, 620f));
            UiFactory.Column(card.gameObject, 20f, new RectOffset(48, 48, 44, 40));
            _infoTitle = _ui.Text("Title", card.transform, "", _theme.titleSize, _theme.ink, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            _infoTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 72f;
            _infoBody = _ui.Text("Body", card.transform, "", _theme.bodySize, _theme.ink, TextAlignmentOptions.TopLeft);
            _infoBody.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
            _ui.Button("Close", card.transform, "Close", HideInfo, out _);

            _infoLayer = layer.gameObject;
            _infoLayer.SetActive(false);
        }

        private void BuildDebugLayer(RectTransform safe)
        {
            var layer = UiFactory.Rect("DebugLayer", safe);
            UiFactory.Stretch(layer);
            layer.gameObject.AddComponent<Image>().color = new Color(0.1f, 0.07f, 0.05f, 0.45f);

            var panel = _ui.Panel("Panel", layer, _theme.panel);
            UiFactory.Stretch(panel.rectTransform, 28f);

            var title = _ui.Text("Title", panel.transform, "Debug · Phase 0", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            UiFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(48f, -36f), new Vector2(800f, 80f));

            // Scrollable body so the panel fits short (16:9) and tall phones alike.
            var viewport = UiFactory.Rect("Viewport", panel.transform);
            UiFactory.Stretch(viewport);
            viewport.offsetMin = new Vector2(36f, 36f);
            viewport.offsetMax = new Vector2(-36f, -130f);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0);

            var content = UiFactory.Rect("Content", viewport);
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.sizeDelta = Vector2.zero;
            UiFactory.Column(content.gameObject, 18f, new RectOffset(12, 12, 8, 24));
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            _ui.Button("Close", content, "Close", CloseDebug, out _);
            _ui.Button("SaveNow", content, "Save now", () => Report(_root.Services.Sandbox.SaveNow("debug: save now") ? "Saved." : "Save FAILED, see status."), out _);
            _ui.Button("Reload", content, "Reload from disk", ReloadFromDisk, out _);
            _ui.Button("Corrupt", content, "Corrupt save file (test)", CorruptSave, out _);
            _ui.Button("Reset", content, "Reset sandbox save…", ResetPressed, out _resetButtonText);
            _ui.Button("Labels", content, "", ToggleLabels, out _labelsButtonText);
            _ui.Button("Stack", content, "", ToggleStack, out _stackButtonText);
            _ui.Button("ViewWidth", content, "", CycleViewWidth, out _viewButtonText);
            _ui.Button("Validate", content, "Validate content", ValidateContent, out _);

            _debugStatus = _ui.Text("Status", content, "", _theme.smallSize - 4f, _theme.ink, TextAlignmentOptions.TopLeft);
            _debugStatus.richText = true;

            _debugLayer = layer.gameObject;
            _debugLayer.SetActive(false);
        }

        // ------------------------------------------------------------------ behaviour

        private void Update()
        {
            UpdateContextButton();
            if (_resetArmedUntil > 0 && Time.unscaledTime > _resetArmedUntil)
            {
                _resetArmedUntil = -1f;
                if (_resetButtonText != null) _resetButtonText.text = "Reset sandbox save…";
            }
        }

        private void UpdateContextButton()
        {
            var target = SandboxSceneController.Current != null ? SandboxSceneController.Current.ContextTarget : null;
            if (target == _shownContext && _contextLabel.text.Length > 0) return;
            _shownContext = target;
            _contextButton.interactable = target != null;
            _contextGroup.alpha = target != null ? 1f : 0.55f;
            _contextLabel.text = target != null ? target.Title : "Walk closer";
        }

        private void OnContextPressed()
        {
            var c = SandboxSceneController.Current;
            if (c != null && c.ContextTarget != null) c.Inspect(c.ContextTarget);
        }

        public void ShowInfo(Inspectable target)
        {
            _infoTitle.text = target.Title;
            _infoBody.text = target.BuildDetail();
            if (!IsInfoOpen)
            {
                _infoLayer.SetActive(true);
                TouchInputRouter.PushModal();
            }
        }

        public void HideInfo()
        {
            if (!IsInfoOpen) return;
            _infoLayer.SetActive(false);
            TouchInputRouter.PopModal();
        }

        public void OpenDebug()
        {
            if (IsDebugOpen) return;
            HideInfo();
            _debugLayer.SetActive(true);
            TouchInputRouter.PushModal();
            _debugMessage = "";
            Refresh();
        }

        public void CloseDebug()
        {
            if (!IsDebugOpen) return;
            _debugLayer.SetActive(false);
            TouchInputRouter.PopModal();
        }

        private void ReloadFromDisk()
        {
            _root.Services.Sandbox.LoadFromDisk();
            SandboxSceneController.Current?.SnapPlayerToSavedPosition();
            Report($"Reloaded: {_root.Services.Saves.Status.LoadStatus}.");
        }

        private void CorruptSave()
        {
            _root.Services.Saves.Store.DebugCorruptPrimary();
            Report("Primary save file truncated. Tap 'Reload from disk' (or relaunch) to watch it recover from the backup.");
        }

        private void ResetPressed()
        {
            if (_resetArmedUntil < 0)
            {
                _resetArmedUntil = Time.unscaledTime + 3f;
                _resetButtonText.text = "Tap again to ERASE sandbox save";
                return;
            }
            _resetArmedUntil = -1f;
            _resetButtonText.text = "Reset sandbox save…";
            _root.Services.Sandbox.ResetProfile();
            SandboxSceneController.Current?.SnapPlayerToSavedPosition();
            Report("Sandbox profile erased and restarted. (Only the sandbox profile is affected.)");
        }

        private void ToggleLabels()
        {
            WorldLabel.Visible = !WorldLabel.Visible;
            Refresh();
        }

        private void ToggleStack()
        {
            CarryStackFixture.Visible = !CarryStackFixture.Visible;
            Refresh();
        }

        private void CycleViewWidth()
        {
            var s = _root.Config.SandboxSettings;
            var i = FollowCamera.ViewWidthIndex < 0 ? s.defaultViewWidthIndex : FollowCamera.ViewWidthIndex;
            FollowCamera.ViewWidthIndex = (i + 1) % s.viewWidthOptions.Length;
            Refresh();
        }

        private void ValidateContent()
        {
            var r = _root.Services.RevalidateContent();
            Report(r.IsValid ? $"Content OK ({r.DefinitionCount} definitions, all IDs unique)." : r.ToString());
        }

        private void Report(string message)
        {
            _debugMessage = message;
            Refresh();
        }

        private void Refresh()
        {
            if (_root == null || _root.Services == null) return;
            var status = _root.Services.Saves.Status;
            var state = _root.Services.Sandbox.State;

            var recovered = status.LoadStatus == SaveLoadStatus.RecoveredFromBackup || status.LoadStatus == SaveLoadStatus.Unrecoverable;
            _persistIcon.sprite = status.LastError != null || recovered ? _theme.warning : _theme.check;
            _persistText.text =
                $"Bell rung <b>{state.BellRingCount}</b>  ·  Session <b>{state.SessionCount}</b>\n" +
                $"Save gen {status.Generation}  ·  {Describe(status.LoadStatus)}" +
                (status.LastError != null ? "\n<color=#FFB3A0>Save error</color>" : "");

            if (_labelsButtonText != null) _labelsButtonText.text = $"Building labels: {(WorldLabel.Visible ? "ON" : "OFF")}";
            if (_stackButtonText != null) _stackButtonText.text = $"Carry stack fixture: {(CarryStackFixture.Visible ? "ON" : "OFF")}";
            if (_viewButtonText != null) _viewButtonText.text = $"View width: {FollowCamera.CurrentViewWidth(_root.Config.SandboxSettings):0.#} units";
            if (_debugStatus != null && IsDebugOpen) _debugStatus.text = BuildStatusText(status, state);
        }

        private string BuildStatusText(SaveStatus status, Domain.Sandbox.SandboxState state)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(_debugMessage)) sb.Append("<b>").Append(_debugMessage).Append("</b>\n\n");
            sb.Append("<b>Save</b>\n");
            sb.Append($"Profile: {_root.Services.Profile}  ·  schema {SaveEnvelope.CurrentSchemaVersion}\n");
            sb.Append($"Folder: {status.Directory}\n");
            sb.Append($"Loaded: {status.LoadStatus}");
            if (status.MigratedFromSchema > 0) sb.Append($" (migrated from schema {status.MigratedFromSchema})");
            sb.Append('\n');
            if (!string.IsNullOrEmpty(status.LoadDetail)) sb.Append($"Load notes: {status.LoadDetail}\n");
            sb.Append($"Generation: {status.Generation}\n");
            sb.Append($"Last saved: {(status.LastSavedUtc.HasValue ? status.LastSavedUtc.Value.ToString("u") : "never")} ({status.LastSaveReason})\n");
            if (status.LastError != null) sb.Append($"<color=#B44034>Last error: {status.LastError}</color>\n");
            if (status.ClockWentBackwards) sb.Append("Clock moved backwards this session (ignored, time kept).\n");
            sb.Append($"World seed: {_root.Services.Saves.WorldSeed}\n");
            sb.Append($"Player pos: ({state.PlayerX:0.0}, {state.PlayerY:0.0})\n\n");

            var r = _root.Services.ContentReport;
            sb.Append("<b>Content</b>\n").Append(r.IsValid ? $"OK: {r.DefinitionCount} definitions" : $"<color=#B44034>{r.Errors.Count} errors</color>").Append("\n\n");

            sb.Append("<b>Display</b>\n");
            sb.Append($"Screen {Screen.width}x{Screen.height}  ·  aspect {(float)Screen.height / Mathf.Max(1, Screen.width):0.00}:1\n");
            sb.Append($"Safe area {Screen.safeArea}\n");
            sb.Append($"Build {UnityEngine.Application.version}  ·  Unity {UnityEngine.Application.unityVersion}\n");
            sb.Append("\nGold / reputation / carry chips are layout fixtures, not systems.");
            return sb.ToString();
        }

        private static string Describe(SaveLoadStatus s)
        {
            switch (s)
            {
                case SaveLoadStatus.NoSave: return "new profile";
                case SaveLoadStatus.LoadedPrimary: return "loaded";
                case SaveLoadStatus.RecoveredFromBackup: return "<color=#FFD27A>recovered from backup</color>";
                case SaveLoadStatus.Unrecoverable: return "<color=#FFB3A0>unreadable, started fresh (files kept)</color>";
                default: throw new ArgumentOutOfRangeException(nameof(s), s, null);
            }
        }
    }
}
