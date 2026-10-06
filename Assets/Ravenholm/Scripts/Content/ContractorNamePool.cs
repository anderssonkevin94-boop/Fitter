using System.Collections.Generic;
using UnityEngine;

namespace Ravenholm.Content
{
    /// <summary>Names for generated contractors (original, invented). Edit freely; saves keep generated names.</summary>
    [CreateAssetMenu(menuName = "Ravenholm/Config/Contractor Name Pool", fileName = "ContractorNamePool")]
    public sealed class ContractorNamePool : ScriptableObject
    {
        [SerializeField] private List<string> givenNames = new List<string>();
        [SerializeField] private List<string> familyNames = new List<string>();

        public IReadOnlyList<string> GivenNames => givenNames;
        public IReadOnlyList<string> FamilyNames => familyNames;

#if UNITY_EDITOR
        public void EditorSetup(List<string> given, List<string> family)
        {
            givenNames = given;
            familyNames = family;
        }
#endif
    }
}
