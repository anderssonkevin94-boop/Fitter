using Ravenholm.Content;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    /// <summary>Editor and build-time content validation. Broken content fails loudly.</summary>
    public sealed class ContentValidationMenu : IPreprocessBuildWithReport
    {
        public const string CataloguePath = "Assets/Ravenholm/Content/Config/ContentCatalogue.asset";

        public int callbackOrder => 0;

        [MenuItem("Ravenholm/Validate Content", priority = 1)]
        public static void ValidateFromMenu()
        {
            var report = ValidateProjectCatalogue();
            if (report.IsValid) Debug.Log("[Ravenholm] " + report);
            else Debug.LogError("[Ravenholm] " + report);
        }

        public static ContentValidationReport ValidateProjectCatalogue()
        {
            var catalogue = AssetDatabase.LoadAssetAtPath<ContentCatalogue>(CataloguePath);
            if (catalogue != null) return catalogue.Validate();
            var r = new ContentValidationReport();
            r.Errors.Add($"Content catalogue not found at {CataloguePath}");
            return r;
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            var result = ValidateProjectCatalogue();
            if (!result.IsValid) throw new BuildFailedException("Content validation failed:\n" + result);
            Debug.Log("[Ravenholm] Build content check: " + result);
        }
    }
}
