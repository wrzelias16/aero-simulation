using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace Windkanal
{
    public sealed class MainForm : Form
    {
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

        Canvas view, graph, header, statusBar;
        DropDown cbShape, cbRes, cbView;
        FlatSlider tbAngle, tbSize, tbRe;
        Toggle chkSmoke, chkWalls;
        FlatButton btnRun, btnReset, btnInfo, btnTheme;
        NumberBox numMeters;
        InfoRows rowsGeo, rowsPhys;
        Panel side;

        // Messwerte für die Kacheln und die Statusleiste
        string tCd = "–", tCdAvg = "", tCl = "–", tClAvg = "", tLd = "–", tSt = "–", tSettle = "", perfText = "";
        string statusText = "";
        bool statusWarn;

        readonly Timer timer = new Timer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastUi, fpsClock, stepMsAcc, mlups, fps, msPerStep = 1;
        readonly double[] batchFx = new double[500], batchFy = new double[500];
        long stepsAcc;
        int frames;
        string warning;
        double warningUntil;
        Point mouse = new Point(-1, -1);
        int drawButton;
        PointF lastDraw;
        bool hasLastDraw;

        public MainForm() : this(null) { }

        /// <param name="report">meldet den Ladefortschritt an das Startfenster (Text, 0..1)</param>
        public MainForm(Action<string, float> report)
        {
            Text = "Windkanal 2D";
            var wa = Screen.PrimaryScreen.WorkingArea;
            ClientSize = new Size(Math.Min(1480, wa.Width - 40), Math.Min(940, wa.Height - 60));
            MinimumSize = new Size(1100, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Base;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            if (report != null) report("Oberfläche wird aufgebaut …", 0.2f);
            BuildUi();
            if (report != null) report("Grafikkarte wird gesucht …", 0.4f);
            CreateSolver();
            if (report != null) report("Rechengitter wird vorbereitet …", 0.75f);
            RebuildGeometry(true);

            timer.Interval = 1;
            timer.Tick += OnTick;
            Shown += delegate { timer.Start(); };
            FormClosing += delegate { timer.Stop(); };
            KeyDown += OnKey;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        // ------------------------------------------------------------------ UI

        void BuildUi()
        {
            view = new Canvas { Dock = DockStyle.Fill, BackColor = Theme.Bg, Cursor = Cursors.Cross };
            view.Paint += OnViewPaint;
            view.Resize += delegate { dirty = true; };
            view.MouseDown += OnViewMouseDown;
            view.MouseMove += OnViewMouseMove;
            view.MouseUp += delegate { drawButton = 0; hasLastDraw = false; };
            view.MouseLeave += delegate { mouse = new Point(-1, -1); UpdateStatus(); };

            graph = new Canvas { Dock = DockStyle.Bottom, Height = 196, BackColor = Theme.Chrome };
            graph.Paint += OnGraphPaint;

            statusBar = new Canvas { Dock = DockStyle.Bottom, Height = 30, BackColor = Theme.Chrome };
            statusBar.Paint += OnStatusPaint;

            header = new Canvas { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Chrome };
            header.Paint += OnHeaderPaint;
            header.Resize += delegate { LayoutHeader(); };
            btnRun = new FlatButton { Text = "Pause", Icon = "", Primary = true, BackColor = Theme.Chrome };
            btnReset = new FlatButton { Text = "Neu starten", Icon = "", BackColor = Theme.Chrome };
            btnInfo = new FlatButton { Text = "Physik & Grenzen", Icon = "", BackColor = Theme.Chrome };
            btnTheme = new FlatButton { Text = "", BackColor = Theme.Chrome };
            header.Controls.AddRange(new Control[] { btnRun, btnReset, btnInfo, btnTheme });
            btnTheme.Click += delegate { SwitchTheme(); };
            UpdateThemeButton();
            btnRun.Click += delegate { SetRunning(!running); };
            btnReset.Click += delegate { ResetFlow(); };
            btnInfo.Click += delegate { ShowInfo(); };

            side = new Panel { Dock = DockStyle.Right, Width = 340, BackColor = Theme.Chrome, AutoScroll = true };
            side.HandleCreated += delegate { Theme.DarkScrollbars(side.Handle); };
            side.Paint += (s, e) =>
            {
                using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 0, 0, 0, side.Height);
            };

            Controls.Add(view);
            Controls.Add(graph);
            Controls.Add(statusBar);
            Controls.Add(side);
            Controls.Add(header);

            int y = 14;

            // --- Objekt
            var card = NewCard("Objekt", ref y);
            cbShape = card.Add(new DropDown(), 32, 14);
            foreach (var k in Shapes.All) cbShape.Items.Add(Shapes.Name(k));
            cbShape.SelectedIndex = 0;
            cbShape.SelectedIndexChanged += OnShapeChanged;

            tbAngle = card.Add(new FlatSlider { Text = "Anstellwinkel", Minimum = -90, Maximum = 90 }, 44, 8);
            tbAngle.ValueChanged += delegate
            {
                angleDeg = tbAngle.Value; UpdateSliderLabels();
                if (!suppress) RebuildGeometry(false);
            };
            tbSize = card.Add(new FlatSlider { Text = "Größe (Anteil der Tunnelhöhe)", Minimum = 4, Maximum = 60, Value = sizePercent }, 44, 8);
            tbSize.ValueChanged += delegate
            {
                sizePercent = tbSize.Value; UpdateSliderLabels();
                if (!suppress) RebuildGeometry(false);
            };
            rowsGeo = card.Add(new InfoRows(), 2 * InfoRows.Row, 12);
            var btnClear = card.Add(new FlatButton { Text = "Leere Fläche zum Zeichnen", Icon = "" }, 32, 4);
            btnClear.Click += delegate
            {
                customMask = new bool[solver.N];
                SelectShape(ShapeKind.Eigene);
                RebuildGeometry(true);
            };
            card.Finish();
            y += card.Height + 12;

            // --- Strömung
            card = NewCard("Strömung", ref y);
            tbRe = card.Add(new FlatSlider { Text = "Reynoldszahl Re", Minimum = 0, Maximum = 200, Value = ReToSlider(reynolds) }, 44, 12);
            tbRe.ValueChanged += delegate
            {
                reynolds = SliderToRe(tbRe.Value); UpdateSliderLabels();
                if (!suppress) { UpdateFlowParams(); stats.Clear(); }
            };
            card.Add(new HintLabel("Auflösung (Rechengitter)") { ForeColor = Theme.Muted }, 18, 4);
            cbRes = card.Add(new DropDown(), 32, 12);
            foreach (var r in ResNames) cbRes.Items.Add(r);
            cbRes.SelectedIndex = resIndex;
            cbRes.SelectedIndexChanged += delegate
            {
                if (suppress) return;
                resIndex = cbRes.SelectedIndex;
                CreateSolver();
                RebuildGeometry(true);
            };
            chkWalls = card.Add(new Toggle { Text = "Wände mit Reibung (Haftbedingung)" }, 26, 4);
            chkWalls.CheckedChanged += delegate { solver.NoSlipWalls = chkWalls.Checked; stats.Clear(); };
            card.Finish();
            y += card.Height + 12;

            // --- Darstellung
            card = NewCard("Darstellung", ref y);
            cbView = card.Add(new DropDown(), 32, 12);
            cbView.Items.AddRange(new[] { "Geschwindigkeit", "Druck (cp)", "Wirbelstärke", "Nur Rauch" });
            cbView.SelectedIndex = 0;
            cbView.SelectedIndexChanged += delegate { viewMode = (ViewMode)cbView.SelectedIndex; dirty = true; };
            chkSmoke = card.Add(new Toggle { Text = "Rauchlinien anzeigen", Checked = true }, 26, 4);
            chkSmoke.CheckedChanged += delegate { showSmoke = chkSmoke.Checked; particles.Clear(); dirty = true; };
            card.Finish();
            y += card.Height + 12;

            // --- Umrechnung
            card = NewCard("Umrechnung auf Luft (20 °C)", ref y);
            numMeters = card.Add(new NumberBox { Caption = "Echte Bezugslänge", Minimum = 0.001m, Maximum = 100m, Increment = 0.01m, Decimals = 3, Unit = "m" }, 32, 10);
            numMeters.Value = 0.1m;
            numMeters.ValueChanged += delegate { UpdateReadouts(); };
            rowsPhys = card.Add(new InfoRows { Note = "Kräfte je Meter Spannweite" }, 4 * InfoRows.Row, 4);
            card.Finish();
            y += card.Height + 14;

            var spacer = new Panel { BackColor = Theme.Chrome };
            spacer.SetBounds(0, y, 1, 1);
            side.Controls.Add(spacer);

            UpdateSliderLabels();
        }

        Card NewCard(string title, ref int y)
        {
            var c = new Card(title);
            c.SetBounds(14, y, 300, 100);
            side.Controls.Add(c);
            return c;
        }

        void LayoutHeader()
        {
            int h = 34, top = (header.Height - h) / 2, x = header.Width - 16;
            foreach (var b in new[] { btnRun, btnReset, btnInfo, btnTheme })
            {
                int w = b == btnRun ? 112 : b == btnReset ? 136 : b == btnInfo ? 170 : 34;
                x -= w;
                b.SetBounds(x, top, w, h);
                x -= 8;
            }
        }

        readonly ToolTip tips = new ToolTip();

        void UpdateThemeButton()
        {
            btnTheme.Icon = Theme.Dark ? "" : "";   // Sonne bzw. Mond
            if (Theme.Icons == null) btnTheme.Text = Theme.Dark ? "Hell" : "Dunkel";
            tips.SetToolTip(btnTheme, Theme.Dark ? "Helles Design" : "Dunkles Design");
            btnTheme.Invalidate();
        }

        /// <summary>Zwischen hellem und dunklem Design umschalten und die Wahl merken.</summary>
        void SwitchTheme()
        {
            Theme.Apply(!Theme.Dark);
            Theme.SaveDark(Theme.Dark);
            SuspendLayout();
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Recolor(this);
            ResumeLayout();
            UpdateThemeButton();
            Theme.DarkTitleBar(Handle);
            Theme.RefreshFrame(this);
            Theme.DarkScrollbars(side.Handle);
            dirty = true;
            Invalidate(true);
        }

        void Recolor(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TextBox) { c.BackColor = Theme.Ctl; c.ForeColor = Theme.Text; }
                else if (c == view) c.BackColor = Theme.Bg;
                else if (c.Parent is Card) c.BackColor = Theme.Card;
                else c.BackColor = Theme.Chrome;
                if (c is Painted) c.ForeColor = Theme.Text;
                Recolor(c);
            }
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
            tbAngle.ValueText = angleDeg + "°";
            tbSize.ValueText = sizePercent + " %";
            tbRe.ValueText = reynolds.ToString("#,0", De);
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
            if (e.KeyCode == Keys.Space && !(ActiveControl is TextBox))
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
            btnRun.Icon = r ? "" : "";
            btnRun.Invalidate();
            header.Invalidate();
        }

        // ------------------------------------------------------------ Simulation

        void CreateSolver()
        {
            bool[] oldMask = solver != null ? solver.Solid : null;
            int oldNx = solver != null ? solver.NX : 0, oldNy = solver != null ? solver.NY : 0;
            if (solver != null) solver.Dispose();
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
                    // Pakete so groß wählen, dass sie ins Zeitbudget passen (GPU rechnet ein Paket ohne Pause durch)
                    double left = 22 - sw.Elapsed.TotalMilliseconds;
                    int batch = solver.OnGpu ? (int)Math.Max(1, Math.Min(batchFx.Length, 0.8 * left / Math.Max(1e-4, msPerStep))) : 1;
                    batch = Math.Min(batch, 500 - steps);
                    double t0 = sw.Elapsed.TotalMilliseconds;
                    solver.StepMany(batch, batchFx, batchFy);
                    double dt = sw.Elapsed.TotalMilliseconds - t0;
                    msPerStep = 0.7 * msPerStep + 0.3 * dt / batch;
                    for (int k = 0; k < batch; k++) stats.Add((float)(batchFx[k] / q), (float)(batchFy[k] / q));
                    steps += batch;
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

            tCd = F(cd, "0.000");
            tCl = F(cl, "0.000");
            tCdAvg = settled ? "Mittel Ø " + F(mcd, "0.000") : "Mittel: läuft ein …";
            tClAvg = settled ? "Mittel Ø " + F(mcl, "0.000") : "Mittel: läuft ein …";
            tLd = settled && Math.Abs(mcd) > 1e-3 ? F(mcl / mcd, "0.0") : "–";
            tSt = settled && freq > 0 ? F(freq * T, "0.000") : "–";
            tSettle = settled ? "aus den Mittelwerten" : "nach der Einlaufzeit";
            perfText = "Zeit t·U/L = " + F(tStar, "0.0") + "   ·   " + F(mlups, "#,0") + " MLUPS   ·   " + F(fps, "0") + " FPS";

            rowsGeo.Set(new[] { "Versperrung", "Bezugslänge" },
                        new[] { F(100.0 * frontalCells / solver.NY, "0.0") + " %", F(refLen, "0") + " Zellen (" + Shapes.RefName(shape) + ")" });

            double Lm = (double)numMeters.Value;
            double v = reynolds * NuAir / Lm;
            double qd = 0.5 * RhoAir * v * v;
            double useCd = settled ? mcd : cd, useCl = settled ? mcl : cl;
            rowsPhys.Set(new[] { "Anströmung v", "Widerstand FW", "Auftrieb FA" },
                         new[] { Sig(v) + " m/s  (" + Sig(v * 3.6) + " km/h)", Sig(useCd * qd * Lm) + " N/m", Sig(useCl * qd * Lm) + " N/m" });
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
                statusWarn = true;
                statusText = "⚠  " + warning;
            }
            else
            {
                statusWarn = false;
                statusText = text.Length > 0 ? text
                    : "Tipp: Mit der Maus direkt ins Bild zeichnen (links = Wand, rechts = radieren)   ·   Leertaste = Start/Pause";
            }
            statusBar.Invalidate();
        }

        void OnStatusPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Chrome);
            var r = statusBar.ClientRectangle;
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, 0, r.Width, 0);
            Theme.Draw(g, perfText, Theme.Small, Theme.Faint, new Rectangle(0, 0, r.Width - 16, r.Height),
                       TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            int pw = TextRenderer.MeasureText(perfText, Theme.Small).Width;
            Theme.Draw(g, statusText, Theme.Small, statusWarn ? Theme.Warn : Theme.Muted,
                       new Rectangle(16, 0, Math.Max(0, r.Width - 48 - pw), r.Height), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void OnHeaderPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Chrome);
            var r = header.ClientRectangle;
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, r.Height - 1, r.Width, r.Height - 1);

            // kleines Logo: Stromlinien um einen Kreis
            int lx = 18, ly = (r.Height - 34) / 2;
            Theme.DrawLogo(g, lx, ly, 34);

            int tx = lx + 46;
            Theme.Draw(g, "Windkanal 2D", Theme.Title, Theme.Text, new Rectangle(tx, 9, 300, 24), TextFormatFlags.Left);
            Theme.Draw(g, "Lattice-Boltzmann-Strömungssimulation", Theme.Small, Theme.Muted, new Rectangle(tx, 34, 300, 18), TextFormatFlags.Left);

            // Zustand: läuft/pausiert + Rechenwerk
            if (solver == null) return;
            string state = (running ? "Läuft" : "Pausiert") + "  ·  " + solver.Backend;
            int sw = TextRenderer.MeasureText(state, Theme.Small).Width;
            int px = tx + 290, pw = sw + 34, ph = 26, py = (r.Height - ph) / 2;
            if (px + pw > btnInfo.Left - 16) return;
            Theme.FillRound(g, Theme.Card, new RectangleF(px, py, pw, ph), ph / 2f);
            Theme.StrokeRound(g, Theme.Border, new RectangleF(px, py, pw, ph), ph / 2f);
            using (var b = new SolidBrush(running ? Theme.Good : Theme.Warn)) g.FillEllipse(b, px + 12, py + ph / 2f - 4, 8, 8);
            Theme.Draw(g, state, Theme.Small, Theme.Text, new Rectangle(px + 26, py, sw + 4, ph), TextFormatFlags.VerticalCenter);
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
            switch (viewMode)
            {
                case ViewMode.Druck: title = "Druckbeiwert cp"; lo = "−2"; mid = "0"; hi = "+1"; break;
                case ViewMode.Wirbel: title = "Wirbelstärke ω·L/U∞"; lo = "−6 (im UZS)"; mid = "0"; hi = "+6"; break;
                default: title = "Geschwindigkeit |u| / U∞"; lo = "0"; mid = "1"; hi = "2"; break;
            }
            int w = 200, h = 8;
            int x0 = view.ClientSize.Width - w - 30, y0 = view.ClientSize.Height - 46;
            if (x0 < 10 || y0 < 10) return;
            Theme.Prepare(g);
            Theme.FillRound(g, Color.FromArgb(225, Theme.Chrome), new RectangleF(x0 - 14, y0 - 30, w + 28, 64), 10);
            Theme.StrokeRound(g, Color.FromArgb(160, Theme.Border), new RectangleF(x0 - 14, y0 - 30, w + 28, 64), 10);
            using (var bmp = new Bitmap(w, 1))
            {
                for (int i = 0; i < w; i++) bmp.SetPixel(i, 0, Renderer.LutColor(viewMode, i / (float)(w - 1), renderer));
                using (var path = Theme.Round(new RectangleF(x0, y0, w, h), h / 2f))
                using (var tb = new TextureBrush(bmp, WrapMode.TileFlipY))
                {
                    tb.TranslateTransform(x0, y0);
                    g.FillPath(tb, path);
                }
            }
            Theme.Draw(g, title, Theme.Small, Theme.Text, new Rectangle(x0, y0 - 22, w, 16), TextFormatFlags.Left);
            var lr = new Rectangle(x0, y0 + h + 4, w, 16);
            Theme.Draw(g, lo, Theme.Small, Theme.Muted, lr, TextFormatFlags.Left);
            Theme.Draw(g, mid, Theme.Small, Theme.Muted, lr, TextFormatFlags.HorizontalCenter);
            Theme.Draw(g, hi, Theme.Small, Theme.Muted, lr, TextFormatFlags.Right);
        }

        void DrawTile(Graphics g, Rectangle r, string caption, string value, string sub, Color accent)
        {
            Theme.FillRound(g, Theme.Card, r, 10);
            Theme.StrokeRound(g, Theme.Border, r, 10);
            if (accent != Color.Empty)
                Theme.FillRound(g, accent, new RectangleF(r.X + 14, r.Y + 14, 8, 8), 4);
            int cx = r.X + (accent != Color.Empty ? 28 : 14);
            Theme.Draw(g, caption, Theme.Caps, Theme.Muted, new Rectangle(cx, r.Y + 10, r.Right - cx - 8, 16), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, value, Theme.Big, Theme.Text, new Rectangle(r.X + 12, r.Y + 28, r.Width - 20, 32), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, sub, Theme.Small, Theme.Faint, new Rectangle(r.X + 14, r.Bottom - 22, r.Width - 20, 16), TextFormatFlags.VerticalCenter);
        }

        void OnGraphPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Chrome);
            var r = graph.ClientRectangle;
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, 0, r.Width, 0);

            // Messwert-Kacheln (2 × 2)
            int tw = 158, th = (r.Height - 14 - 14 - 10) / 2, gx = 14, gy = 14;
            DrawTile(g, new Rectangle(gx, gy, tw, th), "WIDERSTAND  cw", tCd, tCdAvg, Theme.Cd);
            DrawTile(g, new Rectangle(gx + tw + 10, gy, tw, th), "AUFTRIEB  ca", tCl, tClAvg, Theme.Cl);
            DrawTile(g, new Rectangle(gx, gy + th + 10, tw, th), "GLEITZAHL  ca/cw", tLd, tSettle, Color.Empty);
            DrawTile(g, new Rectangle(gx + tw + 10, gy + th + 10, tw, th), "STROUHAL  St", tSt, tSettle, Color.Empty);

            // Verlauf der Kraftbeiwerte
            var box = new Rectangle(gx + 2 * tw + 24, gy, r.Width - (gx + 2 * tw + 24) - 14, r.Height - 28);
            if (box.Width < 120) return;
            Theme.FillRound(g, Theme.Card, box, 10);
            Theme.StrokeRound(g, Theme.Border, box, 10);
            var plot = new Rectangle(box.Left + 52, box.Top + 36, box.Width - 52 - 18, box.Height - 36 - 28);
            Theme.Draw(g, "Kraftbeiwerte über der Zeit", Theme.Semi, Theme.Text, new Rectangle(box.Left + 14, box.Top + 10, 220, 18), TextFormatFlags.VerticalCenter);
            int lx = box.Left + 240;
            Theme.FillRound(g, Theme.Cd, new RectangleF(lx, box.Top + 17, 14, 4), 2);
            Theme.Draw(g, "cw (Widerstand)", Theme.Small, Theme.Muted, new Rectangle(lx + 20, box.Top + 10, 110, 18), TextFormatFlags.VerticalCenter);
            Theme.FillRound(g, Theme.Cl, new RectangleF(lx + 130, box.Top + 17, 14, 4), 2);
            Theme.Draw(g, "ca (Auftrieb)", Theme.Small, Theme.Muted, new Rectangle(lx + 150, box.Top + 10, 100, 18), TextFormatFlags.VerticalCenter);

            if (plot.Width < 20 || plot.Height < 20) return;
            double T = ConvectiveTime;
            int span = Math.Min(stats.Count, (int)(60 * T));
            if (span < 2)
            {
                Theme.Draw(g, "Noch keine Daten", Theme.Small, Theme.Muted, plot, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
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

            using (var grid = new Pen(Theme.Grid))
            {
                for (int i = 0; i <= 4; i++)
                {
                    double v = lo + (hi - lo) * i / 4;
                    float yy = (float)(plot.Bottom - (v - lo) / (hi - lo) * plot.Height);
                    g.DrawLine(grid, plot.Left, yy, plot.Right, yy);
                    Theme.Draw(g, F(v, "0.00"), Theme.Small, Theme.Faint, new Rectangle(box.Left + 4, (int)yy - 8, 40, 16),
                               TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }
                float y0 = (float)(plot.Bottom - (0 - lo) / (hi - lo) * plot.Height);
                using (var zero = new Pen(Theme.Zero)) g.DrawLine(zero, plot.Left, y0, plot.Right, y0);
            }
            double spanT = span / T;
            Theme.Draw(g, "← letzte " + F(spanT, "0") + " Zeiteinheiten t·U/L", Theme.Small, Theme.Faint,
                       new Rectangle(plot.Left, plot.Bottom + 6, 300, 16), TextFormatFlags.Left);
            Theme.Draw(g, "jetzt", Theme.Small, Theme.Faint, new Rectangle(plot.Right - 60, plot.Bottom + 6, 60, 16), TextFormatFlags.Right);

            DrawSeries(g, plot, span, lo, hi, true);
            DrawSeries(g, plot, span, lo, hi, false);
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
            using (var p = new Pen(cd ? Theme.Cd : Theme.Cl, 1.8f)) g.DrawLines(p, pts);
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
                "VALIDIERUNG\n" +
                "Zylinder bei Re = 100 (Kármánsche Wirbelstraße):\n" +
                "• Literatur: St ≈ 0,164–0,167, cw ≈ 1,33–1,40\n" +
                "• Diese App: St ≈ 0,166–0,172, cw ≈ 1,50–1,54\n" +
                "Die Frequenz stimmt sehr gut. Der Widerstand liegt ca. 10 % zu hoch: durch die Versperrung " +
                "(wie im echten Windkanal) und die treppenförmige Körperkontur im Rechengitter. " +
                "Höhere Auflösung und kleinere Objekte bringen die Werte näher an die Literatur.\n\n" +
                "GRENZEN\n" +
                "• 2D: Das Objekt ist unendlich lang (Profilschnitt). 3D-Effekte wie Randwirbel fehlen.\n" +
                "• Ab ca. Re > 1.000 ist echte Turbulenz dreidimensional; 2D-Werte sind dann Richtwerte, gut für Vergleiche (Form A vs. Form B).\n" +
                "• Höhere Auflösung = genauere Grenzschicht = verlässlichere Beiwerte.\n" +
                "• Die Mittelwerte stehen erst nach der Einlaufzeit (≈ 15 Zeiteinheiten) zur Verfügung.";
            using (var dlg = new InfoDialog("Physik & Grenzen", t)) dlg.ShowDialog(this);
        }
    }
}
