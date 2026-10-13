using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ravenholm.Presentation
{
    /// <summary>PROTOTYPE entry: a signpost in town that opens a prototype scene (no save changes).</summary>
    public sealed class PrototypeSign : Inspectable
    {
        [SerializeField] private string sceneName = "";

        public void EditorSetupScene(string scene) => sceneName = scene;

        public override string ContextVerb => "Try";
        public override bool OpensOwnPanel => true;

        public override void OnInspected()
        {
            if (!string.IsNullOrEmpty(sceneName)) SceneManager.LoadScene(sceneName);
        }
    }
}
