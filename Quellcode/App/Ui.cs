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
    /// <summary>Farben, Schriften und Zeichenhilfen für das dunkle Design.</summary>
    static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(18, 21, 26);       // = Hintergrund des Strömungsbilds
        public static readonly Color Chrome = Color.FromArgb(22, 25, 31);   // Kopf-, Seiten- und Fußleiste
        public static readonly Color Card = Color.FromArgb(30, 34, 42);
        public static readonly Color Ctl = Color.FromArgb(40, 45, 55);
        public static readonly Color CtlHover = Color.FromArgb(50, 56, 68);
        public static readonly Color Border = Color.FromArgb(48, 54, 65);
        public static readonly Color Track = Color.FromArgb(55, 61, 74);
        public static readonly Color Text = Color.FromArgb(230, 233, 238);
        public static readonly Color Muted = Color.FromArgb(140, 148, 162);
        public static readonly Color Faint = Color.FromArgb(95, 102, 116);
        public static readonly Color Accent = Color.FromArgb(77, 163, 255);
        public static readonly Color AccentHover = Color.FromArgb(104, 178, 255);
        public static readonly Color Good = Color.FromArgb(80, 210, 140);
        public static readonly Color Warn = Color.FromArgb(255, 196, 87);
        public static readonly Color Cd = Color.FromArgb(255, 159, 67);
        public static readonly Color Cl = Color.FromArgb(84, 200, 255);

        public static readonly Font Base = new Font("Segoe UI", 9f);
        public static readonly Font Small = new Font("Segoe UI", 8.25f);
        public static readonly Font Semi = new Font("Segoe UI Semibold", 9f);
        public static readonly Font Caps = new Font("Segoe UI Semibold", 7.75f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 13f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 17f);
        public static readonly Font Icons = MakeIconFont();

        static Font MakeIconFont()
        {
            var f = new Font("Segoe MDL2 Assets", 9f);
            return f.Name == "Segoe MDL2 Assets" ? f : null;   // fehlt nur auf sehr alten Windows-Versionen
        }

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
            using (var p = Round(r, rad))
            using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad))
            using (var pen = new Pen(c)) g.DrawPath(pen, p);
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

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        /// <summary>Dunkle Titelleiste (Windows 10 ab 1809 und Windows 11).</summary>
        public static void DarkTitleBar(IntPtr hwnd)
        {
            int on = 1;
            try
            {
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }

        /// <summary>Dunkle Bildlaufleisten für ein Fenster.</summary>
        public static void DarkScrollbars(IntPtr hwnd)
        {
            try { SetWindowTheme(hwnd, "DarkMode_Explorer", null); } catch { }
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

    /// <summary>Flache Schaltfläche mit abgerundeten Ecken und optionalem Symbol.</summary>
    sealed class FlatButton : Painted
    {
        public bool Primary;
        public string Icon;   // Zeichen aus "Segoe MDL2 Assets"

        public FlatButton()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Height = 32;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            Color fill = Primary ? (Hover ? Theme.AccentHover : Theme.Accent) : (Hover ? Theme.CtlHover : Theme.Ctl);
            if (Pressed) fill = Theme.Mix(fill, Color.Black, 0.15f);
            if (!Enabled) fill = Theme.Mix(fill, BackColor, 0.5f);
            Theme.FillRound(g, fill, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 6);
            Color fg = Primary ? Color.FromArgb(10, 18, 30) : Theme.Text;
            if (!Enabled) fg = Theme.Faint;

            Size ts = TextRenderer.MeasureText(g, Text, Font, Size, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            bool icon = Icon != null && Theme.Icons != null;
            int iw = icon ? 16 + (Text.Length > 0 ? 8 : 0) : 0;
            int x = (Width - ts.Width - iw) / 2;
            if (icon)
                Theme.Draw(g, Icon, Theme.Icons, fg, new Rectangle(x, 0, 16, Height), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Text, Font, fg, new Rectangle(x + iw, 0, ts.Width + 2, Height), TextFormatFlags.VerticalCenter);
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
            Height = 44;
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

        const int Pad = 9, TrackY = 31;

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
            if (e.Y >= TrackY - 12) SetFromMouse(e.X);
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
            Theme.Draw(g, Text, Font, Enabled ? Theme.Muted : Theme.Faint, new Rectangle(0, 0, Width, 20), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, valueText, Theme.Semi, Enabled ? Theme.Text : Theme.Faint, new Rectangle(0, 0, Width, 20),
                       TextFormatFlags.VerticalCenter | TextFormatFlags.Right);

            float t = max > min ? (val - min) / (float)(max - min) : 0;
            float x0 = Pad, x1 = Width - Pad, xt = x0 + t * (x1 - x0);
            Theme.FillRound(g, Theme.Track, new RectangleF(x0, TrackY - 2, x1 - x0, 4), 2);
            Color acc = Enabled ? Theme.Accent : Theme.Faint;
            Theme.FillRound(g, acc, new RectangleF(x0, TrackY - 2, xt - x0, 4), 2);
            float r = Pressed || Hover ? 8 : 7;
            if (Focused && Enabled)
                using (var halo = new SolidBrush(Color.FromArgb(60, Theme.Accent))) g.FillEllipse(halo, xt - r - 4, TrackY - r - 4, 2 * r + 8, 2 * r + 8);
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.Muted)) g.FillEllipse(b, xt - r, TrackY - r, 2 * r, 2 * r);
            using (var b = new SolidBrush(acc)) g.FillEllipse(b, xt - 3, TrackY - 3, 6, 6);
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
            Height = 26;
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
            const int w = 34, h = 18;
            float y = (Height - h) / 2f;
            Color track = on ? (Hover ? Theme.AccentHover : Theme.Accent) : (Hover ? Theme.CtlHover : Theme.Track);
            Theme.FillRound(g, track, new RectangleF(0, y, w, h), h / 2f);
            float kx = on ? w - h + 3 : 3;
            using (var b = new SolidBrush(on ? Color.White : Theme.Muted)) g.FillEllipse(b, kx, y + 3, h - 6, h - 6);
            Theme.Draw(g, Text, Font, Theme.Text, new Rectangle(w + 10, 0, Width - w - 10, Height), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Auswahlliste im dunklen Stil (öffnet ein Menü statt einer Windows-Combobox).</summary>
    sealed class DropDown : Painted
    {
        public readonly List<string> Items = new List<string>();
        int sel = -1;
        public event EventHandler SelectedIndexChanged;

        public DropDown()
        {
            SetStyle(ControlStyles.Selectable, false);
            Cursor = Cursors.Hand;
            Height = 32;
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
                Renderer = new DarkMenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false,
                BackColor = Theme.Ctl, ForeColor = Theme.Text, Font = Font, Padding = new Padding(3), DropShadowEnabled = true
            };
            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                var it = new ToolStripMenuItem(Items[i])
                {
                    AutoSize = false, Size = new Size(Width - 6, 30), ForeColor = Theme.Text,
                    Font = i == sel ? Theme.Semi : Font, Padding = new Padding(4, 0, 0, 0)
                };
                it.Click += delegate { SelectedIndex = idx; };
                menu.Items.Add(it);
            }
            menu.Closed += delegate { Pressed = false; Invalidate(); BeginInvoke((Action)menu.Dispose); };
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, Hover || Pressed ? Theme.CtlHover : Theme.Ctl, r, 6);
            string s = sel >= 0 && sel < Items.Count ? Items[sel] : "";
            Theme.Draw(g, s, Font, Theme.Text, new Rectangle(10, 0, Width - 36, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            // Pfeil nach unten
            float cx = Width - 16, cy = Height / 2f;
            using (var p = new Pen(Theme.Muted, 1.6f))
                g.DrawLines(p, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, Theme.CtlHover, new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2), 5);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
        }

        sealed class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Ctl; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Theme.CtlHover; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Ctl; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Ctl; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Ctl; } }
        }
    }

    /// <summary>Zahleneingabe (deutsches Format) mit Einheit; Pfeiltasten und Mausrad ändern den Wert.</summary>
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
            Height = 32;
            box = new TextBox
            {
                BorderStyle = BorderStyle.None, BackColor = Theme.Ctl, ForeColor = Theme.Text, Font = Theme.Semi,
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
            if (box == null) return;
            int uw = TextRenderer.MeasureText(Unit, Font).Width;
            int cw = Caption.Length > 0 ? TextRenderer.MeasureText(Caption, Font).Width + 16 : 0;
            box.SetBounds(10 + cw, (Height - box.PreferredHeight) / 2 + 1, Width - 26 - uw - cw, box.PreferredHeight);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, Theme.Ctl, r, 6);
            Color border = !valid ? Color.FromArgb(235, 90, 90) : box.Focused ? Theme.Accent : Theme.Ctl;
            Theme.StrokeRound(g, border, r, 6);
            Theme.Draw(g, Unit, Font, Theme.Muted, new Rectangle(0, 0, Width - 10, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            Theme.Draw(g, Caption, Font, Theme.Muted, new Rectangle(10, 0, Width - 20, Height), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Mehrzeiliger, gedämpfter Hinweistext.</summary>
    sealed class HintLabel : Painted
    {
        public HintLabel(string text)
        {
            SetStyle(ControlStyles.Selectable, false);
            Text = text;
            ForeColor = Theme.Muted;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            Theme.Draw(e.Graphics, Text, Font, ForeColor, ClientRectangle, TextFormatFlags.WordBreak);
        }
    }

    /// <summary>Liste aus Bezeichnung (links) und Wert (rechts).</summary>
    sealed class InfoRows : Painted
    {
        public string[] Keys = new string[0], Values = new string[0];
        public string Note;
        public const int Row = 22;

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
                Theme.Draw(g, i < Values.Length ? Values[i] : "", Theme.Semi, Theme.Text, r, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            }
            if (Note != null)
                Theme.Draw(g, Note, Theme.Small, Theme.Faint, new Rectangle(0, y + 2, Width, Row), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Karte mit Überschrift; die Inhalte werden untereinander angeordnet.</summary>
    sealed class Card : Panel
    {
        public const int Inset = 14;
        readonly string title;
        int y = 40;

        public Card(string title)
        {
            this.title = title;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Chrome;
        }

        public int InnerWidth { get { return Width - 2 * Inset; } }

        public T Add<T>(T c, int height, int gapAfter = 10) where T : Control
        {
            c.BackColor = Theme.Card;
            c.SetBounds(Inset, y, InnerWidth, height);
            Controls.Add(c);
            y += height + gapAfter;
            return c;
        }

        public void Finish() { Height = y + Inset - 10 + 4; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            Theme.FillRound(g, Theme.Card, r, 10);
            Theme.StrokeRound(g, Theme.Border, r, 10);
            Theme.Draw(g, title.ToUpper(CultureInfo.GetCultureInfo("de-DE")), Theme.Caps, Theme.Accent,
                       new Rectangle(Inset, 12, Width - 2 * Inset, 18), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Dunkles Fenster für den Hilfetext „Physik &amp; Grenzen“.</summary>
    sealed class InfoDialog : Form
    {
        readonly string[] lines;

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
            ClientSize = new Size(760, Measure() + 80);
            var ok = new FlatButton { Text = "Schließen", Primary = true, BackColor = Theme.Card };
            ok.SetBounds(ClientSize.Width - 24 - 120, ClientSize.Height - 24 - 34, 120, 34);
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

        int Measure() { return Flow(null); }

        static bool IsHeader(string s) { return s.Length > 0 && s == s.ToUpperInvariant() && char.IsLetter(s[0]); }

        int Flow(Graphics g)
        {
            int y = 24, w = 760 - 56;
            const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            foreach (var raw in lines)
            {
                string s = raw.TrimEnd();
                if (s.Length == 0) { y += 10; continue; }
                if (IsHeader(s))
                {
                    if (g != null) Theme.Draw(g, s, Theme.Caps, Theme.Accent, new Rectangle(28, y, w, 20), TextFormatFlags.Left);
                    y += 24;
                    continue;
                }
                bool bullet = s.StartsWith("• ");
                string body = bullet ? s.Substring(2) : s;
                int x = bullet ? 44 : 28;
                int h = TextRenderer.MeasureText(body, Font, new Size(w - (x - 28), 1000), flags).Height;
                if (g != null)
                {
                    if (bullet) using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, 31, y + 7, 5, 5);
                    Theme.Draw(g, body, Font, Theme.Text, new Rectangle(x, y, w - (x - 28), h), flags);
                }
                y += h + 4;
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
