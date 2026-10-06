using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>Permanent unlock of one or more recipes (GDD 9: never consumed, never sold).</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Content/Blueprint Definition", fileName = "blueprint_")]
    public sealed class BlueprintDefinition : ContentDefinition
    {
        [Tooltip("Owned from the start of a new company (GDD 15 starting state).")]
        [SerializeField] private bool ownedAtStart;
        [TextArea(1, 3)] [SerializeField] private string revealText = "";

        public override string IdKind => "blueprint";
        public bool OwnedAtStart => ownedAtStart;
        public string RevealText => revealText;

#if UNITY_EDITOR
        public void EditorSetupBlueprint(bool owned, string reveal)
        {
            ownedAtStart = owned;
            revealText = reveal;
        }
#endif
    }
}
