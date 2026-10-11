using UnityEngine.SceneManagement;

namespace Ravenholm.Presentation
{
    /// <summary>PROTOTYPE entry: a signpost by the Mine that opens the option-A digging prototype (no save changes).</summary>
    public sealed class PrototypeSign : Inspectable
    {
        public override string ContextVerb => "Try";
        public override bool OpensOwnPanel => true;

        public override void OnInspected() => SceneManager.LoadScene(MiningPrototype.SceneName);
    }
}
