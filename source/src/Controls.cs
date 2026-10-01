using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace KF2Tweaker
{
    /// <summary>Base for owner-drawn controls: double buffered, tracks hover/press, draws a focus ring.</summary>
    public class Skin : Control
    {
        protected bool Hot, Pressed;
        public Skin()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = Theme.Body;
        }
        protected override void OnMouseEnter(EventArgs e) { Hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hot = false; Pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Focus(); Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected static Graphics Smooth(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return e.Graphics;
        }

        protected void FocusRing(Graphics g, RectangleF r, float radius)
        {
            if (!Focused || !ShowFocusCues) return;
            using (var p = new Pen(Theme.AccentText, Theme.Px(2)))
            using (var path = Theme.Round(RectangleF.Inflate(r, Theme.Px(2), Theme.Px(2)), radius + Theme.Px(2)))
                g.DrawPath(p, path);
        }


        protected Color Fade(Color c) { return Enabled ? c : Theme.Mix(c, Theme.Window, 0.55f); }
    }

    // ---------------------------------------------------------------------------------------- toggle

    public sealed class ToggleSwitch : Skin
    {
        bool on;
        public event EventHandler Changed;
        public ToggleSwitch() { Size = new Size(Theme.Px(38), Theme.Px(20)); Cursor = Cursors.Hand; TabStop = true; }
        public bool Checked { get { return on; } set { if (on != value) { on = value; Invalidate(); } } }

        void Flip() { if (!Enabled) return; on = !on; Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }
        protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Flip(); base.OnMouseClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Flip(); e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(1, 1, Width - 3, Height - 3);
            Color track = on ? (Hot ? Theme.AccentHover : Theme.Accent) : (Hot ? Theme.Mix(Theme.Track, Theme.Text, 0.12f) : Theme.Track);
            using (var b = new SolidBrush(Fade(track)))
            using (var path = Theme.Round(r, r.Height / 2)) g.FillPath(b, path);
            float d = r.Height - Theme.Px(6);
            float x = on ? r.Right - d - Theme.Px(3) : r.X + Theme.Px(3);
            using (var b = new SolidBrush(Fade(on ? Theme.OnAccent : Theme.Text2)))
                g.FillEllipse(b, x, r.Y + Theme.Px(3), d, d);
            FocusRing(g, r, r.Height / 2);
        }
    }

    // ---------------------------------------------------------------------------------------- segmented

    public sealed class Segmented : Skin
    {
        readonly List<Opt> opts;
        string value;
        int hover = -1;
        public event EventHandler Changed;
        public Segmented(IEnumerable<Opt> options)
        {
            opts = options.ToList();
            Cursor = Cursors.Hand; TabStop = true;
            Height = Theme.Px(26);
        }
        public string Value { get { return value; } set { this.value = value; Invalidate(); } }

        int Index { get { return opts.FindIndex(o => IniValue.Same(o.Value, value)); } }
        RectangleF Seg(int i) { float w = (Width - 1f) / opts.Count; return new RectangleF(i * w, 0, w, Height - 1); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Math.Min(opts.Count - 1, Math.Max(0, (int)(e.X / ((Width - 1f) / opts.Count))));
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left && hover >= 0) Pick(hover); base.OnMouseClick(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int i = Index;
            if (e.KeyCode == Keys.Left) Pick(Math.Max(0, i - 1));
            if (e.KeyCode == Keys.Right) Pick(Math.Min(opts.Count - 1, i + 1));
            base.OnKeyDown(e);
        }
        void Pick(int i)
        {
            if (!Enabled || i < 0 || IniValue.Same(opts[i].Value, value)) return;
            value = opts[i].Value; Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var outer = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            float rad = Theme.Px(6);
            using (var b = new SolidBrush(Theme.Surface)) using (var p = Theme.Round(outer, rad)) g.FillPath(b, p);
            int sel = Index;
            for (int i = 0; i < opts.Count; i++)
            {
                var r = Seg(i);
                var inner = RectangleF.Inflate(r, -Theme.Px(3), -Theme.Px(3));
                if (i == sel)
                    using (var b = new SolidBrush(Fade(Theme.Accent))) using (var p = Theme.Round(inner, Theme.Px(4))) g.FillPath(b, p);
                else if (i == hover && Enabled)
                    using (var b = new SolidBrush(Theme.Hover)) using (var p = Theme.Round(inner, Theme.Px(4))) g.FillPath(b, p);
                TextRenderer.DrawText(g, opts[i].Label, i == sel ? Theme.SmallBold : Theme.Small, Rectangle.Round(r),
                    Fade(i == sel ? Theme.OnAccent : Theme.Text),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            using (var pen = new Pen(Theme.Line)) using (var p = Theme.Round(outer, rad)) g.DrawPath(pen, p);
            FocusRing(g, outer, rad);
        }
    }

    // ---------------------------------------------------------------------------------------- dropdown

    sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Raised; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color MenuItemBorder { get { return Theme.Hover; } }
        public override Color MenuItemSelected { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Hover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Hover; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Raised; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Raised; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Raised; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Line; } }
    }

    sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new MenuColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            float d = Theme.Px(7);
            using (var b = new SolidBrush(Theme.AccentText))
                g.FillEllipse(b, r.X + (r.Width - d) / 2f, r.Y + (r.Height - d) / 2f, d, d);
        }
    }

    public sealed class Dropdown : Skin
    {
        readonly List<Opt> opts;
        string value;
        public Func<string, string> LabelFor;
        public event EventHandler Changed;

        public Dropdown(IEnumerable<Opt> options)
        {
            opts = options.ToList();
            Height = Theme.Px(26); Cursor = Cursors.Hand; TabStop = true;
        }
        public string Value { get { return value; } set { this.value = value; Invalidate(); } }

        string Label
        {
            get
            {
                var o = opts.FirstOrDefault(x => x.Value == value) ?? opts.FirstOrDefault(x => IniValue.Same(x.Value, value));
                if (o != null) return o.Label;
                return LabelFor != null ? LabelFor(value) : (value ?? "");
            }
        }

        protected override void OnMouseClick(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Open(); base.OnMouseClick(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Down || k == Keys.Up || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter || (e.KeyCode == Keys.Down && e.Alt)) { Open(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                int i = opts.FindIndex(o => IniValue.Same(o.Value, value));
                i = e.KeyCode == Keys.Down ? Math.Min(opts.Count - 1, i + 1) : Math.Max(0, i - 1);
                Pick(opts[i].Value); e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        void Pick(string v)
        {
            if (IniValue.Same(v, value) && opts.Any(o => o.Value == value)) return;
            value = v; Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        void Open()
        {
            if (!Enabled) return;
            var menu = new ContextMenuStrip { Renderer = new MenuRenderer(), ShowImageMargin = true, Font = Theme.Body, ShowCheckMargin = false };
            foreach (var o in opts)
            {
                var item = new ToolStripMenuItem(o.Label) { Checked = IniValue.Same(o.Value, value), Tag = o.Value, Padding = new Padding(0, Theme.Px(3), 0, Theme.Px(3)) };
                item.Click += (s, e) => Pick((string)((ToolStripMenuItem)s).Tag);
                menu.Items.Add(item);
            }
            menu.MinimumSize = new Size(Width, 0);
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(this, new Point(0, Height + Theme.Px(2)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            float rad = Theme.Px(6);
            using (var b = new SolidBrush(Hot && Enabled ? Theme.Hover : Theme.Surface)) using (var p = Theme.Round(r, rad)) g.FillPath(b, p);
            using (var pen = new Pen(Theme.Line)) using (var p = Theme.Round(r, rad)) g.DrawPath(pen, p);
            var text = new Rectangle(Theme.Px(8), 0, Width - Theme.Px(28), Height);
            TextRenderer.DrawText(g, Label, Theme.Small, text, Fade(Theme.Text),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            // chevron
            float cx = Width - Theme.Px(14), cy = Height / 2f, s = Theme.Px(3.5f);
            using (var pen = new Pen(Fade(Theme.Muted), Theme.Px(1.6f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
            FocusRing(g, r, rad);
        }
    }

    // ---------------------------------------------------------------------------------------- slider

    public sealed class Slider : Skin
    {
        double min, max, step, val;
        int decimals;
        bool dragging;
        readonly Func<double, string> fmt;
        public event EventHandler Committed;

        public Slider(double min, double max, double step, int decimals, Func<double, string> format)
        {
            this.min = min; this.max = max; this.step = step; this.decimals = decimals; fmt = format;
            Height = Theme.Px(26); Cursor = Cursors.Hand; TabStop = true;
        }

        public double Value { get { return val; } set { val = Clamp(value); Invalidate(); } }
        public string ValueText { get { return IniValue.Num(val, decimals); } }

        double Clamp(double v)
        {
            v = Math.Max(min, Math.Min(max, v));
            if (step > 0) v = min + Math.Round((v - min) / step) * step;
            return Math.Round(Math.Max(min, Math.Min(max, v)), Math.Max(decimals, 0));
        }

        int LabelWidth { get { return TextRenderer.MeasureText(fmt != null ? fmt(max) : IniValue.Num(max, decimals), Theme.Small).Width + Theme.Px(4); } }
        RectangleF Track { get { float pad = Theme.Px(7); return new RectangleF(pad, Height / 2f - Theme.Px(2), Width - LabelWidth - pad * 2, Theme.Px(4)); } }

        void SetFromX(int x)
        {
            var t = Track;
            double f = Math.Max(0, Math.Min(1, (x - t.X) / t.Width));
            double nv = Clamp(min + f * (max - min));
            if (nv != val) { val = nv; Invalidate(); }
        }

        void Commit() { if (Committed != null) Committed(this, EventArgs.Empty); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled || e.X > Width - LabelWidth) return;
            dragging = true; Capture = true; SetFromX(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false; Capture = false; Commit();
        }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            double s = step > 0 ? step : (max - min) / 100;
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value = val - s; Commit(); e.Handled = true; }
            if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value = val + s; Commit(); e.Handled = true; }
            if (e.KeyCode == Keys.Home) { Value = min; Commit(); }
            if (e.KeyCode == Keys.End) { Value = max; Commit(); }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var t = Track;
            double f = max > min ? (val - min) / (max - min) : 0;
            float fx = t.X + (float)(f * t.Width);
            using (var b = new SolidBrush(Fade(Theme.Track))) using (var p = Theme.Round(t, t.Height / 2)) g.FillPath(b, p);
            var filled = new RectangleF(t.X, t.Y, Math.Max(t.Height, fx - t.X), t.Height);
            using (var b = new SolidBrush(Fade(Theme.Accent))) using (var p = Theme.Round(filled, t.Height / 2)) g.FillPath(b, p);
            float kd = Theme.Px(Hot || dragging ? 14 : 12);
            var knob = new RectangleF(fx - kd / 2, Height / 2f - kd / 2, kd, kd);
            using (var b = new SolidBrush(Fade(Theme.Accent))) g.FillEllipse(b, knob);
            using (var b = new SolidBrush(Fade(Theme.Window))) g.FillEllipse(b, RectangleF.Inflate(knob, -Theme.Px(4), -Theme.Px(4)));
            if (Focused) using (var pen = new Pen(Theme.AccentText, Theme.Px(2))) g.DrawEllipse(pen, RectangleF.Inflate(knob, Theme.Px(3), Theme.Px(3)));
            string label = fmt != null ? fmt(val) : IniValue.Num(val, decimals);
            TextRenderer.DrawText(g, label, Theme.Small, new Rectangle(Width - LabelWidth, 0, LabelWidth, Height), Fade(Theme.Text),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    // ---------------------------------------------------------------------------------------- number box

    public sealed class NumberBox : Skin
    {
        readonly TextBox box;
        readonly double min, max;
        readonly int decimals;
        readonly string suffix;
        string committed = "";
        public event EventHandler Committed;

        public NumberBox(double min, double max, int decimals, string suffix)
        {
            this.min = min; this.max = max; this.decimals = decimals; this.suffix = suffix;
            Height = Theme.Px(26);
            box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Small, BackColor = Theme.Surface, ForeColor = Theme.Text, TextAlign = HorizontalAlignment.Right };
            Controls.Add(box);
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; box.Text = committed; }
            };
            box.Leave += (s, e) => Commit();
            box.GotFocus += (s, e) => Invalidate();
            box.LostFocus += (s, e) => Invalidate();
        }

        public string Value
        {
            get { return committed; }
            set { committed = Format(value); box.Text = committed; }
        }

        string Format(string v)
        {
            double d;
            if (!IniValue.TryNumber(v, out d)) return v ?? "";
            return decimals == 0 ? ((long)Math.Round(d)).ToString(IniValue.Inv) : d.ToString("0." + new string('#', decimals), IniValue.Inv);
        }

        void Commit()
        {
            double d;
            string txt = box.Text.Trim().Replace(',', '.');
            if (!IniValue.TryNumber(txt, out d)) { box.Text = committed; return; }
            d = Math.Max(min, Math.Min(max, d));
            string f = Format(IniValue.Num(d, decimals));
            box.Text = f;
            if (f == committed) return;
            committed = f;
            if (Committed != null) Committed(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e) { if (box != null) box.Enabled = Enabled; base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { if (box != null) box.Focus(); base.OnGotFocus(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (box == null) return;
            int sw = string.IsNullOrEmpty(suffix) ? 0 : TextRenderer.MeasureText(suffix, Theme.Small).Width + Theme.Px(4);
            box.SetBounds(Theme.Px(10), (Height - box.PreferredHeight) / 2, Width - Theme.Px(20) - sw, box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            float rad = Theme.Px(6);
            using (var b = new SolidBrush(Theme.Surface)) using (var p = Theme.Round(r, rad)) g.FillPath(b, p);
            bool bf = box != null && box.Focused;
            using (var pen = new Pen(bf ? Theme.AccentText : Theme.Line, bf ? Theme.Px(1.5f) : 1)) using (var p = Theme.Round(r, rad)) g.DrawPath(pen, p);
            if (!string.IsNullOrEmpty(suffix))
                TextRenderer.DrawText(g, suffix, Theme.Small, new Rectangle(0, 0, Width - Theme.Px(10), Height), Theme.Muted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    // ---------------------------------------------------------------------------------------- radio list

    /// <summary>Radio list with a one-line note under each option, laid out in columns.</summary>
    public sealed class RadioGrid : Skin
    {
        readonly List<Opt> opts;
        readonly Dictionary<string, string> notes;
        string value;
        int hover = -1;
        public event EventHandler Changed;

        public RadioGrid(IEnumerable<Opt> options, Dictionary<string, string> notes)
        {
            opts = options.ToList(); this.notes = notes ?? new Dictionary<string, string>();
            Cursor = Cursors.Hand; TabStop = true;
            Height = HeightFor(Theme.Px(600));
        }

        public string Value { get { return value; } set { this.value = value; Invalidate(); } }

        static int ItemH { get { return Theme.Px(38); } }
        int Cols(int w) { return 1; } // one list, like the original Gore Control screen
        int Rows(int w) { return (opts.Count + Cols(w) - 1) / Cols(w); }
        public int HeightFor(int w) { return Rows(w) * ItemH; }

        Rectangle Item(int i)
        {
            int cols = Cols(Width), rows = Rows(Width), cw = Width / cols;
            int col = i / rows, row = i % rows;   // fill down each column, like the original list
            return new Rectangle(col * cw, row * ItemH, cw, ItemH);
        }

        int HitTest(Point p) { for (int i = 0; i < opts.Count; i++) if (Item(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e) { int h = HitTest(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { int h = HitTest(e.Location); if (e.Button == MouseButtons.Left && h >= 0) Pick(h); base.OnMouseClick(e); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Up || k == Keys.Down || k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            int i = opts.FindIndex(o => o.Value == value);
            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Right) Pick(Math.Min(opts.Count - 1, i + 1));
            if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Left) Pick(Math.Max(0, i - 1));
            base.OnKeyDown(e);
        }

        void Pick(int i)
        {
            if (!Enabled || i < 0 || opts[i].Value == value) return;
            value = opts[i].Value; Invalidate();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        const TextFormatFlags One = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            for (int i = 0; i < opts.Count; i++)
            {
                var r = Item(i);
                bool sel = opts[i].Value == value;
                var box = new RectangleF(r.X + Theme.Px(2), r.Y + Theme.Px(2), r.Width - Theme.Px(6), r.Height - Theme.Px(4));
                if (sel || (i == hover && Enabled))
                    using (var b = new SolidBrush(sel ? Theme.Mix(Theme.Panel, Theme.Hover, 0.8f) : Theme.Mix(Theme.Panel, Theme.Hover, 0.45f)))
                    using (var p = Theme.Round(box, Theme.Px(3))) g.FillPath(b, p);
                float d = Theme.Px(14), cx = box.X + Theme.Px(8), cy = box.Y + Theme.Px(8);
                using (var pen = new Pen(Fade(sel ? Theme.AccentHover : Theme.Muted), Theme.Px(1.5f))) g.DrawEllipse(pen, cx, cy, d, d);
                if (sel) using (var b = new SolidBrush(Fade(Theme.AccentHover))) g.FillEllipse(b, cx + Theme.Px(3.5f), cy + Theme.Px(3.5f), d - Theme.Px(7), d - Theme.Px(7));
                int tx = (int)(cx + d + Theme.Px(8)), tw = (int)(box.Right - tx - Theme.Px(6));
                TextRenderer.DrawText(g, opts[i].Label, Theme.Body, new Rectangle(tx, (int)box.Y + Theme.Px(4), tw, Theme.Px(17)),
                    Fade(sel ? Theme.Text : Theme.Text2), One);
                string note;
                if (notes.TryGetValue(opts[i].Value, out note))
                    TextRenderer.DrawText(g, note, Theme.Small, new Rectangle(tx, (int)box.Y + Theme.Px(21), tw, Theme.Px(15)), Fade(Theme.Muted), One);
            }
            if (Focused && ShowFocusCues)
            {
                int i = opts.FindIndex(o => o.Value == value);
                if (i >= 0) FocusRing(g, RectangleF.Inflate(Item(i), -Theme.Px(3), -Theme.Px(3)), Theme.Px(3));
            }
        }
    }

    // ---------------------------------------------------------------------------------------- buttons & nav

    public enum BtnStyle { Primary, Secondary, Ghost, Danger }

    public sealed class Btn : Skin
    {
        public BtnStyle Style;
        public Btn(string text, BtnStyle style)
        {
            Text = text; Style = style; Cursor = Cursors.Hand; TabStop = true;
            Font = Theme.Small;
            Height = Theme.Px(28);
            Fit();
        }
        string Label { get { return (Text ?? "").ToUpperInvariant(); } }
        void Fit() { Width = TextRenderer.MeasureText(Label, Font, Size.Empty, TextFormatFlags.NoPrefix).Width + Theme.Px(Style == BtnStyle.Ghost ? 12 : 24); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; } base.OnKeyDown(e); }
        protected override void OnTextChanged(EventArgs e) { if (Font != null) Fit(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            float rad = Theme.Px(3);
            Color fill, text, border = Color.Empty;
            switch (Style)
            {
                case BtnStyle.Primary: fill = Hot ? Theme.Mix(Theme.BtnRed, Theme.AccentHover, 0.45f) : Theme.BtnRed; text = Theme.OnAccent; border = Theme.BtnRedLine; break;
                case BtnStyle.Danger: fill = Hot ? Theme.Mix(Theme.Panel, Theme.Danger, 0.18f) : Theme.Panel; text = Theme.Danger; border = Theme.Mix(Theme.Line, Theme.Danger, 0.5f); break;
                case BtnStyle.Ghost: fill = Hot ? Theme.Hover : Color.Transparent; text = Theme.AccentText; break;
                default: fill = Hot ? Theme.Hover : Theme.Mix(Theme.Panel, Theme.Hover, 0.45f); text = Theme.Text2; border = Theme.Line; break;
            }
            if (Pressed) fill = Theme.Mix(fill, Theme.Window, 0.25f);
            if (!Enabled)
            {
                fill = Style == BtnStyle.Ghost ? Color.Transparent : C1817; // PRESTIGE-button grey
                text = Theme.Mix(Theme.Faint, Theme.Window, 0.3f);
                border = Style == BtnStyle.Ghost ? Color.Empty : Theme.Mix(Theme.Line, Theme.Window, 0.5f);
            }
            if (fill.A > 0) using (var b = new SolidBrush(fill)) using (var p = Theme.Round(r, rad)) g.FillPath(b, p);
            if (!border.IsEmpty) using (var pen = new Pen(border)) using (var p = Theme.Round(r, rad)) g.DrawPath(pen, p);
            TextRenderer.DrawText(g, Label, Font, Rectangle.Round(r), text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            FocusRing(g, r, rad);
        }
        static readonly Color C1817 = Color.FromArgb(0x1B, 0x18, 0x17);
    }

    /// <summary>Sidebar tab styled like a KF2 "Select perk" row: red icon tab, dark body, chamfered corners, number on the right.</summary>
    public sealed class NavItem : Skin
    {
        bool selected;
        int badge;
        public string Icon = "";
        public string Number = "";
        public NavItem(string text) { Text = text; Height = Theme.Px(40); Cursor = Cursors.Hand; TabStop = true; }
        public bool Selected { get { return selected; } set { selected = value; Invalidate(); } }
        public int Badge { get { return badge; } set { if (badge != value) { badge = value; Invalidate(); } } }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }

        // colours sampled from the game's perk list
        static readonly Color TabSel = Color.FromArgb(0x7A, 0x04, 0x00), TabIdle = Color.FromArgb(0x4D, 0x03, 0x01),
                              BodySel = Color.FromArgb(0x4D, 0x03, 0x01), BodyIdle = Color.FromArgb(0x06, 0x05, 0x05),
                              IconC = Color.FromArgb(0xFF, 0xD7, 0xC3), LabelIdle = Color.FromArgb(0xCC, 0xBF, 0xAF), LabelSel = Color.FromArgb(0xFF, 0xE7, 0xD2);

        static GraphicsPath Poly(params PointF[] pts) { var p = new GraphicsPath(); p.AddPolygon(pts); return p; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            float m = Theme.Px(8), x = m, w = Width - m * 2, h = Height - 1;
            float c = Theme.Px(4.5f), slant = Theme.Px(5), tabW = (float)Math.Round(h * 1.05f), gap = Theme.Px(2);
            float bx = x + tabW + gap, r = x + w;

            // body
            Color body = selected ? BodySel : (Hot ? Theme.Mix(BodyIdle, Color.FromArgb(0x3A, 0x0E, 0x0B), 0.55f) : BodyIdle);
            using (var path = Poly(new PointF(bx, 0), new PointF(r - c, 0), new PointF(r, c), new PointF(r, h - c), new PointF(r - c, h), new PointF(bx - slant, h)))
            using (var b = new SolidBrush(Fade(body))) g.FillPath(b, path);
            // tab
            Color tab = selected ? TabSel : (Hot ? Theme.Mix(TabIdle, TabSel, 0.5f) : TabIdle);
            using (var path = Poly(new PointF(x + c, 0), new PointF(x + tabW, 0), new PointF(x + tabW - slant, h), new PointF(x + c, h), new PointF(x, h - c), new PointF(x, c)))
            using (var b = new SolidBrush(Fade(tab))) g.FillPath(b, path);

            DrawIcon(g, Icon, new RectangleF(x + (tabW - slant / 2 - Theme.Px(16)) / 2f, (h - Theme.Px(16)) / 2f, Theme.Px(16), Theme.Px(16)), Fade(IconC));

            // number on the right, label after the tab
            float numW = 0;
            if (Number.Length > 0)
            {
                var ns = TextRenderer.MeasureText(Number, Theme.Heading, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                numW = ns.Width;
                TextRenderer.DrawText(g, Number, Theme.Heading, new Rectangle((int)(r - Theme.Px(12) - ns.Width), 0, ns.Width + 2, (int)h), Fade(selected ? LabelSel : LabelIdle),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            float right = r - Theme.Px(12) - (numW > 0 ? numW + Theme.Px(8) : 0);
            if (badge > 0)
            {
                string t = badge.ToString();
                var sz = TextRenderer.MeasureText(t, Theme.Small, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                float bw = Math.Max(Theme.Px(17), sz.Width + Theme.Px(9)), bh = Theme.Px(16);
                var br = new RectangleF(right - bw, (h - bh) / 2f, bw, bh);
                using (var b = new SolidBrush(Theme.AccentText)) using (var p = Theme.Round(br, bh / 2)) g.FillPath(b, p);
                TextRenderer.DrawText(g, t, Theme.Small, Rectangle.Round(br), Color.FromArgb(0x2A, 0x08, 0x04),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                right = br.X - Theme.Px(6);
            }
            int lx = (int)(bx + Theme.Px(10));
            TextRenderer.DrawText(g, Text, Theme.Body, new Rectangle(lx, 0, (int)Math.Max(10, right - lx), (int)h), Fade(selected ? LabelSel : LabelIdle),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

            if (Focused && ShowFocusCues)
                using (var pen = new Pen(Theme.AccentText, Theme.Px(1.5f)))
                using (var path = Poly(new PointF(x + c, 1), new PointF(r - c, 1), new PointF(r - 1, c), new PointF(r - 1, h - c), new PointF(r - c, h - 1), new PointF(x + c, h - 1), new PointF(x + 1, h - c), new PointF(x + 1, c)))
                    g.DrawPath(pen, path);
        }

        /// <summary>Small line icons, drawn in a 16 x 16 box.</summary>
        static void DrawIcon(Graphics g, string kind, RectangleF b, Color col)
        {
            float u = b.Width / 16f, x = b.X, y = b.Y;
            Func<float, float> X = v => x + v * u;
            Func<float, float> Y = v => y + v * u;
            using (var pen = new Pen(col, Math.Max(1.3f, 1.5f * u)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var fill = new SolidBrush(col))
            {
                switch (kind)
                {
                    case "overview":
                        foreach (var q in new[] { new PointF(1.5f, 1.5f), new PointF(9f, 1.5f), new PointF(1.5f, 9f), new PointF(9f, 9f) })
                            g.FillRectangle(fill, X(q.X), Y(q.Y), 5.5f * u, 5.5f * u);
                        break;
                    case "display":
                        g.DrawRectangle(pen, X(1.5f), Y(2.5f), 13 * u, 8.5f * u);
                        g.DrawLine(pen, X(5.5f), Y(14f), X(10.5f), Y(14f));
                        g.DrawLine(pen, X(8f), Y(11f), X(8f), Y(14f));
                        break;
                    case "graphics":   // sun
                        g.DrawEllipse(pen, X(4.5f), Y(4.5f), 7 * u, 7 * u);
                        for (int k = 0; k < 8; k++)
                        {
                            double a = Math.PI / 4 * k;
                            g.DrawLine(pen, X(8 + (float)Math.Cos(a) * 5.4f), Y(8 + (float)Math.Sin(a) * 5.4f), X(8 + (float)Math.Cos(a) * 7.4f), Y(8 + (float)Math.Sin(a) * 7.4f));
                        }
                        break;
                    case "performance": // gauge
                        g.DrawArc(pen, X(1.5f), Y(2.5f), 13 * u, 13 * u, 160, 220);
                        g.DrawLine(pen, X(8f), Y(9f), X(11.5f), Y(5.2f));
                        g.FillEllipse(fill, X(6.6f), Y(7.6f), 2.8f * u, 2.8f * u);
                        break;
                    case "gore":       // drop
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(X(8), Y(1.5f), X(12.5f), Y(7f), X(13f), Y(9f), X(13f), Y(10f));
                            p.AddBezier(X(13), Y(10f), X(13f), Y(12.8f), X(10.8f), Y(14.5f), X(8f), Y(14.5f));
                            p.AddBezier(X(8), Y(14.5f), X(5.2f), Y(14.5f), X(3f), Y(12.8f), X(3f), Y(10f));
                            p.AddBezier(X(3), Y(10f), X(3f), Y(9f), X(3.5f), Y(7f), X(8f), Y(1.5f));
                            g.FillPath(fill, p);
                        }
                        break;
                    case "audio":      // speaker
                        g.FillPolygon(fill, new[] { new PointF(X(1.5f), Y(6f)), new PointF(X(5f), Y(6f)), new PointF(X(9f), Y(2.5f)), new PointF(X(9f), Y(13.5f)), new PointF(X(5f), Y(10f)), new PointF(X(1.5f), Y(10f)) });
                        g.DrawArc(pen, X(8.5f), Y(4.5f), 5 * u, 7 * u, -60, 120);
                        g.DrawArc(pen, X(9.5f), Y(1.8f), 6 * u, 12.4f * u, -55, 110);
                        break;
                    case "input":      // mouse
                        g.DrawArc(pen, X(3.5f), Y(1.5f), 9 * u, 9 * u, 180, 180);
                        g.DrawLine(pen, X(3.5f), Y(6f), X(3.5f), Y(10.5f));
                        g.DrawLine(pen, X(12.5f), Y(6f), X(12.5f), Y(10.5f));
                        g.DrawArc(pen, X(3.5f), Y(6.5f), 9 * u, 8 * u, 0, 180);
                        g.DrawLine(pen, X(8f), Y(1.8f), X(8f), Y(6.3f));
                        break;
                    case "hud":        // crosshair
                        g.DrawEllipse(pen, X(3.5f), Y(3.5f), 9 * u, 9 * u);
                        g.DrawLine(pen, X(8f), Y(0.8f), X(8f), Y(5f)); g.DrawLine(pen, X(8f), Y(11f), X(8f), Y(15.2f));
                        g.DrawLine(pen, X(0.8f), Y(8f), X(5f), Y(8f)); g.DrawLine(pen, X(11f), Y(8f), X(15.2f), Y(8f));
                        break;
                    case "tools":      // wrench
                        g.DrawLine(pen, X(3f), Y(13f), X(9f), Y(7f));
                        g.DrawArc(pen, X(7.5f), Y(1.5f), 7 * u, 7 * u, 100, 300);
                        break;
                }
            }
        }
    }

    public sealed class SearchBox : Skin
    {
        public readonly TextBox Box;
        readonly string placeholder;
        public SearchBox(string placeholder)
        {
            this.placeholder = placeholder;
            Height = Theme.Px(30);
            Box = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Body, BackColor = Theme.Surface, ForeColor = Theme.Text, Visible = false };
            Controls.Add(Box);
            Box.TextChanged += (s, e) => Invalidate();
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => { if (Box.Text.Length == 0) Box.Visible = false; Invalidate(); };
            Cursor = Cursors.IBeam;
            TabStop = true;
        }
        public void FocusBox() { Box.Visible = true; Box.Focus(); Box.SelectAll(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); FocusBox(); }
        protected override void OnClick(EventArgs e) { FocusBox(); base.OnClick(e); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Box == null) return;
            Box.SetBounds(Theme.Px(32), (Height - Box.PreferredHeight) / 2, Width - Theme.Px(42), Box.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Smooth(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            bool focused = Box != null && Box.Focused;
            using (var b = new SolidBrush(Theme.Surface)) using (var p = Theme.Round(r, Theme.Px(6))) g.FillPath(b, p);
            using (var pen = new Pen(focused ? Theme.AccentText : Theme.Line, focused ? Theme.Px(1.5f) : 1))
            using (var p = Theme.Round(r, Theme.Px(6))) g.DrawPath(pen, p);
            float cx = Theme.Px(16), cy = Height / 2f - Theme.Px(1), rad = Theme.Px(5);
            using (var pen = new Pen(Theme.Muted, Theme.Px(1.6f)) { EndCap = LineCap.Round })
            {
                g.DrawEllipse(pen, cx - rad, cy - rad, rad * 2, rad * 2);
                g.DrawLine(pen, cx + rad * 0.7f, cy + rad * 0.7f, cx + rad * 1.6f, cy + rad * 1.6f);
            }
            if (Box != null && !Box.Visible)
                TextRenderer.DrawText(g, placeholder + "  (Ctrl+F)", Theme.Body, new Rectangle(Theme.Px(31), 0, Width, Height), Theme.Faint,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>Buttons laid out right-to-left for action rows.</summary>
    public sealed class BtnGroup : Control
    {
        public BtnGroup(params Control[] items)
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            foreach (var i in items) Controls.Add(i);
            Height = items.Length == 0 ? 0 : items.Max(i => i.Height);
        }
        public int Needed { get { int w = 0; foreach (Control c in Controls) w += c.Width + Theme.Px(6); return Math.Max(0, w - Theme.Px(6)); } }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int x = Width;
            for (int i = Controls.Count - 1; i >= 0; i--)
            {
                var c = Controls[i];
                x -= c.Width;
                c.Location = new Point(x, (Height - c.Height) / 2);
                x -= Theme.Px(6);
            }
        }
    }

    // ---------------------------------------------------------------------------------------- rows & layout

    /// <summary>
    /// Compact setting cell: one line with the title on the left and the control on the right.
    /// Optional second line for a short description. Full details live in the tooltip and the info bar.
    /// </summary>
    public class Cell : Control
    {
        public string Title, Description, Info;
        public Level NoteLevel;
        public bool ShowDesc, Changed, ShowReset, Disabled, Half = true, EditorBelow;
        public Control Editor;
        public int EditorWidth;
        public event EventHandler ResetClicked;
        Rectangle resetRect;
        bool hot, resetHot;

        public Cell(string title, string description, Control editor, int editorWidth)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Title = title; Description = description; Editor = editor; EditorWidth = editorWidth;
            if (editor != null) Controls.Add(editor);
            Height = RowHeight(false);
        }

        public static int RowHeight(bool desc) { return Theme.Px(desc ? 42 : 31); }
        int Head { get { return Theme.Px(30); } }
        public void Measure()
        {
            if (EditorBelow && Editor != null)
            {
                var rg = Editor as RadioGrid;
                if (rg != null) Editor.Height = rg.HeightFor(Width - PadX * 2 + Theme.Px(4));
                Height = Head + Editor.Height + Theme.Px(6);
                return;
            }
            Height = RowHeight(ShowDesc && !string.IsNullOrEmpty(Description));
        }

        int PadX { get { return Theme.Px(10); } }
        int EditorW { get { return Editor == null || EditorBelow ? 0 : Math.Min(EditorWidth, Width - Theme.Px(140)); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Editor == null) return;
            if (EditorBelow) Editor.SetBounds(PadX - Theme.Px(2), Head, Width - PadX * 2 + Theme.Px(4), Editor.Height);
            else Editor.SetBounds(Width - PadX - EditorW, (Height - Editor.Height) / 2, EditorW, Editor.Height);
            Editor.Enabled = !Disabled;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; resetHot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool r = ShowReset && !Disabled && resetRect.Contains(e.Location);
            if (r != resetHot) { resetHot = r; Cursor = r ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (ShowReset && !Disabled && resetRect.Contains(e.Location) && ResetClicked != null) ResetClicked(this, EventArgs.Empty);
            base.OnMouseClick(e);
        }

        const TextFormatFlags One = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;

        void DrawReset(Graphics g, Rectangle rr)
        {
            int d = rr.Width;
            if (resetHot) using (var b = new SolidBrush(Theme.Hover)) g.FillEllipse(b, rr);
            var c = new RectangleF(rr.X + d * 0.27f, rr.Y + d * 0.27f, d * 0.46f, d * 0.46f);
            using (var pen = new Pen(resetHot ? Theme.AccentText : Theme.Muted, Theme.Px(1.5f)) { StartCap = LineCap.Round })
            {
                g.DrawArc(pen, c, -60, 290);
                float ax = c.X + c.Width * 0.75f, ay = c.Y + c.Height * 0.07f;
                g.DrawLine(pen, ax, ay, ax + Theme.Px(3.5f), ay - Theme.Px(0.5f));
                g.DrawLine(pen, ax, ay, ax + Theme.Px(0.8f), ay + Theme.Px(3.2f));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(hot ? Theme.Mix(Theme.Panel, Theme.Hover, 0.55f) : Theme.Panel))
            using (var p = Theme.Round(new RectangleF(0, 0, Width - 1, Height - 1), Theme.Px(3))) g.FillPath(b, p);
            int x = PadX;
            int right = Width - PadX - EditorW - Theme.Px(ShowReset ? 30 : 10);
            if (NoteLevel == Level.Warn || NoteLevel == Level.Danger)
            {
                using (var b = new SolidBrush(NoteLevel == Level.Danger ? Theme.Danger : Theme.Warn))
                    g.FillEllipse(b, x, (EditorBelow ? Head / 2 : ShowDesc && !string.IsNullOrEmpty(Description) ? Theme.Px(15) : Height / 2) - Theme.Px(3), Theme.Px(6), Theme.Px(6));
                x += Theme.Px(12);
            }
            bool two = ShowDesc && !string.IsNullOrEmpty(Description) && !EditorBelow;
            if (EditorBelow)
            {
                var tr = new Rectangle(x, 0, Width - x - Theme.Px(40), Head);
                TextRenderer.DrawText(g, Title, Theme.Body, tr, Disabled ? Theme.Faint : Theme.Text, One);
                if (!string.IsNullOrEmpty(Description))
                {
                    int tw = TextRenderer.MeasureText(Title, Theme.Body, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                    TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(x + tw + Theme.Px(14), 0, Width - x - tw - Theme.Px(70), Head), Theme.Muted, One);
                }
                if (Changed) using (var b = new SolidBrush(Theme.AccentText)) g.FillRectangle(b, 0, Theme.Px(5), Theme.Px(3), Height - Theme.Px(10));
                resetRect = Rectangle.Empty;
                if (ShowReset && !Disabled)
                {
                    int d = Theme.Px(20);
                    resetRect = new Rectangle(Width - PadX - d, (Head - d) / 2, d, d);
                    DrawReset(g, resetRect);
                }
                return;
            }
            var titleR = two ? new Rectangle(x, Theme.Px(4), right - x, Theme.Px(18)) : new Rectangle(x, 0, right - x, Height);
            TextRenderer.DrawText(g, Title, Theme.Body, titleR, Disabled ? Theme.Faint : Theme.Text2, One);
            if (two)
                TextRenderer.DrawText(g, Description, Theme.Small, new Rectangle(PadX, Theme.Px(22), right - PadX, Theme.Px(16)), Disabled ? Theme.Faint : Theme.Muted, One);
            if (Changed)
                using (var b = new SolidBrush(Theme.AccentText)) g.FillRectangle(b, 0, Theme.Px(5), Theme.Px(3), Height - Theme.Px(10));
            resetRect = Rectangle.Empty;
            if (ShowReset && !Disabled && Editor != null)
            {
                int d = Theme.Px(20);
                resetRect = new Rectangle(Editor.Left - d - Theme.Px(6), (Height - d) / 2, d, d);
                DrawReset(g, resetRect);
            }
        }
    }

    /// <summary>KF-style section header: upper-case label followed by a thin rule.</summary>
    public sealed class SectionHead : Control
    {
        public SectionHead(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text; Height = Theme.Px(27);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Window);
            string t = Text.ToUpperInvariant();
            var sz = TextRenderer.MeasureText(t, Theme.Heading, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int y = Height - sz.Height - Theme.Px(4);
            TextRenderer.DrawText(g, t, Theme.Heading, new Point(Theme.Px(2), y), Theme.Text2, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int ly = y + sz.Height / 2 + 1;
            using (var pen = new Pen(Theme.Rule)) g.DrawLine(pen, Theme.Px(2) + sz.Width + Theme.Px(10), ly, Width - Theme.Px(2), ly);
        }
    }

    /// <summary>Plain text block (page titles, group headings, paragraphs).</summary>
    public sealed class TextBlock : Control
    {
        readonly Font font; readonly Func<Color> color; readonly int top, bottom;
        public TextBlock(string text, Font f, Func<Color> c, int padTop, int padBottom)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Text = text; font = f; color = c; top = padTop; bottom = padBottom;
        }
        int TextWidth { get { return Math.Max(10, Width - Theme.Px(20)); } }
        const TextFormatFlags F = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
        public void Measure()
        {
            Height = top + bottom + TextRenderer.MeasureText(Text, font, new Size(TextWidth, int.MaxValue), F).Height;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Window);
            TextRenderer.DrawText(e.Graphics, Text, font, new Rectangle(Theme.Px(2), top, TextWidth, Height), color(), F);
        }
    }

    /// <summary>Vertical stack that sizes children to its width and scrolls.</summary>
    public sealed class Stack : Panel
    {
        public int MaxContentWidth = 0;
        public Stack()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            AutoScroll = true;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (HorizontalScroll.Visible) { HorizontalScroll.Visible = false; HorizontalScroll.Enabled = false; }
        }

        public void Relayout()
        {
            SuspendLayout();
            int w = Width - SystemInformation.VerticalScrollBarWidth - Theme.Px(20);
            if (MaxContentWidth > 0) w = Math.Min(w, MaxContentWidth);
            int x0 = Theme.Px(8), gap = Theme.Px(8);
            bool twoCol = w >= Theme.Px(700);
            int y = Theme.Px(4);
            int sy = AutoScrollPosition.Y;
            var list = Controls.Cast<Control>().Where(c => c.Visible).ToList();
            for (int i = 0; i < list.Count; )
            {
                var cell = list[i] as Cell;
                if (cell != null && cell.Half)
                {
                    var run = new List<Cell>();
                    while (i < list.Count && list[i] is Cell && ((Cell)list[i]).Half) { run.Add((Cell)list[i]); i++; }
                    foreach (var c in run) c.Measure();
                    int h = run.Max(c => c.Height) + Theme.Px(3); // pitch includes the gap between tiles
                    int cols = twoCol ? 2 : 1, rows = (run.Count + cols - 1) / cols;
                    int colW = cols == 2 ? (w - gap) / 2 : w;
                    for (int k = 0; k < run.Count; k++)
                    {
                        int col = k / rows, row = k % rows;
                        run[k].SetBounds(x0 + col * (colW + gap), y + sy + row * h, colW, h - Theme.Px(3));
                    }
                    y += rows * h;
                    continue;
                }
                var ctl = list[i++];
                ctl.Width = w;
                var c2 = ctl as Cell; if (c2 != null) c2.Measure();
                var tb = ctl as TextBlock; if (tb != null) tb.Measure();
                ctl.Location = new Point(x0, y + sy);
                y += ctl.Height;
            }
            AutoScrollMinSize = new Size(0, y + Theme.Px(12));
            ResumeLayout();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Relayout(); }

        public void ScrollByWheel(int delta)
        {
            int step = Math.Max(1, SystemInformation.MouseWheelScrollLines) * Theme.Px(22);
            int y = -AutoScrollPosition.Y - delta * step / 120;
            AutoScrollPosition = new Point(0, Math.Max(0, y));
        }
        protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Theme.Window); }
    }
}
