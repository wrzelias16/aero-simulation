using System;
using System.Diagnostics;
using System.Drawing;
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
        const int W = 520, H = 330, Pad = 18, StageH = 172;

        // eigene Schriften, weil dieses Fenster in einem anderen Thread zeichnet als das Hauptfenster
        readonly Font fWord = Theme.Medium(16f), fSmall = Theme.Regular(9f), fSmallMed = Theme.Medium(8.75f);
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
            Text = "Windkanal";
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

        /// <summary>Ausblenden und schließen (aus dem Haupt-Thread).</summary>
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
            if (target < 0.9f) target += 0.0008f;   // kriecht langsam weiter, solange nichts gemeldet wird
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
            float t = (float)clock.Elapsed.TotalSeconds;
            using (var p = new Pen(Theme.Border)) g.DrawRectangle(p, 0, 0, W - 1, H - 1);

            var stage = new RectangleF(Pad, Pad, W - 2 * Pad, StageH);
            Theme.FillRound(g, Theme.Surface, stage, 16);
            DrawStage(g, stage, t);

            int ty = Pad + StageH + 20;
            Theme.DrawLogo(g, Pad + 2, ty, 42, Theme.Ink, Theme.OnInk);
            Theme.Draw(g, "windkanal", fWord, Theme.Text, new Rectangle(Pad + 56, ty - 2, 240, 28), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "Strömungssimulation in 2D und 3D", fSmall, Theme.Muted, new Rectangle(Pad + 57, ty + 24, 240, 18), TextFormatFlags.VerticalCenter);
            string ver = "Version " + AppInfo.Version;
            int vw = TextRenderer.MeasureText(ver, fSmallMed, Size.Empty, TextFormatFlags.NoPadding).Width + 20;
            Theme.FillRound(g, Theme.Ctl, new RectangleF(W - Pad - vw, ty + 9, vw, 24), 12);
            Theme.Draw(g, ver, fSmallMed, Theme.Muted, new Rectangle(W - Pad - vw, ty + 9, vw, 24), TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);

            // Fortschritt als Kapseln
            Theme.Capsules(g, new RectangleF(Pad + 2, H - 52, W - 2 * Pad - 4, 12), 40, Math.Min(1, shown), Theme.Ink, Theme.Track);
            Theme.Draw(g, status, fSmall, Theme.Muted, new Rectangle(Pad + 2, H - 34, W - 140, 20), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, (int)Math.Round(Math.Min(1, shown) * 100) + " %", fSmallMed, Theme.Text, new Rectangle(W - Pad - 102, H - 34, 100, 20),
                       TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        /// <summary>Mini-Windkanal: Stromlinien um einen Zylinder, Rauchteilchen wandern mit, dahinter pendelt die Wirbelstraße.</summary>
        static void DrawStage(Graphics g, RectangleF r, float t)
        {
            var clip = g.Clip;
            using (var path = Theme.Round(r, 16)) g.SetClip(path);
            float cx = r.X + 130, cy = r.Y + r.Height / 2, rad = 19;
            const int lines = 9;
            for (int i = 0; i < lines; i++)
            {
                float y0 = r.Y + 18 + (r.Height - 36) * i / (lines - 1f);
                var pts = new PointF[64];
                for (int k = 0; k < pts.Length; k++)
                {
                    float x = r.X + r.Width * k / (pts.Length - 1f);
                    pts[k] = new PointF(x, LineY(x, y0, cx, cy, t));
                }
                using (var p = new Pen(Theme.Track, 1.2f)) g.DrawLines(p, pts);

                for (int k = 0; k < 7; k++)
                {
                    float x = r.X + ((t * 110 + k * (r.Width / 6.5f) + i * 37) % (r.Width + 30)) - 15;
                    float y = LineY(x, y0, cx, cy, t);
                    float d = (x - cx) / 55f, dy = (y0 - cy) / 40f;
                    float fast = (float)(Math.Exp(-d * d) * Math.Exp(-dy * dy));
                    float s = 4 + 2.5f * fast;
                    Color c = fast > 0.45f ? Theme.Pink : Theme.Accent;   // schnell am Zylinder vorbei: pink
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, x - s / 2, y - s / 2, s, s);
                }
            }
            using (var b = new SolidBrush(Theme.Ink)) g.FillEllipse(b, cx - rad, cy - rad, 2 * rad, 2 * rad);
            g.Clip = clip;
        }

        static float LineY(float x, float y0, float cx, float cy, float t)
        {
            float d = (x - cx) / 44f;
            float dist = y0 - cy;
            float side = dist < 0 ? -1 : 1;
            float push = 30 * (float)Math.Exp(-dist * dist / 900f) + 3;
            float y = y0 + side * push * (float)Math.Exp(-d * d);
            if (x > cx)   // Wirbelstraße hinter dem Zylinder
            {
                float behind = Math.Min(1, (x - cx) / 110f);
                float near = (float)Math.Exp(-dist * dist / 2500f);
                y += behind * near * 9 * (float)Math.Sin((x - cx) * 0.045f - t * 5.0f);
            }
            return y;
        }
    }
}
