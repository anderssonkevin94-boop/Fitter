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
        private TextMeshProUGUI _carryText;
        private TextMeshProUGUI _goldText;
        private TextMeshProUGUI _repText;
        private TextMeshProUGUI _boardBadge;
        private JobBoardPanel _jobBoard;
        private LoadoutPanel _loadout;
        private ResultPanel _result;
        private TextMeshProUGUI _forceButtonText;
        private RectTransform _carryChip;
        private float _carryPulse;
        private Image _joyRing;
        private Image _joyKnob;
        private CanvasGroup _toastGroup;
        private TextMeshProUGUI _toastText;
        private float _toastUntil;
        private Image _persistIcon;
        private Button _contextButton;
        private CanvasGroup _contextGroup;
        private TextMeshProUGUI _contextLabel;
        private GameObject _infoLayer;
        private TextMeshProUGUI _infoTitle;
        private TextMeshProUGUI _infoBody;
        private GameObject _debugLayer;
        private SitePanel _sitePanel;
        private StationPanel _stationPanel;
        private StorePanel _storePanel;
        private TraderPanel _traderPanel;
        private int _grantIndex;
        private TextMeshProUGUI _grantPickText;
        private GameObject _banner;
        private readonly System.Collections.Generic.List<GameObject> _devOnly = new System.Collections.Generic.List<GameObject>();
        private SettingsPanel _settingsPanel;
        private TipCard _tips;
        private AwayReportPanel _awayPanel;
        private bool _traderWasPresent;
        private bool _awayChecked;
        private TextMeshProUGUI _speedButtonText;
        private TextMeshProUGUI _debugStatus;
        private TextMeshProUGUI _labelsButtonText;
        private TextMeshProUGUI _viewButtonText;
        private TextMeshProUGUI _resetButtonText;
        private string _debugMessage = "";
        private float _resetArmedUntil = -1f;
        private Inspectable _shownContext;

        public bool IsInfoOpen => _infoLayer != null && _infoLayer.activeSelf;
        public bool IsDebugOpen => _debugLayer != null && _debugLayer.activeSelf;
        public SitePanel SitePanel => _sitePanel;
        public StationPanel StationPanel => _stationPanel;
        public StorePanel StorePanel => _storePanel;
        public TraderPanel TraderPanel => _traderPanel;
        public AwayReportPanel AwayPanel => _awayPanel;

        public SettingsPanel SettingsPanel => _settingsPanel;
        public TipCard Tips => _tips;

        /// <summary>Player settings (GDD 19): audio, haptics, motion, text size, joystick side, tips; developer tools inside.</summary>
        public void ShowSettings()
        {
            HideInfo();
            _settingsPanel.Show();
        }

        public void ShowTrader()
        {
            HideInfo();
            _traderPanel.Show();
        }

        public void ShowAwayReport(Services.AwayReport report)
        {
            HideInfo();
            _awayPanel.Show(report);
        }

        public void ShowStation(StationInspectable station)
        {
            HideInfo();
            _stationPanel.Show(station);
        }

        public JobBoardPanel JobBoard => _jobBoard;
        public LoadoutPanel Loadout => _loadout;
        public ResultPanel Result => _result;

        /// <summary>Opens a result card on demand (results never block movement on their own, GDD 2).</summary>
        public void ShowResult(string expeditionId)
        {
            HideInfo();
            _jobBoard.Close();
            _result.Show(expeditionId);
        }

        public void ShowJobBoard()
        {
            HideInfo();
            _storePanel.Close();
            _loadout.Close();
            _jobBoard.Show();
        }

        /// <summary>Meet an applicant: card, loadout, forecast and Send (closes the board behind it).</summary>
        public void ShowLoadout(string contractorId)
        {
            HideInfo();
            _jobBoard.Close();
            _loadout.Show(contractorId);
        }

        public void ShowStore()
        {
            HideInfo();
            _storePanel.Show();
        }

        /// <summary>Opens the Mine or Forest panel (strata or trail, decisions).</summary>
        public void ShowSite(string siteId)
        {
            HideInfo();
            _sitePanel.Show(siteId);
        }
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
            BuildToast(safe);
            _tips = TipCard.Create(safe, _ui, root);   // below every modal layer: tips never cover a panel
            BuildJoystick();
            BuildInfoLayer(safe);
            _sitePanel = SitePanel.Create(safe, _ui, root);
            _stationPanel = StationPanel.Create(safe, _ui, root);
            _storePanel = StorePanel.Create(safe, _ui, root);
            _jobBoard = JobBoardPanel.Create(safe, _ui, root);
            _loadout = LoadoutPanel.Create(safe, _ui, root);
            _result = ResultPanel.Create(safe, _ui, root);
            _traderPanel = TraderPanel.Create(safe, _ui, root);
            _settingsPanel = SettingsPanel.Create(safe, _ui, root);
            _awayPanel = AwayReportPanel.Create(safe, _ui, root);
            root.ReturnedFromAway += ShowAwayReport;
            _root.Services.Jobs.Completed += r =>
            {
                ShowToast(r.Died ? $"{r.ContractorName} did not return" : $"{r.ContractorName} is back: {ResultPanel.OutcomeWord(r.Outcome)}");
                var good = r.Outcome == Domain.Jobs.Outcome.Success || r.Outcome == Domain.Jobs.Outcome.GreatSuccess;
                FeedbackAudio.Play(r.BlueprintGranted != null ? FeedbackAudio.Cue.Discovery : good ? FeedbackAudio.Cue.ReturnGood : FeedbackAudio.Cue.ReturnBad);
            };
            BuildDebugLayer(safe);

            _root.Services.Saves.StatusChanged += _ => Refresh();
            _root.Services.Sandbox.StateChanged += _ => Refresh();
            _root.Services.Logistics.System.Changed += RefreshCarry;
            _root.Services.Logistics.Reloaded += RefreshCarry;
            Refresh();
            RefreshCarry();
        }

        private void OnDestroy()
        {
            if (IsInfoOpen) TouchInputRouter.PopModal();
            if (IsDebugOpen) TouchInputRouter.PopModal();
            if (_root != null) _root.ReturnedFromAway -= ShowAwayReport;
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

            _goldText = Chip(chips, _theme.coin, "0", 250f);
            _repText = Chip(chips, _theme.reputation, "0", 220f);
            _carryText = Chip(chips, _theme.carry, "0/0", 270f);
            _carryChip = (RectTransform)_carryText.transform.parent;

            // Job Board shortcut (GDD 11: also reachable from the Store).
            var board = _ui.Panel("BoardButton", bar, _theme.roundButton);
            board.type = Image.Type.Simple;
            UiFactory.Place(board.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-150f, 0), new Vector2(132f, 132f));
            board.gameObject.AddComponent<Button>().onClick.AddListener(ShowJobBoard);
            var bi = _ui.Icon("Icon", board.transform, _theme.board != null ? _theme.board : _theme.interact, 80f);
            UiFactory.Place(bi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4f), new Vector2(80f, 80f));
            var badge = _ui.Panel("Badge", board.transform, _theme.chip, new Color32(200, 72, 60, 255));
            badge.raycastTarget = false;
            UiFactory.Place(badge.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(10f, 10f), new Vector2(64f, 56f));
            _boardBadge = _ui.Text("Count", badge.transform, "", _theme.smallSize - 4f, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(_boardBadge.rectTransform);

            var gear = _ui.Panel("DebugButton", bar, _theme.roundButton);
            gear.type = Image.Type.Simple;
            UiFactory.Place(gear.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(132f, 132f));
            gear.gameObject.AddComponent<Button>().onClick.AddListener(ShowSettings);
            var gi = _ui.Icon("Icon", gear.transform, _theme.settings, 76f);
            UiFactory.Place(gi.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 4f), new Vector2(76f, 76f));

            var banner = _ui.Panel("PhaseBanner", safe, _theme.panelDark);
            _banner = banner.gameObject;
            UiFactory.Place(banner.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -180f), new Vector2(560f, 76f));
            banner.raycastTarget = false;
            var onDebugProfile = _root.ActiveProfile == Services.PlayerSettingsService.DebugProfile;
            var bt = _ui.Text("Text", banner.transform, onDebugProfile ? "DEVELOPER SAVE · not your company" : "DEBUG SANDBOX · PHASE 9", _theme.smallSize - 4f, _theme.inkOnDark, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(bt.rectTransform, 8f);
            AutoFit(bt, 18f);
        }

        /// <summary>Fixed-size labels shrink to fit large text settings instead of overflowing.</summary>
        public static void AutoFit(TextMeshProUGUI t, float min)
        {
            t.enableAutoSizing = true;
            t.fontSizeMax = t.fontSize;
            t.fontSizeMin = Mathf.Min(min, t.fontSize);
        }

        private TextMeshProUGUI Chip(Transform parent, Sprite icon, string value, float width)
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
            t.enableAutoSizing = true;   // large text settings shrink to fit the chip rather than overflow
            t.fontSizeMin = 24f;
            t.fontSizeMax = _theme.titleSize - 6f;
            t.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            return t;
        }

        private void BuildToast(RectTransform safe)
        {
            var toast = _ui.Panel("Toast", safe, _theme.panelDark);
            toast.raycastTarget = false;
            UiFactory.Place(toast.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -280f), new Vector2(460f, 96f));
            _toastText = _ui.Text("Text", toast.transform, "", _theme.bodySize, _theme.inkOnDark, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(_toastText.rectTransform, 10f);
            _toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            _toastGroup.alpha = 0f;
            _toastGroup.blocksRaycasts = false;
        }

        private void BuildJoystick()
        {
            // Sits on the canvas root (not the safe area) so it can follow the thumb anywhere.
            _joyRing = _ui.Icon("JoystickRing", transform, _theme.joystickRing, 10f);
            _joyKnob = _ui.Icon("JoystickKnob", transform, _theme.joystickKnob, 10f);
            foreach (var img in new[] { _joyRing, _joyKnob })
            {
                Destroy(img.GetComponent<LayoutElement>());
                img.rectTransform.anchorMin = img.rectTransform.anchorMax = Vector2.zero;
                img.color = new Color(1, 1, 1, 0.85f);
                img.gameObject.SetActive(false);
            }
        }

        /// <summary>Brief, non-blocking message. Repeats of the same text while visible are ignored (no spam).</summary>
        public void ShowToast(string message)
        {
            if (Time.unscaledTime < _toastUntil && _toastText.text == message) return;
            _toastText.text = message;
            _toastUntil = Time.unscaledTime + 1.4f;
        }

        public string ToastText => Time.unscaledTime < _toastUntil ? _toastText.text : "";

        private void RefreshCarry()
        {
            var sys = _root.Services.Logistics.System;
            var text = $"{sys.CarryCount}/{sys.Capacity}";
            if (_carryText.text != text && sys.IsFull) _carryPulse = 1f;
            _carryText.text = text;
            _carryText.color = sys.IsFull ? _theme.danger : _theme.ink;
            if (IsDebugOpen) Refresh();
        }

        private void BuildPersistenceCard(RectTransform safe)
        {
            var card = _ui.Panel("PersistenceCard", safe, _theme.panelDark);
            _devOnly.Add(card.gameObject);   // Phase 0 save readout: developer mode only
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
            _contextHold = btnImg.gameObject.AddComponent<HoldSensor>();
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

            // The prospector's tap (Mine only): above the context button, so the thumb finds it.
            var tap = _ui.Panel("TapButton", safe, _theme.roundButton);
            tap.type = Image.Type.Simple;
            UiFactory.Place(tap.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-100f, 400f), new Vector2(150f, 150f));
            _tapButton = tap.gameObject.AddComponent<Button>();
            _tapButton.onClick.AddListener(() => MineProspectView.Current?.Tap());
            _tapLabel = _ui.Text("Text", tap.transform, "Tap", _theme.smallSize - 4f, _theme.ink, TextAlignmentOptions.Center, FontStyles.Bold);
            UiFactory.Stretch(_tapLabel.rectTransform, 12f);
            tap.gameObject.SetActive(false);
        }

        private Button _tapButton;
        private TextMeshProUGUI _tapLabel;

        /// <summary>The prospector's tap shows near the Mine, greyed while it recovers.</summary>
        private void UpdateTapButton()
        {
            var view = MineProspectView.Current;
            var show = view != null && view.CanTap && !IsAnyPanelOpenForTap;
            if (_tapButton.gameObject.activeSelf != show) _tapButton.gameObject.SetActive(show);
            if (!show) return;
            var wait = view.TapCooldownLeft;
            _tapButton.interactable = wait <= 0f;
            var text = wait > 0f ? Mathf.CeilToInt(wait).ToString() : "Tap";
            if (_tapLabel.text != text) _tapLabel.text = text;
        }

        private bool IsAnyPanelOpenForTap => TouchInputRouter.IsBlocked;

        public bool TapButtonShowing => _tapButton != null && _tapButton.gameObject.activeSelf;

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

            var title = _ui.Text("Title", panel.transform, "Debug · Phase 9", _theme.titleSize, _theme.ink, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
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
            _ui.Button("Refill", content, "Refill test piles and pads", () =>
            {
                _root.Services.Logistics.RefillFixtures();
                Report("Fixtures refilled to their authored contents.");
            }, out _);
            _ui.Button("Unload", content, "Unload carry into Store (debug)", () =>
            {
                var n = _root.Services.Logistics.DebugUnloadCarry();
                Report($"Unloaded {n} carried unit(s) into shared inventory / Store.");
            }, out _);
            _ui.Button("Unstuck", content, "Unstuck (keeps carried goods)", () =>
            {
                SandboxSceneController.Current?.Unstuck();
                Report("Moved to the Store front. Carry unchanged.");
            }, out _);
            _ui.Button("Audit", content, "Run ownership audit", () =>
            {
                var problems = _root.Services.Logistics.System.Audit();
                Report(problems.Count == 0 ? "Ownership audit OK: every unit has exactly one owner." : "AUDIT FAILED:\n" + string.Join("\n", problems));
            }, out _);
            _ui.Button("Reset", content, "Reset sandbox save…", ResetPressed, out _resetButtonText);
            _ui.Button("Labels", content, "", ToggleLabels, out _labelsButtonText);
            _ui.Button("ViewWidth", content, "", CycleViewWidth, out _viewButtonText);
            _ui.Button("Speed", content, "", CycleSpeed, out _speedButtonText);
            _ui.Button("MineDone", content, "Complete Mine section (debug)", () =>
            {
                _root.Services.Sites.DebugCompleteSection("site.mine");
                Report("Mine section completed instantly. Units were still extracted and must be hauled.");
            }, out _);
            _ui.Button("ForestDone", content, "Complete Forest clearing (debug)", () =>
            {
                _root.Services.Sites.DebugCompleteSection("site.forest");
                Report("Forest clearing completed instantly. Units were still extracted and must be hauled.");
            }, out _);
            _ui.Button("Measure", content, "Reset throughput measurement", () =>
            {
                _root.Services.Sites.ResetMeasurements();
                Report("Throughput measurement restarted for both sites.");
            }, out _);
            _ui.Button("Blueprint", content, "Grant test blueprint: Copper ward staff", () =>
            {
                _root.Services.Production.DebugGrantBlueprint("blueprint.copper_ward_staff");
                Report("Test unlock granted: Copper ward staff (Workbench). Advanced recipes otherwise come from quests (Phase 5).");
            }, out _);
            _ui.Button("Reveal", content, "Reveal all threat knowledge (debug)", () =>
                Report($"{_root.Services.Jobs.DebugRevealKnowledge()} threat facts revealed."), out _);
            _ui.Button("Pick", content, "", () =>
            {
                var res = _root.Services.Catalogue.Resources;
                _grantIndex = (_grantIndex + 1) % res.Count;
                Refresh();
            }, out _grantPickText);
            _ui.Button("Grant", content, "Add 10 of the chosen material (debug)", () =>
            {
                var r = _root.Services.Catalogue.Resources[_grantIndex % _root.Services.Catalogue.Resources.Count];
                _root.Services.Production.DebugGrantMaterials(new System.Collections.Generic.List<Content.ResourceCost> { new Content.ResourceCost { resource = r, quantity = 10 } });
                Report($"Added 10 {r.DisplayName} (debug source).");
            }, out _);
            _ui.Button("Materials", content, "Add test materials (ingots, lumber, herbs)", () =>
            {
                var c = _root.Services.Catalogue;
                var grant = new System.Collections.Generic.List<Content.ResourceCost>();
                foreach (var (id, q) in new[] { ("resource.iron_ingot", 10), ("resource.copper_ingot", 6), ("resource.cedar_lumber", 10), ("resource.herb", 6) })
                    grant.Add(new Content.ResourceCost { resource = c.Get<Content.ResourceDefinition>(id), quantity = q });
                _root.Services.Production.DebugGrantMaterials(grant);
                Report("Added 10 iron ingots, 6 copper ingots, 10 cedar lumber, 6 herbs to shared inventory (debug source).");
            }, out _);
            _ui.Button("Force", content, "", () =>
            {
                var jobs = _root.Services.Jobs;
                var cur = jobs.State.ForcedNext;
                Domain.Jobs.Outcome? next = cur == null ? Domain.Jobs.Outcome.GreatSuccess
                    : cur == Domain.Jobs.Outcome.Death ? (Domain.Jobs.Outcome?)null : (Domain.Jobs.Outcome)((int)cur.Value - 1);
                jobs.DebugForceNext(next);
                Refresh();
            }, out _forceButtonText);
            _ui.Button("FinishNow", content, "Finish expeditions now (debug)", () =>
            {
                _root.Services.Jobs.DebugFinishExpeditionsNow();
                Report("Running expeditions will resolve on the next frame.");
            }, out _);
            _ui.Button("Departures", content, "Return test departures (debug)", () =>
            {
                var n = _root.Services.Jobs.DebugReturnTestDepartures();
                Report($"{n} test departure(s) undone: gear back in the Store, fees refunded, contractors available.");
            }, out _);
            _ui.Button("Gold", content, "Set gold: 0 / 60 / 500 (debug)", () =>
            {
                var g = _root.Services.Jobs.State.Gold;
                _root.Services.Jobs.DebugSetGold(g == 0 ? 60 : g == 60 ? 500 : 0);
                Report($"Gold set to {_root.Services.Jobs.State.Gold}.");
            }, out _);
            _ui.Button("Rep", content, "Set reputation: 0 / 20 / 40 / 70 (debug)", () =>
            {
                var r = _root.Services.Jobs.State.Reputation;
                _root.Services.Jobs.DebugSetReputation(r < 20 ? 20 : r < 40 ? 40 : r < 70 ? 70 : 0);
                Report($"Reputation set to {_root.Services.Jobs.State.Reputation} (changes applicant count and fees for new postings).");
            }, out _);
            foreach (var (label, hours) in new[] { ("+1 h", 1), ("+8 h", 8), ("+24 h", 24) })
            {
                var h = hours;
                _ui.Button("Away" + h, content, $"Simulate time away {label} (debug)", () =>
                {
                    CloseDebug();
                    var report = _root.Services.DebugTimeAway(h * 3600000L);
                    ShowAwayReport(report);
                }, out _);
            }
            _ui.Button("Profile", content, "Switch save: company / developer scratch (on next launch)", () =>
            {
                var toDebug = _root.Settings.Current.Profile == null;
                _root.Settings.Update(s => s.Profile = toDebug ? Services.PlayerSettingsService.DebugProfile : null);
                Report(toDebug ? "Next launch uses the developer scratch save. Your company save is untouched. Close and reopen the app."
                               : "Next launch returns to the company save. Close and reopen the app.");
            }, out _);
            _ui.Button("Diag", content, "Export diagnostic report", () =>
            {
                var dir = System.IO.Path.Combine(UnityEngine.Application.persistentDataPath, "Diagnostics");
                System.IO.Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, $"diag_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
                System.IO.File.WriteAllText(path, _root.Services.DiagnosticReport(UnityEngine.Application.version, _root.Services.Catalogue.BalanceRevision));
                Report("Diagnostic report written to " + path);
            }, out _);
            _ui.Button("Perf", content, "Record performance (60 s, writes a report)", () =>
            {
                CloseDebug();
                PerfProbe.Begin(60f);
                ShowToast("Recording performance for 60 s");
            }, out _);
            _ui.Button("Trader", content, "Summon the Trader now (debug)", () =>
            {
                _root.Services.Economy.DebugSummonTrader();
                Report("The Trader is in town (schedule re-anchored to now).");
            }, out _);
            _ui.Button("Validate", content, "Validate content", ValidateContent, out _);

            _debugStatus = _ui.Text("Status", content, "", _theme.smallSize - 4f, _theme.ink, TextAlignmentOptions.TopLeft);
            _debugStatus.richText = true;

            _debugLayer = layer.gameObject;
            _debugLayer.SetActive(false);
        }

        // ------------------------------------------------------------------ behaviour

        private void Update()
        {
            var dev = Feel.DeveloperMode;
            foreach (var go in _devOnly) if (go.activeSelf != dev) go.SetActive(dev);
            var showBanner = dev || _root.ActiveProfile == Services.PlayerSettingsService.DebugProfile;   // never hide which save is in use
            if (_banner != null && _banner.activeSelf != showBanner) _banner.SetActive(showBanner);
            UpdateContextButton();
            UpdateHold();
            UpdateTapButton();
            UpdateJoystick();
            _toastGroup.alpha = Mathf.MoveTowards(_toastGroup.alpha, Time.unscaledTime < _toastUntil ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            _carryPulse = Mathf.MoveTowards(_carryPulse, 0f, Time.unscaledDeltaTime * 3f);
            _carryChip.localScale = Vector3.one * (Feel.ReducedMotion ? 1f : 1f + _carryPulse * 0.12f);
            var jobs = _root.Services.Jobs.State;
            _goldText.text = jobs.Gold.ToString();
            _repText.text = jobs.Reputation.ToString();
            var arrived = 0;
            if (jobs.Posting != null) foreach (var a in jobs.Posting.Applicants) if (_root.Services.Jobs.Board.HasArrived(a)) arrived++;
            arrived += _root.Services.Jobs.Board.UnacknowledgedResults;
            _boardBadge.text = arrived > 0 ? arrived.ToString() : "";
            _boardBadge.transform.parent.gameObject.SetActive(arrived > 0);

            // Away summary from the boot catch-up, once the scene is up (GDD 15).
            if (!_awayChecked && SandboxSceneController.Current != null)
            {
                _awayChecked = true;
                var pending = _root.Services.Simulation.PendingReport;
                if (pending != null) ShowAwayReport(pending);
            }
            var trader = _root.Services.Economy.Trader;
            var present = trader != null && trader.IsPresent(_root.Services.Economy.Now);
            if (present && !_traderWasPresent) { ShowToast("The Trader has arrived at the Trader Pad"); FeedbackAudio.Play(FeedbackAudio.Cue.Arrival); }
            _traderWasPresent = present;
            if (_resetArmedUntil > 0 && Time.unscaledTime > _resetArmedUntil)
            {
                _resetArmedUntil = -1f;
                if (_resetButtonText != null) _resetButtonText.text = "Reset sandbox save…";
            }
        }

        private void UpdateJoystick()
        {
            var r = _root.InputRouter;
            var show = r != null && r.JoystickVisible;
            _joyRing.gameObject.SetActive(show);
            _joyKnob.gameObject.SetActive(show);
            if (!show) return;
            var canvasRect = (RectTransform)transform;
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, r.JoystickAnchorPixels, cam, out var anchor);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, r.JoystickKnobPixels, cam, out var knob);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, r.JoystickAnchorPixels + Vector2.right * r.JoystickRadiusPixels, cam, out var edge);
            var diameter = (edge - anchor).magnitude * 2f;
            var offset = canvasRect.rect.size * 0.5f;   // local points are centre-based; anchors are bottom-left
            _joyRing.rectTransform.anchoredPosition = anchor + offset;
            _joyRing.rectTransform.sizeDelta = Vector2.one * (diameter + 60f);
            _joyKnob.rectTransform.anchoredPosition = knob + offset;
            _joyKnob.rectTransform.sizeDelta = Vector2.one * 110f;
        }

        private void UpdateContextButton()
        {
            var target = SandboxSceneController.Current != null ? SandboxSceneController.Current.ContextTarget : null;
            var text = target != null ? $"{target.ContextVerb} {target.Title}" : "Walk closer";   // some targets change verb in place
            if (target == _shownContext && _contextLabel.text == text) return;
            _shownContext = target;
            _contextButton.interactable = target != null;
            _contextGroup.alpha = target != null ? 1f : 0.55f;
            _contextLabel.text = text;
        }

        private HoldSensor _contextHold;

        /// <summary>True while the context button is held down (tests and the hold-to-work loop).</summary>
        public bool ContextHeld => _contextHold != null && _contextHold.Held || ScriptedHold;
        public bool ScriptedHold { get; set; }

        private void OnContextPressed()
        {
            var c = SandboxSceneController.Current;
            if (c == null || c.ContextTarget == null) return;
            if (c.ContextTarget.Holdable) return;   // holdable work happens while held (see UpdateHold)
            c.Inspect(c.ContextTarget);
        }

        /// <summary>Holding the context button on mining or chopping works it every frame, with a swing.</summary>
        private void UpdateHold()
        {
            var c = SandboxSceneController.Current;
            if (c == null || !ContextHeld) return;
            var target = c.ContextTarget;
            if (target == null || !target.Holdable) return;
            target.OnHold(Time.deltaTime);
            c.Player.GetComponent<PlayerMotor>()?.PlaySwing();
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
            _sitePanel.Close();
            _stationPanel.Close();
            _storePanel.Close();
            _traderPanel.Close();
            _awayPanel.Close();
            _settingsPanel.Close();
            _jobBoard.Close();
            _loadout.Close();
            if (_result.IsOpen) _result.Continue();
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


        private void CycleViewWidth()
        {
            var s = _root.Config.SandboxSettings;
            var i = FollowCamera.ViewWidthIndex < 0 ? s.defaultViewWidthIndex : FollowCamera.ViewWidthIndex;
            FollowCamera.ViewWidthIndex = (i + 1) % s.viewWidthOptions.Length;
            Refresh();
        }

        private static readonly float[] Speeds = { 1f, 5f, 20f };

        private void CycleSpeed()
        {
            var sites = _root.Services.Sites;
            var i = System.Array.IndexOf(Speeds, sites.SpeedMultiplier);
            sites.SpeedMultiplier = Speeds[(i + 1) % Speeds.Length];
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
            if (_grantPickText != null && _root.Services.Catalogue.Resources.Count > 0)
                _grantPickText.text = $"Material to grant: {_root.Services.Catalogue.Resources[_grantIndex % _root.Services.Catalogue.Resources.Count].DisplayName} · tap to change";
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
            if (_forceButtonText != null)
            {
                var f = _root.Services.Jobs.State.ForcedNext;
                _forceButtonText.text = f == null ? "Force next outcome: off (debug)" : $"Force next outcome: {f} (debug)";
            }
            if (_speedButtonText != null) _speedButtonText.text = $"Site sim speed: ×{_root.Services.Sites.SpeedMultiplier:0} (debug accelerated time)";
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

            var st = _root.Services.Logistics.System.State;
            sb.Append("<b>Logistics</b>\n");
            sb.Append($"Carry {st.Carry.Count}/{_root.Services.Logistics.System.Capacity}: ");
            foreach (var u in st.Carry) sb.Append(u.Kind == Domain.Logistics.UnitKind.Item ? "◆" : "•");
            sb.Append('\n');
            sb.Append("Shared: ");
            foreach (var kv in st.SharedMaterials) sb.Append($"{UnitVisuals.DisplayName(kv.Key)} {kv.Value}  ");
            sb.Append($"\nStore items: {st.StoreItems.Count}  ·  item instances: {st.ItemDefinitions.Count}\n");
            sb.Append("Recent transactions:\n");
            var shown = 0;
            foreach (var t in _root.Services.Logistics.System.History)
            {
                if (shown++ >= 8) break;
                sb.Append("  ").Append(UnitVisuals.PrettyIds(t.ToString())).Append('\n');
            }
            sb.Append('\n');

            var r = _root.Services.ContentReport;
            sb.Append("<b>Content</b>\n").Append(r.IsValid ? $"OK: {r.DefinitionCount} definitions" : $"<color=#B44034>{r.Errors.Count} errors</color>").Append("\n\n");

            sb.Append("<b>Display</b>\n");
            sb.Append($"Screen {Screen.width}x{Screen.height}  ·  aspect {(float)Screen.height / Mathf.Max(1, Screen.width):0.00}:1\n");
            sb.Append($"Safe area {Screen.safeArea}\n");
            sb.Append($"Build {UnityEngine.Application.version}  ·  Unity {UnityEngine.Application.unityVersion}\n");
            
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
