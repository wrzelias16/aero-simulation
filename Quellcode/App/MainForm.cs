using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Windkanal
{
    sealed class Canvas : Control
    {
        public Canvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
    }

    public sealed class MainForm : Form
    {
        static readonly Color CBg = Color.FromArgb(18, 21, 26);
        static readonly Color CPanel = Color.FromArgb(29, 33, 40);
        static readonly Color CCtl = Color.FromArgb(42, 47, 56);
        static readonly Color CBorder = Color.FromArgb(62, 68, 80);
        static readonly Color CText = Color.FromArgb(226, 229, 234);
        static readonly Color CMuted = Color.FromArgb(145, 152, 164);
        static readonly Color CAccent = Color.FromArgb(77, 163, 255);
        static readonly Color CWarn = Color.FromArgb(255, 196, 87);
        static readonly Color CCd = Color.FromArgb(255, 159, 67);
        static readonly Color CCl = Color.FromArgb(84, 200, 255);

        static readonly int[] ResNX = { 400, 600, 900, 1200 };
        static readonly int[] ResNY = { 160, 240, 360, 480 };
        static readonly string[] ResNames =
        {
            "Niedrig (400 × 160) – schnell", "Mittel (600 × 240)",
            "Hoch (900 × 360)", "Sehr hoch (1200 × 480) – genau"
        };

        const double NuAir = 1.516e-5;   // m²/s bei 20 °C
        const double RhoAir = 1.204;     // kg/m³ bei 20 °C
        static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

        Solver solver;
        Particles particles;
        readonly Renderer renderer = new Renderer();
        readonly ForceStats stats = new ForceStats(400000);

        ShapeKind shape = ShapeKind.Zylinder;
        bool[] customMask;
        int resIndex = 1, sizePercent = 10, angleDeg = 0;
        double reynolds = 100;
        float refLen = 24, frontalCells;
        bool running = true, showSmoke = true, dirty = true, suppress;
        ViewMode viewMode = ViewMode.Geschwindigkeit;

        Canvas view, graph;
        Label status, lblAngle, lblSize, lblRe, lblResults, lblPhys, lblHint;
        ComboBox cbShape, cbRes, cbView;
        TrackBar tbAngle, tbSize, tbRe;
        CheckBox chkSmoke, chkWalls;
        Button btnRun;
        NumericUpDown numMeters;
        Panel side;
        int layoutY;

        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastUi, fpsClock, stepMsAcc, mlups, fps;
        long stepsAcc;
        int frames;
        string warning;
        double warningUntil;
        Point mouse = new Point(-1, -1);
        int drawButton;
        PointF lastDraw;
        bool hasLastDraw;

        public MainForm()
        {
            Text = "Windkanal 2D";
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1480, wa.Width - 40), Math.Min(940, wa.Height - 60));
            MinimumSize = new Size(1040, 680);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = CBg;
            ForeColor = CText;
            Font = new Font("Segoe UI", 9f);
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            BuildUi();
            CreateSolver();
            RebuildGeometry(true);

            timer.Interval = 1;
            timer.Tick += OnTick;
            Shown += delegate { timer.Start(); };
            FormClosing += delegate { timer.Stop(); };
            KeyDown += OnKey;
        }

        // ------------------------------------------------------------------ UI

        void BuildUi()
        {
            view = new Canvas { Dock = DockStyle.Fill, BackColor = CBg, Cursor = Cursors.Cross };
            view.Paint += OnViewPaint;
            view.Resize += delegate { dirty = true; };
            view.MouseDown += OnViewMouseDown;
            view.MouseMove += OnViewMouseMove;
            view.MouseUp += delegate { drawButton = 0; hasLastDraw = false; };
            view.MouseLeave += delegate { mouse = new Point(-1, -1); UpdateStatus(); };

            graph = new Canvas { Dock = DockStyle.Bottom, Height = 160, BackColor = CPanel };
            graph.Paint += OnGraphPaint;

            status = new Label
            {
                Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(23, 26, 32), ForeColor = CMuted,
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(12, 0, 0, 0)
            };

            side = new Panel { Dock = DockStyle.Right, Width = 330, BackColor = CPanel, AutoScroll = true };

            Controls.Add(view);
            Controls.Add(graph);
            Controls.Add(status);
            Controls.Add(side);

            layoutY = 16;
            var title = AddLabel("Windkanal 2D", 30);
            title.Font = new Font("Segoe UI Semibold", 15f);
            var sub = AddLabel("Lattice-Boltzmann-Strömungssimulation", 18);
            sub.ForeColor = CMuted;
            layoutY += 6;

            AddHeader("OBJEKT");
            cbShape = AddCombo();
            foreach (var k in Shapes.All) cbShape.Items.Add(Shapes.Name(k));
            cbShape.SelectedIndex = 0;
            cbShape.SelectedIndexChanged += OnShapeChanged;

            lblAngle = AddLabel("", 18);
            tbAngle = AddTrack(-90, 90, 0);
            tbAngle.ValueChanged += delegate
            {
                angleDeg = tbAngle.Value; UpdateSliderLabels();
                if (!suppress) RebuildGeometry(false);
            };
            lblSize = AddLabel("", 18);
            tbSize = AddTrack(4, 60, sizePercent);
            tbSize.ValueChanged += delegate
            {
                sizePercent = tbSize.Value; UpdateSliderLabels();
                if (!suppress) RebuildGeometry(false);
            };
            var btnClear = AddButton("Leere Fläche zum Zeichnen", 280);
            btnClear.Click += delegate
            {
                customMask = new bool[solver.N];
                SelectShape(ShapeKind.Eigene);
                RebuildGeometry(true);
            };
            lblHint = AddLabel("Mit der Maus direkt ins Bild zeichnen:\nlinke Taste = Wand, rechte Taste = radieren.", 34);
            lblHint.ForeColor = CMuted;

            AddHeader("STRÖMUNG");
            lblRe = AddLabel("", 18);
            tbRe = AddTrack(0, 200, ReToSlider(reynolds));
            tbRe.ValueChanged += delegate
            {
                reynolds = SliderToRe(tbRe.Value); UpdateSliderLabels();
                if (!suppress) { UpdateFlowParams(); stats.Clear(); }
            };
            AddLabel("Auflösung (Rechengitter)", 18);
            cbRes = AddCombo();
            foreach (var r in ResNames) cbRes.Items.Add(r);
            cbRes.SelectedIndex = resIndex;
            cbRes.SelectedIndexChanged += delegate
            {
                if (suppress) return;
                resIndex = cbRes.SelectedIndex;
                CreateSolver();
                RebuildGeometry(true);
            };
            chkWalls = AddCheck("Tunnelwände mit Reibung (Haftbedingung)", false);
            chkWalls.CheckedChanged += delegate { solver.NoSlipWalls = chkWalls.Checked; stats.Clear(); };

            AddHeader("DARSTELLUNG");
            cbView = AddCombo();
            cbView.Items.AddRange(new object[] { "Geschwindigkeit", "Druck (cp)", "Wirbelstärke", "Nur Rauch" });
            cbView.SelectedIndex = 0;
            cbView.SelectedIndexChanged += delegate { viewMode = (ViewMode)cbView.SelectedIndex; dirty = true; };
            chkSmoke = AddCheck("Rauchlinien anzeigen", true);
            chkSmoke.CheckedChanged += delegate { showSmoke = chkSmoke.Checked; particles.Clear(); dirty = true; };

            btnRun = AddButton("Pause", 136);
            var btnReset = new Button();
            StyleButton(btnReset, "Neu starten");
            btnReset.SetBounds(16 + 144, btnRun.Top, 136, btnRun.Height);
            side.Controls.Add(btnReset);
            btnRun.Click += delegate { SetRunning(!running); };
            btnReset.Click += delegate { ResetFlow(); };

            AddHeader("MESSWERTE");
            lblResults = AddLabel("", 136);
            lblResults.Font = new Font("Consolas", 9.5f);

            AddHeader("UMRECHNUNG AUF LUFT (20 °C)");
            AddLabel("Echte Bezugslänge des Objekts in Metern:", 18);
            numMeters = new NumericUpDown
            {
                DecimalPlaces = 3, Minimum = 0.001m, Maximum = 100m, Increment = 0.01m, Value = 0.1m,
                BackColor = CCtl, ForeColor = CText, BorderStyle = BorderStyle.FixedSingle
            };
            numMeters.SetBounds(16, layoutY, 120, 24);
            side.Controls.Add(numMeters);
            layoutY += 32;
            numMeters.ValueChanged += delegate { UpdateReadouts(); };
            lblPhys = AddLabel("", 88);
            lblPhys.Font = new Font("Consolas", 9.5f);

            var btnInfo = AddButton("Physik && Grenzen der Simulation …", 280);
            btnInfo.Click += delegate { ShowInfo(); };
            layoutY += 10;
            AddLabel("", 4);

            UpdateSliderLabels();
        }

        Label AddLabel(string text, int h)
        {
            var l = new Label { Text = text, ForeColor = CText, AutoSize = false };
            l.SetBounds(16, layoutY, 290, h);
            side.Controls.Add(l);
            layoutY += h + 4;
            return l;
        }

        void AddHeader(string text)
        {
            layoutY += 10;
            var sep = new Panel { BackColor = CBorder };
            sep.SetBounds(16, layoutY, 280, 1);
            side.Controls.Add(sep);
            layoutY += 8;
            var l = AddLabel(text, 18);
            l.ForeColor = CAccent;
            l.Font = new Font("Segoe UI Semibold", 8.5f);
        }

        ComboBox AddCombo()
        {
            var c = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = CCtl, ForeColor = CText
            };
            c.SetBounds(16, layoutY, 280, 24);
            side.Controls.Add(c);
            layoutY += 32;
            return c;
        }

        TrackBar AddTrack(int min, int max, int val)
        {
            var t = new TrackBar
            {
                Minimum = min, Maximum = max, Value = val, TickStyle = TickStyle.None,
                AutoSize = false, BackColor = CPanel, SmallChange = 1, LargeChange = 5
            };
            t.SetBounds(10, layoutY, 292, 28);
            side.Controls.Add(t);
            layoutY += 32;
            return t;
        }

        CheckBox AddCheck(string text, bool val)
        {
            var c = new CheckBox { Text = text, Checked = val, ForeColor = CText, FlatStyle = FlatStyle.Flat };
            c.SetBounds(16, layoutY, 290, 22);
            side.Controls.Add(c);
            layoutY += 26;
            return c;
        }

        Button AddButton(string text, int width)
        {
            var b = new Button();
            StyleButton(b, text);
            b.SetBounds(16, layoutY, width, 30);
            side.Controls.Add(b);
            layoutY += 38;
            return b;
        }

        void StyleButton(Button b, string text)
        {
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = CCtl;
            b.ForeColor = CText;
            b.FlatAppearance.BorderColor = CBorder;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(52, 58, 69);
            b.Cursor = Cursors.Hand;
        }

        static double SliderToRe(int v)
        {
            double re = 10 * Math.Pow(2000, v / 200.0);
            double mag = Math.Pow(10, Math.Floor(Math.Log10(re)) - 1);
            return Math.Round(re / mag) * mag;
        }

        static int ReToSlider(double re)
        {
            return (int)Math.Round(200 * Math.Log(re / 10) / Math.Log(2000));
        }

        void UpdateSliderLabels()
        {
            lblAngle.Text = "Anstellwinkel: " + angleDeg + "°";
            lblSize.Text = "Größe: " + sizePercent + " % der Tunnelhöhe";
            lblRe.Text = "Reynoldszahl: Re = " + reynolds.ToString("#,0", De);
            bool custom = shape == ShapeKind.Eigene;
            tbAngle.Enabled = !custom;
            tbSize.Enabled = !custom;
        }

        void SelectShape(ShapeKind k)
        {
            suppress = true;
            shape = k;
            cbShape.SelectedIndex = Array.IndexOf(Shapes.All, k);
            suppress = false;
            UpdateSliderLabels();
        }

        void OnShapeChanged(object sender, EventArgs e)
        {
            if (suppress) return;
            var k = Shapes.All[cbShape.SelectedIndex];
            if (k == ShapeKind.Eigene)
            {
                // aktuelle Form als Ausgangspunkt zum Weiterzeichnen übernehmen
                customMask = (bool[])solver.Solid.Clone();
                shape = k;
                UpdateSliderLabels();
                RebuildGeometry(false);
                return;
            }
            shape = k;
            suppress = true;
            sizePercent = Shapes.DefaultSizePercent(k);
            tbSize.Value = sizePercent;
            angleDeg = k == ShapeKind.Naca0012 || k == ShapeKind.Naca2412 || k == ShapeKind.Naca4412 ? 5 : 0;
            tbAngle.Value = angleDeg;
            reynolds = Shapes.DefaultReynolds(k);
            tbRe.Value = ReToSlider(reynolds);
            reynolds = SliderToRe(tbRe.Value);
            suppress = false;
            UpdateSliderLabels();
            RebuildGeometry(true);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space && !(ActiveControl is NumericUpDown))
            {
                SetRunning(!running);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        void SetRunning(bool r)
        {
            running = r;
            btnRun.Text = r ? "Pause" : "Start";
        }

        // ------------------------------------------------------------ Simulation

        void CreateSolver()
        {
            bool[] oldMask = solver != null ? solver.Solid : null;
            int oldNx = solver != null ? solver.NX : 0, oldNy = solver != null ? solver.NY : 0;
            solver = new Solver(ResNX[resIndex], ResNY[resIndex]);
            solver.NoSlipWalls = chkWalls != null && chkWalls.Checked;
            particles = new Particles(solver.NX, solver.NY);

            // eigene Zeichnung auf das neue Gitter übertragen
            if (shape == ShapeKind.Eigene && oldMask != null)
            {
                customMask = new bool[solver.N];
                for (int y = 0; y < solver.NY; y++)
                    for (int x = 0; x < solver.NX; x++)
                    {
                        int ox = x * oldNx / solver.NX, oy = y * oldNy / solver.NY;
                        customMask[y * solver.NX + x] = oldMask[oy * oldNx + ox];
                    }
            }
        }

        void RebuildGeometry(bool resetFlow)
        {
            int nx = solver.NX, ny = solver.NY;
            var mask = new bool[nx * ny];
            float sizeCells = sizePercent / 100f * ny;
            if (shape == ShapeKind.Eigene)
            {
                if (customMask != null && customMask.Length == mask.Length) Array.Copy(customMask, mask, mask.Length);
            }
            else
            {
                float px = (shape == ShapeKind.Auto ? 0.3f : 0.25f) * solver.VisibleNX;
                float py = (ny - 1) / 2f;
                var pts = Shapes.Transform(Shapes.Polygon(shape), sizeCells, angleDeg, px, py);
                if (shape == ShapeKind.Auto)
                {
                    // Auto knapp über dem (reibungsfreien = mitbewegten) Boden platzieren
                    float minY = float.MaxValue;
                    foreach (var p in pts) minY = Math.Min(minY, p.Y);
                    float shift = 3f - minY;
                    for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(pts[i].X, pts[i].Y + shift);
                }
                Shapes.Fill(pts, mask, nx, ny);
            }

            int rows = 0;
            for (int y = 0; y < ny; y++)
                for (int x = 2; x < nx - 2; x++)
                    if (mask[y * nx + x]) { rows++; break; }
            frontalCells = rows;
            refLen = Shapes.RefIsSize(shape) ? sizeCells : Math.Max(rows, 0);
            if (refLen < 2) refLen = 0.1f * ny;

            solver.ApplyMask(mask);
            UpdateFlowParams();
            if (resetFlow)
            {
                solver.Reset();
                particles.Clear();
            }
            stats.Clear();
            dirty = true;
        }

        void UpdateFlowParams()
        {
            double L = Math.Max(2.0, refLen);
            solver.U0 = Solver.ChooseU0(reynolds, L, frontalCells / (double)solver.NY);
            solver.Nu = (float)(solver.U0 * L / reynolds);
        }

        void ResetFlow()
        {
            solver.Reset();
            particles.Clear();
            stats.Clear();
            dirty = true;
        }

        double ConvectiveTime { get { return refLen / solver.U0; } }

        void OnTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            if (running)
            {
                var sw = Stopwatch.StartNew();
                int steps = 0;
                float q = 0.5f * solver.U0 * solver.U0 * refLen;
                do
                {
                    solver.Step();
                    stats.Add((float)(solver.Fx / q), (float)(solver.Fy / q));
                    steps++;
                } while (sw.Elapsed.TotalMilliseconds < 22 && steps < 500);
                stepMsAcc += sw.Elapsed.TotalMilliseconds;
                stepsAcc += steps;

                if (!solver.IsStable())
                {
                    ResetFlow();
                    warning = "Simulation wurde instabil und neu gestartet. Tipp: Reynoldszahl senken oder Auflösung erhöhen.";
                    warningUntil = now + 9000;
                    return;
                }
                if (showSmoke) particles.Update(solver, steps, 2.5f * solver.NX / solver.U0);
                dirty = true;
            }

            if (dirty && view.ClientSize.Width > 0 && view.ClientSize.Height > 0)
            {
                renderer.EnsureSize(view.ClientSize.Width, view.ClientSize.Height);
                renderer.Render(solver, particles, viewMode, showSmoke, refLen);
                view.Invalidate();
                dirty = false;
                frames++;
            }

            if (now - fpsClock >= 1000)
            {
                double dt = now - fpsClock;
                fps = frames * 1000.0 / dt;
                mlups = stepMsAcc > 0 ? solver.N * (double)stepsAcc / (stepMsAcc * 1000.0) : 0;
                frames = 0; stepsAcc = 0; stepMsAcc = 0; fpsClock = now;
            }
            if (now - lastUi >= 250)
            {
                lastUi = now;
                UpdateReadouts();
                UpdateStatus();
                graph.Invalidate();
            }
        }

        // ------------------------------------------------------------ Anzeige

        static string F(double v, string fmt) { return v.ToString(fmt, De); }

        static string Sig(double v)
        {
            double a = Math.Abs(v);
            if (a == 0) return "0";
            if (a >= 1000) return v.ToString("#,0", De);
            if (a >= 100) return v.ToString("0", De);
            if (a >= 10) return v.ToString("0.0", De);
            if (a >= 1) return v.ToString("0.00", De);
            if (a >= 0.01) return v.ToString("0.000", De);
            return v.ToString("0.0E+0", De);
        }

        void UpdateReadouts()
        {
            double T = ConvectiveTime;
            double tStar = solver.Steps / T;
            int transient = (int)(15 * T);
            int window = Math.Min((int)(40 * T), stats.Count - transient);
            bool settled = window > 5 * T;

            double cd = stats.Count > 0 ? stats.Cd(0) : 0, cl = stats.Count > 0 ? stats.Cl(0) : 0;
            double mcd = 0, mcl = 0, freq = 0;
            if (settled) stats.Analyze(window, (int)(0.5 * T), out mcd, out mcl, out freq);
            string avg = settled ? "" : "  (läuft ein…)";

            string ld = "–";
            if (settled && Math.Abs(mcd) > 1e-3) ld = F(mcl / mcd, "0.0");
            string st = settled && freq > 0 ? F(freq * T, "0.000") : "–";

            lblResults.Text =
                "                jetzt     Mittel Ø\n" +
                "Widerstand cw  " + F(cd, "0.000").PadLeft(7) + "   " + (settled ? F(mcd, "0.000").PadLeft(7) : "   –") + "\n" +
                "Auftrieb   ca  " + F(cl, "0.000").PadLeft(7) + "   " + (settled ? F(mcl, "0.000").PadLeft(7) : "   –") + "\n" +
                "Gleitzahl ca/cw = " + ld + "\n" +
                "Strouhal-Zahl St = " + st + "\n" +
                "Versperrung      = " + F(100.0 * frontalCells / solver.NY, "0.0") + " %\n" +
                "Bezugslänge = " + F(refLen, "0") + " Zellen (" + Shapes.RefName(shape) + ")\n" +
                "Zeit t·U/L  = " + F(tStar, "0.0") + avg + "\n" +
                "Leistung    = " + F(mlups, "0") + " MLUPS · " + F(fps, "0") + " FPS";

            double Lm = (double)numMeters.Value;
            double v = reynolds * NuAir / Lm;
            double qd = 0.5 * RhoAir * v * v;
            double useCd = settled ? mcd : cd, useCl = settled ? mcl : cl;
            lblPhys.Text =
                "Anströmung  v  = " + Sig(v) + " m/s\n" +
                "                 (" + Sig(v * 3.6) + " km/h)\n" +
                "Widerstand  FW = " + Sig(useCd * qd * Lm) + " N/m\n" +
                "Auftrieb    FA = " + Sig(useCl * qd * Lm) + " N/m\n" +
                "(Kräfte je Meter Spannweite)";
        }

        void UpdateStatus()
        {
            string text = "";
            if (mouse.X >= 0 && solver != null)
            {
                var g = renderer.ToGrid(solver, mouse.X, mouse.Y);
                int x = (int)Math.Round(g.X), y = (int)Math.Round(g.Y);
                if (x >= 0 && y >= 0 && x < solver.NX && y < solver.NY)
                {
                    int c = y * solver.NX + x;
                    if (solver.Solid[c]) text = "Cursor: im Körper";
                    else
                    {
                        double u = Math.Sqrt(solver.Ux[c] * solver.Ux[c] + solver.Uy[c] * solver.Uy[c]) / solver.U0;
                        double cp = (solver.Rho[c] - solver.RhoInf) / 3.0 / (0.5 * solver.U0 * solver.U0);
                        double vInf = reynolds * NuAir / (double)numMeters.Value;
                        text = "Cursor:   |u| / U∞ = " + F(u, "0.00") + "   ·   cp = " + F(cp, "0.00") +
                               "   ·   ≈ " + Sig(u * vInf) + " m/s";
                    }
                }
            }
            if (warning != null && clock.Elapsed.TotalMilliseconds < warningUntil)
            {
                status.ForeColor = CWarn;
                status.Text = "⚠ " + warning;
                return;
            }
            status.ForeColor = CMuted;
            status.Text = text.Length > 0 ? text
                : (running ? "Läuft" : "Pausiert") + "   ·   Leertaste = Start/Pause   ·   Maus links = Wand zeichnen, rechts = radieren";
        }

        void OnViewPaint(object sender, PaintEventArgs e)
        {
            if (renderer.Bitmap == null) return;
            e.Graphics.DrawImageUnscaled(renderer.Bitmap, 0, 0);
            DrawLegend(e.Graphics);
        }

        void DrawLegend(Graphics g)
        {
            if (viewMode == ViewMode.Rauch) return;
            string title, lo, mid, hi;
            float tMid = 0.5f;
            switch (viewMode)
            {
                case ViewMode.Druck: title = "Druckbeiwert cp"; lo = "−2"; mid = "0"; hi = "+1"; break;
                case ViewMode.Wirbel: title = "Wirbelstärke ω·L/U∞"; lo = "−6 (im UZS)"; mid = "0"; hi = "+6"; break;
                default: title = "Geschwindigkeit |u| / U∞"; lo = "0"; mid = "1"; hi = "2"; break;
            }
            int w = 200, h = 10;
            int x0 = view.ClientSize.Width - w - 20, y0 = view.ClientSize.Height - 46;
            if (x0 < 10 || y0 < 10) return;
            using (var bg = new SolidBrush(Color.FromArgb(170, 14, 16, 20)))
                g.FillRectangle(bg, x0 - 10, y0 - 22, w + 20, 54);
            for (int i = 0; i < w; i++)
                using (var p = new Pen(Renderer.LutColor(viewMode, i / (float)(w - 1), renderer)))
                    g.DrawLine(p, x0 + i, y0, x0 + i, y0 + h);
            using (var f = new Font("Segoe UI", 8f))
            using (var b = new SolidBrush(CText))
            {
                g.DrawString(title, f, b, x0, y0 - 18);
                g.DrawString(lo, f, b, x0 - 2, y0 + h + 1);
                var ms = g.MeasureString(mid, f);
                g.DrawString(mid, f, b, x0 + w * tMid - ms.Width / 2, y0 + h + 1);
                var hs = g.MeasureString(hi, f);
                g.DrawString(hi, f, b, x0 + w - hs.Width + 2, y0 + h + 1);
            }
        }

        void OnGraphPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(CPanel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = graph.ClientRectangle;
            var plot = new Rectangle(r.Left + 56, r.Top + 30, r.Width - 56 - 20, r.Height - 30 - 26);
            using (var f = new Font("Segoe UI", 8.5f))
            using (var muted = new SolidBrush(CMuted))
            {
                g.DrawString("Kraftbeiwerte über der Zeit", new Font("Segoe UI Semibold", 9f), new SolidBrush(CText), r.Left + 12, r.Top + 7);
                using (var b1 = new SolidBrush(CCd)) g.FillRectangle(b1, r.Left + 210, r.Top + 13, 14, 3);
                g.DrawString("cw (Widerstand)", f, muted, r.Left + 228, r.Top + 7);
                using (var b2 = new SolidBrush(CCl)) g.FillRectangle(b2, r.Left + 340, r.Top + 13, 14, 3);
                g.DrawString("ca (Auftrieb)", f, muted, r.Left + 358, r.Top + 7);

                if (plot.Width < 20 || plot.Height < 20) return;
                double T = ConvectiveTime;
                int span = Math.Min(stats.Count, (int)(60 * T));
                if (span < 2)
                {
                    g.DrawString("Noch keine Daten", f, muted, plot.Left, plot.Top + plot.Height / 2 - 8);
                    return;
                }
                double lo = double.MaxValue, hi = double.MinValue;
                int stride = Math.Max(1, span / 2000);
                int skip = Math.Min(span - 1, (int)(3 * T)); // Einschwingspitze nicht skalieren
                for (int k = 0; k < span - skip; k += stride)
                {
                    double a = stats.Cd(k), b = stats.Cl(k);
                    lo = Math.Min(lo, Math.Min(a, b)); hi = Math.Max(hi, Math.Max(a, b));
                }
                lo = Math.Min(lo, 0); hi = Math.Max(hi, 0);
                double pad = Math.Max(0.1, (hi - lo) * 0.1);
                lo -= pad; hi += pad;

                using (var grid = new Pen(Color.FromArgb(50, 56, 66)))
                {
                    for (int i = 0; i <= 4; i++)
                    {
                        double v = lo + (hi - lo) * i / 4;
                        float yy = (float)(plot.Bottom - (v - lo) / (hi - lo) * plot.Height);
                        g.DrawLine(grid, plot.Left, yy, plot.Right, yy);
                        g.DrawString(F(v, "0.00"), f, muted, r.Left + 8, yy - 8);
                    }
                    float y0 = (float)(plot.Bottom - (0 - lo) / (hi - lo) * plot.Height);
                    using (var zero = new Pen(Color.FromArgb(90, 98, 112))) g.DrawLine(zero, plot.Left, y0, plot.Right, y0);
                }
                double spanT = span / T;
                g.DrawString("← letzte " + F(spanT, "0") + " Zeiteinheiten t·U/L", f, muted, plot.Left, plot.Bottom + 5);
                g.DrawString("jetzt", f, muted, plot.Right - 26, plot.Bottom + 5);

                DrawSeries(g, plot, span, lo, hi, true);
                DrawSeries(g, plot, span, lo, hi, false);
            }
        }

        void DrawSeries(Graphics g, Rectangle plot, int span, double lo, double hi, bool cd)
        {
            int n = Math.Min(plot.Width, span);
            if (n < 2) return;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                int back = (int)((long)(n - 1 - i) * (span - 1) / (n - 1));
                double v = cd ? stats.Cd(back) : stats.Cl(back);
                v = Math.Max(lo, Math.Min(hi, v));
                float x = plot.Right - (float)back / (span - 1) * plot.Width;
                pts[i] = new PointF(x, (float)(plot.Bottom - (v - lo) / (hi - lo) * plot.Height));
            }
            using (var p = new Pen(cd ? CCd : CCl, 1.6f)) g.DrawLines(p, pts);
        }

        // ------------------------------------------------------------ Zeichnen

        void OnViewMouseDown(object sender, MouseEventArgs e)
        {
            view.Focus();
            drawButton = e.Button == MouseButtons.Left ? 1 : e.Button == MouseButtons.Right ? 2 : 0;
            hasLastDraw = false;
            if (drawButton > 0) PaintAt(e.Location);
        }

        void OnViewMouseMove(object sender, MouseEventArgs e)
        {
            mouse = e.Location;
            if (drawButton > 0) PaintAt(e.Location);
            else if (!running) UpdateStatus();
        }

        void PaintAt(Point p)
        {
            var g = renderer.ToGrid(solver, p.X, p.Y);
            if (shape != ShapeKind.Eigene)
            {
                customMask = (bool[])solver.Solid.Clone();
                SelectShape(ShapeKind.Eigene);
            }
            if (customMask == null || customMask.Length != solver.N) customMask = new bool[solver.N];

            int nx = solver.NX, ny = solver.NY;
            int rad = Math.Max(1, ny / 70);
            bool add = drawButton == 1;
            if (!add) rad *= 2;
            PointF from = hasLastDraw ? lastDraw : g;
            float dist = (float)Math.Sqrt((g.X - from.X) * (g.X - from.X) + (g.Y - from.Y) * (g.Y - from.Y));
            int stepsN = Math.Max(1, (int)(dist / Math.Max(0.5f, rad * 0.5f)));
            bool changed = false;
            for (int s = 0; s <= stepsN; s++)
            {
                float t = s / (float)stepsN;
                int cx = (int)Math.Round(from.X + (g.X - from.X) * t);
                int cy = (int)Math.Round(from.Y + (g.Y - from.Y) * t);
                for (int dy = -rad; dy <= rad; dy++)
                    for (int dx = -rad; dx <= rad; dx++)
                    {
                        if (dx * dx + dy * dy > rad * rad + rad) continue;
                        int x = cx + dx, y = cy + dy;
                        if (x < 2 || x > solver.VisibleNX - 3 || y < 0 || y > ny - 1) continue;
                        int c = y * nx + x;
                        if (customMask[c] != add) { customMask[c] = add; changed = true; }
                    }
            }
            lastDraw = g;
            hasLastDraw = true;
            if (changed) RebuildGeometry(false);
        }

        // ------------------------------------------------------------ Info

        void ShowInfo()
        {
            string t =
                "SO RECHNET DIE SIMULATION\n" +
                "• Lattice-Boltzmann-Methode (D2Q9): löst die Navier-Stokes-Gleichungen für schwach kompressible Strömung.\n" +
                "• Turbulenzmodell: Smagorinsky-LES – kleine, nicht auflösbare Wirbel werden über eine Zusatzviskosität erfasst.\n" +
                "• Körperoberfläche: Bounce-Back (Haftbedingung). Kräfte über die Impulsaustausch-Methode.\n" +
                "• Einlass mit fester Geschwindigkeit, Auslass mit Dämpfungszone gegen Reflexionen.\n\n" +
                "BEGRIFFE\n" +
                "• Re = U·L/ν: Verhältnis von Trägheits- zu Zähigkeitskräften. Gleiche Re = gleiches Strömungsbild, egal ob Modell oder Original.\n" +
                "• cw, ca: Widerstands- und Auftriebsbeiwert, bezogen auf q·L (q = ½ρU²).\n" +
                "• St = f·L/U: dimensionslose Frequenz der Wirbelablösung.\n" +
                "• cp: Druckbeiwert. +1 = Staupunkt, negativ = Unterdruck (Sog).\n\n" +
                "VALIDIERUNG (Zylinder bei Re = 100, Kármánsche Wirbelstraße)\n" +
                "• Literatur: St ≈ 0,164–0,167, cw ≈ 1,33–1,40\n" +
                "• Diese App: St ≈ 0,166–0,172, cw ≈ 1,50–1,54\n" +
                "Die Frequenz stimmt sehr gut. Der Widerstand liegt ca. 10 % zu hoch: durch die Versperrung\n" +
                "(wie im echten Windkanal) und die treppenförmige Körperkontur im Rechengitter.\n" +
                "Höhere Auflösung und kleinere Objekte bringen die Werte näher an die Literatur.\n\n" +
                "GRENZEN\n" +
                "• 2D: Das Objekt ist unendlich lang (Profilschnitt). 3D-Effekte wie Randwirbel fehlen.\n" +
                "• Ab ca. Re > 1.000 ist echte Turbulenz dreidimensional; 2D-Werte sind dann Richtwerte, gut für Vergleiche (Form A vs. Form B).\n" +
                "• Höhere Auflösung = genauere Grenzschicht = verlässlichere Beiwerte.\n" +
                "• Die Mittelwerte stehen erst nach der Einlaufzeit (≈ 15 Zeiteinheiten) zur Verfügung.";
            MessageBox.Show(this, t, "Physik & Grenzen", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
