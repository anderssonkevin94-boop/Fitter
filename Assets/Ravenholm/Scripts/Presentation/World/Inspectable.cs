using UnityEngine;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Tappable world object for the sandbox. Shows what a placeholder represents and which
    /// phase brings its behaviour, so the user can judge readability without final art.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class Inspectable : MonoBehaviour
    {
        [SerializeField] private string title = "";
        [TextArea(2, 5)] [SerializeField] private string description = "";
        [Tooltip("e.g. 'Phase 3: processing'. Empty for scenery.")]
        [SerializeField] private string plannedPhase = "";

        public string Title => title;

        /// <summary>Verb shown on the context button (GDD 4 microcopy: short action names).</summary>
        public virtual string ContextVerb => "Inspect";

        /// <summary>World units a target counts as nearer when picking the context button: work beats looking around.</summary>
        public virtual float ContextPriority => 0f;

        /// <summary>True for work the supplier does by holding the button (mining, chopping).</summary>
        public virtual bool Holdable => false;

        /// <summary>Called every frame while the context button is held on a holdable target.</summary>
        public virtual void OnHold(float deltaSeconds) { }

        /// <summary>False while this target has nothing to offer here (it is then never the context-button target).</summary>
        public virtual bool IsContextAvailable => true;

        /// <summary>True when <see cref="OnInspected"/> opens its own panel instead of the generic info card.</summary>
        public virtual bool OpensOwnPanel => false;

        public virtual string BuildDetail()
        {
            return string.IsNullOrEmpty(plannedPhase) ? description : $"{description}\n\n<b>Planned:</b> {plannedPhase}";
        }

        /// <summary>Called when the player taps this or uses the context button on it.</summary>
        public virtual void OnInspected() { }

        public void EditorSetup(string newTitle, string newDescription, string phase)
        {
            title = newTitle;
            description = newDescription;
            plannedPhase = phase;
        }
    }
}
