using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Ravenholm.Infrastructure.Save
{
    public enum SaveLoadStatus
    {
        /// <summary>No save files existed; caller starts a fresh profile.</summary>
        NoSave,
        /// <summary>The primary file was valid.</summary>
        LoadedPrimary,
        /// <summary>The primary file was missing or invalid; the backup was valid and has been restored as primary.</summary>
        RecoveredFromBackup,
        /// <summary>Files existed but none were readable. They were quarantined (renamed), never deleted.</summary>
        Unrecoverable,
    }

    public sealed class SaveLoadResult
    {
        public SaveLoadStatus Status;
        public string Json;
        public readonly List<string> Problems = new List<string>();
        public readonly List<string> QuarantinedFiles = new List<string>();
    }

    /// <summary>
    /// File storage for one save profile directory. Writes are atomic: temp file -> flush ->
    /// verify -> replace primary, moving the previous valid primary to the backup slot.
    /// Corrupt files are renamed aside rather than deleted. Synchronous by design for Phase 0;
    /// the generation guard still prevents an older write replacing a newer one.
    /// </summary>
    public sealed class SaveStore
    {
        public const string PrimaryName = "profile.sav";
        public const string BackupName = "profile.bak";
        public const string TempName = "profile.tmp";

        private long _lastWrittenGeneration = long.MinValue;

        public SaveStore(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Save directory required", nameof(directory));
            Directory = directory;
        }

        public string Directory { get; }
        public string PrimaryPath => Path.Combine(Directory, PrimaryName);
        public string BackupPath => Path.Combine(Directory, BackupName);
        public string TempPath => Path.Combine(Directory, TempName);

        /// <summary>Lets the store reject writes older than what was loaded from disk.</summary>
        public void NoteLoadedGeneration(long generation)
        {
            if (generation > _lastWrittenGeneration) _lastWrittenGeneration = generation;
        }

        public SaveLoadResult Load(Func<string, string> validateJson)
        {
            var result = new SaveLoadResult();
            System.IO.Directory.CreateDirectory(Directory);

            // A temp file only survives if a previous write crashed before the swap; it is never trusted.
            if (File.Exists(TempPath)) TryDelete(TempPath);

            var primaryExists = File.Exists(PrimaryPath);
            var backupExists = File.Exists(BackupPath);
            if (!primaryExists && !backupExists)
            {
                result.Status = SaveLoadStatus.NoSave;
                return result;
            }

            string json = null, problem = null;
            if (primaryExists && TryReadValid(PrimaryPath, validateJson, out json, out problem))
            {
                result.Status = SaveLoadStatus.LoadedPrimary;
                result.Json = json;
                return result;
            }
            if (primaryExists) result.Problems.Add("primary: " + problem);
            else result.Problems.Add("primary: missing");

            if (backupExists && TryReadValid(BackupPath, validateJson, out json, out problem))
            {
                if (primaryExists) result.QuarantinedFiles.Add(Quarantine(PrimaryPath));
                File.Copy(BackupPath, PrimaryPath, overwrite: true);
                result.Status = SaveLoadStatus.RecoveredFromBackup;
                result.Json = json;
                return result;
            }
            result.Problems.Add(backupExists ? "backup: " + problem : "backup: missing");

            if (primaryExists) result.QuarantinedFiles.Add(Quarantine(PrimaryPath));
            if (backupExists) result.QuarantinedFiles.Add(Quarantine(BackupPath));
            result.Status = SaveLoadStatus.Unrecoverable;
            return result;
        }

        /// <summary>Writes <paramref name="json"/> atomically. Returns false (and changes nothing) on failure.</summary>
        public bool Write(string json, long generation, out string error)
        {
            if (generation <= _lastWrittenGeneration)
            {
                error = $"stale write rejected: generation {generation} <= {_lastWrittenGeneration}";
                return false;
            }

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                var text = SaveFileCodec.Encode(json);
                var bytes = new UTF8Encoding(false).GetBytes(text);
                using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }

                // Verify what actually reached the disk before it can replace anything.
                if (!SaveFileCodec.TryDecode(File.ReadAllText(TempPath, Encoding.UTF8), out _, out var verifyError))
                {
                    TryDelete(TempPath);
                    error = "write verification failed: " + verifyError;
                    return false;
                }

                if (File.Exists(PrimaryPath))
                {
                    if (SaveFileCodec.TryDecode(File.ReadAllText(PrimaryPath, Encoding.UTF8), out _, out _))
                    {
                        File.Replace(TempPath, PrimaryPath, BackupPath);   // old primary becomes last-known-good backup
                    }
                    else
                    {
                        Quarantine(PrimaryPath);                            // never rotate a corrupt file into the backup slot
                        File.Move(TempPath, PrimaryPath);
                    }
                }
                else
                {
                    File.Move(TempPath, PrimaryPath);
                }

                _lastWrittenGeneration = generation;
                error = null;
                return true;
            }
            catch (Exception e)
            {
                TryDelete(TempPath);
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        /// <summary>DEBUG FIXTURE: damages the primary file so recovery can be exercised.</summary>
        public void DebugCorruptPrimary()
        {
            if (!File.Exists(PrimaryPath)) return;
            var text = File.ReadAllText(PrimaryPath, Encoding.UTF8);
            File.WriteAllText(PrimaryPath, text.Substring(0, Math.Max(1, text.Length / 2)), Encoding.UTF8);
        }

        /// <summary>DEBUG FIXTURE: removes this profile's save files (including quarantined copies).</summary>
        public void DebugDeleteProfile()
        {
            if (!System.IO.Directory.Exists(Directory)) return;
            foreach (var f in System.IO.Directory.GetFiles(Directory, "profile.*")) TryDelete(f);
            _lastWrittenGeneration = long.MinValue;
        }

        private static bool TryReadValid(string path, Func<string, string> validateJson, out string json, out string problem)
        {
            json = null;
            try
            {
                if (!SaveFileCodec.TryDecode(File.ReadAllText(path, Encoding.UTF8), out var body, out problem)) return false;
                problem = validateJson?.Invoke(body);
                if (problem != null) return false;
                json = body;
                return true;
            }
            catch (Exception e)
            {
                problem = e.Message;
                return false;
            }
        }

        private static string Quarantine(string path)
        {
            var target = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddTHHmmssfff}";
            File.Move(path, target);
            return target;
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }
}
