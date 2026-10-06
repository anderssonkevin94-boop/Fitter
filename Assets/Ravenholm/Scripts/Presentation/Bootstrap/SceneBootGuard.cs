using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Put in every playable scene. Pressing Play in that scene directly routes through
    /// Bootstrap so the composition root is never skipped or duplicated.
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public sealed class SceneBootGuard : MonoBehaviour
    {
        private void Awake()
        {
            if (GameRoot.Instance != null) return;
            Debug.Log("[SceneBootGuard] No composition root yet; loading Bootstrap first.");
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
        }
    }
}
