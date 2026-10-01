using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace KF2Tweaker
{
    public enum KF { Engine, Game, Input, System }

    /// <summary>
    /// Holds the four KF2 config files in memory. Tweaks read and write through this class.
    /// A clone shares nothing with the original, so the UI can preview staged changes safely.
    /// </summary>
    public sealed class ConfigStore
    {
        public static readonly KF[] AllFiles = { KF.Engine, KF.Game, KF.Input, KF.System };
        public const string GameProcess = "KFGame";

        public string ConfigDir { get; private set; }
        readonly Dictionary<KF, IniDocument> docs = new Dictionary<KF, IniDocument>();

        public static string FileName(KF f)
        {
            switch (f)
            {
                case KF.Engine: return "KFEngine.ini";
                case KF.Game: return "KFGame.ini";
                case KF.Input: return "KFInput.ini";
                default: return "KFSystemSettings.ini";
            }
        }

        ConfigStore() { }

        public static ConfigStore Open(string configDir)
        {
            var s = new ConfigStore();
            s.ConfigDir = configDir;
            foreach (var f in AllFiles) s.docs[f] = IniDocument.Load(Path.Combine(configDir, FileName(f)));
            return s;
        }

        public ConfigStore Clone()
        {
            var s = new ConfigStore();
            s.ConfigDir = ConfigDir;
            foreach (var kv in docs) s.docs[kv.Key] = kv.Value.Clone();
            return s;
        }

        public IniDocument Doc(KF f) { return docs[f]; }
        public bool Has(KF f) { return docs[f].Exists; }
        public string PathOf(KF f) { return docs[f].Path; }

        public int FoundCount { get { return AllFiles.Count(Has); } }

        // ------------------------------------------------------------------ value access

        public string Get(KF f, string section, string key)
        {
            var d = docs[f];
            return d.Exists ? d.Get(section, key) : null;
        }

        /// <summary>Writes a value, matching the formatting style of what was there before.</summary>
        public void Set(KF f, string section, string key, string value)
        {
            var d = docs[f];
            if (!d.Exists) return; // never create config files; the game generates them
            d.Set(section, key, IniValue.Like(d.Get(section, key), value));
        }

        public void SetIfPresent(KF f, string section, string key, string value)
        {
            var d = docs[f];
            if (d.Exists && d.Get(section, key) != null) Set(f, section, key, value);
        }

        // ------------------------------------------------------------------ disk sync

        public bool AnyChangedOnDisk()
        {
            return docs.Values.Any(d => d.ChangedOnDisk());
        }

        public void ReloadAll()
        {
            foreach (var d in docs.Values) d.ReadFromDisk();
        }

        public bool IsReadOnly(KF f)
        {
            string p = PathOf(f);
            return File.Exists(p) && (File.GetAttributes(p) & FileAttributes.ReadOnly) != 0;
        }

        public void SetReadOnly(KF f, bool on)
        {
            string p = PathOf(f);
            if (!File.Exists(p)) return;
            var a = File.GetAttributes(p);
            a = on ? (a | FileAttributes.ReadOnly) : (a & ~FileAttributes.ReadOnly);
            File.SetAttributes(p, a);
        }

        /// <summary>Saves changed files. Read-only (locked) files are unlocked for the write and locked again.</summary>
        public List<KF> SaveDirty()
        {
            var saved = new List<KF>();
            foreach (var f in AllFiles)
            {
                var d = docs[f];
                if (!d.Dirty) continue;
                bool locked = IsReadOnly(f);
                if (locked) SetReadOnly(f, false);
                try { d.Save(); }
                finally { if (locked) SetReadOnly(f, true); }
                saved.Add(f);
            }
            return saved;
        }

        // ------------------------------------------------------------------ locating the folder

        public static string AppDataDir
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string p = Path.Combine(root, "KF TOOL 26");
                if (!Directory.Exists(p))
                {
                    string old = Path.Combine(root, "KF TOOL 2026"); // v1.0 folder: keep its backups and settings
                    try { if (Directory.Exists(old)) Directory.Move(old, p); } catch { }
                }
                Directory.CreateDirectory(p);
                return p;
            }
        }

        const string Tail = @"My Games\KillingFloor2\KFGame\Config";

        public static IEnumerable<string> CandidateDirs(string savedOverride)
        {
            if (!string.IsNullOrEmpty(savedOverride)) yield return savedOverride;

            // Compatibility with the original KF2 Tweaker: MyDocsPath.txt next to the exe.
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string legacy = Path.Combine(exeDir, "MyDocsPath.txt");
            if (File.Exists(legacy))
            {
                string docsPath = null;
                try { docsPath = File.ReadAllText(legacy).Trim().Trim('"'); } catch { }
                if (!string.IsNullOrEmpty(docsPath)) yield return Path.Combine(docsPath, Tail);
            }

            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs)) yield return Path.Combine(docs, Tail);

            string profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(profile))
            {
                yield return Path.Combine(profile, "Documents", Tail);
                yield return Path.Combine(profile, "OneDrive", "Documents", Tail);
            }
            string oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(oneDrive)) yield return Path.Combine(oneDrive, "Documents", Tail);
        }

        public static bool LooksLikeConfigDir(string dir)
        {
            try
            {
                return Directory.Exists(dir) &&
                    (File.Exists(Path.Combine(dir, "KFEngine.ini")) || File.Exists(Path.Combine(dir, "KFGame.ini")));
            }
            catch { return false; }
        }

        public static string FindConfigDir(string savedOverride)
        {
            foreach (var c in CandidateDirs(savedOverride))
                if (LooksLikeConfigDir(c)) return c;
            return null;
        }

        /// <summary>Accepts the Config folder itself, KFGame, KillingFloor2, My Games or Documents.</summary>
        public static string NormalizePickedDir(string picked)
        {
            if (string.IsNullOrEmpty(picked)) return null;
            string[] tries =
            {
                picked,
                Path.Combine(picked, "Config"),
                Path.Combine(picked, @"KFGame\Config"),
                Path.Combine(picked, @"KillingFloor2\KFGame\Config"),
                Path.Combine(picked, Tail),
            };
            foreach (var t in tries) if (LooksLikeConfigDir(t)) return t;
            return null;
        }

        public string CacheDir
        {
            get { return Path.GetFullPath(Path.Combine(ConfigDir, "..", "Cache")); }
        }

        public static bool GameRunning()
        {
            try
            {
                var p = Process.GetProcessesByName(GameProcess);
                bool r = p.Length > 0;
                foreach (var x in p) x.Dispose();
                return r;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------ backups

        public static string BackupRoot
        {
            get
            {
                string p = Path.Combine(AppDataDir, "Backups");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        public sealed class Backup
        {
            public string Dir;
            public DateTime When;
            public string Reason;
            public int FileCount;
            public override string ToString()
            {
                return When.ToString("MMM d, yyyy  h:mm tt") + "    " + Reason + " (" + FileCount + " files)";
            }
        }

        /// <summary>Copies every .ini in the Config folder into a timestamped backup folder.</summary>
        public Backup CreateBackup(string reason)
        {
            DateTime now = DateTime.Now;
            string dir = Path.Combine(BackupRoot, now.ToString("yyyy-MM-dd_HHmmss"));
            int n = 1;
            while (Directory.Exists(dir)) dir = Path.Combine(BackupRoot, now.ToString("yyyy-MM-dd_HHmmss") + "_" + (++n));
            Directory.CreateDirectory(dir);
            int count = 0;
            foreach (var file in Directory.GetFiles(ConfigDir, "*.ini"))
            {
                File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
                count++;
            }
            File.WriteAllText(Path.Combine(dir, "backup.txt"),
                "reason=" + reason + "\r\nsource=" + ConfigDir + "\r\n", Encoding.UTF8);
            Prune(30);
            return new Backup { Dir = dir, When = now, Reason = reason, FileCount = count };
        }

        public static List<Backup> ListBackups()
        {
            var r = new List<Backup>();
            foreach (var d in Directory.GetDirectories(BackupRoot))
            {
                var b = new Backup { Dir = d, Reason = "Backup" };
                b.When = Directory.GetCreationTime(d);
                string info = Path.Combine(d, "backup.txt");
                if (File.Exists(info))
                    foreach (var l in File.ReadAllLines(info))
                        if (l.StartsWith("reason=")) b.Reason = l.Substring(7);
                DateTime parsed;
                string name = Path.GetFileName(d);
                if (name.Length >= 17 && DateTime.TryParseExact(name.Substring(0, 17), "yyyy-MM-dd_HHmmss",
                    IniValue.Inv, System.Globalization.DateTimeStyles.None, out parsed)) b.When = parsed;
                b.FileCount = Directory.GetFiles(d, "*.ini").Length;
                r.Add(b);
            }
            return r.OrderByDescending(b => b.When).ToList();
        }

        static void Prune(int keep)
        {
            var all = ListBackups();
            foreach (var b in all.Skip(keep))
                try { Directory.Delete(b.Dir, true); } catch { }
        }

        public void RestoreBackup(Backup b)
        {
            foreach (var file in Directory.GetFiles(b.Dir, "*.ini"))
            {
                string dest = Path.Combine(ConfigDir, Path.GetFileName(file));
                bool locked = File.Exists(dest) && (File.GetAttributes(dest) & FileAttributes.ReadOnly) != 0;
                if (locked) File.SetAttributes(dest, File.GetAttributes(dest) & ~FileAttributes.ReadOnly);
                File.Copy(file, dest, true);
                if (locked) File.SetAttributes(dest, File.GetAttributes(dest) | FileAttributes.ReadOnly);
            }
            ReloadAll();
        }

        /// <summary>Moves all config .ini files into a backup so the game regenerates fresh defaults.</summary>
        public Backup ResetAll()
        {
            var b = CreateBackup("Before full reset");
            foreach (var file in Directory.GetFiles(ConfigDir, "*.ini"))
            {
                var a = File.GetAttributes(file);
                if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(file, a & ~FileAttributes.ReadOnly);
                File.Delete(file);
            }
            ReloadAll();
            return b;
        }

        // ------------------------------------------------------------------ key binding backup (same file name the old KF2 Tweaker used)

        public string KeybindBackupPath { get { return Path.Combine(ConfigDir, "KFInput_BACKUP.ini"); } }

        public void BackupKeybinds()
        {
            File.Copy(PathOf(KF.Input), KeybindBackupPath, true);
            var a = File.GetAttributes(KeybindBackupPath);
            if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(KeybindBackupPath, a & ~FileAttributes.ReadOnly);
        }

        public void RestoreKeybinds()
        {
            bool locked = IsReadOnly(KF.Input);
            if (locked) SetReadOnly(KF.Input, false);
            File.Copy(KeybindBackupPath, PathOf(KF.Input), true);
            if (locked) SetReadOnly(KF.Input, true);
            docs[KF.Input].ReadFromDisk();
        }

        // ------------------------------------------------------------------ download cache

        public long CacheSize(out int files)
        {
            files = 0;
            long total = 0;
            if (!Directory.Exists(CacheDir)) return 0;
            foreach (var f in Directory.EnumerateFiles(CacheDir, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; files++; } catch { }
            }
            return total;
        }

        public void ClearCache()
        {
            if (!Directory.Exists(CacheDir)) return;
            foreach (var d in Directory.GetDirectories(CacheDir)) Directory.Delete(d, true);
            foreach (var f in Directory.GetFiles(CacheDir)) File.Delete(f);
        }
    }

    /// <summary>Tiny key=value settings file for the tool itself (theme, folder override, …).</summary>
    public static class AppSettings
    {
        static readonly string FilePath = Path.Combine(ConfigStore.AppDataDir, "settings.txt");
        static Dictionary<string, string> values;

        static void Ensure()
        {
            if (values != null) return;
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                    foreach (var l in File.ReadAllLines(FilePath))
                    {
                        int eq = l.IndexOf('=');
                        if (eq > 0) values[l.Substring(0, eq)] = l.Substring(eq + 1);
                    }
            }
            catch { }
        }

        public static string Get(string key, string fallback)
        {
            Ensure();
            string v;
            return values.TryGetValue(key, out v) ? v : fallback;
        }

        public static void Set(string key, string value)
        {
            Ensure();
            values[key] = value ?? "";
            try { File.WriteAllLines(FilePath, values.Select(kv => kv.Key + "=" + kv.Value).ToArray()); } catch { }
        }
    }
}
