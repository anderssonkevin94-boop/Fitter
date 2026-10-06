using Ravenholm.Content;
using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>Everything the composition root needs, assigned in the Bootstrap scene.</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Boot Config", fileName = "BootConfig")]
    public sealed class BootConfig : ScriptableObject
    {
        [SerializeField] private ContentCatalogue catalogue;
        [SerializeField] private SandboxSettings sandboxSettings;
        [SerializeField] private UiTheme uiTheme;

        [Tooltip("Save profile folder name. Debug sandboxes must never share the production profile.")]
        [SerializeField] private string saveProfile = "sandbox";

        [Tooltip("Scene loaded after services are composed.")]
        [SerializeField] private string firstScene = "DebugSandbox";

        public ContentCatalogue Catalogue => catalogue;
        public SandboxSettings SandboxSettings => sandboxSettings;
        public UiTheme UiTheme => uiTheme;
        public string SaveProfile => saveProfile;
        public string FirstScene => firstScene;

#if UNITY_EDITOR
        public void EditorSetup(ContentCatalogue c, SandboxSettings s, UiTheme t)
        {
            catalogue = c;
            sandboxSettings = s;
            uiTheme = t;
        }
#endif
    }
}
