using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Windkanal
{
    /// <summary>Farben, Schrift (Outfit, eingebettet) und Zeichenhilfen für das helle und das dunkle Design.</summary>
    static class Theme
    {
        public static bool Dark = true;
        public static Color Bg, Card, Surface, Ctl, CtlHover, Border, Track, Text, Muted, Faint,
                            Ink, OnInk, Accent, Pink, Green, Orange, Grid, Shadow;

        static Theme()
        {
            LoadFonts();
            Base = Regular(10f); Small = Regular(8.75f); Label = Medium(9.75f); SmallMed = Medium(8.75f);
            Title = Medium(11.5f); Word = Medium(15f); Mid = Regular(18f); Big = Regular(26f);
            Icons = MakeIconFont();
            Apply(LoadDark());
        }

        static Color C(int r, int g, int b) { return Color.FromArgb(r, g, b); }

        public static void Apply(bool dark)
        {
            Dark = dark;
            if (dark)
            {
                Bg = C(13, 14, 16); Card = C(23, 25, 28); Surface = C(31, 34, 38);
                Ctl = C(36, 39, 44); CtlHover = C(45, 49, 55); Border = C(40, 43, 48); Track = C(52, 56, 63);
                Text = C(241, 242, 244); Muted = C(150, 156, 165); Faint = C(102, 108, 117);
                Ink = C(241, 242, 244); OnInk = C(18, 19, 22);
                Accent = C(98, 128, 255); Pink = C(255, 104, 152); Green = C(62, 207, 132); Orange = C(255, 148, 77);
                Grid = C(44, 47, 53); Shadow = Color.Black;
            }
            else
            {
                Bg = C(229, 232, 237); Card = C(246, 247, 249); Surface = Color.White;
                Ctl = C(238, 240, 243); CtlHover = C(228, 231, 236); Border = C(226, 229, 234); Track = C(222, 225, 230);
                Text = C(21, 23, 26); Muted = C(108, 114, 124); Faint = C(160, 165, 173);
                Ink = C(21, 23, 26); OnInk = Color.White;
                Accent = C(48, 86, 245); Pink = C(240, 82, 139); Green = C(36, 178, 104); Orange = C(232, 112, 36);
                Grid = C(229, 232, 236); Shadow = C(40, 52, 80);
            }
            Renderer.BgColor = Card.ToArgb();
        }

        /// <summary>Hintergrund für kleine Etiketten in der Farbe <paramref name="c"/>.</summary>
        public static Color Tint(Color c) { return Mix(Card, c, Dark ? 0.2f : 0.13f); }

        // ------------------------------------------------------------ Schrift

        [DllImport("gdi32.dll")]
        static extern IntPtr AddFontMemResourceEx(IntPtr data, uint len, IntPtr pdv, ref uint fonts);

        static PrivateFontCollection fonts;
        static FontFamily famRegular, famMedium;

        /// <summary>Outfit liegt als Ressource in der .exe und wird nur für diesen Prozess geladen (keine Installation).</summary>
        static void LoadFonts()
        {
            try
            {
                fonts = new PrivateFontCollection();
                var asm = typeof(Theme).Assembly;
                foreach (var name in new[] { "Outfit-Regular.ttf", "Outfit-Medium.ttf" })
                    using (var s = asm.GetManifestResourceStream("Fonts." + name))
                    {
                        if (s == null) continue;
                        var data = new byte[s.Length];
                        int read = 0;
                        while (read < data.Length) { int n = s.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
                        IntPtr p = Marshal.AllocCoTaskMem(data.Length);   // bleibt bis Programmende reserviert
                        Marshal.Copy(data, 0, p, data.Length);
                        fonts.AddMemoryFont(p, data.Length);
                        uint count = 0;
                        AddFontMemResourceEx(p, (uint)data.Length, IntPtr.Zero, ref count);   // damit auch TextRenderer/GDI sie findet
                    }
                foreach (var f in fonts.Families)
                {
                    if (f.Name == "Outfit") famRegular = f;
                    else if (f.Name == "Outfit Medium") famMedium = f;
                }
            }
            catch { }
            if (famRegular == null) famRegular = new FontFamily("Segoe UI");
            if (famMedium == null) famMedium = famRegular;
        }

        public static Font Regular(float pt) { return new Font(famRegular, pt, FontStyle.Regular, GraphicsUnit.Point); }
        public static Font Medium(float pt) { return new Font(famMedium, pt, FontStyle.Regular, GraphicsUnit.Point); }

        public static readonly Font Base, Small, Label, SmallMed, Title, Word, Mid, Big, Icons;

        static Font MakeIconFont()
        {
            var f = new Font("Segoe MDL2 Assets", 10f);
            return f.Name == "Segoe MDL2 Assets" ? f : null;   // fehlt nur auf sehr alten Windows-Versionen
        }

        // ------------------------------------------------------------ Zeichnen

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad))
            using (var pen = new Pen(c)) g.DrawPath(pen, p);
        }

        /// <summary>Weicher Schatten unter einer Fläche (nur im hellen Design sichtbar).</summary>
        public static void SoftShadow(Graphics g, RectangleF r, float rad)
        {
            if (Dark) return;
            for (int i = 1; i <= 6; i++)
            {
                var s = RectangleF.Inflate(r, i, i);
                s.Offset(0, 3);
                FillRound(g, Color.FromArgb(5, Shadow), s, rad + i);
            }
        }

        public static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }

        public static void Draw(Graphics g, string s, Font f, Color c, Rectangle r, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        public static int Width(string s, Font f)
        {
            return TextRenderer.MeasureText(s, f, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        }

        /// <summary>Kleines Etikett mit runden Enden, z. B. „Ø 1,526“. Gibt die Breite zurück.</summary>
        public static int Chip(Graphics g, string text, float x, float y, Color fg, Color bg, int h = 24, bool dot = false)
        {
            int tw = Width(text, SmallMed), pad = 10, dw = dot ? 14 : 0;
            int w = tw + 2 * pad + dw;
            FillRound(g, bg, new RectangleF(x, y, w, h), h / 2f);
            if (dot) using (var b = new SolidBrush(fg)) g.FillEllipse(b, x + pad, y + h / 2f - 3.5f, 7, 7);
            Draw(g, text, SmallMed, fg, new Rectangle((int)x + pad + dw, (int)y, tw + 2, h), TextFormatFlags.VerticalCenter);
            return w;
        }

        public static int ChipWidth(string text, bool dot = false) { return Width(text, SmallMed) + 20 + (dot ? 14 : 0); }

        /// <summary>Reihe aus Kapseln; <paramref name="filled"/> Anteil (0..1) in Farbe <paramref name="on"/>.</summary>
        public static void Capsules(Graphics g, RectangleF r, int n, float filled, Color on, Color off)
        {
            float gap = 4, w = (r.Width - gap * (n - 1)) / n;
            int k = (int)Math.Round(filled * n);
            for (int i = 0; i < n; i++)
                FillRound(g, i < k ? on : off, new RectangleF(r.X + i * (w + gap), r.Y, w, r.Height), w / 2);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        /// <summary>Logo: Stromlinien um einen Zylinder, einfarbig.</summary>
        public static void DrawLogo(Graphics g, float x, float y, float s, Color bg, Color fg)
        {
            FillRound(g, bg, new RectangleF(x, y, s, s), s * 0.3f);
            float cx = x + s * 0.42f, cy = y + s * 0.5f, r = s * 0.12f;
            float[] offs = { 0.13f, 0.25f };
            int[] alpha = { 255, 150 };
            for (int i = 0; i < offs.Length; i++)
                using (var pen = new Pen(Color.FromArgb(alpha[i], fg), Math.Max(1.3f, s * 0.06f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    foreach (int sgn in new[] { -1, 1 })
                    {
                        var pts = new PointF[21];
                        for (int k = 0; k < pts.Length; k++)
                        {
                            float px = x + s * (0.18f + 0.66f * k / (pts.Length - 1));
                            float d = (px - cx) / (s * 0.17f);
                            pts[k] = new PointF(px, cy + sgn * s * (offs[i] + (0.13f - 0.04f * i) * (float)Math.Exp(-d * d)));
                        }
                        g.DrawLines(pen, pts);
                    }
            using (var b = new SolidBrush(fg)) g.FillEllipse(b, cx - r, cy - r, 2 * r, 2 * r);
        }

        // ------------------------------------------------------------ Fensterrahmen

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        /// <summary>Titelleiste passend zum Design (dunkel: Windows 10 ab 1809 und Windows 11).</summary>
        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = Dark ? 1 : 0;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }

        /// <summary>Titelleiste nach einem Designwechsel neu zeichnen lassen.</summary>
        public static void RefreshFrame(Form f)
        {
            try
            {
                bool active = Form.ActiveForm == f;
                SendMessage(f.Handle, 0x86, active ? IntPtr.Zero : (IntPtr)1, IntPtr.Zero);   // WM_NCACTIVATE
                SendMessage(f.Handle, 0x86, active ? (IntPtr)1 : IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        /// <summary>Bildlaufleisten passend zum Design.</summary>
        public static void ThemeScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        // ------------------------------------------------------------ Einstellung merken

        // HKCU\Software\Windkanal2D, Wert "Design"
        const string PrefKey = @"Software\Windkanal2D";

        static bool LoadDark()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(PrefKey))
                {
                    var v = k != null ? k.GetValue("Design") as string : null;
                    if (v == "hell") return false;
                    if (v == "dunkel") return true;
                }
            }
            catch { }
            return false;
        }

        public static void SaveDark(bool dark)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(PrefKey))
                    k.SetValue("Design", dark ? "dunkel" : "hell");
            }
            catch { }
        }
    }

    /// <summary>Basis für selbst gezeichnete Bedienelemente.</summary>
    class Painted : Control
    {
        protected bool Hover, Pressed;

        public Painted()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Base;
        }

        protected override void OnMouseEnter(EventArgs e) { Hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { Hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    }

    /// <summary>Zeichenfläche ohne Flackern.</summary>
    sealed class Canvas : Control
    {
        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }

    /// <summary>Karte mit großen runden Ecken und Titel; Inhalte zeichnet der Besitzer über das Paint-Ereignis.</summary>
    sealed class Card : Panel
    {
        public const int Radius = 12, Pad = 16, Head = 48;
        public string Title;

        public Card(string title)
        {
            Title = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }

        public int Inner { get { return Width - 2 * Pad; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width - 0.5f, Height - 0.5f);
            Theme.FillRound(g, Theme.Card, r, Radius);
            Theme.StrokeRound(g, Theme.Dark ? Theme.Border : Color.White, r, Radius);
            if (!string.IsNullOrEmpty(Title))
                Theme.Draw(g, Title, Theme.Title, Theme.Text, new Rectangle(Pad, 12, Width - 2 * Pad, 24), TextFormatFlags.VerticalCenter);
            base.OnPaint(e);
        }
    }

    /// <summary>
    /// Zeichnet eine scrollende Karte nach jedem Bildlauf neu. Ohne das bleiben beim Scrollen mit dem Mausrad
    /// Reste von Rahmen und Ecken stehen (das Rad löst kein Scroll-Ereignis aus).
    /// </summary>
    sealed class ScrollRepaint : NativeWindow
    {
        readonly Control target;

        ScrollRepaint(Control c)
        {
            target = c;
            AssignHandle(c.Handle);
            c.HandleDestroyed += delegate { ReleaseHandle(); };
        }

        public static void Attach(Control c)
        {
            if (c.IsHandleCreated) new ScrollRepaint(c);
            else c.HandleCreated += delegate { new ScrollRepaint(c); };
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_HSCROLL, WM_VSCROLL, WM_MOUSEWHEEL
            if (m.Msg == 0x114 || m.Msg == 0x115 || m.Msg == 0x20A) target.Invalidate();
        }
    }

    /// <summary>
    /// Weiches Überblenden bei großen Wechseln (2D ↔ 3D, hell ↔ dunkel): Das bisherige Fensterbild liegt kurz als Foto
    /// über allem und wird ausgeblendet, darunter steht schon der neue Zustand. Ohne Windows-Animationen sofort.
    /// </summary>
    static class Transition
    {
        /// <param name="window">Fenster, dessen Bild überblendet wird</param>
        /// <param name="change">der eigentliche Wechsel (Farben anwenden, anderes Fenster zeigen …)</param>
        /// <param name="shown">Fenster, das danach zu sehen ist (wird vor dem Ausblenden fertig gezeichnet)</param>
        public static void CrossFade(Form window, Action change, Form shown = null, int ms = 240)
        {
            if (!SystemInformation.UIEffectsEnabled || !window.Visible || window.WindowState == FormWindowState.Minimized)
            {
                change();
                return;
            }
            Rectangle b = window.Bounds;
            Bitmap shot;
            try
            {
                shot = new Bitmap(b.Width, b.Height);
                using (var g = Graphics.FromImage(shot)) g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            }
            catch { change(); return; }
            var cover = new Cover(shot) { Bounds = b };
            cover.Show();
            cover.Update();
            change();
            var target = shown ?? window;
            target.Update();
            cover.BringToFront();
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timer = new Timer { Interval = 10 };
            timer.Tick += delegate
            {
                double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / ms);
                double e = 1 - Math.Pow(1 - t, 3);   // schnell los, weich aus
                if (t >= 1)
                {
                    timer.Stop(); timer.Dispose();
                    cover.Close(); cover.Dispose(); shot.Dispose();
                    if (!target.IsDisposed && target.Visible) target.Activate();
                    return;
                }
                cover.Opacity = 1 - e;
            };
            timer.Start();
        }

        sealed class Cover : Form
        {
            readonly Bitmap img;

            public Cover(Bitmap image)
            {
                img = image;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                TopMost = true;
                DoubleBuffered = true;
                Opacity = 1;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e) { e.Graphics.DrawImageUnscaled(img, 0, 0); }
        }
    }

    /// <summary>
    /// Scrollbereich in einer Karte: Titel und Rahmen der Karte bleiben stehen, nur der Inhalt darunter scrollt.
    /// </summary>
    sealed class ScrollPanel : Panel
    {
        public ScrollPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            AutoScroll = true;
            AutoScrollMargin = new Size(0, 12);
            BackColor = Theme.Card;
            ScrollRepaint.Attach(this);
            HandleCreated += delegate { Theme.ThemeScrollbars(Handle); };
        }
    }

    /// <summary>Schaltfläche als Pille (schwarz = Hauptaktion) oder runder Symbolknopf.</summary>
    sealed class FlatButton : Painted
    {
        public bool Primary;
        public string Icon;   // Zeichen aus "Segoe MDL2 Assets"

        public FlatButton()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Font = Theme.Label;
            Height = 40;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill, fg;
            if (Primary)
            {
                fill = Hover ? Theme.Mix(Theme.Ink, Theme.Muted, 0.25f) : Theme.Ink;
                fg = Theme.OnInk;
            }
            else
            {
                fill = Hover ? Theme.CtlHover : Theme.Surface;
                fg = Theme.Text;
            }
            if (Pressed) fill = Theme.Mix(fill, Theme.Muted, 0.2f);
            if (!Enabled) { fill = Theme.Ctl; fg = Theme.Faint; }
            Theme.FillRound(g, fill, r, 9);
            if (!Primary) Theme.StrokeRound(g, Theme.Border, r, 9);

            bool icon = Icon != null && Theme.Icons != null;
            int tw = Text.Length > 0 ? Theme.Width(Text, Font) : 0;
            int iw = icon ? 16 + (tw > 0 ? 8 : 0) : 0;
            int x = (Width - tw - iw) / 2;
            if (icon)
                Theme.Draw(g, Icon, Theme.Icons, fg, new Rectangle(x, 1, 16, Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (tw > 0)
                Theme.Draw(g, Text, Font, fg, new Rectangle(x + iw, 0, tw + 2, Height - 1), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Umschalter mit mehreren Einträgen in einer Pille (wie eine Navigation).</summary>
    sealed class Segmented : Painted
    {
        public readonly List<string> Items = new List<string>();
        int sel, hot = -1;
        public event EventHandler SelectedIndexChanged;

        public Segmented()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Font = Theme.Label;
            Height = 44;
        }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value == sel) return;
                sel = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        public int PreferredWidth
        {
            get
            {
                int w = 8;
                foreach (var s in Items) w += Theme.Width(s, Font) + 36;
                return w;
            }
        }

        RectangleF ItemRect(int i)
        {
            float x = 4;
            for (int k = 0; k < i; k++) x += Theme.Width(Items[k], Font) + 36;
            return new RectangleF(x, 4, Theme.Width(Items[i], Font) + 36, Height - 9);
        }

        int HitTest(Point p)
        {
            for (int i = 0; i < Items.Count; i++)
                if (ItemRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int h = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && h >= 0) SelectedIndex = h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Dark ? Theme.Card : Color.FromArgb(240, 242, 245), r, 10);
            Theme.StrokeRound(g, Theme.Dark ? Theme.Border : Color.White, r, 10);
            for (int i = 0; i < Items.Count; i++)
            {
                var ir = ItemRect(i);
                if (i == sel)
                {
                    Theme.FillRound(g, Theme.Dark ? Theme.CtlHover : Theme.Surface, ir, 7);
                }
                else if (i == hot)
                    Theme.FillRound(g, Theme.Dark ? Theme.Surface : Theme.CtlHover, ir, 7);
                Theme.Draw(g, Items[i], Font, i == sel ? Theme.Text : Theme.Muted, Rectangle.Round(ir),
                           TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }

    /// <summary>Schieberegler mit Beschriftung und Wertanzeige.</summary>
    sealed class FlatSlider : Painted
    {
        int min, max = 100, val;
        string valueText = "";
        public event EventHandler ValueChanged;

        public FlatSlider()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Height = 46;
        }

        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public string ValueText { get { return valueText; } set { valueText = value; Invalidate(); } }

        public int Value
        {
            get { return val; }
            set
            {
                value = Math.Max(min, Math.Min(max, value));
                if (value == val) return;
                val = value;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        const int Pad = 9, TrackY = 34;

        void SetFromMouse(int x)
        {
            float t = (x - Pad) / (float)Math.Max(1, Width - 2 * Pad);
            Value = (int)Math.Round(min + Math.Max(0, Math.Min(1, t)) * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus();
            if (e.Y >= TrackY - 14) SetFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Pressed && Enabled) SetFromMouse(e.X);
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            int step = Math.Max(1, (max - min) / 40);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) { Value -= e.Shift ? step : 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) { Value += e.Shift ? step : 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Home) Value = min;
            else if (e.KeyCode == Keys.End) Value = max;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            Theme.Draw(g, Text, Font, Enabled ? Theme.Muted : Theme.Faint, new Rectangle(0, 0, Width, 22), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, valueText, Theme.Label, Enabled ? Theme.Text : Theme.Faint, new Rectangle(0, 0, Width, 22),
                       TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

            float t = max > min ? (val - min) / (float)(max - min) : 0;
            float x0 = Pad, x1 = Width - Pad, xt = x0 + t * (x1 - x0);
            Theme.FillRound(g, Theme.Track, new RectangleF(x0, TrackY - 3, x1 - x0, 6), 3);
            Color acc = Enabled ? Theme.Accent : Theme.Faint;
            Theme.FillRound(g, acc, new RectangleF(x0, TrackY - 3, xt - x0, 6), 3);
            float r = Pressed || Hover ? 9 : 8;
            if (Focused && Enabled)
                using (var halo = new SolidBrush(Color.FromArgb(50, Theme.Accent))) g.FillEllipse(halo, xt - r - 4, TrackY - r - 4, 2 * r + 8, 2 * r + 8);
            Theme.SoftShadow(g, new RectangleF(xt - r + 2, TrackY - r + 2, 2 * r - 4, 2 * r - 4), r);
            using (var b = new SolidBrush(Theme.Dark ? Theme.Text : Color.White)) g.FillEllipse(b, xt - r, TrackY - r, 2 * r, 2 * r);
            using (var p = new Pen(Theme.Dark ? Theme.Text : Theme.Border)) g.DrawEllipse(p, xt - r, TrackY - r, 2 * r, 2 * r);
        }
    }

    /// <summary>Ein/Aus-Schalter mit Beschriftung.</summary>
    sealed class Toggle : Painted
    {
        bool on;
        public event EventHandler CheckedChanged;

        public Toggle()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Height = 28;
        }

        public bool Checked
        {
            get { return on; }
            set
            {
                if (on == value) return;
                on = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            const int w = 38, h = 22;
            float y = (Height - h) / 2f;
            Color track = on ? Theme.Accent : (Hover ? Theme.CtlHover : Theme.Track);
            Theme.FillRound(g, track, new RectangleF(0, y, w, h), h / 2f);
            float kx = on ? w - h + 3 : 3;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx, y + 3, h - 6, h - 6);
            Theme.Draw(g, Text, Font, Theme.Text, new Rectangle(w + 12, 0, Width - w - 12, Height), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Auswahlliste (öffnet ein Menü statt einer Windows-Combobox).</summary>
    sealed class DropDown : Painted
    {
        public readonly List<string> Items = new List<string>();
        int sel = -1;
        public event EventHandler SelectedIndexChanged;

        public DropDown()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Height = 40;
        }

        public int SelectedIndex
        {
            get { return sel; }
            set
            {
                if (value == sel) return;
                sel = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Items.Count == 0) return;
            var menu = new ContextMenuStrip
            {
                Renderer = new MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false,
                BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base, Padding = new Padding(4), DropShadowEnabled = true
            };
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(Items[i])
                {
                    AutoSize = false, Size = new Size(Width - 8, 34), ForeColor = Theme.Text,
                    Font = i == sel ? Theme.Label : Theme.Base, Padding = new Padding(6, 0, 0, 0)
                };
                it.Click += delegate { SelectedIndex = idx; };
                menu.Items.Add(it);
            }
            menu.Closed += delegate { Pressed = false; Invalidate(); BeginInvoke((Action)menu.Dispose); };
            menu.Show(this, new Point(0, Height + 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Hover || Pressed ? Theme.CtlHover : Theme.Surface, r, 8);
            Theme.StrokeRound(g, Theme.Border, r, 8);
            string s = sel >= 0 && sel < Items.Count ? Items[sel] : "";
            Theme.Draw(g, s, Font, Theme.Text, new Rectangle(14, 0, Width - 44, Height - 1), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            float cx = Width - 20, cy = Height / 2f;
            using (var p = new Pen(Theme.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(p, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    /// <summary>
    /// Modellauswahl: zeigt Kategorie und Modell; das Menü listet die Kategorien, jede mit einem Untermenü ihrer Modelle.
    /// </summary>
    sealed class ModelPicker : Painted
    {
        readonly List<KeyValuePair<string, List<Model>>> groups;
        Model sel;
        public event EventHandler SelectedChanged;

        public ModelPicker(List<KeyValuePair<string, List<Model>>> groups)
        {
            this.groups = groups;
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Height = 48;
        }

        public Model Selected
        {
            get { return sel; }
            set
            {
                if (value == sel) return;
                sel = value;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        ContextMenuStrip NewMenu()
        {
            return new ContextMenuStrip
            {
                Renderer = new MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false,
                BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base, Padding = new Padding(4), DropShadowEnabled = true
            };
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || groups.Count == 0) return;
            var menu = NewMenu();
            foreach (var g in groups)
            {
                bool current = sel != null && g.Key == sel.Category;
                var cat = new ToolStripMenuItem(g.Key)
                {
                    AutoSize = false, Size = new Size(Width - 8, 34), ForeColor = Theme.Text,
                    Font = current ? Theme.Label : Theme.Base, Padding = new Padding(6, 0, 0, 0),
                    ShortcutKeyDisplayString = g.Value.Count.ToString(), ShowShortcutKeys = true
                };
                if (g.Value.Count == 1)
                {
                    // Kategorie mit nur einem Eintrag (Eigene Zeichnung) direkt wählbar
                    var only = g.Value[0];
                    cat.ShortcutKeyDisplayString = "";
                    cat.Click += delegate { Selected = only; };
                    menu.Items.Add(cat);
                    continue;
                }
                var sub = (ToolStripDropDownMenu)cat.DropDown;
                sub.Renderer = new MenuRenderer();
                sub.ShowImageMargin = false;
                sub.ShowCheckMargin = false;
                sub.BackColor = Theme.Surface;
                sub.Padding = new Padding(4);
                sub.DropShadowEnabled = true;
                int w = 0;
                foreach (var m in g.Value) w = Math.Max(w, Theme.Width(m.Name, Theme.Label));
                w = Math.Max(Width - 8, w + 36);
                // lange Listen in zwei Spalten wären unübersichtlich; das Untermenü scrollt, wenn es höher als der Bildschirm ist
                foreach (var m in g.Value)
                {
                    var model = m;
                    var it = new ToolStripMenuItem(m.Name)
                    {
                        AutoSize = false, Size = new Size(w, 30), ForeColor = Theme.Text,
                        Font = m == sel ? Theme.Label : Theme.Base, Padding = new Padding(6, 0, 0, 0),
                        ToolTipText = m.Description
                    };
                    it.Click += delegate { Selected = model; };
                    cat.DropDownItems.Add(it);
                }
                sub.ShowItemToolTips = true;
                menu.Items.Add(cat);
            }
            menu.Closed += delegate { Pressed = false; Invalidate(); BeginInvoke((Action)menu.Dispose); };
            menu.Show(this, new Point(0, Height + 4));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Hover || Pressed ? Theme.CtlHover : Theme.Surface, r, 8);
            Theme.StrokeRound(g, Theme.Border, r, 8);
            if (sel != null)
            {
                Theme.Draw(g, sel.Category, Theme.Small, Theme.Muted, new Rectangle(14, 6, Width - 44, 16), TextFormatFlags.EndEllipsis);
                Theme.Draw(g, sel.Name, Theme.Label, Theme.Text, new Rectangle(14, 22, Width - 44, 20), TextFormatFlags.EndEllipsis);
            }
            float cx = Width - 20, cy = Height / 2f;
            using (var p = new Pen(Theme.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(p, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colors()) { RoundedEdges = false; }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(e.Graphics, Theme.Ctl, new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2), 8);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var mi = e.Item as ToolStripMenuItem;
            bool shortcut = mi != null && !string.IsNullOrEmpty(mi.ShortcutKeyDisplayString) && e.Text == mi.ShortcutKeyDisplayString && e.Text != mi.Text;
            e.TextColor = shortcut ? Theme.Faint : Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        sealed class Colors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Theme.Ctl; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        }
    }

    /// <summary>Zahleneingabe (deutsches Format) mit Bezeichnung und Einheit; Pfeiltasten und Mausrad ändern den Wert.</summary>
    sealed class NumberBox : Painted
    {
        readonly TextBox box;
        readonly CultureInfo de = CultureInfo.GetCultureInfo("de-DE");
        decimal val = 1, min = 0, max = 100, inc = 1;
        bool valid = true, updating;
        public string Unit = "", Caption = "";
        public int Decimals = 2;
        public event EventHandler ValueChanged;

        public NumberBox()
        {
            SetStyle(ControlStyles.Selectable, false);
            Height = 40;
            box = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Label,
                TextAlign = HorizontalAlignment.Right
            };
            Controls.Add(box);
            box.TextChanged += OnBoxChanged;
            box.KeyDown += OnBoxKey;
            box.Leave += delegate { ShowValue(); };
            box.GotFocus += delegate { Invalidate(); };
            box.LostFocus += delegate { Invalidate(); };
            Cursor = Cursors.IBeam;
        }

        public decimal Minimum { get { return min; } set { min = value; } }
        public decimal Maximum { get { return max; } set { max = value; } }
        public decimal Increment { get { return inc; } set { inc = value; } }

        public decimal Value
        {
            get { return val; }
            set
            {
                value = Math.Max(min, Math.Min(max, value));
                bool changed = value != val;
                val = value;
                valid = true;
                ShowValue();
                if (changed && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }

        void ShowValue()
        {
            updating = true;
            box.Text = val.ToString("F" + Decimals, de);
            updating = false;
            valid = true;
            Invalidate();
        }

        void OnBoxChanged(object sender, EventArgs e)
        {
            if (updating) return;
            decimal v;
            valid = decimal.TryParse(box.Text.Trim(), NumberStyles.Number, de, out v) && v >= min && v <= max;
            if (valid && v != val)
            {
                val = v;
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
            Invalidate();
        }

        void OnBoxKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Value = val + inc; e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Value = val - inc; e.Handled = true; e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Enter) { ShowValue(); e.Handled = true; e.SuppressKeyPress = true; }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (box.Focused) Value = val + Math.Sign(e.Delta) * inc;
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) { box.Focus(); base.OnMouseDown(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (box == null) return;
            int uw = Theme.Width(Unit, Font);
            int cw = Caption.Length > 0 ? Theme.Width(Caption, Font) + 16 : 0;
            box.SetBounds(14 + cw, (Height - box.PreferredHeight) / 2 + 1, Width - 34 - uw - cw, box.PreferredHeight);
        }

        public void Recolor() { box.BackColor = Theme.Surface; box.ForeColor = Theme.Text; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Surface, r, 8);
            Color border = !valid ? Theme.Pink : box.Focused ? Theme.Accent : Theme.Border;
            Theme.StrokeRound(g, border, r, 8);
            Theme.Draw(g, Unit, Font, Theme.Muted, new Rectangle(0, 0, Width - 14, Height - 1), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            Theme.Draw(g, Caption, Font, Theme.Muted, new Rectangle(14, 0, Width - 28, Height - 1), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Zwischenüberschrift mit feiner Linie.</summary>
    sealed class Section : Painted
    {
        public Section(string text)
        {
            SetStyle(ControlStyles.Selectable, false);
            Text = text;
            Height = 20;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            // Abschnittstitel in Großbuchstaben, wie in technischen Programmen
            string t = Text.ToUpper(CultureInfo.GetCultureInfo("de-DE"));
            Theme.Draw(g, t, Theme.SmallMed, Theme.Muted, new Rectangle(0, 0, Width, Height), TextFormatFlags.VerticalCenter);
            int tw = Theme.Width(t, Theme.SmallMed);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, tw + 10, Height / 2, Width, Height / 2);
        }
    }

    /// <summary>Mehrzeiliger, gedämpfter Hinweistext.</summary>
    sealed class HintLabel : Painted
    {
        public HintLabel(string text)
        {
            SetStyle(ControlStyles.Selectable, false);
            Text = text;
            Font = Theme.Small;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Theme.Draw(e.Graphics, Text, Font, Theme.Muted, ClientRectangle, TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Liste aus Bezeichnung (links) und Wert (rechts).</summary>
    sealed class InfoRows : Painted
    {
        public string[] Keys = new string[0], Values = new string[0];
        public const int Row = 26;
        /// <summary>Zeile, deren Wert als Warnung (orange) erscheint, -1 = keine.</summary>
        public int WarnRow = -1;

        public InfoRows() { SetStyle(ControlStyles.Selectable, false); }

        public void Set(string[] keys, string[] values) { Keys = keys; Values = values; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            int y = 0;
            for (int i = 0; i < Keys.Length; i++, y += Row)
            {
                var r = new Rectangle(0, y, Width, Row);
                Theme.Draw(g, Keys[i], Font, Theme.Muted, r, TextFormatFlags.VerticalCenter);
                Theme.Draw(g, i < Values.Length ? Values[i] : "", Theme.Label, i == WarnRow ? Theme.Orange : Theme.Text, r, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
        }
    }

    /// <summary>Fenster für den Hilfetext „Physik &amp; Grenzen“.</summary>
    sealed class InfoDialog : Form
    {
        readonly string[] lines;
        const int W = 780, X = 32;

        public InfoDialog(string title, string text)
        {
            Text = title;
            lines = text.Split('\n');
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Base;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            DoubleBuffered = true;
            ClientSize = new Size(W, Flow(null) + 84);
            var ok = new FlatButton { Text = "Schließen", Primary = true, BackColor = Theme.Card };
            ok.SetBounds(ClientSize.Width - X - 124, ClientSize.Height - 28 - 40, 124, 40);
            ok.Click += delegate { Close(); };
            Controls.Add(ok);
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) Close(); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        static bool IsHeader(string s) { return s.Length > 0 && s == s.ToUpperInvariant() && char.IsLetter(s[0]); }

        static string Sentence(string s)
        {
            // "SO RECHNET DIE SIMULATION" -> "So rechnet die Simulation"
            var parts = s.ToLower(CultureInfo.GetCultureInfo("de-DE")).Split(' ');
            for (int i = 0; i < parts.Length; i++)
                if (i == 0 || parts[i] == "simulation")
                    parts[i] = parts[i].Length > 0 ? char.ToUpper(parts[i][0]) + parts[i].Substring(1) : parts[i];
            return string.Join(" ", parts);
        }

        int Flow(Graphics g)
        {
            int y = 28, w = W - 2 * X;
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            foreach (var raw in lines)
            {
                string s = raw.TrimEnd();
                if (s.Length == 0) { y += 14; continue; }
                if (IsHeader(s))
                {
                    if (g != null) Theme.Draw(g, Sentence(s), Theme.Title, Theme.Text, new Rectangle(X, y, w, 24), TextFormatFlags.Left);
                    y += 32;
                    continue;
                }
                bool bullet = s.StartsWith("• ");
                string body = bullet ? s.Substring(2) : s;
                int x = bullet ? X + 18 : X;
                int h = TextRenderer.MeasureText(body, Font, new Size(w - (x - X), 1000), flags).Height;
                if (g != null)
                {
                    if (bullet) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, X + 2, y + 7, 6, 6);
                    Theme.Draw(g, body, Font, bullet ? Theme.Text : Theme.Muted, new Rectangle(x, y, w - (x - X), h), flags);
                }
                y += h + 6;
            }
            return y;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Flow(e.Graphics);
        }
    }
}
