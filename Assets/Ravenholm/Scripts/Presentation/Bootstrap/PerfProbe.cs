using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Ravenholm.Domain.Time;
using Ravenholm.Services;
using UnityEngine;
using UnityEngine.Profiling;

namespace Ravenholm.Presentation
{
    /// <summary>
    /// Developer performance capture (GDD 24): boot time, frame times, managed allocations, memory and an
    /// 8-hour offline catch-up timed on a throwaway copy of the game (never the player's save). Writes JSON
    /// to persistentDataPath/Perf. Starts from the developer tools, or automatically when a file named
    /// "perf_autorun" is present in persistentDataPath (so a device run can be captured without touching it).
    /// </summary>
    public sealed class PerfProbe : MonoBehaviour
    {
        public const string AutorunFile = "perf_autorun";
        /// <summary>Seconds from launch until the services were ready, and until the town scene started.</summary>
        public static float BootSeconds = -1f;
        public static float SceneReadySeconds = -1f;
        public static bool Running { get; private set; }
        public static string LastReportPath { get; private set; }

        public static void Begin(float seconds)
        {
            if (Running || GameRoot.Instance == null) return;
            var probe = GameRoot.Instance.gameObject.AddComponent<PerfProbe>();
            probe.StartCoroutine(probe.Run(seconds));
        }

        public static void CheckAutorun()
        {
            var flag = Path.Combine(UnityEngine.Application.persistentDataPath, AutorunFile);
            if (!File.Exists(flag)) return;
            var seconds = 60f;
            try { float.TryParse(File.ReadAllText(flag).Trim(), out seconds); } catch { /* default */ }
            try { File.Delete(flag); } catch { /* best effort */ }
            Begin(seconds > 5f ? seconds : 60f);
        }

        private IEnumerator Run(float seconds)
        {
            Running = true;
            yield return new WaitForSeconds(2f);   // let the scene settle

            var frames = new List<float>(4096);
            long gcStart = GC.CollectionCount(0);
            long monoStart = Profiler.GetMonoUsedSizeLong();
            long peakTotal = 0;
            var end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000f);
                peakTotal = Math.Max(peakTotal, Profiler.GetTotalAllocatedMemoryLong());
            }
            var gcCount = GC.CollectionCount(0) - gcStart;
            var monoGrowth = Profiler.GetMonoUsedSizeLong() - monoStart;

            var catchUpMs = TimeCatchUp();
            frames.Sort();
            float Pct(float p) => frames.Count == 0 ? 0 : frames[Mathf.Clamp((int)(frames.Count * p), 0, frames.Count - 1)];
            float sum = 0;
            var over33 = 0;
            foreach (var f in frames) { sum += f; if (f > 33.4f) over33++; }

            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append($"  \"device\": \"{SystemInfo.deviceModel}\",\n  \"os\": \"{SystemInfo.operatingSystem}\",\n  \"build\": \"{UnityEngine.Application.version}\",\n");
            sb.Append($"  \"utc\": \"{DateTime.UtcNow:O}\",\n  \"bootSeconds\": {BootSeconds:0.00},\n  \"sceneReadySeconds\": {SceneReadySeconds:0.00},\n  \"sampleSeconds\": {seconds:0},\n  \"frames\": {frames.Count},\n");
            sb.Append($"  \"frameMsAvg\": {(frames.Count > 0 ? sum / frames.Count : 0):0.00},\n  \"frameMsP50\": {Pct(0.5f):0.00},\n  \"frameMsP95\": {Pct(0.95f):0.00},\n");
            sb.Append($"  \"frameMsP99\": {Pct(0.99f):0.00},\n  \"frameMsMax\": {(frames.Count > 0 ? frames[frames.Count - 1] : 0):0.00},\n");
            sb.Append($"  \"framesOver33ms\": {over33},\n  \"gcCollections\": {gcCount},\n  \"monoGrowthKB\": {monoGrowth / 1024},\n");
            sb.Append($"  \"peakAllocatedMB\": {peakTotal / (1024 * 1024)},\n  \"reservedMB\": {Profiler.GetTotalReservedMemoryLong() / (1024 * 1024)},\n");
            sb.Append($"  \"catchUp8hMs\": {catchUpMs},\n  \"targetFrameRate\": {UnityEngine.Application.targetFrameRate}\n}}\n");

            var dir = Path.Combine(UnityEngine.Application.persistentDataPath, "Perf");
            Directory.CreateDirectory(dir);
            LastReportPath = Path.Combine(dir, $"perf_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            File.WriteAllText(LastReportPath, sb.ToString());
            UnityEngine.Debug.Log("[PerfProbe] " + LastReportPath + "\n" + sb);
            Running = false;
            Destroy(this);
        }

        /// <summary>Times an 8-hour catch-up on a fresh throwaway company in a temporary folder.</summary>
        private static long TimeCatchUp()
        {
            var root = GameRoot.Instance;
            var dir = Path.Combine(UnityEngine.Application.temporaryCachePath, "perf-catchup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                var g = new GameServices(new ManualClock(t0), root.Config.Catalogue, root.Config.Balance, dir, "perf", "perf", 1.5f);
                g.Start();
                SimulationService.MakeBusyForProfiling(g);
                var sw = Stopwatch.StartNew();
                g.Simulation.Simulate(SimulationService.OfflineResourceCapMs, "perf");
                sw.Stop();
                return sw.ElapsedMilliseconds;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[PerfProbe] catch-up timing failed: " + e.Message);
                return -1;
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* best effort */ }
            }
        }
    }
}
