using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace KF2Tweaker
{
    public sealed class MainForm : Form
    {
        ConfigStore store;
        readonly List<Tweak> tweaks = Catalog.All();
        readonly List<KeyValuePair<Tweak, string>> pending = new List<KeyValuePair<Tweak, string>>();
        Dictionary<string, string> cur = new Dictionary<string, string>(), disp = new Dictionary<string, string>();

        Panel side, bar;
        Stack stack;
        SearchBox search;
        readonly Dictionary<string, NavItem> nav = new Dictionary<string, NavItem>();
        Btn applyBtn, discardBtn, reviewBtn;
        string page = Cat.Overview;
        bool gameRunning, syncing;
        Cell hover;
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 30000, InitialDelay = 700, ReshowDelay = 200 };
        string toast; DateTime toastUntil;
        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2500 };

        sealed class Live { public Tweak T; public Cell R; public Action<string> Set; }
        sealed class HoverInfo { public string Desc, Note, Keys; public Level Level; }
        readonly List<Live> live = new List<Live>();

        public MainForm(string configDir)
        {
            Text = "KF TOOL 26";
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.Px(1100), Theme.Px(740));
            MinimumSize = new Size(Theme.Px(820), Theme.Px(520));
            KeyPreview = true;
            DoubleBuffered = true;
            try { if (Native.IsWindows) Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            OpenStore(configDir);
            BuildChrome();
            ApplyColors();
            Navigate(Cat.Overview);

            poll.Tick += (s, e) => CheckGame();
            poll.Start();
            CheckGame();
            Activated += (s, e) => CheckDisk();
            Application.AddMessageFilter(new WheelFilter(this));
        }

        /// <summary>Scrolls the settings list with the mouse wheel wherever the pointer is over it, regardless of focus.</summary>
        sealed class WheelFilter : IMessageFilter
        {
            readonly MainForm f;
            public WheelFilter(MainForm f) { this.f = f; }
            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg == 0x0200 && f.stack != null && !f.stack.IsDisposed) { f.TrackHover(Control.FromHandle(m.HWnd)); return false; }
                if (m.Msg != 0x020A || f.stack == null || f.stack.IsDisposed || !f.ContainsFocus && Form.ActiveForm != f) return false;
                if (!f.stack.RectangleToScreen(f.stack.ClientRectangle).Contains(Cursor.Position)) return false;
                var target = Control.FromHandle(m.HWnd);
                if (target is ToolStrip || target == f.stack) return false;
                int delta = (short)((long)m.WParam >> 16 & 0xFFFF);
                f.stack.ScrollByWheel(delta);
                return true;
            }
        }

        // =============================================================================== store & staging

        void OpenStore(string dir)
        {
            if (string.IsNullOrEmpty(dir))
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), @"My Games\KillingFloor2\KFGame\Config");
            store = ConfigStore.Open(dir);
            pending.Clear();
            ReadCurrent();
        }

        static string SafeRead(Tweak t, ConfigStore s)
        {
            try { return t.Read(s); } catch { return t.Default ?? ""; }
        }

        void ReadCurrent()
        {
            cur = tweaks.ToDictionary(t => t.Id, t => SafeRead(t, store));
            RecomputePreview();
        }

        ConfigStore Simulate(IEnumerable<KeyValuePair<Tweak, string>> items)
        {
            var sim = store.Clone();
            foreach (var p in items) { try { p.Key.Write(sim, p.Value); } catch { } }
            return sim;
        }

        void RecomputePreview()
        {
            var sim = Simulate(pending);
            disp = tweaks.ToDictionary(t => t.Id, t => SafeRead(t, sim));
        }

        IEnumerable<Tweak> ChangedTweaks { get { return tweaks.Where(t => !t.SameValue(disp[t.Id], cur[t.Id])); } }

        void StageCore(Tweak t, string v)
        {
            pending.RemoveAll(p => p.Key == t);
            var without = SafeRead(t, Simulate(pending));
            if (!t.SameValue(without, v)) pending.Add(new KeyValuePair<Tweak, string>(t, v));
        }

        void Stage(Tweak t, string v)
        {
            if (syncing) return;
            StageCore(t, v);
            RecomputePreview();
            Sync();
        }

        void StageMany(IEnumerable<KeyValuePair<string, string>> values, string what)
        {
            int before = ChangedTweaks.Count();
            foreach (var kv in values)
            {
                var t = tweaks.FirstOrDefault(x => x.Id == kv.Key);
                if (t != null && t.Available(store)) StageCore(t, kv.Value);
            }
            RecomputePreview();
            int after = ChangedTweaks.Count();
            Toast(after == before ? what + ": already matches your settings." : what + " loaded. Review the changes, then apply.");
            Sync();
        }

        void Discard()
        {
            pending.Clear();
            RecomputePreview();
            Toast("Changes discarded.");
            if (page == "review") Navigate(Cat.Overview); else Sync();
        }

        void Apply()
        {
            if (pending.Count == 0) return;
            if (ConfigStore.GameRunning() &&
                MessageBox.Show(this, "Killing Floor 2 is running. It rewrites its config files when it closes, which can undo these changes.\n\nApply anyway?",
                    "KF TOOL 26", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                if (store.AnyChangedOnDisk()) store.ReloadAll();
                int count = ChangedTweaks.Count();
                store.CreateBackup("Before applying " + count + (count == 1 ? " change" : " changes"));
                foreach (var p in pending) p.Key.Write(store, p.Value);
                var saved = store.SaveDirty();
                pending.Clear();
                ReadCurrent();
                Toast(saved.Count == 0 ? "Nothing to save." :
                    "Saved " + string.Join(", ", saved.Select(ConfigStore.FileName).ToArray()) + ". Backup made first.");
                if (page == "review") Navigate(Cat.Overview); else Sync();
            }
            catch (UnauthorizedAccessException ex)
            {
                store.ReloadAll(); ReadCurrent(); Sync();
                MessageBox.Show(this, "Windows blocked writing a config file.\n\n" + ex.Message +
                    "\n\nIf your Documents folder is protected by Controlled folder access or synced by OneDrive, allow KF TOOL 26 or pause syncing, then try again.",
                    "KF TOOL 26", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                store.ReloadAll(); ReadCurrent(); Sync();
                MessageBox.Show(this, "The changes couldn't be saved, and nothing was changed.\n\n" + ex.Message, "KF TOOL 26",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        void CheckDisk()
        {
            try
            {
                if (!store.AnyChangedOnDisk()) return;
                store.ReloadAll();
                ReadCurrent();
                Toast("Config files changed outside KF TOOL, so they were reloaded.");
                Rebuild();
            }
            catch { }
        }

        void CheckGame()
        {
            bool r = ConfigStore.GameRunning();
            if (r != gameRunning) { gameRunning = r; side.Invalidate(); UpdateBar(); }
            if (toast != null && DateTime.Now > toastUntil) { toast = null; UpdateBar(); }
        }

        void Toast(string text) { toast = text; toastUntil = DateTime.Now.AddSeconds(6); UpdateBar(); }

        // =============================================================================== chrome

        void BuildChrome()
        {
            side = new DoubleBufferedPanel { Dock = DockStyle.Left, Width = Theme.Px(196) };
            side.Paint += PaintSide;
            bar = new DoubleBufferedPanel { Dock = DockStyle.Bottom, Height = Theme.Px(50) };
            bar.Paint += PaintBar;
            bar.Resize += (s, e) => LayoutBar();
            stack = new Stack { Dock = DockStyle.Fill, MaxContentWidth = Theme.Px(1240) };

            Controls.Add(stack);
            Controls.Add(bar);
            Controls.Add(side);

            search = new SearchBox("Search");
            search.SetBounds(Theme.Px(12), Theme.Px(78), Theme.Px(172), Theme.Px(30));
            search.Box.TextChanged += (s, e) =>
            {
                if (search.Box.Text.Trim().Length > 0) Navigate("search"); else if (page == "search") Navigate(Cat.Overview);
            };
            search.Box.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Escape) return;
                search.Box.Text = ""; e.SuppressKeyPress = true;
                ActiveControl = nav[page == "search" ? Cat.Overview : page];
            };
            side.Controls.Add(search);

            var icons = new Dictionary<string, string> { { Cat.Overview, "overview" }, { Cat.Display, "display" }, { Cat.Graphics, "graphics" }, { Cat.Performance, "performance" },
                { Cat.Gore, "gore" }, { Cat.Audio, "audio" }, { Cat.Input, "input" }, { Cat.Hud, "hud" }, { Cat.Tools, "tools" } };
            // numbers shown on the right of each tab (Overview and Tools have none)
            var numbers = new Dictionary<string, string> { { Cat.Display, "1" }, { Cat.Graphics, "2" }, { Cat.Performance, "3" }, { Cat.Gore, "4" },
                { Cat.Audio, "5" }, { Cat.Input, "7" }, { Cat.Hud, "8" } };
            foreach (var c in Cat.Pages)
            {
                var n = new NavItem(c) { Icon = icons[c], Number = numbers.ContainsKey(c) ? numbers[c] : "" };
                string target = c;
                n.Click += (s, e) => { if (search.Box.Text.Length > 0) search.Box.Text = ""; Navigate(target); };
                side.Controls.Add(n);
                nav[c] = n;
            }
            side.Resize += (s, e) => LayoutNav();
            LayoutNav();

            applyBtn = new Btn("Apply changes", BtnStyle.Primary);
            discardBtn = new Btn("Discard", BtnStyle.Secondary);
            reviewBtn = new Btn("Review", BtnStyle.Ghost);
            applyBtn.Click += (s, e) => Apply();
            discardBtn.Click += (s, e) => Discard();
            reviewBtn.Click += (s, e) => Navigate("review");
            bar.Controls.AddRange(new Control[] { applyBtn, discardBtn, reviewBtn });
            LayoutBar();
        }

        /// <summary>Stacks the tabs under the search box, shrinking them a little when the window is short.</summary>
        void LayoutNav()
        {
            int top = Theme.Px(118), bottom = side.Height - Theme.Px(56);
            int gap = Theme.Px(4), group = Theme.Px(8), count = Cat.Pages.Length;
            int h = (bottom - top - gap * (count - 1) - group * 2) / count;
            h = Math.Max(Theme.Px(30), Math.Min(Theme.Px(42), h));
            int y = top;
            foreach (var c in Cat.Pages)
            {
                var n = nav[c];
                n.SetBounds(0, y, side.Width, h);
                y += h + gap;
                if (c == Cat.Overview || c == Cat.Hud) y += group;
            }
        }

        sealed class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }
        }

        void ApplyColors()
        {
            BackColor = Theme.Window;
            side.BackColor = Theme.PanelHi;
            bar.BackColor = Theme.Surface;
            stack.BackColor = Theme.Window;
            search.Box.BackColor = Theme.Surface; search.Box.ForeColor = Theme.Text;
            if (IsHandleCreated) { Native.TitleBar(Handle, Theme.Dark); Native.ScrollbarTheme(stack.Handle, Theme.Dark); }
            Invalidate(true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ActiveControl = nav[Cat.Overview];
            stack.Relayout();
#if PREVIEW
            string pg = Environment.GetEnvironmentVariable("KF2T_PAGE");
            string st = Environment.GetEnvironmentVariable("KF2T_STAGE");
            if (!string.IsNullOrEmpty(st))
                StageMany(st.Split(';').Select(x => x.Split('=')).Where(x => x.Length == 2).Select(x => new KeyValuePair<string, string>(x[0], x[1])), "Preview");
            if (!string.IsNullOrEmpty(pg)) { if (pg.StartsWith("search:")) { search.FocusBox(); search.Box.Text = pg.Substring(7); } else Navigate(pg); }
            if (Environment.GetEnvironmentVariable("KF2T_APPLY") == "1") Apply();
#endif
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.TitleBar(Handle, Theme.Dark);
            Native.ScrollbarTheme(stack.Handle, Theme.Dark);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S)) { Apply(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { search.FocusBox(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (pending.Count > 0 && ChangedTweaks.Any())
            {
                var r = MessageBox.Show(this, "You have changes that haven't been applied. Apply them before closing?", "KF TOOL 26",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; return; }
                if (r == DialogResult.Yes) { Apply(); if (pending.Count > 0) { e.Cancel = true; return; } }
            }
            base.OnFormClosing(e);
        }

        const TextFormatFlags OneLine = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        void PaintSide(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.PanelHi);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Perk-style hexagon badge
            float R = Theme.Px(20), cx = Theme.Px(12) + R, cy = Theme.Px(12) + R;
            var hex = new PointF[6];
            for (int k = 0; k < 6; k++)
            {
                double a = Math.PI / 180 * (-90 + 60 * k);
                hex[k] = new PointF(cx + (float)(R * Math.Cos(a)), cy + (float)(R * Math.Sin(a)));
            }
            using (var b = new SolidBrush(Theme.Mix(Theme.PanelHi, Theme.Hover, 0.6f))) g.FillPolygon(b, hex);
            using (var pen = new Pen(Theme.AccentText, Theme.Px(2.4f)) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round }) g.DrawPolygon(pen, hex);
            using (var b = new SolidBrush(Theme.AccentText))
                g.FillPolygon(b, new[] { new PointF(cx - Theme.Px(5), cy + R + Theme.Px(2)), new PointF(cx + Theme.Px(5), cy + R + Theme.Px(2)), new PointF(cx, cy + R + Theme.Px(7)) });
            float[] ys = { -0.36f, 0f, 0.36f }, ks = { 0.3f, -0.3f, 0.12f };
            using (var pen = new Pen(Theme.Text2, Theme.Px(1.8f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
            using (var knob = new SolidBrush(Theme.AccentHover))
                for (int k = 0; k < 3; k++)
                {
                    float yy = cy + R * ys[k];
                    g.DrawLine(pen, cx - R * 0.5f, yy, cx + R * 0.5f, yy);
                    float kx = cx + R * ks[k], kr = Theme.Px(3.2f);
                    g.FillEllipse(knob, kx - kr, yy - kr, kr * 2, kr * 2);
                }

            int tx = (int)(cx + R + Theme.Px(10));
            TextRenderer.DrawText(g, "KF TOOL 26", Theme.Brand, new Point(tx, Theme.Px(11)), Theme.Text, OneLine);
            TextRenderer.DrawText(g, "v1.1   by db4ks", Theme.BrandSmall, new Point(tx + Theme.Px(1), Theme.Px(38)), Theme.Muted, OneLine);
            // XP-bar style red strip
            using (var b = new SolidBrush(Color.FromArgb(0x5B, 0x13, 0x11))) g.FillRectangle(b, 0, Theme.Px(62), side.Width, Theme.Px(4));

            int x = Theme.Px(14), y = side.Height - Theme.Px(48);
            using (var b = new SolidBrush(gameRunning ? Theme.Warn : Theme.Good)) g.FillEllipse(b, x, y + Theme.Px(4), Theme.Px(7), Theme.Px(7));
            TextRenderer.DrawText(g, gameRunning ? "KF2 is running" : "KF2 is closed", Theme.Small, new Point(x + Theme.Px(13), y), Theme.Text2, OneLine);
            int found = store.FoundCount;
            using (var b = new SolidBrush(found == 4 ? Theme.Faint : Theme.Danger)) g.FillEllipse(b, x, y + Theme.Px(22), Theme.Px(7), Theme.Px(7));
            TextRenderer.DrawText(g, found + " of 4 config files found", Theme.Small,
                new Point(x + Theme.Px(13), y + Theme.Px(18)), found == 4 ? Theme.Muted : Theme.Danger, OneLine);
        }

        void LayoutBar()
        {
            if (applyBtn == null) return;
            int y = (bar.Height - applyBtn.Height) / 2 + 1;
            int r = bar.Width - Theme.Px(14);
            applyBtn.Location = new Point(r - applyBtn.Width, y);
            discardBtn.Location = new Point(applyBtn.Left - Theme.Px(8) - discardBtn.Width, y);
            reviewBtn.Location = new Point(discardBtn.Left - Theme.Px(4) - reviewBtn.Width, y);
        }

        void UpdateBar()
        {
            if (applyBtn == null) return;
            int n = ChangedTweaks.Count();
            applyBtn.Enabled = discardBtn.Enabled = pending.Count > 0;
            reviewBtn.Visible = n > 0 && page != "review";
            applyBtn.Text = n > 0 ? (n == 1 ? "Apply 1 change" : "Apply " + n + " changes") : "Apply changes";
            LayoutBar();
            foreach (var kv in nav)
                kv.Value.Badge = tweaks.Count(t => t.Category == kv.Key && !t.SameValue(disp[t.Id], cur[t.Id]));
            bar.Invalidate();
        }

        /// <summary>Called for every mouse move: shows the hovered setting's details in the bottom bar.</summary>
        internal void TrackHover(Control c)
        {
            Cell found = null;
            for (var x = c; x != null; x = x.Parent) { found = x as Cell; if (found != null) break; if (x == stack) break; }
            if (found != null && !(found.Tag is HoverInfo)) found = null;
            if (found == hover) return;
            hover = found;
            bar.Invalidate();
        }

        void PaintBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int n = ChangedTweaks.Count();
            g.Clear(n > 0 ? Theme.AccentSoft : Theme.Surface);
            using (var b = new SolidBrush(n > 0 ? Theme.BtnRedLine : Theme.Line)) g.FillRectangle(b, 0, 0, bar.Width, n > 0 ? Theme.Px(3) : 1);
            int left = Theme.Px(16);
            int right = (reviewBtn.Visible ? reviewBtn.Left : discardBtn.Left) - Theme.Px(14);
            var info = hover != null && !hover.IsDisposed ? hover.Tag as HoverInfo : null;
            if (toast == null && info != null)
            {
                string second = !string.IsNullOrEmpty(info.Note) ? info.Note : info.Keys;
                Color sc = info.Level == Level.Danger ? Theme.Danger : info.Level == Level.Warn ? Theme.Warn : Theme.Faint;
                if (string.IsNullOrEmpty(info.Note)) sc = Theme.Faint;
                int top = string.IsNullOrEmpty(second) ? 0 : -Theme.Px(8);
                TextRenderer.DrawText(g, info.Desc, Theme.Small, new Rectangle(left, top, right - left, bar.Height), Theme.Text2,
                    OneLine | TextFormatFlags.VerticalCenter);
                if (!string.IsNullOrEmpty(second))
                    TextRenderer.DrawText(g, second, string.IsNullOrEmpty(info.Note) ? Theme.Mono : Theme.Small,
                        new Rectangle(left, Theme.Px(8), right - left, bar.Height), sc, OneLine | TextFormatFlags.VerticalCenter);
                return;
            }
            string text; Color c = Theme.Muted;
            if (toast != null) { text = toast; c = Theme.Text; }
            else if (n > 0 && gameRunning) { text = "Close Killing Floor 2 before applying. It rewrites these files when it exits."; c = Theme.Warn; }
            else if (n > 0) { text = (n == 1 ? "1 setting changed." : n + " settings changed.") + " Nothing is saved until you apply."; c = Theme.Text; }
            else text = "Hover a setting to see what it does. Changes are saved only when you apply, after a backup.";
            TextRenderer.DrawText(g, text, Theme.Body, new Rectangle(left, 0, right - left, bar.Height), c, OneLine | TextFormatFlags.VerticalCenter);
        }

        // =============================================================================== navigation

        void Navigate(string target)
        {
            page = target;
            foreach (var kv in nav) kv.Value.Selected = kv.Key == target;
            Rebuild();
            stack.AutoScrollPosition = Point.Empty;
        }

        void Rebuild()
        {
            stack.SuspendLayout();
            var old = stack.Controls.Cast<Control>().ToList();
            stack.Controls.Clear();
            foreach (var c in old) c.Dispose();
            live.Clear();
            hover = null;
            tip.RemoveAll();

            if (page == Cat.Overview) BuildOverview();
            else if (page == Cat.Tools) BuildTools();
            else if (page == "search") BuildSearch(search.Box.Text);
            else if (page == "review") BuildReview();
            else BuildCategory(page);

            stack.ResumeLayout();
            Sync();
        }

        void Add(Control c) { stack.Controls.Add(c); }

        void PageTitle(string title, string blurb, Control action)
        {
            Add(new HeaderRow(title, blurb, action));
        }

        void Heading(string text) { Add(new SectionHead(text)); }

        /// <summary>Page header styled like the KF2 perk header: grey panel, large light title, red bar underneath.</summary>
        sealed class HeaderRow : Control
        {
            readonly Control action; readonly string blurb;
            public HeaderRow(string title, string blurb, Control action)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Text = title; this.blurb = blurb; this.action = action;
                if (action != null) Controls.Add(action);
                Height = Theme.Px(52);
            }
            int Bar { get { return Theme.Px(4); } }
            int PanelH { get { return Height - Theme.Px(6); } }
            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                if (action != null) action.Location = new Point(Width - Theme.Px(10) - action.Width, (PanelH - Bar - action.Height) / 2);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Theme.Window);
                using (var b = new SolidBrush(Theme.PanelHi)) g.FillRectangle(b, 0, 0, Width, PanelH - Bar);
                using (var b = new SolidBrush(Color.FromArgb(0x5B, 0x13, 0x11))) g.FillRectangle(b, 0, PanelH - Bar, Width, Bar);
                var sz = TextRenderer.MeasureText(Text, Theme.Title, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                int ty = (PanelH - Bar - sz.Height) / 2;
                TextRenderer.DrawText(g, Text, Theme.Title, new Point(Theme.Px(12), ty), Theme.Text, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                if (string.IsNullOrEmpty(blurb)) return;
                int bx = Theme.Px(12) + sz.Width + Theme.Px(16);
                int br = (action != null ? action.Left : Width) - Theme.Px(12);
                var bs = TextRenderer.MeasureText(blurb, Theme.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, blurb, Theme.Small, new Rectangle(bx, (PanelH - Bar - bs.Height) / 2 + Theme.Px(2), br - bx, bs.Height), Theme.Muted,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
        }

        void AddTweakCells(IEnumerable<Tweak> list, bool categoryHeadings)
        {
            string group = null;
            foreach (var t in list)
            {
                string g = categoryHeadings ? t.Category : t.Group;
                if (g != group) { Heading(g); group = g; }
                Add(MakeTweakCell(t));
            }
        }

        static int EditorWidthFor(Tweak t, Control editor)
        {
            switch (t.Kind)
            {
                case Kind.Toggle: return editor.Width;
                case Kind.Slider: return Theme.Px(200);
                case Kind.Number: return Theme.Px(104);
                case Kind.Pair: return Theme.Px(250);
                default: return editor is Segmented ? Theme.Px(176) : Theme.Px(184);
            }
        }

        Cell MakeTweakCell(Tweak t)
        {
            Action<string> set;
            var editor = MakeEditor(t, out set);
            var cell = new Cell(t.Title, t.Description, editor, EditorWidthFor(t, editor)) { NoteLevel = t.NoteLevel };
            if (t.Radio) { cell.EditorBelow = true; cell.Half = false; }
            cell.ResetClicked += (s, e) => { if (t.Default != null) Stage(t, t.Default); };
            cell.Tag = new HoverInfo { Desc = t.Description, Note = t.Note, Level = t.NoteLevel, Keys = t.KeysText };
            live.Add(new Live { T = t, R = cell, Set = set });
            return cell;
        }

        /// <summary>Action cell for Overview/Tools: title, one-line description, buttons.</summary>
        Cell Act(string title, string desc, Control editor, int editorWidth = 0)
        {
            var bg = editor as BtnGroup;
            int w = editorWidth > 0 ? editorWidth : (bg != null ? bg.Needed : (editor == null ? 0 : editor.Width));
            var cell = new Cell(title, desc, editor, w) { ShowDesc = true };
            cell.Tag = new HoverInfo { Desc = desc };
            if (!string.IsNullOrEmpty(desc)) tip.SetToolTip(cell, desc);
            return cell;
        }

        Control MakeEditor(Tweak t, out Action<string> set)
        {
            switch (t.Kind)
            {
                case Kind.Toggle:
                {
                    var c = new ToggleSwitch();
                    c.Changed += (s, e) => Stage(t, c.Checked ? "1" : "0");
                    set = v => c.Checked = v == "1";
                    return c;
                }
                case Kind.Slider:
                {
                    var c = new Slider(t.Min, t.Max, t.Step, t.Decimals, t.Format);
                    c.Committed += (s, e) => Stage(t, c.ValueText);
                    set = v => { double d; if (IniValue.TryNumber(v, out d)) c.Value = d; };
                    return c;
                }
                case Kind.Number:
                {
                    string suffix = null;
                    if (t.Format != null) { string f = t.Format(1); int sp = f.IndexOf(' '); if (sp > 0) suffix = f.Substring(sp + 1); }
                    var c = new NumberBox(t.Min, t.Max, t.Decimals, suffix);
                    c.Committed += (s, e) => Stage(t, c.Value);
                    set = v => c.Value = v;
                    return c;
                }
                case Kind.Pair:
                {
                    var a = new Dropdown(t.Options);
                    var b = new Dropdown(t.Options2);
                    var holder = new PairHolder(a, b);
                    EventHandler changed = (s, e) => Stage(t, a.Value + "|" + b.Value);
                    a.Changed += changed; b.Changed += changed;
                    set = v => { var p = (v ?? "0|M").Split('|'); a.Value = p[0]; b.Value = p.Length > 1 ? p[1] : ""; };
                    return holder;
                }
                default:
                {
                    if (t.Radio)
                    {
                        var r = new RadioGrid(t.Options, t.OptionNotes);
                        r.Changed += (s, e) => Stage(t, r.Value);
                        set = v => r.Value = v;
                        return r;
                    }
                    bool segmented = t.Options.Count <= 3 && t.CustomLabel == null && t.Options.All(o => o.Label.Length <= 10);
                    if (segmented)
                    {
                        var c = new Segmented(t.Options);
                        c.Changed += (s, e) => Stage(t, c.Value);
                        set = v => c.Value = v;
                        return c;
                    }
                    var d = new Dropdown(t.Options) { LabelFor = t.LabelFor };
                    d.Changed += (s, e) => Stage(t, d.Value);
                    set = v => d.Value = v;
                    return d;
                }
            }
        }

        sealed class PairHolder : Control
        {
            readonly Control a, b;
            public PairHolder(Control a, Control b)
            {
                this.a = a; this.b = b;
                SetStyle(ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
                Controls.Add(a); Controls.Add(b);
                Height = a.Height;
            }
            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                int gap = Theme.Px(6), wa = (Width - gap) * 55 / 100;
                a.SetBounds(0, 0, wa, a.Height);
                b.SetBounds(wa + gap, 0, Width - wa - gap, b.Height);
            }
            protected override void OnEnabledChanged(EventArgs e) { a.Enabled = b.Enabled = Enabled; base.OnEnabledChanged(e); }
        }

        /// <summary>Pushes preview values into every visible editor and refreshes markers.</summary>
        void Sync()
        {
            syncing = true;
            try
            {
                foreach (var l in live)
                {
                    var t = l.T;
                    string v = disp[t.Id], c = cur[t.Id];
                    l.Set(v);
                    bool ok = t.Available(store);
                    var cell = l.R;
                    cell.Disabled = !ok;
                    cell.Changed = !t.SameValue(v, c);
                    cell.ShowReset = ok && t.Default != null && !t.SameValue(v, t.Default);
                    var info = (HoverInfo)cell.Tag;
                    info.Note = ok ? t.Note : ConfigStore.FileName(t.Binds[0].File) + " wasn't found. Start Killing Floor 2 once so it creates its config files.";
                    info.Level = ok ? t.NoteLevel : Level.Warn;
                    cell.NoteLevel = info.Level;
                    if (page == "review")
                    {
                        cell.ShowDesc = true;
                        cell.Description = "Currently " + t.Describe(c) + ", will be " + t.Describe(v);
                    }
                    string levels = t.OptionNotes == null ? "" : "\n\n" + string.Join("\n", t.Options.Where(o => t.OptionNotes.ContainsKey(o.Value))
                                        .Select(o => o.Label.ToUpperInvariant() + ": " + t.OptionNotes[o.Value]).ToArray());
                    string tipText = t.Description + levels + (info.Note != null ? "\n\n" + info.Note : "") +
                                     (cell.ShowReset ? "\n\nDefault: " + t.Describe(t.Default) : "") +
                                     "\n\n" + t.KeysText + (t.OriginalNote != null ? "\n" + t.OriginalNote : "");
                    tip.SetToolTip(cell, tipText);
                    cell.PerformLayout();
                    cell.Invalidate(true);
                }
            }
            finally { syncing = false; }
            stack.Relayout();
            UpdateBar();
        }

        // =============================================================================== pages

        void BuildCategory(string cat)
        {
            var list = tweaks.Where(t => t.Category == cat).ToList();
            var reset = new Btn("Use defaults on this page", BtnStyle.Secondary);
            reset.Click += (s, e) => StageMany(list.Where(t => t.Default != null).Select(t => new KeyValuePair<string, string>(t.Id, t.Default)),
                "Defaults for " + cat);
            PageTitle(cat, Cat.Blurb(cat), reset);
            AddTweakCells(list, false);
        }

        void BuildSearch(string q)
        {
            var terms = q.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = tweaks.Where(t => terms.All(term => t.SearchText.Contains(term))).ToList();
            PageTitle("Search", list.Count == 0 ? "No settings match \"" + q.Trim() + "\". Try a word like stutter or a key name like PoolSize."
                                                : list.Count + (list.Count == 1 ? " setting matches" : " settings match") + " \"" + q.Trim() + "\"", null);
            AddTweakCells(list, true);
        }

        void BuildReview()
        {
            var list = ChangedTweaks.ToList();
            PageTitle("Review changes", list.Count == 0 ? "Nothing is waiting to be applied." : "Written when you apply. Adjust any of them here, or discard them all.", null);
            AddTweakCells(list, true);
        }

        void BuildOverview()
        {
            PageTitle("Overview", "Hover any setting to see what it does. Nothing is saved until you apply.", null);
            if (store.FoundCount == 0)
            {
                var pick = new Btn("Choose folder", BtnStyle.Primary);
                pick.Click += (s, e) => PickFolder();
                Heading("Setup");
                Add(Act("Config folder not found", "Looked in " + store.ConfigDir + ". Start KF2 once, or choose the folder yourself.", new BtnGroup(pick)));
            }

            Heading("Worth a look");
            var checks = HealthChecks();
            if (checks.Count == 0)
                Add(new TextBlock("Nothing needs attention. Your config files look healthy.", Theme.Small, () => Theme.Muted, Theme.Px(2), Theme.Px(4)));
            foreach (var c in checks) Add(c);

            Heading("Quick presets");
            foreach (var p in Catalog.Presets())
            {
                var preset = p;
                var b = new Btn("Load", BtnStyle.Secondary);
                b.Click += (s, e) => StageMany(preset.Values, preset.Title);
                var cell = Act(preset.Title, preset.Description, new BtnGroup(b));
                ((HoverInfo)cell.Tag).Note = "Loading a preset only fills in settings. Review them, then apply.";
                Add(cell);
            }

            Heading("Your setup");
            var open = new Btn("Open", BtnStyle.Secondary);
            open.Click += (s, e) => OpenPath(store.ConfigDir);
            var change = new Btn("Change", BtnStyle.Ghost);
            change.Click += (s, e) => PickFolder();
            Add(Act("Config folder", store.ConfigDir, new BtnGroup(change, open)));
            var backups = ConfigStore.ListBackups();
            var toTools = new Btn("Manage", BtnStyle.Ghost);
            toTools.Click += (s, e) => Navigate(Cat.Tools);
            Add(Act("Backups", backups.Count == 0 ? "None yet. One is made automatically every time you apply."
                : backups.Count + " saved. Latest " + backups[0].When.ToString("MMM d, h:mm tt"), new BtnGroup(toTools)));
        }

        List<Cell> HealthChecks()
        {
            var rows = new List<Cell>();
            Func<string, string, string, string, string, Cell> fix = (id, value, title, desc, btn) =>
            {
                var b = new Btn(btn, BtnStyle.Secondary);
                var t = tweaks.First(x => x.Id == id);
                b.Click += (s, e) => Stage(t, value);
                var c = Act(title, desc, new BtnGroup(b));
                c.NoteLevel = Level.Warn;
                return c;
            };
            foreach (var f in ConfigStore.AllFiles.Where(f => !store.Has(f)))
                rows.Add(Act(ConfigStore.FileName(f) + " is missing", "Start Killing Floor 2 once and close it. The game creates its config files on first launch.", null));
            if (store.Has(KF.Input))
            {
                if (cur["turnscale"] == "0")
                    rows.Add(fix("turnscale", "1", "Controller and key turning is off",
                        "The old KF2 Tweaker's \"mouse movement scale\" set this to zero. Mouse aim is fine; sticks and turn keys don't work.", "Turn on"));
                if (store.IsReadOnly(KF.Input))
                {
                    var b = new Btn("Unlock", BtnStyle.Secondary);
                    b.Click += (s, e) => { SetLock(KF.Input, false); Rebuild(); };
                    rows.Add(Act("KFInput.ini is locked", "Stops key binds resetting, but binds you change in the game won't be saved.", new BtnGroup(b)));
                }
            }
            if (store.Has(KF.System) && cur["distortion"] == "0")
                rows.Add(fix("distortion", "1", "Distortion is off", "Cloaked Stalkers are much harder to spot without it.", "Turn on"));
            if (store.Has(KF.Engine) && cur["particles"] == "minimal")
                rows.Add(fix("particles", "reduced", "Particles are on Minimal", "Can make Husk fireballs invisible. Reduced keeps most of the benefit.", "Use Reduced"));
            if (store.Has(KF.Engine) && cur["gclimit"] == "custom")
                rows.Add(fix("gclimit", "33476", "Crash fix isn't applied", "Widely recommended for stability, and needed by many modded servers.", "Apply fix"));
            if (store.Has(KF.Input) && cur["smoothing"] == "1")
                rows.Add(fix("smoothing", "0", "Mouse smoothing is on", "Most players turn this off for direct, one-to-one aim.", "Turn off"));
            return rows;
        }

        void BuildTools()
        {
            PageTitle(Cat.Tools, "Backups, file locks and housekeeping. These act immediately.", null);

            Heading("Backups");
            var now = new Btn("Back up now", BtnStyle.Secondary);
            now.Click += (s, e) => Try(() => { store.CreateBackup("Manual backup"); Toast("Backup saved."); Rebuild(); });
            var restore = new Btn("Restore", BtnStyle.Secondary);
            restore.Click += (s, e) => RestoreDialog();
            var backups = ConfigStore.ListBackups();
            Add(Act("Config backups", "Made before every Apply; last 30 kept." +
                (backups.Count > 0 ? " Latest " + backups[0].When.ToString("MMM d, h:mm tt") + "." : ""), new BtnGroup(restore, now)));
            var openB = new Btn("Open", BtnStyle.Secondary);
            openB.Click += (s, e) => OpenPath(ConfigStore.BackupRoot);
            Add(Act("Backup folder", ConfigStore.BackupRoot, new BtnGroup(openB)));

            Heading("Protect files and key binds");
            LockCell(KF.Input, "Stops the key-bind reset bug. Binds changed in the game won't stick while locked.");
            LockCell(KF.System, "Stops the in-game graphics menu overwriting tweaks from this tool.");
            LockCell(KF.Engine, "Keeps engine tweaks such as Hor+ FOV and streaming in place.");
            LockCell(KF.Game, "Keeps FOV, frame rate and gore. In-game options like volume won't save while locked.");
            string kb = File.Exists(store.KeybindBackupPath) ? "Saved " + File.GetLastWriteTime(store.KeybindBackupPath).ToString("MMM d, yyyy") + "." : "No copy saved yet.";
            var saveK = new Btn("Save", BtnStyle.Secondary);
            saveK.Click += (s, e) => Try(() => { store.BackupKeybinds(); Toast("Key bindings copied to KFInput_BACKUP.ini."); Rebuild(); });
            var restK = new Btn("Restore", BtnStyle.Secondary) { Enabled = File.Exists(store.KeybindBackupPath) };
            restK.Click += (s, e) =>
            {
                if (Confirm("Replace your current key bindings with the saved copy?"))
                    Try(() => { store.RestoreKeybinds(); ReadCurrent(); Toast("Key bindings restored."); Rebuild(); });
            };
            var kbCell = Act("Key binding copy", "Spare copy of KFInput.ini (KFInput_BACKUP.ini). " + kb, new BtnGroup(restK, saveK));
            kbCell.Disabled = !store.Has(KF.Input);
            Add(kbCell);

            Heading("Game");
            var clear = new Btn("Clear", BtnStyle.Danger);
            var cacheCell = Act("Download cache", "Measuring\u2026", new BtnGroup(clear));
            ((HoverInfo)cacheCell.Tag).Desc = "Maps, mods and mutators downloaded from servers. Clearing doesn't touch progress or items.";
            clear.Click += (s, e) =>
            {
                if (!Confirm("Delete everything in the download cache?\n\n" + store.CacheDir + "\n\nCustom maps will disappear from the solo map list until you download them again.")) return;
                Try(() => { store.ClearCache(); Toast("Download cache cleared."); Rebuild(); });
            };
            Add(cacheCell);
            MeasureCache(cacheCell);
            var copy = new Btn("Copy", BtnStyle.Secondary);
            copy.Click += (s, e) => Try(() => { Clipboard.SetText("-nostartupmovies"); Toast("Copied. Paste it into Steam: Killing Floor 2, Properties, Launch options."); });
            Add(Act("Launch option -nostartupmovies", "Skips intros even if an update resets your config files.", new BtnGroup(copy)));
            var launch = new Btn("Launch", BtnStyle.Secondary);
            launch.Click += (s, e) => OpenPath("steam://rungameid/232090");
            var openC = new Btn("Config", BtnStyle.Ghost);
            openC.Click += (s, e) => OpenPath(store.ConfigDir);
            Add(Act("Killing Floor 2", "Open the config folder, or start the game through Steam.", new BtnGroup(openC, launch)));
            var reset = new Btn("Reset\u2026", BtnStyle.Danger);
            reset.Click += (s, e) =>
            {
                if (!Confirm("Move every config file into a backup so Killing Floor 2 creates fresh defaults next time it starts?\n\nYour perk progress and items are stored by Steam and aren't affected. You can restore the backup from this page.")) return;
                Try(() => { store.ResetAll(); ReadCurrent(); Toast("Config files moved to a backup. Start the game to create fresh ones."); Rebuild(); });
            };
            Add(Act("Reset all game settings", "Back to the game's defaults, including graphics, audio and key binds. Backed up first.", new BtnGroup(reset)));

            Heading("This app");
            var change = new Btn("Change", BtnStyle.Secondary);
            change.Click += (s, e) => PickFolder();
            Add(Act("Config folder", store.ConfigDir, new BtnGroup(change)));
            Add(Act("KF TOOL 26", "Version 1.1 by db4ks. Builds on the old KF2 Tweaker by RejZoR.", null));
        }

        void LockCell(KF f, string desc)
        {
            var sw = new ToggleSwitch { Checked = store.IsReadOnly(f) };
            sw.Changed += (s, e) => SetLock(f, sw.Checked);
            var c = Act("Lock " + ConfigStore.FileName(f), desc, sw, sw.Width);
            c.Disabled = !store.Has(f);
            Add(c);
        }

        void SetLock(KF f, bool on)
        {
            Try(() => { store.SetReadOnly(f, on); Toast(ConfigStore.FileName(f) + (on ? " locked." : " unlocked.")); });
        }

        void MeasureCache(Cell cell)
        {
            var s = store;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text;
                try
                {
                    int files; long bytes = s.CacheSize(out files);
                    text = files == 0 ? "The cache is empty." : FormatBytes(bytes) + " in " + files + (files == 1 ? " file" : " files") + ". Progress and items aren't affected.";
                }
                catch { text = "Couldn't read the cache folder."; }
                try { BeginInvoke(new Action(() => { if (!cell.IsDisposed) { cell.Description = text; cell.Invalidate(); } })); } catch { }
            });
        }

        static string FormatBytes(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
            if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0") + " MB";
            return (b / 1024.0).ToString("0") + " KB";
        }

        // =============================================================================== helpers

        bool Confirm(string text)
        {
            return MessageBox.Show(this, text, "KF TOOL 26", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;
        }

        void Try(Action a)
        {
            try { a(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "KF TOOL 26", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(this, "Couldn't open " + path + "\n\n" + ex.Message, "KF TOOL 26"); }
        }

        void PickFolder()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Choose the Killing Floor 2 Config folder (or your Documents folder)", ShowNewFolderButton = false })
            {
                if (Directory.Exists(store.ConfigDir)) dlg.SelectedPath = store.ConfigDir;
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string dir = ConfigStore.NormalizePickedDir(dlg.SelectedPath);
                if (dir == null)
                {
                    MessageBox.Show(this, "That folder doesn't contain KFEngine.ini or KFGame.ini.\n\nPick Documents\\My Games\\KillingFloor2\\KFGame\\Config, or any folder above it.",
                        "KF TOOL 26", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AppSettings.Set("configDir", dir);
                OpenStore(dir);
                side.Invalidate();
                Toast("Using " + dir);
                Navigate(page == "search" || page == "review" ? Cat.Overview : page);
            }
        }

        void RestoreDialog()
        {
            var list = ConfigStore.ListBackups();
            if (list.Count == 0) { MessageBox.Show(this, "There are no backups yet.", "KF TOOL 26"); return; }
            using (var f = new Form
            {
                Text = "Restore a backup", StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, BackColor = Theme.Window, Font = Theme.Body,
                ClientSize = new Size(Theme.Px(520), Theme.Px(380))
            })
            {
                var lb = new ListBox
                {
                    BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Body,
                    IntegralHeight = false, ItemHeight = Theme.Px(24)
                };
                lb.SetBounds(Theme.Px(16), Theme.Px(52), Theme.Px(488), Theme.Px(250));
                foreach (var b in list) lb.Items.Add(b);
                lb.SelectedIndex = 0;
                var info = new TextBlock("Pick a backup. Your current files are backed up first, so this can be undone.", Theme.Small, () => Theme.Muted, Theme.Px(16), 0);
                info.SetBounds(0, 0, Theme.Px(520), Theme.Px(44));
                var ok = new Btn("Restore", BtnStyle.Primary);
                var cancel = new Btn("Cancel", BtnStyle.Secondary);
                ok.Location = new Point(Theme.Px(504) - ok.Width, Theme.Px(322));
                cancel.Location = new Point(ok.Left - Theme.Px(10) - cancel.Width, Theme.Px(322));
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                lb.DoubleClick += (s, e) => { f.DialogResult = DialogResult.OK; };
                f.Controls.AddRange(new Control[] { info, lb, ok, cancel });
                f.HandleCreated += (s, e) => Native.TitleBar(f.Handle, Theme.Dark);
                if (f.ShowDialog(this) != DialogResult.OK || lb.SelectedItem == null) return;
                var chosen = (ConfigStore.Backup)lb.SelectedItem;
                Try(() =>
                {
                    store.CreateBackup("Before restoring a backup");
                    store.RestoreBackup(chosen);
                    pending.Clear();
                    ReadCurrent();
                    Toast("Restored the backup from " + chosen.When.ToString("MMM d, h:mm tt") + ".");
                    Rebuild();
                });
            }
        }
    }
}
