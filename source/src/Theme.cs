using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KF2Tweaker
{
    public static class Theme
    {
        public static bool Dark = true; // KF2 look is dark only
        public static float Scale = 1f;

        public static Color Window, Surface, Raised, Hover, Line, Text, Text2, Muted, Faint, Accent, AccentHover, OnAccent, AccentSoft, AccentText,
                            Danger, Warn, Good, Track, Panel, PanelHi, BtnRed, BtnRedLine, Rule;

        public static Font Body, BodyBold, Small, SmallBold, Title, Heading, Mono, Brand, BrandSmall;
        public static string FamilyName = "";

        /// <summary>Colours sampled from the Killing Floor 2 perk screen.</summary>
        public static void Apply(bool dark)
        {
            Dark = true;
            Window = C("#110F0F");      // near-black backdrop
            Panel = C("#2C2A2A");       // setting tiles (loadout tiles)
            PanelHi = C("#323131");     // sidebar and header panels (#323232 in game)
            Surface = C("#1D1B1B");     // input wells
            Raised = C("#2A2827");      // menus
            Hover = C("#3A3736");
            Line = C("#4A4643");
            Rule = C("#4F4A46");
            Text = C("#ECE6DE");        // big numbers / titles
            Text2 = C("#CDBFAE");       // skill-list beige
            Muted = C("#9C9189");
            Faint = C("#6E6660");
            Accent = C("#8E1D17");      // KF blood red (toggles, sliders, selection)
            AccentHover = C("#A82A21");
            OnAccent = C("#FFE4D0");    // CONFIGURE button text
            AccentSoft = C("#3B1210");  // XP bar red, darkened
            AccentText = C("#F29B69");  // perk-icon orange
            BtnRed = C("#5A0F0C");      // CONFIGURE button
            BtnRedLine = C("#8A2319");
            Danger = C("#E8604E"); Warn = C("#EE8A4E"); Good = C("#7DB36A"); Track = C("#474442");
        }

        static Color C(string hex) { return ColorTranslator.FromHtml(hex); }

        // ---- fonts: Century Gothic if installed, else the bundled Questrial (OFL), else Segoe UI

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
        static readonly System.Drawing.Text.PrivateFontCollection pfc = new System.Drawing.Text.PrivateFontCollection();

        static FontFamily LoadBundled()
        {
            try
            {
                using (var st = typeof(Theme).Assembly.GetManifestResourceStream("Questrial.ttf"))
                {
                    if (st == null) return null;
                    var data = new byte[st.Length];
                    st.Read(data, 0, data.Length);
                    IntPtr mem = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length); // kept for the app's lifetime
                    System.Runtime.InteropServices.Marshal.Copy(data, 0, mem, data.Length);
                    if (Native.IsWindows) { uint n = 0; AddFontMemResourceEx(mem, (uint)data.Length, IntPtr.Zero, ref n); } // lets GDI text use it
                    pfc.AddMemoryFont(mem, data.Length);
                    return pfc.Families.Length > 0 ? pfc.Families[0] : null;
                }
            }
            catch { return null; }
        }

        static Font F(FontFamily fam, float size, FontStyle style)
        {
            try { return new Font(fam, size, style); } catch { return new Font(fam, size, FontStyle.Regular); }
        }

        public static void InitFonts()
        {
            FontFamily fam = null;
            if (Has("Century Gothic")) fam = new FontFamily("Century Gothic");
            if (fam == null && Has("Questrial")) fam = new FontFamily("Questrial"); // user-installed copy
            if (fam == null && Native.IsWindows) fam = LoadBundled();
            if (fam == null) fam = new FontFamily(Has("Segoe UI") ? "Segoe UI" : SystemFonts.MessageBoxFont.FontFamily.Name);
            FamilyName = fam.Name;
            string mono = Has("Cascadia Mono") ? "Cascadia Mono" : (Has("Consolas") ? "Consolas" : FontFamily.GenericMonospace.Name);

            // KF2 uses regular weights almost everywhere; hierarchy comes from size and colour.
            Body = F(fam, 9.5f, FontStyle.Regular);
            BodyBold = Body;
            Small = F(fam, 8.5f, FontStyle.Regular);
            SmallBold = Small;
            Heading = F(fam, 10f, FontStyle.Regular);
            Title = F(fam, 17f, FontStyle.Regular);
            Brand = F(fam, 16f, FontStyle.Regular);
            BrandSmall = F(fam, 8.5f, FontStyle.Regular);
            Mono = new Font(mono, 8.25f);
        }

        static bool Has(string name)
        {
            return FontFamily.Families.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static int Px(float v) { return (int)Math.Round(v * Scale); }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static bool SystemPrefersDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) return (int)v == 0;
                }
            }
            catch { }
            return true;
        }
    }

    static class Native
    {
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        public static void MakeDpiAware()
        {
            if (!IsWindows) return;
            try { SetProcessDpiAwarenessContext(new IntPtr(-2)); return; } catch { } // system aware; we scale manually
            try { SetProcessDPIAware(); } catch { }
        }

        public static void TitleBar(IntPtr hwnd, bool dark)
        {
            if (!IsWindows) return;
            try
            {
                int v = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref v, 4);
            }
            catch { }
        }

        public static void ScrollbarTheme(IntPtr hwnd, bool dark)
        {
            if (!IsWindows) return;
            try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }
    }
}
