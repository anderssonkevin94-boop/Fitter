using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Ravenholm.Domain.Time;
using Ravenholm.Infrastructure.Save;
using Ravenholm.Services;

namespace Ravenholm.Tests
{
    /// <summary>Every test uses its own temporary folder; real player saves are never touched.</summary>
    public abstract class TempDirFixture
    {
        protected string Dir;

        [SetUp]
        public void MakeDir()
        {
            Dir = Path.Combine(Path.GetTempPath(), "ravenholm-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Dir);
        }

        [TearDown]
        public void RemoveDir()
        {
            try { Directory.Delete(Dir, true); } catch { /* best effort */ }
        }

        protected static string Validator(string json) => SaveSerializer.TryFromJson(json, out _, out _, out var e) ? null : e;

        protected static string EnvelopeJson(long generation, int bells)
        {
            var env = new SaveEnvelope
            {
                Profile = "sandbox",
                SaveGeneration = generation,
                LastAcceptedUtc = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            };
            env.SetWorldSeed(99);
            env.Sandbox.BellRingCount = bells;
            return SaveSerializer.ToJson(env);
        }

        protected static int BellsIn(string json)
        {
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out _, out var e), e);
            return env.Sandbox.BellRingCount;
        }
    }

    public sealed class SaveCodecTests
    {
        [Test]
        public void RoundTrip()
        {
            const string json = "{\"a\":\"åäö ✓\"}";
            Assert.IsTrue(SaveFileCodec.TryDecode(SaveFileCodec.Encode(json), out var back, out var err), err);
            Assert.AreEqual(json, back);
        }

        [Test]
        public void DetectsTruncation()
        {
            var text = SaveFileCodec.Encode(EnvelopeJsonForCodec());
            Assert.IsFalse(SaveFileCodec.TryDecode(text.Substring(0, text.Length - 10), out _, out var err));
            StringAssert.Contains("length", err);
        }

        [Test]
        public void DetectsTampering()
        {
            var text = SaveFileCodec.Encode("{\"bells\":1}").Replace("\"bells\":1", "\"bells\":9");
            Assert.IsFalse(SaveFileCodec.TryDecode(text, out _, out var err));
            StringAssert.Contains("checksum", err);
        }

        [Test]
        public void RejectsForeignAndEmptyFiles()
        {
            Assert.IsFalse(SaveFileCodec.TryDecode("", out _, out _));
            Assert.IsFalse(SaveFileCodec.TryDecode("{\"plain\":\"json\"}", out _, out _));
            Assert.IsFalse(SaveFileCodec.TryDecode("OTHER 1 bytes=2 sha256=00\n{}", out _, out _));
        }

        private static string EnvelopeJsonForCodec() => "{\"payload\":\"" + new string('x', 200) + "\"}";
    }

    public sealed class SaveStoreTests : TempDirFixture
    {
        [Test]
        public void EmptyFolderMeansNoSave()
        {
            var r = new SaveStore(Dir).Load(Validator);
            Assert.AreEqual(SaveLoadStatus.NoSave, r.Status);
        }

        [Test]
        public void WriteThenLoad()
        {
            var store = new SaveStore(Dir);
            Assert.IsTrue(store.Write(EnvelopeJson(1, 5), 1, out var e), e);
            var r = new SaveStore(Dir).Load(Validator);
            Assert.AreEqual(SaveLoadStatus.LoadedPrimary, r.Status);
            Assert.AreEqual(5, BellsIn(r.Json));
            Assert.IsFalse(File.Exists(store.TempPath));
        }

        [Test]
        public void SecondWriteKeepsPreviousAsBackup()
        {
            var store = new SaveStore(Dir);
            store.Write(EnvelopeJson(1, 1), 1, out _);
            store.Write(EnvelopeJson(2, 2), 2, out _);
            Assert.IsTrue(File.Exists(store.BackupPath));
            Assert.IsTrue(SaveFileCodec.TryDecode(File.ReadAllText(store.BackupPath), out var backup, out _));
            Assert.AreEqual(1, BellsIn(backup));
        }

        [Test]
        public void CorruptPrimaryRecoversFromBackupAndKeepsTheBadFile()
        {
            var store = new SaveStore(Dir);
            store.Write(EnvelopeJson(1, 1), 1, out _);
            store.Write(EnvelopeJson(2, 2), 2, out _);
            store.DebugCorruptPrimary();

            var r = new SaveStore(Dir).Load(Validator);
            Assert.AreEqual(SaveLoadStatus.RecoveredFromBackup, r.Status);
            Assert.AreEqual(1, BellsIn(r.Json));
            Assert.AreEqual(1, r.QuarantinedFiles.Count);
            Assert.IsTrue(File.Exists(r.QuarantinedFiles[0]), "corrupt file must be kept aside, not deleted");
            // Primary was restored from the backup, so the next normal load is clean.
            Assert.AreEqual(SaveLoadStatus.LoadedPrimary, new SaveStore(Dir).Load(Validator).Status);
        }

        [Test]
        public void BothCorruptIsUnrecoverableButNothingIsDeleted()
        {
            var store = new SaveStore(Dir);
            store.Write(EnvelopeJson(1, 1), 1, out _);
            store.Write(EnvelopeJson(2, 2), 2, out _);
            File.WriteAllText(store.PrimaryPath, "garbage");
            File.WriteAllText(store.BackupPath, "garbage too");

            var r = new SaveStore(Dir).Load(Validator);
            Assert.AreEqual(SaveLoadStatus.Unrecoverable, r.Status);
            Assert.AreEqual(2, r.QuarantinedFiles.Count);
            Assert.IsTrue(r.QuarantinedFiles.All(File.Exists));
        }

        [Test]
        public void ValidButUnreadableJsonFallsBack()
        {
            var store = new SaveStore(Dir);
            store.Write(EnvelopeJson(1, 3), 1, out _);
            store.Write("{\"schemaVersion\": 999, \"profile\":\"sandbox\"}", 2, out _); // well-formed file, future schema
            var r = new SaveStore(Dir).Load(Validator);
            Assert.AreEqual(SaveLoadStatus.RecoveredFromBackup, r.Status);
            StringAssert.Contains("newer", r.Problems[0]);
        }

        [Test]
        public void StaleGenerationIsRejected()
        {
            var store = new SaveStore(Dir);
            Assert.IsTrue(store.Write(EnvelopeJson(5, 5), 5, out _));
            Assert.IsFalse(store.Write(EnvelopeJson(4, 4), 4, out var e));
            StringAssert.Contains("stale", e);
            Assert.AreEqual(5, BellsIn(new SaveStore(Dir).Load(Validator).Json));
        }

        [Test]
        public void LoadedGenerationGuardsAgainstOlderWrites()
        {
            new SaveStore(Dir).Write(EnvelopeJson(9, 9), 9, out _);
            var fresh = new SaveStore(Dir);
            fresh.NoteLoadedGeneration(9);
            Assert.IsFalse(fresh.Write(EnvelopeJson(3, 3), 3, out _));
        }

        [Test]
        public void LeftoverTempFileFromCrashIsIgnored()
        {
            var store = new SaveStore(Dir);
            store.Write(EnvelopeJson(1, 1), 1, out _);
            File.WriteAllText(store.TempPath, "half-written");
            var r = new SaveStore(Dir).Load(Validator);
            Assert.AreEqual(SaveLoadStatus.LoadedPrimary, r.Status);
            Assert.IsFalse(File.Exists(store.TempPath));
        }

        [Test]
        public void CorruptPrimaryIsNeverRotatedIntoBackupSlot()
        {
            var store = new SaveStore(Dir);
            store.Write(EnvelopeJson(1, 1), 1, out _);
            store.Write(EnvelopeJson(2, 2), 2, out _);   // backup = gen 1
            store.DebugCorruptPrimary();
            Assert.IsTrue(store.Write(EnvelopeJson(3, 3), 3, out var e), e);
            Assert.IsTrue(SaveFileCodec.TryDecode(File.ReadAllText(store.BackupPath), out var backup, out _), "backup must still be valid");
            Assert.AreEqual(1, BellsIn(backup));
        }
    }

    public sealed class MigrationTests
    {
        private const string FixturePath = "Assets/Ravenholm/Tests/Fixtures/save_v1_fixture.json";

        [Test]
        public void V1FixtureMigratesToCurrent()
        {
            var json = File.ReadAllText(FixturePath);
            Assert.IsTrue(SaveSerializer.TryFromJson(json, out var env, out var from, out var err), err);
            Assert.AreEqual(1, from);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, env.SchemaVersion);
            Assert.AreEqual("sandbox", env.Profile);
            Assert.AreEqual(9876543210123UL, env.GetWorldSeed());
            Assert.AreEqual(41, env.SaveGeneration);
            Assert.AreEqual(new DateTime(2026, 9, 30, 18, 45, 12, DateTimeKind.Utc), env.LastAcceptedUtc);
            Assert.AreEqual(DateTimeKind.Utc, env.LastAcceptedUtc.Kind);
            Assert.AreEqual(7, env.Sandbox.BellRingCount);
            Assert.AreEqual(3, env.Sandbox.SessionCount);
            Assert.AreEqual(-2.5f, env.Sandbox.PlayerX);
            Assert.AreEqual(1.25f, env.Sandbox.PlayerY);
        }

        [Test]
        public void CurrentSchemaRoundTripsUnchanged()
        {
            var env = new SaveEnvelope { Profile = "sandbox", SaveGeneration = 3, LastAcceptedUtc = DateTime.UtcNow };
            env.SetWorldSeed(ulong.MaxValue);
            env.Sandbox.BellRingCount = 4;
            Assert.IsTrue(SaveSerializer.TryFromJson(SaveSerializer.ToJson(env), out var back, out var from, out var err), err);
            Assert.AreEqual(SaveEnvelope.CurrentSchemaVersion, from);
            Assert.AreEqual(ulong.MaxValue, back.GetWorldSeed());
            Assert.AreEqual(4, back.Sandbox.BellRingCount);
        }

        [Test]
        public void FutureSchemaIsRefusedNotGuessed()
        {
            Assert.IsFalse(SaveSerializer.TryFromJson("{\"schemaVersion\":99,\"profile\":\"x\"}", out _, out _, out var err));
            StringAssert.Contains("newer", err);
        }

        [Test]
        public void MissingVersionIsRefused()
        {
            Assert.IsFalse(SaveSerializer.TryFromJson("{\"profile\":\"x\"}", out _, out _, out _));
        }
    }

    public sealed class SaveCoordinatorTests : TempDirFixture
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

        [Test]
        public void SampleValueSurvivesRestart()
        {
            var clock = new ManualClock(T0);
            var first = new SandboxSession(new SaveScheduler(new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1), 3f));
            first.Start();
            first.RingBell();
            first.RingBell();
            first.ReportPlayerPosition(2f, -1f);
            first.Flush("quit");

            // "Restart": brand new service graph over the same folder.
            var second = new SandboxSession(new SaveScheduler(new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1), 3f));
            second.Start();
            Assert.AreEqual(2, second.State.BellRingCount);
            Assert.AreEqual(2, second.State.SessionCount);
            Assert.AreEqual(2f, second.State.PlayerX);
            Assert.AreEqual(-1f, second.State.PlayerY);
        }

        [Test]
        public void WorldSeedIsStableAcrossLoads()
        {
            var clock = new ManualClock(T0);
            var a = new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1);
            new SandboxSession(new SaveScheduler(a, 3f)).Start();
            var b = new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1);
            new SandboxSession(new SaveScheduler(b, 3f)).Start();
            Assert.AreEqual(a.WorldSeed, b.WorldSeed);
        }

        [Test]
        public void GenerationIncreasesPerSave()
        {
            var c = new SaveCoordinator(new ManualClock(T0), new SaveStore(Dir), "sandbox", "test", 1);
            var s = new SandboxSession(new SaveScheduler(c, 3f));
            s.Start();
            var g = c.Status.Generation;
            s.RingBell();
            Assert.AreEqual(g + 1, c.Status.Generation);
        }

        [Test]
        public void ClockGoingBackwardsKeepsLastAcceptedTime()
        {
            var clock = new ManualClock(T0);
            var c = new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1);
            var s = new SandboxSession(new SaveScheduler(c, 3f));
            s.Start();
            clock.Set(T0.AddDays(-2));
            s.RingBell();
            Assert.IsTrue(c.Status.ClockWentBackwards);
            Assert.AreEqual(T0, c.Status.LastSavedUtc);
        }

        [Test]
        public void DebouncedPositionSavesAfterDelayOnly()
        {
            var c = new SaveCoordinator(new ManualClock(T0), new SaveStore(Dir), "sandbox", "test", 1);
            var s = new SandboxSession(new SaveScheduler(c, 3f));
            s.Start();
            var g = c.Status.Generation;
            s.ReportPlayerPosition(1, 1);
            s.Tick(1f);
            Assert.AreEqual(g, c.Status.Generation, "no save before debounce");
            s.Tick(2.5f);
            Assert.AreEqual(g + 1, c.Status.Generation, "save after debounce");
        }

        [Test]
        public void OtherProfileFolderIsNotLoaded()
        {
            var clock = new ManualClock(T0);
            var main = new SandboxSession(new SaveScheduler(new SaveCoordinator(clock, new SaveStore(Dir), "main", "test", 1), 3f));
            main.Start();
            main.RingBell();
            var c = new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1);
            var sandbox = new SandboxSession(new SaveScheduler(c, 3f));
            sandbox.Start();
            Assert.AreEqual(0, sandbox.State.BellRingCount);
            StringAssert.Contains("profile mismatch", c.Status.LoadDetail);
        }

        [Test]
        public void CorruptSaveFallsBackAndKeepsPlaying()
        {
            var clock = new ManualClock(T0);
            var c = new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1);
            var s = new SandboxSession(new SaveScheduler(c, 3f));
            s.Start();          // gen 1
            s.RingBell();       // gen 2 (backup = gen 1)
            s.RingBell();       // gen 3 (backup = gen 2, bells 1)
            c.Store.DebugCorruptPrimary();

            var c2 = new SaveCoordinator(clock, new SaveStore(Dir), "sandbox", "test", 1);
            var s2 = new SandboxSession(new SaveScheduler(c2, 3f));
            s2.Start();
            Assert.AreEqual(SaveLoadStatus.RecoveredFromBackup, c2.Status.LoadStatus);
            Assert.AreEqual(1, s2.State.BellRingCount, "falls back to the last known good save");
            Assert.IsNull(c2.Status.LastError, "and keeps saving normally afterwards");
        }
    }
}
