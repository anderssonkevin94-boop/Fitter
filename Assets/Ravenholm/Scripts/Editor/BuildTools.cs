using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ravenholm.EditorTools
{
    /// <summary>
    /// iOS Xcode-project export. Batch mode:
    ///   -executeMethod Ravenholm.EditorTools.BuildTools.BuildIosFromCommandLine -rsc-sdk=simulator|device [-rsc-dev]
    /// Output: Builds/iOS-Simulator or Builds/iOS-Device (git-ignored). Signing is chosen in Xcode;
    /// no team ID or provisioning data is stored in the project.
    /// </summary>
    public static class BuildTools
    {
        [MenuItem("Ravenholm/Build/iOS Xcode Project (Device)", priority = 40)]
        public static void BuildDeviceMenu() => BuildIos(simulator: false, development: true);

        [MenuItem("Ravenholm/Build/iOS Xcode Project (Simulator)", priority = 41)]
        public static void BuildSimulatorMenu() => BuildIos(simulator: true, development: true);

        public static void BuildIosFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var simulator = args.Any(a => a == "-rsc-sdk=simulator");
            var dev = args.Contains("-rsc-dev");
            var ok = BuildIos(simulator, dev);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool BuildIos(bool simulator, bool development)
        {
            var previousSdk = PlayerSettings.iOS.sdkVersion;
            PlayerSettings.iOS.sdkVersion = simulator ? iOSSdkVersion.SimulatorSDK : iOSSdkVersion.DeviceSDK;
            try
            {
                var output = Path.GetFullPath(simulator ? "Builds/iOS-Simulator" : "Builds/iOS-Device");
                var options = new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                    locationPathName = output,
                    target = BuildTarget.iOS,
                    targetGroup = BuildTargetGroup.iOS,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };
                var report = BuildPipeline.BuildPlayer(options);
                var s = report.summary;
                Debug.Log($"[Build] iOS {(simulator ? "simulator" : "device")} -> {output}: {s.result}, {s.totalErrors} errors, {s.totalWarnings} warnings, {s.totalTime.TotalSeconds:0}s");
                return s.result == BuildResult.Succeeded;
            }
            finally
            {
                PlayerSettings.iOS.sdkVersion = previousSdk;
            }
        }
    }
}
