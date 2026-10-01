using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace KF2Tweaker
{
    /// <summary>
    /// Unreal Engine 3 style INI file that keeps every line, comment, duplicate key and the
    /// original encoding/line endings intact. Only the lines we touch are rewritten.
    /// Section and key names are compared case-insensitively, like the engine does.
    /// </summary>
    public sealed class IniDocument
    {
        public string Path { get; private set; }
        public bool Exists { get; private set; }
        public bool Dirty { get; private set; }
        public DateTime DiskWriteTime { get; private set; }

        List<string> lines = new List<string>();
        Encoding encoding = Latin1;
        bool writeBom;
        string newline = "\r\n";
        bool endsWithNewline = true;

        // section name (lower) -> header line index; rebuilt lazily after structural edits
        Dictionary<string, int> index;

        static readonly Encoding Latin1 = GetLatin1();
        static Encoding GetLatin1()
        {
            try { return Encoding.GetEncoding(1252); } catch { return Encoding.GetEncoding("iso-8859-1"); }
        }

        IniDocument() { }

        public static IniDocument Load(string path)
        {
            var d = new IniDocument();
            d.Path = path;
            d.ReadFromDisk();
            return d;
        }

        public IniDocument Clone()
        {
            var d = new IniDocument();
            d.Path = Path; d.Exists = Exists; d.Dirty = Dirty; d.DiskWriteTime = DiskWriteTime;
            d.lines = new List<string>(lines);
            d.encoding = encoding; d.writeBom = writeBom; d.newline = newline; d.endsWithNewline = endsWithNewline;
            return d;
        }

        public void ReadFromDisk()
        {
            lines.Clear();
            index = null;
            Dirty = false;
            Exists = File.Exists(Path);
            if (!Exists) return;

            byte[] bytes = File.ReadAllBytes(Path);
            DiskWriteTime = File.GetLastWriteTimeUtc(Path);
            int skip = 0;
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) { encoding = Encoding.Unicode; writeBom = true; skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) { encoding = Encoding.BigEndianUnicode; writeBom = true; skip = 2; }
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) { encoding = new UTF8Encoding(false); writeBom = true; skip = 3; }
            else { encoding = Latin1; writeBom = false; }

            string text = encoding.GetString(bytes, skip, bytes.Length - skip);
            newline = text.Contains("\r\n") ? "\r\n" : (text.Contains("\n") ? "\n" : "\r\n");
            endsWithNewline = text.EndsWith("\n");
            string[] parts = text.Split('\n');
            int count = parts.Length;
            if (endsWithNewline) count--; // trailing empty element after final newline
            for (int i = 0; i < count; i++)
            {
                string l = parts[i];
                if (l.EndsWith("\r")) l = l.Substring(0, l.Length - 1);
                lines.Add(l);
            }
        }

        public bool ChangedOnDisk()
        {
            bool nowExists = File.Exists(Path);
            if (nowExists != Exists) return true;
            if (!nowExists) return false;
            return File.GetLastWriteTimeUtc(Path) != DiskWriteTime;
        }

        public void Save()
        {
            if (!Dirty) return;
            var sb = new StringBuilder(lines.Count * 40);
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(lines[i]);
                if (i < lines.Count - 1 || endsWithNewline) sb.Append(newline);
            }
            byte[] body = encoding.GetBytes(sb.ToString());
            byte[] preamble = writeBom ? encoding.GetPreamble() : new byte[0];
            if (writeBom && preamble.Length == 0 && encoding is UTF8Encoding) preamble = new byte[] { 0xEF, 0xBB, 0xBF };

            string tmp = Path + ".kf2t.tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(preamble, 0, preamble.Length);
                fs.Write(body, 0, body.Length);
                fs.Flush(true);
            }
            if (File.Exists(Path))
            {
                try { File.Replace(tmp, Path, null); }
                catch { File.Copy(tmp, Path, true); File.Delete(tmp); }
            }
            else File.Move(tmp, Path);

            Exists = true;
            Dirty = false;
            DiskWriteTime = File.GetLastWriteTimeUtc(Path);
        }

        // ---------------------------------------------------------------- parsing helpers

        static bool IsHeader(string line, out string name)
        {
            name = null;
            string t = line.Trim();
            if (t.Length < 2 || t[0] != '[' || t[t.Length - 1] != ']') return false;
            name = t.Substring(1, t.Length - 2).Trim();
            return true;
        }

        public static bool TryParseKV(string line, out string key, out string value)
        {
            key = null; value = null;
            string t = line.TrimStart();
            if (t.Length == 0 || t[0] == ';' || t[0] == '[') return false;
            int eq = t.IndexOf('=');
            if (eq <= 0) return false;
            key = t.Substring(0, eq).Trim();
            value = t.Substring(eq + 1);
            return true;
        }

        void BuildIndex()
        {
            index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < lines.Count; i++)
            {
                string name;
                if (IsHeader(lines[i], out name) && !index.ContainsKey(name)) index[name] = i;
            }
        }

        int HeaderOf(string section)
        {
            if (index == null) BuildIndex();
            int h;
            return index.TryGetValue(section, out h) ? h : -1;
        }

        int EndOf(int header)
        {
            string n;
            for (int i = header + 1; i < lines.Count; i++) if (IsHeader(lines[i], out n)) return i;
            return lines.Count;
        }

        public bool HasSection(string section) { return HeaderOf(section) >= 0; }

        public IEnumerable<string> SectionNames()
        {
            if (index == null) BuildIndex();
            return index.Keys;
        }

        // ---------------------------------------------------------------- reads

        public string Get(string section, string key)
        {
            int h = HeaderOf(section);
            if (h < 0) return null;
            int end = EndOf(h);
            for (int i = h + 1; i < end; i++)
            {
                string k, v;
                if (TryParseKV(lines[i], out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) return v;
            }
            return null;
        }

        public List<string> GetAll(string section, string key)
        {
            var r = new List<string>();
            int h = HeaderOf(section);
            if (h < 0) return r;
            int end = EndOf(h);
            for (int i = h + 1; i < end; i++)
            {
                string k, v;
                if (TryParseKV(lines[i], out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) r.Add(v);
            }
            return r;
        }

        /// <summary>Raw lines of a section (without header), for custom logic.</summary>
        public List<string> SectionLines(string section)
        {
            var r = new List<string>();
            int h = HeaderOf(section);
            if (h < 0) return r;
            int end = EndOf(h);
            for (int i = h + 1; i < end; i++) r.Add(lines[i]);
            return r;
        }

        // ---------------------------------------------------------------- writes

        /// <summary>Sets every occurrence of key in the section (normally one). Adds it if missing.</summary>
        public void Set(string section, string key, string value)
        {
            int h = HeaderOf(section);
            if (h < 0)
            {
                AppendSection(section);
                h = HeaderOf(section);
            }
            int end = EndOf(h);
            bool found = false;
            for (int i = h + 1; i < end; i++)
            {
                string k, v;
                if (TryParseKV(lines[i], out k, out v) && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    if (v != value)
                    {
                        int eq = lines[i].IndexOf('=');
                        lines[i] = lines[i].Substring(0, eq + 1) + value;
                        Dirty = true;
                    }
                }
            }
            if (!found) InsertLine(section, key + "=" + value, null);
        }

        void AppendSection(string section)
        {
            if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length != 0) lines.Add("");
            lines.Add("[" + section + "]");
            index = null;
            Dirty = true;
        }

        /// <summary>
        /// Inserts a raw line into a section: before the first line matching 'before' if given,
        /// otherwise after the last non-blank line of the section.
        /// </summary>
        public void InsertLine(string section, string rawLine, Func<string, bool> before)
        {
            int h = HeaderOf(section);
            if (h < 0) { AppendSection(section); h = HeaderOf(section); }
            int end = EndOf(h);
            int at = -1;
            if (before != null)
                for (int i = h + 1; i < end; i++) if (before(lines[i])) { at = i; break; }
            if (at < 0)
            {
                at = end;
                while (at - 1 > h && lines[at - 1].Trim().Length == 0) at--;
            }
            lines.Insert(at, rawLine);
            index = null;
            Dirty = true;
        }

        /// <summary>Removes lines in a section for which the predicate returns true. Returns count removed.</summary>
        public int RemoveLines(string section, Func<string, bool> predicate)
        {
            int h = HeaderOf(section);
            if (h < 0) return 0;
            int end = EndOf(h);
            int removed = 0;
            for (int i = end - 1; i > h; i--)
                if (predicate(lines[i])) { lines.RemoveAt(i); removed++; }
            if (removed > 0) { index = null; Dirty = true; }
            return removed;
        }

        /// <summary>Rewrites lines in a section. The mapper returns the new text or null to keep the line.</summary>
        public int MapLines(string section, Func<string, string> mapper)
        {
            int h = HeaderOf(section);
            if (h < 0) return 0;
            int end = EndOf(h);
            int changed = 0;
            for (int i = h + 1; i < end; i++)
            {
                string n = mapper(lines[i]);
                if (n != null && n != lines[i]) { lines[i] = n; changed++; }
            }
            if (changed > 0) { index = null; Dirty = true; }
            return changed;
        }
    }

    /// <summary>Value helpers shared by the tweak catalog.</summary>
    public static class IniValue
    {
        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool TryNumber(string s, out double d)
        {
            d = 0;
            if (s == null) return false;
            s = s.Trim();
            if (s.EndsWith("f", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 1);
            if (s.StartsWith("+")) s = s.Substring(1);
            return double.TryParse(s, NumberStyles.Float, Inv, out d);
        }

        public static bool? TryBool(string s)
        {
            if (s == null) return null;
            s = s.Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        /// <summary>Case-insensitive, numeric-aware equality ("1" == "1.000000", "TRUE" == "True").</summary>
        public static bool Same(string a, string b)
        {
            if (a == null || b == null) return a == b;
            double x, y;
            if (TryNumber(a, out x) && TryNumber(b, out y)) return Math.Abs(x - y) < 1e-4;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Formats a new value in the style of the existing one (TRUE vs True, 1.000000 vs 1).</summary>
        public static string Like(string existing, string value)
        {
            bool? nb = TryBool(value);
            if (nb.HasValue && !IsNumeric(value))
            {
                if (existing != null)
                {
                    string e = existing.Trim();
                    if (e == "TRUE" || e == "FALSE") return nb.Value ? "TRUE" : "FALSE";
                    if (e == "true" || e == "false") return nb.Value ? "true" : "false";
                }
                return nb.Value ? "True" : "False";
            }
            double d;
            if (TryNumber(value, out d) && IsNumeric(value))
            {
                int decimals = -1;
                if (existing != null && IsNumeric(existing))
                {
                    string e = existing.Trim();
                    if (e.EndsWith("f", StringComparison.OrdinalIgnoreCase)) e = e.Substring(0, e.Length - 1);
                    int dot = e.IndexOf('.');
                    decimals = dot < 0 ? 0 : e.Length - dot - 1;
                }
                if (decimals < 0) return value.Trim();
                if (decimals == 0 && Math.Abs(d - Math.Round(d)) > 1e-9) decimals = 6;
                return d.ToString("F" + decimals, Inv);
            }
            return value;
        }

        public static bool IsNumeric(string s)
        {
            double d;
            if (s == null) return false;
            string t = s.Trim();
            if (t.Length == 0) return false;
            if (t.EndsWith("f", StringComparison.OrdinalIgnoreCase)) t = t.Substring(0, t.Length - 1);
            return double.TryParse(t, NumberStyles.Float, Inv, out d);
        }

        public static string Num(double d, int decimals)
        {
            return d.ToString("F" + decimals, Inv);
        }
    }
}
