using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace Windkanal
{
    /// <summary>
    /// Kleines Startfenster mit Ladeanimation. Läuft in einem eigenen Thread, damit die Animation
    /// flüssig bleibt, während das Hauptfenster (und die GPU) im Haupt-Thread vorbereitet wird.
    /// </summary>
    sealed class Splash : Form
    {
        const int W = 480, H = 300, Banner = 158;

        // eigene Schriften, weil dieses Fenster in einem anderen Thread zeichnet als das Hauptfenster
        readonly Font fTitle = new Font("Segoe UI Semibold", 16f), fBase = new Font("Segoe UI", 9f), fSmall = new Font("Segoe UI", 8.25f);
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 15 };
        string status = "Wird gestartet …";
        float target = 0.05f, shown;
        bool closing;
        double closeAt;

        Splash()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(W, H);
            BackColor = Theme.Card;
            Text = "Windkanal 2D";
            ShowInTaskbar = true;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            timer.Tick += OnTick;
            Load += delegate { timer.Start(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                return cp;
            }
        }

        /// <summary>Startfenster in eigenem Thread öffnen. Gibt null zurück, wenn das nicht klappt.</summary>
        public static Splash Start()
        {
            try
            {
                Splash s = null;
                var ready = new ManualResetEvent(false);
                var t = new Thread(() =>
                {
                    s = new Splash();
                    s.Shown += delegate { ready.Set(); };
                    Application.Run(s);
                    ready.Set();
                });
                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
                ready.WaitOne(3000);
                return s != null && s.IsHandleCreated ? s : null;
            }
            catch { return null; }
        }

        /// <summary>Fortschritt melden (aus dem Haupt-Thread).</summary>
        public void Report(string text, float progress)
        {
            Post(() => { status = text; target = Math.Max(target, progress); });
        }

        /// <summary>Wartet, bis das Startfenster mindestens <paramref name="minMs"/> zu sehen war und der Balken voll ist.</summary>
        public void WaitReady(int minMs)
        {
            Post(() => { status = "Bereit"; target = 1; });
            var sw = Stopwatch.StartNew();
            while ((clock.ElapsedMilliseconds < minMs || shown < 0.98f) && sw.ElapsedMilliseconds < 4000 && !IsDisposed)
                Thread.Sleep(15);
        }

        /// <summary>Langsam ausblenden und schließen (aus dem Haupt-Thread).</summary>
        public void FadeOut()
        {
            Post(() => { closing = true; closeAt = clock.Elapsed.TotalMilliseconds; });
        }

        void Post(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }

        void OnTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            // Balken läuft weich hinterher und kriecht langsam weiter, solange nichts gemeldet wird
            if (target < 0.9f) target += 0.0008f;
            shown += (target - shown) * 0.09f;
            if (closing)
            {
                double f = 1 - (now - closeAt) / 220.0;
                if (f <= 0) { timer.Stop(); Close(); return; }
                Opacity = f;
            }
            else if (Opacity < 1) Opacity = Math.Min(1, now / 220.0);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            float t = (float)(clock.Elapsed.TotalSeconds);
            DrawBanner(g, t);

            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, W - 1, H - 1);

            Theme.DrawLogo(g, 28, Banner + 24, 44);
            Theme.Draw(g, "Windkanal 2D", fTitle, Theme.Text, new Rectangle(84, Banner + 20, 360, 30), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "Lattice-Boltzmann-Strömungssimulation", fBase, Theme.Muted, new Rectangle(85, Banner + 48, 360, 20), TextFormatFlags.VerticalCenter);

            // Fortschrittsbalken
            var bar = new RectangleF(28, H - 44, W - 56, 4);
            Theme.FillRound(g, Theme.Track, bar, 2);
            if (shown > 0.01f)
            {
                var fill = new RectangleF(bar.X, bar.Y, bar.Width * Math.Min(1, shown), bar.Height);
                Theme.FillRound(g, Theme.Accent, fill, 2);
                // wandernder Glanz auf dem Balken
                float gx = fill.X + (t * 260 % (fill.Width + 80)) - 40;
                using (var shine = new LinearGradientBrush(new RectangleF(gx - 40, 0, 80, 1), Color.FromArgb(0, Color.White), Color.FromArgb(0, Color.White), 0f))
                {
                    var blend = new ColorBlend
                    {
                        Colors = new[] { Color.FromArgb(0, Color.White), Color.FromArgb(150, Color.White), Color.FromArgb(0, Color.White) },
                        Positions = new[] { 0f, 0.5f, 1f }
                    };
                    shine.InterpolationColors = blend;
                    g.SetClip(fill);
                    g.FillRectangle(shine, gx - 40, fill.Y, 80, fill.Height);
                    g.ResetClip();
                }
            }
            Theme.Draw(g, status, fSmall, Theme.Muted, new Rectangle(28, H - 34, W - 140, 20), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, (int)Math.Round(Math.Min(1, shown) * 100) + " %", fSmall, Theme.Faint, new Rectangle(W - 128, H - 34, 100, 20),
                       TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        /// <summary>Animierter Mini-Windkanal: Stromlinien um einen Zylinder mit wanderndem Rauch.</summary>
        static void DrawBanner(Graphics g, float t)
        {
            var r = new RectangleF(0, 0, W, Banner);
            using (var bg = new LinearGradientBrush(r, Color.FromArgb(28, 44, 78), Color.FromArgb(12, 17, 28), 90f))
                g.FillRectangle(bg, r);

            float cx = 150, cy = Banner / 2f, rad = 20;
            const int lines = 11;
            for (int i = 0; i < lines; i++)
            {
                float y0 = 12 + (Banner - 24) * i / (lines - 1f);
                float side = y0 < cy ? -1 : 1;
                float dist = Math.Abs(y0 - cy);
                float push = 34 * (float)Math.Exp(-dist * dist / 900f) + 4;

                // Linie
                var pts = new PointF[60];
                for (int k = 0; k < pts.Length; k++)
                {
                    float x = W * k / (pts.Length - 1f);
                    pts[k] = new PointF(x, LineY(x, y0, side, push, cx, cy, t, i));
                }
                using (var p = new Pen(Color.FromArgb(38, 160, 200, 255), 1f)) g.DrawLines(p, pts);

                // Rauchteilchen, die entlang der Linie wandern
                for (int k = 0; k < 9; k++)
                {
                    float x = (t * 120 + k * (W / 8f) + i * 23) % (W + 40) - 20;
                    float y = LineY(x, y0, side, push, cx, cy, t, i);
                    float d = (x - cx) / 60f;
                    float speed = 0.5f + 0.5f * (float)Math.Exp(-d * d) * (float)Math.Exp(-dist * dist / 1600f);
                    var col = Speed(speed);
                    float s = 2.2f + speed * 1.4f;
                    using (var b = new SolidBrush(Color.FromArgb(70, col))) g.FillEllipse(b, x - s * 1.8f, y - s * 1.8f, s * 3.6f, s * 3.6f);
                    using (var b = new SolidBrush(col)) g.FillEllipse(b, x - s / 2, y - s / 2, s, s);
                }
            }
            using (var b = new SolidBrush(Color.FromArgb(40, 255, 255, 255))) g.FillEllipse(b, cx - rad - 5, cy - rad - 5, 2 * rad + 10, 2 * rad + 10);
            using (var b = new SolidBrush(Color.FromArgb(230, 233, 238))) g.FillEllipse(b, cx - rad, cy - rad, 2 * rad, 2 * rad);
        }

        static float LineY(float x, float y0, float side, float push, float cx, float cy, float t, int i)
        {
            float d = (x - cx) / 46f;
            float y = y0 + side * push * (float)Math.Exp(-d * d);
            if (x > cx)   // Wirbelstraße hinter dem Zylinder
            {
                float behind = Math.Min(1, (x - cx) / 120f);
                float near = (float)Math.Exp(-(y0 - cy) * (y0 - cy) / 2500f);
                y += behind * near * 9 * (float)Math.Sin(x * 0.045f - t * 5.0f);
            }
            return y;
        }

        static Color Speed(float v)
        {
            // blau -> grün -> orange, wie die Geschwindigkeitsfarben in der App
            v = Math.Max(0, Math.Min(1, v));
            Color a = Color.FromArgb(84, 200, 255), b = Color.FromArgb(106, 253, 98), c = Color.FromArgb(255, 159, 67);
            return v < 0.75f ? Theme.Mix(a, b, Math.Max(0, (v - 0.5f) / 0.25f)) : Theme.Mix(b, c, (v - 0.75f) / 0.25f);
        }
    }
}
