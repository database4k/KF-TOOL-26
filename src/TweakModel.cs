using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KF2Tweaker
{
    public enum Kind { Toggle, Choice, Slider, Number, Pair }
    public enum Level { None, Info, Warn, Danger }

    public sealed class Opt
    {
        public string Value, Label;
        public string[] Values; // for multi-key presets (aligned with the tweak's binds; null = leave alone)
        public Opt(string value, string label) { Value = value; Label = label; }
    }

    public sealed class Bind
    {
        public KF File;
        public string Section, Key;
        public string On = "True", Off = "False";
        public bool OnlyIfPresent;
        public Bind(KF f, string section, string key) { File = f; Section = section; Key = key; }
        public Bind Vals(string on, string off) { On = on; Off = off; return this; }
        public Bind IfPresent() { OnlyIfPresent = true; return this; }

        public string Get(ConfigStore s) { return s.Get(File, Section, Key); }
        public void Set(ConfigStore s, string v)
        {
            if (OnlyIfPresent) s.SetIfPresent(File, Section, Key, v); else s.Set(File, Section, Key, v);
        }
    }

    /// <summary>
    /// One user-facing setting. Values are canonical strings: "1"/"0" for toggles, the option value
    /// for choices, an invariant number for sliders/numbers, "a|b" for pairs.
    /// </summary>
    public sealed class Tweak
    {
        public string Id, Category, Group, Title, Description, Note;
        public Level NoteLevel = Level.Info;
        public Kind Kind;
        public List<Opt> Options = new List<Opt>();
        public List<Opt> Options2 = new List<Opt>();
        public double Min, Max = 1, Step = 1;
        public int Decimals;
        public Func<double, string> Format;
        public Func<string, string> CustomLabel;
        public string Default;           // null = no sensible default (usually an in-game option)
        public string OriginalNote;      // "From the original KF2 Tweaker" style provenance
        public Func<ConfigStore, string> Read;
        public Action<ConfigStore, string> Write;
        public bool Radio;                                    // show as a full-width radio list
        public Dictionary<string, string> OptionNotes;        // per-option help text
        public readonly List<Bind> Binds = new List<Bind>();
        public string Tags = "";

        public bool Available(ConfigStore s)
        {
            foreach (var f in Binds.Select(b => b.File).Distinct())
                if (!s.Has(f)) return false;
            return true;
        }

        public string MissingFile(ConfigStore s)
        {
            foreach (var f in Binds.Select(b => b.File).Distinct())
                if (!s.Has(f)) return ConfigStore.FileName(f);
            return null;
        }

        public string KeysText
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var g in Binds.GroupBy(b => ConfigStore.FileName(b.File) + " [" + b.Section + "]"))
                {
                    if (sb.Length > 0) sb.Append("   ");
                    sb.Append(g.Key).Append(' ').Append(string.Join(", ", g.Select(b => b.Key).Distinct().ToArray()));
                }
                return sb.ToString();
            }
        }

        public string SearchText
        {
            get { return (Title + " " + Description + " " + Group + " " + Category + " " + Tags + " " + KeysText).ToLowerInvariant(); }
        }

        public string LabelFor(string value)
        {
            if (value == null) return "";
            foreach (var o in Options) if (o.Value == value || IniValue.Same(o.Value, value)) return o.Label;
            if (CustomLabel != null) return CustomLabel(value);
            if (value.StartsWith("custom:")) return "Custom (" + value.Substring(7) + ")";
            return "Custom";
        }

        public string Describe(string value)
        {
            switch (Kind)
            {
                case Kind.Toggle: return value == "1" ? "On" : "Off";
                case Kind.Slider:
                case Kind.Number:
                    double d;
                    if (IniValue.TryNumber(value, out d)) return Format != null ? Format(d) : IniValue.Num(d, Decimals);
                    return value;
                case Kind.Pair:
                    var p = (value ?? "|").Split('|');
                    string a = Options.Where(o => o.Value == p[0]).Select(o => o.Label).FirstOrDefault() ?? p[0];
                    string b = Options2.Where(o => o.Value == p[1]).Select(o => o.Label).FirstOrDefault() ?? p[1];
                    return a + ", " + b;
                default: return LabelFor(value);
            }
        }

        public bool SameValue(string a, string b)
        {
            if (a == null || b == null) return a == b;
            if (Kind == Kind.Pair) return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            return IniValue.Same(a, b);
        }
    }

    /// <summary>Fluent builders so the catalog reads like a table.</summary>
    public static class Build
    {
        public static Tweak New(string id, string cat, string group, string title, string desc)
        {
            return new Tweak { Id = id, Category = cat, Group = group, Title = title, Description = desc };
        }

        public static Tweak Warn(this Tweak t, string note) { t.Note = note; t.NoteLevel = Level.Warn; return t; }
        public static Tweak Danger(this Tweak t, string note) { t.Note = note; t.NoteLevel = Level.Danger; return t; }
        public static Tweak Info(this Tweak t, string note) { t.Note = note; t.NoteLevel = Level.Info; return t; }
        public static Tweak Tag(this Tweak t, string tags) { t.Tags += " " + tags; return t; }
        public static Tweak Def(this Tweak t, string def) { t.Default = def; return t; }
        public static Tweak Shows(this Tweak t, params Bind[] b) { t.Binds.AddRange(b); return t; }

        /// <summary>On/off switch across one or more keys. The first bind decides the displayed state.</summary>
        public static Tweak Toggle(this Tweak t, bool? defaultOn, params Bind[] binds)
        {
            t.Kind = Kind.Toggle;
            t.Binds.AddRange(binds);
            t.Default = defaultOn.HasValue ? (defaultOn.Value ? "1" : "0") : null;
            var first = binds[0];
            string fallback = t.Default ?? "0";
            t.Read = s =>
            {
                string v = first.Get(s);
                if (v == null) return fallback;
                if (IniValue.Same(v, first.On)) return "1";
                if (IniValue.Same(v, first.Off)) return "0";
                bool? vb = IniValue.TryBool(v), ob = IniValue.TryBool(first.On);
                if (vb.HasValue && ob.HasValue) return vb.Value == ob.Value ? "1" : "0";
                return fallback;
            };
            t.Write = (s, v) =>
            {
                foreach (var b in binds) b.Set(s, v == "1" ? b.On : b.Off);
            };
            return t;
        }

        static Tweak NumberCore(Tweak t, Kind kind, double min, double max, double step, int decimals, string def,
                                Func<double, string> fmt, Bind[] binds)
        {
            t.Kind = kind; t.Min = min; t.Max = max; t.Step = step; t.Decimals = decimals; t.Format = fmt;
            t.Default = def;
            t.Binds.AddRange(binds);
            var first = binds[0];
            t.Read = s =>
            {
                double d;
                if (IniValue.TryNumber(first.Get(s), out d)) return IniValue.Num(d, decimals);
                return def ?? IniValue.Num(min, decimals);
            };
            t.Write = (s, v) =>
            {
                double d;
                if (!IniValue.TryNumber(v, out d)) return;
                string text = IniValue.Num(d, decimals);
                foreach (var b in binds) b.Set(s, text);
            };
            return t;
        }

        public static Tweak Slider(this Tweak t, double min, double max, double step, int decimals, string def,
                                   Func<double, string> fmt, params Bind[] binds)
        {
            return NumberCore(t, Kind.Slider, min, max, step, decimals, def, fmt, binds);
        }

        public static Tweak Number(this Tweak t, double min, double max, int decimals, string def,
                                   Func<double, string> fmt, params Bind[] binds)
        {
            return NumberCore(t, Kind.Number, min, max, decimals == 0 ? 1 : Math.Pow(10, -decimals), decimals, def, fmt, binds);
        }

        /// <summary>Pick one value; every bind receives the same raw value.</summary>
        public static Tweak Pick(this Tweak t, string def, Bind[] binds, params Opt[] opts)
        {
            t.Kind = Kind.Choice;
            t.Default = def;
            t.Binds.AddRange(binds);
            t.Options.AddRange(opts);
            var first = binds[0];
            t.Read = s =>
            {
                string v = first.Get(s);
                if (v == null) return def ?? opts[0].Value;
                foreach (var o in opts) if (IniValue.Same(o.Value, v)) return o.Value;
                return "custom:" + v.Trim();
            };
            t.Write = (s, v) =>
            {
                if (v.StartsWith("custom:")) return;
                foreach (var b in binds) b.Set(s, v);
            };
            return t;
        }

        /// <summary>Named presets that set several keys at once (null entries are left alone and not checked).</summary>
        public static Tweak Preset(this Tweak t, string def, Bind[] binds, params Opt[] opts)
        {
            t.Kind = Kind.Choice;
            t.Default = def;
            t.Binds.AddRange(binds);
            t.Options.AddRange(opts);
            t.Read = s =>
            {
                var current = binds.Select(b => b.Get(s)).ToArray();
                foreach (var o in opts)
                {
                    bool all = true;
                    for (int i = 0; i < binds.Length && all; i++)
                        if (o.Values[i] != null && !IniValue.Same(o.Values[i], current[i])) all = false;
                    if (all) return o.Value;
                }
                return "custom";
            };
            t.Write = (s, v) =>
            {
                var o = opts.FirstOrDefault(x => x.Value == v);
                if (o == null) return;
                for (int i = 0; i < binds.Length; i++)
                    if (o.Values[i] != null) binds[i].Set(s, o.Values[i]);
            };
            return t;
        }

        public static Tweak Custom(this Tweak t, Kind kind, Func<ConfigStore, string> read, Action<ConfigStore, string> write)
        {
            t.Kind = kind; t.Read = read; t.Write = write;
            return t;
        }

        public static Opt O(string value, string label) { return new Opt(value, label); }
        public static Opt P(string value, string label, params string[] values) { return new Opt(value, label) { Values = values }; }
        public static Bind B(KF f, string section, string key) { return new Bind(f, section, key); }
    }
}
