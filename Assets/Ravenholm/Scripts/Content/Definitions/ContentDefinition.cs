using System.Collections.Generic;
using Ravenholm.Domain.Ids;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>
    /// Base for authored, immutable content. Runtime code must never write to these assets;
    /// saves refer to them only by <see cref="Id"/>.
    /// </summary>
    public abstract class ContentDefinition : ScriptableObject
    {
        [Tooltip("Stable save ID, '<kind>.<name>' in lowercase (e.g. resource.iron_ore). Never change after shipping; edit Display Name instead.")]
        [SerializeField] private string id = "";

        [Tooltip("Player-facing name. Safe to change at any time.")]
        [SerializeField] private string displayName = "";

        [Tooltip("Bump when this definition's meaning changes in a way saves or balance notes should track.")]
        [Min(1)] [SerializeField] private int contentVersion = 1;

        public string Id => id;
        public string DisplayName => displayName;
        public int ContentVersion => contentVersion;

        /// <summary>Required ID kind prefix, e.g. "resource".</summary>
        public abstract string IdKind { get; }

        /// <summary>Appends human-readable problems. Base checks cover ID and naming rules.</summary>
        public virtual void Validate(List<string> errors)
        {
            if (!ContentIds.IsValid(id, out var reason)) errors.Add($"{Describe()}: {reason}");
            else if (!ContentIds.HasKind(id, IdKind)) errors.Add($"{Describe()}: ID must start with '{IdKind}.'");
            if (string.IsNullOrWhiteSpace(displayName)) errors.Add($"{Describe()}: display name is empty");
        }

        public string Describe() => $"{GetType().Name} '{(string.IsNullOrEmpty(id) ? name : id)}'";

#if UNITY_EDITOR
        /// <summary>Editor-only authoring hook used by setup tools and tests.</summary>
        /// <summary>Marks an authored balance or content change (recorded in saves for diagnosis).</summary>
        public void EditorBumpVersion() => contentVersion++;

        public void EditorInitialize(string newId, string newDisplayName)
        {
            id = newId;
            displayName = newDisplayName;
        }
#endif
    }
}
