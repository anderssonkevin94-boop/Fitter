using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Ravenholm.Presentation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ravenholm.Tests
{
    /// <summary>
    /// The autoplay playtester (Kevin, 2026-10-17: "an agent whose only task is to play the game and tell me where things
    /// are broken or could be improved"). A bot plays a fresh camp the way a player does: only the stick and the action
    /// button, steering by sight, no teleporting. It explores, chops, digs, delivers, visits the Blacksmith and the Store,
    /// and heads for the gate. It records what happened: where it got stuck and what was in the way, what the button
    /// offered, how long each job took, every error logged, and screenshots at each step.
    ///
    /// Not a pass/fail test: it writes Captures/Autoplay/report.md (with timeline.txt and the autoplay_* screenshots) for
    /// the playtester agent (.claude/agents/playtester.md) to read. Run on demand only:
    ///   Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter AutoplayPlaytest
    /// </summary>
    [Explicit("The autoplay playtest runs on demand (the playtester agent), not with the suites")]
    [Category("Autoplay")]
    public sealed class AutoplayPlaytest
    {
        private string _dir;
        private readonly List<string> _timeline = new List<string>();
        private readonly List<string> _problems = new List<string>();
        private readonly List<string> _notes = new List<string>();
        private readonly List<string> _logged = new List<string>();
        private readonly List<string> _shots = new List<string>();
        private readonly List<float> _frameMs = new List<float>();
        private float _t0;
        private int _shot;
        private bool _reached;

        private static PlayerMotor Motor => SandboxSceneController.Current.Player.GetComponent<PlayerMotor>();
        private static Vector2 At => Motor.transform.position;
        private static GameRoot Root => GameRoot.Instance;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "ravenholm-autoplay", Guid.NewGuid().ToString("N"));
            GameRoot.SaveDirectoryOverride = _dir;
            LogAssert.ignoreFailingMessages = true;   // errors are findings to report, not a reason to stop playing
            Application.logMessageReceived += OnLog;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= OnLog;
            if (Root != null) Root.Hud.ScriptedHold = false;
            if (Root != null) Object.Destroy(Root.gameObject);
            yield return null;
            yield return null;
            GameRoot.SaveDirectoryOverride = null;
            LogAssert.ignoreFailingMessages = false;
            try { Directory.Delete(_dir, true); } catch { /* best effort */ }
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                var first = stack != null ? stack.Split('\n').FirstOrDefault(l => l.Contains("Ravenholm")) ?? "" : "";
                _logged.Add($"[{type}] {condition.Trim()} {first.Trim()}");
            }
            else if (type == LogType.Warning && _logged.Count(l => l.StartsWith("[Warning]")) < 20)
                _logged.Add($"[Warning] {condition.Trim()}");
        }

        // ------------------------------------------------------------------ recording

        private string Now => $"{Time.realtimeSinceStartup - _t0,6:F1}s";

        private void Note(string what) => _timeline.Add($"{Now}  {what}");

        private void Problem(string what)
        {
            _problems.Add($"{Now}  {what}");
            Note("PROBLEM: " + what);
        }

        private IEnumerator Shot(string name)
        {
            var file = $"autoplay_{++_shot:00}_{name}";
            yield return Capture.Take(file);
            _shots.Add(file + ".png");
            Note($"screenshot {file}.png (button: \"{Root.Hud.ContextLabel}\", carry {Root.Services.Logistics.System.CarryCount})");
        }

        /// <summary>What blocks the way near a point: blocker colliders and the ground's height step.</summary>
        private static string Blockers(Vector2 at, Vector2 toward)
        {
            var names = Physics2D.OverlapCircleAll(at + toward.normalized * 0.3f, 0.35f, LayerMask.GetMask(PlayerMotor.BlockerLayerName))
                .Select(c => c.transform.parent != null ? $"{c.transform.parent.name}/{c.name}" : c.name).Distinct().ToList();
            var forest = ForestGridView.Current;
            var cell = IsoMap.Cell(at + toward.normalized * 0.5f);
            var step = forest != null ? forest.HeightAt(cell) - forest.PlayerFeet : 0;
            var ground = MineSlabView.Current != null && !MineSlabView.Current.SurfaceWalkable(at + toward.normalized * 0.5f) ? "no walkable ground ahead" : "";
            var parts = new List<string>();
            if (names.Count > 0) parts.Add("colliders: " + string.Join(", ", names));
            if (step >= 2) parts.Add($"a wall {step} blocks high");
            if (ground != "") parts.Add(ground);
            return parts.Count > 0 ? string.Join("; ", parts) : "nothing visible (an invisible snag?)";
        }

        // ------------------------------------------------------------------ playing with the stick and the button

        /// <summary>
        /// A route over walkable cells (A*, the way a player would plan it: open ground, steps of one block, no blockers),
        /// as world waypoints; null when there is none within reach of the search.
        /// </summary>
        private static List<Vector2> Route(Vector2 from, Vector2 to)
        {
            var forest = ForestGridView.Current;
            var slab = MineSlabView.Current;
            var mask = LayerMask.GetMask(PlayerMotor.BlockerLayerName);
            var radius = Root.Config.Balance.playerFootprintRadius;
            var start = IsoMap.Cell(from);
            var goal = IsoMap.Cell(to);
            bool Open(Vector3Int c)
            {
                var w = IsoMap.World(c);
                return (slab == null || slab.SurfaceWalkable(w)) && Physics2D.OverlapCircle(w, radius * 0.8f, mask) == null;
            }
            int H(Vector3Int c) => forest != null ? forest.HeightAt(c) : 0;
            var openSet = new SortedSet<(float f, int id)>();
            var cells = new List<Vector3Int>();
            var index = new Dictionary<Vector3Int, int>();
            var g = new List<float>();
            var came = new List<int>();
            int Id(Vector3Int c)
            {
                if (index.TryGetValue(c, out var i)) return i;
                index[c] = cells.Count;
                cells.Add(c);
                g.Add(float.MaxValue);
                came.Add(-1);
                return cells.Count - 1;
            }
            var s0 = Id(start);
            g[s0] = 0f;
            openSet.Add((Vector3Int.Distance(start, goal), s0));
            Vector3Int[] steps = { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down,
                new Vector3Int(1, 1, 0), new Vector3Int(-1, -1, 0), new Vector3Int(1, -1, 0), new Vector3Int(-1, 1, 0) };
            var found = -1;
            var visited = 0;
            while (openSet.Count > 0 && visited < 12000)
            {
                var cur = openSet.Min;
                openSet.Remove(cur);
                var c = cells[cur.id];
                visited++;
                if (Mathf.Abs(c.x - goal.x) + Mathf.Abs(c.y - goal.y) <= 1) { found = cur.id; break; }
                foreach (var st in steps)
                {
                    var n = c + st;
                    if (Mathf.Abs(n.x - start.x) > 60 || Mathf.Abs(n.y - start.y) > 60) continue;
                    if (!Open(n) || Mathf.Abs(H(n) - H(c)) > 1) continue;
                    if (st.x != 0 && st.y != 0 && (!Open(c + new Vector3Int(st.x, 0, 0)) || !Open(c + new Vector3Int(0, st.y, 0)))) continue;   // no corner cutting
                    var ni = Id(n);
                    var cost = g[cur.id] + (st.x != 0 && st.y != 0 ? 1.414f : 1f);
                    if (cost >= g[ni]) continue;
                    if (g[ni] < float.MaxValue) openSet.Remove((g[ni] + Vector3Int.Distance(n, goal), ni));
                    g[ni] = cost;
                    came[ni] = cur.id;
                    openSet.Add((cost + Vector3Int.Distance(n, goal), ni));
                }
            }
            if (found < 0) return null;
            var path = new List<Vector2>();
            for (var i = found; i >= 0; i = came[i]) path.Add(IsoMap.World(cells[i]));
            path.Reverse();
            path.Add(to);
            return path;
        }

        /// <summary>
        /// Walks a planned route with the stick (or straight at the point when there is none), sidestepping when stuck;
        /// notes every snag. Sets _reached.
        /// </summary>
        private IEnumerator WalkTo(Vector2 target, string goal, float timeout = 20f, float near = 0.35f)
        {
            if (Root.Hud.IsInfoOpen || Root.Hud.StationPanel.IsOpen) { Note("a card was still open: closing it before walking"); yield return CloseAll(); }
            var route = Route(At, target);
            if (route == null) Problem($"no walkable route to {goal} from {At:F2} (bot's map of open ground, steps of one block)");
            var wp = 0;
            _reached = false;
            var router = Root.InputRouter;
            var start = Time.realtimeSinceStartup;
            var checkAt = start + 1f;
            var lastPos = At;
            var detour = 0f;
            var detourDir = Vector2.zero;
            var snags = 0;
            while (Time.realtimeSinceStartup - start < timeout)
            {
                if ((target - At).magnitude < near) { _reached = true; break; }
                while (route != null && wp < route.Count - 1 && Vector2.Distance(At, route[wp]) < 0.3f) wp++;
                var aim = route != null ? route[wp] : target;
                var d = aim - At;
                var stick = new Vector2(d.x, d.y * 2f).normalized;
                if (Time.realtimeSinceStartup < detour) stick = detourDir;
                router.ScriptedMove = stick;
                yield return null;
                if (Time.realtimeSinceStartup >= checkAt)
                {
                    var moved = Vector2.Distance(At, lastPos);
                    if (moved < 0.08f && Time.realtimeSinceStartup >= detour)
                    {
                        snags++;
                        if (snags <= 3) Note($"stuck at {At:F2} heading for {goal}: {Blockers(At, d)}");
                        var side = snags % 2 == 0 ? 1f : -1f;
                        detourDir = new Vector2(-stick.y, stick.x) * side;
                        detour = Time.realtimeSinceStartup + 0.7f;
                    }
                    lastPos = At;
                    checkAt = Time.realtimeSinceStartup + 1f;
                }
            }
            router.ScriptedMove = null;
            yield return new WaitForSeconds(0.2f);
            var took = Time.realtimeSinceStartup - start;
            if (_reached) Note($"reached {goal} in {took:F1}s ({snags} snags)");
            else Problem($"couldn't reach {goal} in {timeout:F0}s: ended at {At:F2}, {Vector2.Distance(At, target):F1} away ({snags} snags; {Blockers(At, target - At)})");
            if (_reached && snags >= 3) Problem($"reaching {goal} took {snags} snags: awkward to get to");
        }

        /// <summary>Faces a point with a nudge of the stick (standing still, the supplier keeps that facing).</summary>
        private IEnumerator Face(Vector2 point)
        {
            var d = point - At;
            Root.InputRouter.ScriptedMove = new Vector2(d.x, d.y * 2f).normalized * 0.3f;
            yield return new WaitForSeconds(0.08f);
            Root.InputRouter.ScriptedMove = null;
            yield return new WaitForSeconds(0.15f);
        }

        /// <summary>Holds the action button until <paramref name="done"/> or a time limit; returns the seconds held via _held.</summary>
        private IEnumerator Hold(Func<bool> done, float timeout, string what)
        {
            var label = Root.Hud.ContextLabel;
            Note($"hold the button: \"{label}\" ({what})");
            if (label == "Walk closer" || string.IsNullOrEmpty(label)) Problem($"{what}: standing at it, the button says \"{label}\"");
            Root.Hud.ScriptedHold = true;
            var start = Time.realtimeSinceStartup;
            while (!done() && Time.realtimeSinceStartup - start < timeout) yield return null;
            Root.Hud.ScriptedHold = false;
            var took = Time.realtimeSinceStartup - start;
            if (!done()) Problem($"{what}: {timeout:F0}s of holding \"{label}\" didn't finish it");
            else Note($"{what} done in {took:F1}s");
        }

        // ------------------------------------------------------------------ the session

        [UnityTest]
        [Timeout(900000)]
        public IEnumerator PlayASession()
        {
            SceneManager.LoadScene(GameRoot.BootstrapSceneName);
            var deadline = Time.realtimeSinceStartup + 15f;
            while ((Root == null || SandboxSceneController.Current == null || SceneManager.GetActiveScene().name != "DebugSandbox")
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.NotNull(SandboxSceneController.Current, "the game started");
            for (var i = 0; i < 5; i++) yield return null;
            _t0 = Time.realtimeSinceStartup;
            var s = Root.Services;
            var log = s.Logistics.System;
            Note($"new game at {s.Camp.Name}; tips: \"{Root.Hud.ToastText}\"");
            yield return Shot("start");
            Root.Hud.Tips.HideAll();
            var home = At;
            var frameWatch = Root.gameObject.AddComponent<FrameWatch>();

            // 1. Look around: a few seconds' walk each way from the camp.
            Note("— exploring from the camp —");
            string[] names = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
            for (var k = 0; k < 8; k++)
            {
                var a = k * Mathf.PI / 4f;
                var from = At;
                Root.InputRouter.ScriptedMove = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                yield return new WaitForSeconds(2f);
                Root.InputRouter.ScriptedMove = null;
                var went = Vector2.Distance(At, from);
                Note($"walked {names[k]} 2s: {went:F2} units (button now \"{Root.Hud.ContextLabel}\")");
                if (went < 0.6f) Note($"  barely moved going {names[k]}: {Blockers(At, new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.5f))}");
                if (k % 2 == 1) yield return Shot($"explore_{names[k]}");
                yield return WalkTo(home, "back to the camp", 12f, 0.6f);
            }

            // 2. Chop the nearest trees.
            Note("— chopping —");
            var forest = s.Sites.Get("site.forest");
            var view = ForestGridView.Current;
            for (var t = 0; t < 3 && view != null; t++)
            {
                var c0 = IsoMap.Cell(At);
                var trees = new List<(int x, int y, float d)>();
                for (var x = c0.x - 16; x <= c0.x + 16; x++)
                for (var y = c0.y - 16; y <= c0.y + 16; y++)
                    if (forest.CanChop(x, y) && !s.Sites.ToolTooWeak(x, y) && view.CellOf(x, y, out _))
                        trees.Add((x, y, Vector2.Distance(At, IsoMap.World(new Vector3Int(x, y, 0)))));
                if (trees.Count == 0) { Problem("no tree the starting axe can cut within 16 cells of the camp"); break; }
                var tree = trees.OrderBy(q => q.d).First();
                view.CellOf(tree.x, tree.y, out var cell);
                yield return WalkTo(view.WorkSpotOf(cell), $"tree ({tree.x}, {tree.y})", 15f);
                yield return Face(IsoMap.World(cell));
                var carry = log.CarryCount;
                var tx = tree.x;
                var ty = tree.y;
                if (log.IsFull) { Note("carry full: off to deliver first"); break; }
                yield return Hold(() => forest.IsFelled(tx, ty), 25f, $"chop tree ({tx}, {ty})");
                Note($"  carry {carry} → {log.CarryCount}");
                if (t == 0) yield return Shot("chopped");
            }

            // 3. Deliver the logs to the Lumbermill.
            yield return Deliver("receiver.lumbermill", "the Lumbermill");

            // 4. Dig a hill block.
            Note("— digging —");
            if (view != null)
            {
                var c0 = IsoMap.Cell(At);
                var best = (cell: default(Vector3Int), stand: default(Vector3Int), d: float.MaxValue);
                for (var x = c0.x - 30; x <= c0.x + 30; x++)
                for (var y = c0.y - 30; y <= c0.y + 30; y++)
                {
                    var c = new Vector3Int(x, y, 0);
                    if (view.HeightAt(c) < 1 || s.Sites.ToolTooWeak(x, y)) continue;
                    foreach (var n in new[] { Vector3Int.right, Vector3Int.left, Vector3Int.up, Vector3Int.down })
                    {
                        var stand = c + n;
                        if (view.HeightAt(stand) != 0 || !MineSlabView.Current.SurfaceWalkable(IsoMap.World(stand))) continue;
                        var d = Vector2.Distance(At, IsoMap.World(stand));
                        if (d < best.d) best = (c, stand, d);
                    }
                }
                if (best.d == float.MaxValue) Problem("no hill rock with flat ground beside it within 30 cells");
                else
                {
                    yield return WalkTo(IsoMap.World(best.stand), $"hill rock at {best.cell}", 15f);
                    yield return Face(IsoMap.World(best.cell));
                    var carry = log.CarryCount;
                    if (!(SandboxSceneController.Current.ContextTarget is ForestRockSpot))
                        Problem($"facing a hill block from the flat ground beside it, the button says \"{Root.Hud.ContextLabel}\" (expected Dig)");
                    yield return Hold(() => log.CarryCount > carry || log.IsFull, 20f, "dig a hill block");
                    yield return Shot("dug");
                }
            }
            yield return Deliver("receiver.smelter", "the Blacksmith's delivery mat");

            // 5. Visit the Blacksmith: its forge's panel.
            Note("— the Blacksmith —");
            var forge = GameObject.Find("Forge");
            if (forge == null) Problem("no Forge in the scene");
            else
            {
                yield return WalkTo((Vector2)forge.transform.position + new Vector2(0f, -1.1f), "the Forge", 15f, 0.5f);
                yield return Face(forge.transform.position);
                Note($"at the Forge the button says \"{Root.Hud.ContextLabel}\"");
                Root.Hud.ScriptedPress();
                yield return new WaitForSeconds(0.4f);
                if (!Root.Hud.StationPanel.IsOpen)
                    Problem($"standing by the Forge, the button offered \"{Root.Hud.ContextLabel}\": tapping it " +
                            (Root.Hud.IsInfoOpen ? "opened an info card instead of the Forge's crafting panel" : "opened nothing"));
                yield return Shot("forge_panel");
                yield return CloseAll();
            }

            // 6. The Store's counter.
            Note("— the Store —");
            var store = log.State.Receivers.TryGetValue("receiver.store", out var sr) ? new Vector2(sr.X, sr.Y) : home;
            yield return WalkTo(store, "the Store's counter", 20f, 0.5f);
            yield return Shot("store");

            // 7. The way on: toward the gate.
            Note("— the gate —");
            yield return WalkTo(s.Camp.ExitWorld, "the gate on the north rim", 30f, 1.5f);
            yield return Shot("gate");
            Note($"at the gate the button says \"{Root.Hud.ContextLabel}\"; move-on: {s.Camp.MoveOnBlockedReason(At) ?? "allowed"}");

            // The report.
            _frameMs.AddRange(frameWatch.Samples);
            WriteReport();
        }

        /// <summary>Closes whatever card or panel is open (as a player would), noting it.</summary>
        private IEnumerator CloseAll()
        {
            if (Root.Hud.StationPanel.IsOpen) Root.Hud.StationPanel.Close();
            if (Root.Hud.IsInfoOpen) Root.Hud.HideInfo();
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator Deliver(string receiverId, string name)
        {
            var log = Root.Services.Logistics.System;
            if (log.CarryCount == 0) { Note($"nothing to deliver to {name}"); yield break; }
            if (!log.State.Receivers.TryGetValue(receiverId, out var r)) { Problem($"no {receiverId}"); yield break; }
            var carry = log.CarryCount;
            yield return WalkTo(new Vector2(r.X, r.Y), name, 25f, Mathf.Max(0.3f, r.Radius * 0.8f));
            var until = Time.realtimeSinceStartup + 4f;
            while (log.CarryCount == carry && Time.realtimeSinceStartup < until) yield return null;
            yield return new WaitForSeconds(1f);
            if (log.CarryCount == carry) Problem($"standing at {name}, nothing was delivered (carry {carry}: {string.Join(", ", log.State.Carry.Select(u => u.Id).Distinct())})");
            else Note($"delivered {carry - log.CarryCount} to {name}");
        }

        private void WriteReport()
        {
            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Captures", "Autoplay"));
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("# Autoplay playtest report");
            sb.AppendLine($"Run {DateTime.Now:yyyy-MM-dd HH:mm}, {Time.realtimeSinceStartup - _t0:F0}s of play, camp {Root.Services.Camp.Name}.").AppendLine();
            sb.AppendLine($"## Problems the bot hit ({_problems.Count})");
            foreach (var p in _problems) sb.AppendLine("- " + p);
            sb.AppendLine().AppendLine($"## Errors and warnings logged ({_logged.Count})");
            foreach (var l in _logged.Distinct().Take(60)) sb.AppendLine("- " + l);
            if (_frameMs.Count > 0)
            {
                var sorted = _frameMs.OrderBy(x => x).ToList();
                sb.AppendLine().AppendLine("## Frame times (editor, batch mode: relative only)");
                sb.AppendLine($"- median {sorted[sorted.Count / 2]:F1} ms, 95th {sorted[(int)(sorted.Count * 0.95f)]:F1} ms, worst {sorted[sorted.Count - 1]:F1} ms over {sorted.Count} frames");
            }
            sb.AppendLine().AppendLine("## Screenshots (Captures/)");
            foreach (var s in _shots) sb.AppendLine("- " + s);
            File.WriteAllText(Path.Combine(dir, "report.md"), sb.ToString());
            File.WriteAllText(Path.Combine(dir, "timeline.txt"), string.Join("\n", _timeline));
            Debug.Log($"[Autoplay] report: {Path.Combine(dir, "report.md")} ({_problems.Count} problems, {_logged.Count} logged)");
        }

        /// <summary>Frame times while the bot plays.</summary>
        private sealed class FrameWatch : MonoBehaviour
        {
            public readonly List<float> Samples = new List<float>();
            private void Update() { if (Samples.Count < 20000) Samples.Add(Time.unscaledDeltaTime * 1000f); }
        }
    }
}
