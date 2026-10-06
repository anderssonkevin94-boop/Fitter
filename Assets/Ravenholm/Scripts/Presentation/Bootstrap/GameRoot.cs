using System.IO;
using Ravenholm.Domain.Time;
using Ravenholm.Services;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Composition root and lifecycle bridge. Lives in the Bootstrap scene, survives scene
    /// loads, builds the plain C# service graph once, and owns the persistent UI root.
    /// There must be exactly one; additive or later scenes never create managers.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameRoot : MonoBehaviour
    {
        public const string BootstrapSceneName = "Bootstrap";

        [SerializeField] private BootConfig config;

        /// <summary>Tests point saves at a temporary folder and inject a clock.</summary>
        public static string SaveDirectoryOverride;
        public static IClock ClockOverride;
        /// <summary>Tests can suppress the automatic first-scene load.</summary>
        public static bool SkipFirstSceneLoad;

        public static GameRoot Instance { get; private set; }

        public BootConfig Config => config;
        public GameServices Services { get; private set; }
        public TouchInputRouter InputRouter { get; private set; }
        public SandboxHud Hud { get; private set; }

        public static string SaveRootDirectory => Path.Combine(UnityEngine.Application.persistentDataPath, "Saves");

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            UnityEngine.Application.targetFrameRate = 60;

            if (config == null || config.SandboxSettings == null || config.UiTheme == null)
            {
                Debug.LogError("[GameRoot] BootConfig is missing or incomplete. Run Ravenholm > Setup > Run Phase 0 Setup.");
                enabled = false;
                return;
            }

            var settings = config.SandboxSettings;
            var saveDir = SaveDirectoryOverride ?? Path.Combine(SaveRootDirectory, config.SaveProfile);
            Services = new GameServices(ClockOverride ?? new SystemClock(), config.Catalogue, saveDir, config.SaveProfile,
                UnityEngine.Application.version, settings.autosaveDebounceSeconds);

            if (Services.ContentReport.IsValid) Debug.Log("[GameRoot] " + Services.ContentReport);
            else Debug.LogError("[GameRoot] " + Services.ContentReport);

            Services.Sandbox.Start();
            var s = Services.Saves.Status;
            if (s.LoadStatus == Infrastructure.Save.SaveLoadStatus.RecoveredFromBackup || s.LoadStatus == Infrastructure.Save.SaveLoadStatus.Unrecoverable)
                Debug.LogWarning($"[GameRoot] Save load: {s.LoadStatus}. {s.LoadDetail}");

            EnsureEventSystem();
            InputRouter = gameObject.AddComponent<TouchInputRouter>();
            InputRouter.Configure(settings);
            Hud = SandboxHud.Create(transform, this);

            if (!SkipFirstSceneLoad && SceneManager.GetActiveScene().name != config.FirstScene)
                SceneManager.LoadScene(config.FirstScene);
        }

        private void Update()
        {
            Services?.Sandbox.Tick(Time.unscaledDeltaTime);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) return;
            InputRouter?.ResetInput();
            Services?.Sandbox.Flush("app paused");
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            InputRouter?.ResetInput();
            Services?.Sandbox.Flush("focus lost");
        }

        private void OnApplicationQuit()
        {
            Services?.Sandbox.Flush("quit");
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            go.transform.SetParent(transform, false);
        }
    }
}
