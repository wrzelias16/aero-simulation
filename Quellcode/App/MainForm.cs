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

        // Oberfläche: Karten auf hellem bzw. dunklem Grund
        Card cardView, cardCd, cardCl, cardKenn, cardChart, cardSet, cardAir;
        Canvas view;
        Segmented segView;
        DropDown cbShape, cbRes;
        FlatSlider tbAngle, tbSize, tbRe;
        Toggle chkSmoke, chkWalls;
        FlatButton btnRun, btnReset, btnInfo, btnTheme;
        NumberBox numMeters;
        InfoRows rowsGeo, rowsPhys;
        readonly ToolTip tips = new ToolTip();

        const int Outer = 18, Gap = 14, TopH = 76, RightW = 344, BottomH = 206;

        // Messwerte für die Karten
        double curCd, curCl, meanCd, meanCl, tStar, settleFrac;
        string tLd = "–", tSt = "–", statusText = "";
        bool settled, statusWarn;

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
            ClientSize = new Size(Math.Min(1600, wa.Width - 40), Math.Min(960, wa.Height - 60));
            MinimumSize = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Base;
            KeyPreview = true;
            DoubleBuffered = true;
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
            SuspendLayout();

            // --- Kopfzeile: Ansicht als Pillen-Navigation, rechts die Knöpfe
            segView = new Segmented { BackColor = Theme.Bg };
            segView.Items.AddRange(new[] { "Geschwindigkeit", "Druck", "Wirbelstärke", "Rauch" });
            segView.SelectedIndexChanged += delegate
            {
                viewMode = (ViewMode)segView.SelectedIndex;
                // die Rauchansicht zeigt eigenen Rauch; die Rauchlinien gehören zu den Farbansichten
                if (chkSmoke != null) chkSmoke.Visible = viewMode != ViewMode.Rauch;
                if (viewMode == ViewMode.Rauch && particles != null) particles.Clear();
                dirty = true;
                cardView.Invalidate();
            };
            btnRun = new FlatButton { Text = "Pause", Icon = "", Primary = true, BackColor = Theme.Bg };
            btnReset = new FlatButton { Icon = "", BackColor = Theme.Bg };
            btnInfo = new FlatButton { Icon = "", BackColor = Theme.Bg };
            btnTheme = new FlatButton { BackColor = Theme.Bg };
            if (Theme.Icons == null) { btnReset.Text = "Neu"; btnInfo.Text = "?"; }
            tips.SetToolTip(btnReset, "Neu starten");
            tips.SetToolTip(btnInfo, "Physik & Grenzen der Simulation");
            btnRun.Click += delegate { SetRunning(!running); };
            btnReset.Click += delegate { ResetFlow(); };
            btnInfo.Click += delegate { ShowInfo(); };
            btnTheme.Click += delegate { SwitchTheme(); };
            UpdateThemeButton();
            Controls.AddRange(new Control[] { segView, btnRun, btnReset, btnInfo, btnTheme });

            // --- Strömungsbild
            cardView = new Card("Strömungsbild");
            cardView.Paint += OnViewCardPaint;
            view = new Canvas { BackColor = Theme.Card, Cursor = Cursors.Cross };
            view.Paint += OnViewPaint;
            view.Resize += delegate { dirty = true; };
            view.MouseDown += OnViewMouseDown;
            view.MouseMove += OnViewMouseMove;
            view.MouseUp += delegate { drawButton = 0; hasLastDraw = false; };
            view.MouseLeave += delegate { mouse = new Point(-1, -1); UpdateStatus(); };
            chkSmoke = new Toggle { Text = "Rauchlinien", Checked = true, BackColor = Theme.Card };
            chkSmoke.CheckedChanged += delegate { showSmoke = chkSmoke.Checked; particles.Clear(); dirty = true; };
            cardView.Controls.Add(view);
            cardView.Controls.Add(chkSmoke);

            // --- Messwerte und Verlauf
            cardCd = new Card("Widerstand");
            cardCd.Paint += (s, e) => PaintForceCard(e.Graphics, cardCd, true);
            cardCl = new Card("Auftrieb");
            cardCl.Paint += (s, e) => PaintForceCard(e.Graphics, cardCl, false);
            cardKenn = new Card("Kennzahlen");
            cardKenn.Paint += OnKennPaint;
            cardChart = new Card("Kraftbeiwerte über der Zeit");
            cardChart.Paint += OnChartPaint;

            // --- Einstellungen
            cardSet = new Card("Einstellungen");
            int y = Card.Head, w = RightW - 2 * Card.Pad;
            Func<Control, int, int, Control> add = (c, h, gap) =>
            {
                c.BackColor = Theme.Card;
                c.SetBounds(Card.Pad, y, w, h);
                cardSet.Controls.Add(c);
                y += h + gap;
                return c;
            };
            add(new Section("Objekt"), 20, 10);
            cbShape = (DropDown)add(new DropDown(), 40, 12);
            foreach (var k in Shapes.All) cbShape.Items.Add(Shapes.Name(k));
            cbShape.SelectedIndex = 0;
            cbShape.SelectedIndexChanged += OnShapeChanged;
            tbAngle = (FlatSlider)add(new FlatSlider { Text = "Anstellwinkel", Minimum = -90, Maximum = 90 }, 46, 6);
            tbAngle.ValueChanged += delegate
            {
                angleDeg = tbAngle.Value; UpdateSliderLabels();
                if (!suppress) RebuildGeometry(false);
            };
            tbSize = (FlatSlider)add(new FlatSlider { Text = "Größe (Anteil der Tunnelhöhe)", Minimum = 4, Maximum = 60, Value = sizePercent }, 46, 8);
            tbSize.ValueChanged += delegate
            {
                sizePercent = tbSize.Value; UpdateSliderLabels();
                if (!suppress) RebuildGeometry(false);
            };
            rowsGeo = (InfoRows)add(new InfoRows(), 2 * InfoRows.Row, 10);
            var btnClear = (FlatButton)add(new FlatButton { Text = "Leere Fläche zum Zeichnen", Icon = "" }, 40, 22);
            btnClear.Click += delegate
            {
                customMask = new bool[solver.N];
                SelectShape(ShapeKind.Eigene);
                RebuildGeometry(true);
            };

            add(new Section("Strömung"), 20, 10);
            tbRe = (FlatSlider)add(new FlatSlider { Text = "Reynoldszahl Re", Minimum = 0, Maximum = 200, Value = ReToSlider(reynolds) }, 46, 10);
            tbRe.ValueChanged += delegate
            {
                reynolds = SliderToRe(tbRe.Value); UpdateSliderLabels();
                if (!suppress) { UpdateFlowParams(); stats.Clear(); }
            };
            add(new HintLabel("Auflösung (Rechengitter)") { Font = Theme.Base }, 20, 6);
            cbRes = (DropDown)add(new DropDown(), 40, 14);
            foreach (var r in ResNames) cbRes.Items.Add(r);
            cbRes.SelectedIndex = resIndex;
            cbRes.SelectedIndexChanged += delegate
            {
                if (suppress) return;
                resIndex = cbRes.SelectedIndex;
                CreateSolver();
                RebuildGeometry(true);
            };
            chkWalls = (Toggle)add(new Toggle { Text = "Wände mit Reibung (Haftbedingung)" }, 28, 0);
            chkWalls.CheckedChanged += delegate { solver.NoSlipWalls = chkWalls.Checked; stats.Clear(); };

            // --- Umrechnung auf Luft
            cardAir = new Card("Umrechnung auf Luft");
            cardAir.Paint += (s, e) =>
            {
                Theme.Prepare(e.Graphics);
                int tw = Theme.Width(cardAir.Title, Theme.Title);
                Theme.Chip(e.Graphics, "20 °C", Card.Pad + tw + 10, 18, Theme.Muted, Theme.Ctl);
            };
            numMeters = new NumberBox { Caption = "Bezugslänge", Minimum = 0.001m, Maximum = 100m, Increment = 0.01m, Decimals = 3, Unit = "m", BackColor = Theme.Card };
            numMeters.SetBounds(Card.Pad, Card.Head, w, 40);
            numMeters.Value = 0.1m;
            numMeters.ValueChanged += delegate { UpdateReadouts(); };
            rowsPhys = new InfoRows { BackColor = Theme.Card };
            rowsPhys.SetBounds(Card.Pad, Card.Head + 52, w, 3 * InfoRows.Row);
            var note = new HintLabel("Kräfte je Meter Spannweite (2D-Profil)") { BackColor = Theme.Card };
            note.SetBounds(Card.Pad, Card.Head + 52 + 3 * InfoRows.Row + 6, w, 18);
            cardAir.Controls.AddRange(new Control[] { numMeters, rowsPhys, note });

            Controls.AddRange(new Control[] { cardView, cardCd, cardCl, cardKenn, cardChart, cardSet, cardAir });
            ResumeLayout();
            LayoutAll();
            UpdateSliderLabels();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (cardView != null) LayoutAll();
        }

        void LayoutAll()
        {
            int W = ClientSize.Width, H = ClientSize.Height;
            if (W < 200 || H < 200) return;
            int rx = W - Outer - RightW;
            int lw = rx - Gap - Outer;
            int by = H - Outer - BottomH;

            // Kopfzeile
            int bh = 40, by0 = (TopH - bh) / 2 + 2, x = W - Outer;
            x -= 116; btnRun.SetBounds(x, by0, 116, bh);
            foreach (var b in new[] { btnReset, btnInfo, btnTheme })
            {
                int bw = b.Text.Length > 0 ? Theme.Width(b.Text, b.Font) + 32 : bh;
                x -= 10 + bw;
                b.SetBounds(x, by0, bw, bh);
            }
            int sw = segView.PreferredWidth;
            segView.SetBounds(Math.Max(240, Outer + (lw - sw) / 2 + 40), (TopH - 44) / 2 + 2, sw, 44);

            cardView.SetBounds(Outer, TopH, lw, by - Gap - TopH);
            view.SetBounds(12, Card.Head, cardView.Width - 24, cardView.Height - Card.Head - 46);
            chkSmoke.SetBounds(cardView.Width - Card.Pad - 140, 16, 140, 28);

            int cw = 196, kw = 236;
            cardCd.SetBounds(Outer, by, cw, BottomH);
            cardCl.SetBounds(Outer + cw + Gap, by, cw, BottomH);
            cardKenn.SetBounds(Outer + 2 * (cw + Gap), by, kw, BottomH);
            int chx = Outer + 2 * (cw + Gap) + kw + Gap;
            cardChart.SetBounds(chx, by, Math.Max(100, Outer + lw - chx), BottomH);

            int airH = Card.Head + 52 + 3 * InfoRows.Row + 6 + 18 + 18;
            cardAir.SetBounds(rx, H - Outer - airH, RightW, airH);
            cardSet.SetBounds(rx, TopH, RightW, H - Outer - airH - Gap - TopH);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Bg);
            // Schatten unter den Karten
            foreach (Control c in Controls)
                if (c is Card) Theme.SoftShadow(g, new RectangleF(c.Left + 4, c.Top + 4, c.Width - 8, c.Height - 8), Card.Radius);

            // Logo und Name
            int ly = (TopH - 40) / 2 + 2;
            Theme.DrawLogo(g, Outer, ly, 40, Theme.Ink, Theme.OnInk);
            Theme.Draw(g, "windkanal", Theme.Word, Theme.Text, new Rectangle(Outer + 52, ly - 2, 200, 28), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "2D-Strömungssimulation", Theme.Small, Theme.Muted, new Rectangle(Outer + 53, ly + 23, 200, 18), TextFormatFlags.VerticalCenter);
        }

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
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Recolor(this);
            UpdateThemeButton();
            Theme.DarkTitleBar(Handle);
            Theme.RefreshFrame(this);
            dirty = true;
            Invalidate(true);
        }

        void Recolor(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is TextBox) continue;
                c.BackColor = parent is Card || c == view ? Theme.Card : c is NumberBox ? Theme.Card : Theme.Bg;
                if (c is Painted) c.ForeColor = Theme.Text;
                if (c is NumberBox) ((NumberBox)c).Recolor();
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
            angleDeg = Shapes.DefaultAngle(k);
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
            cardView.Invalidate(new Rectangle(0, 0, cardView.Width, Card.Head));
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
                bool vehicle = Shapes.IsVehicle(shape);
                float px = (vehicle ? 0.3f : 0.25f) * solver.VisibleNX;
                float py = (ny - 1) / 2f;
                var parts = Shapes.TransformParts(Shapes.Parts(shape), sizeCells, angleDeg, px, py);
                if (vehicle)
                {
                    // Fahrzeug knapp über dem (reibungsfreien = mitbewegten) Boden platzieren
                    float minY = float.MaxValue;
                    foreach (var pts in parts)
                        foreach (var p in pts) minY = Math.Min(minY, p.Y);
                    float shift = 3f - minY;
                    foreach (var pts in parts)
                        for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(pts[i].X, pts[i].Y + shift);
                }
                foreach (var pts in parts) Shapes.Fill(pts, mask, nx, ny);
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
                solver.AdvectSmoke(steps);
                if (showSmoke && viewMode != ViewMode.Rauch) particles.Update(solver, steps, 2.5f * solver.NX / solver.U0);
                dirty = true;
            }

            if (dirty && view.ClientSize.Width > 0 && view.ClientSize.Height > 0)
            {
                renderer.EnsureSize(view.ClientSize.Width, view.ClientSize.Height);
                if (viewMode == ViewMode.Rauch) solver.ReadSmoke();
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
            tStar = solver.Steps / T;
            int transient = (int)(15 * T);
            int window = Math.Min((int)(40 * T), stats.Count - transient);
            settled = window > 5 * T;
            settleFrac = settled ? 1 : Math.Max(0, Math.Min(0.99, stats.Count / (20 * T)));

            curCd = stats.Count > 0 ? stats.Cd(0) : 0;
            curCl = stats.Count > 0 ? stats.Cl(0) : 0;
            double freq = 0;
            meanCd = meanCl = 0;
            if (settled) stats.Analyze(window, (int)(0.5 * T), out meanCd, out meanCl, out freq);
            tLd = settled && Math.Abs(meanCd) > 1e-3 ? F(meanCl / meanCd, "0.0") : "–";
            tSt = settled && freq > 0 ? F(freq * T, "0.000") : "–";

            rowsGeo.Set(new[] { "Versperrung", "Bezugslänge" },
                        new[] { F(100.0 * frontalCells / solver.NY, "0.0") + " %", F(refLen, "0") + " Zellen (" + Shapes.RefName(shape) + ")" });

            double Lm = (double)numMeters.Value;
            double v = reynolds * NuAir / Lm;
            double qd = 0.5 * RhoAir * v * v;
            double useCd = settled ? meanCd : curCd, useCl = settled ? meanCl : curCl;
            rowsPhys.Set(new[] { "Anströmung v", "Widerstand FW", "Auftrieb FA" },
                         new[] { Sig(v) + " m/s  ·  " + Sig(v * 3.6) + " km/h", Sig(useCd * qd * Lm) + " N/m", Sig(useCl * qd * Lm) + " N/m" });

            foreach (var c in new[] { cardCd, cardCl, cardKenn, cardChart }) c.Invalidate();
            cardView.Invalidate(new Rectangle(0, 0, cardView.Width, Card.Head));
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
                    if (solver.Solid[c]) text = "Cursor im Körper";
                    else
                    {
                        double u = Math.Sqrt(solver.Ux[c] * solver.Ux[c] + solver.Uy[c] * solver.Uy[c]) / solver.U0;
                        double cp = (solver.Rho[c] - solver.RhoInf) / 3.0 / (0.5 * solver.U0 * solver.U0);
                        double vInf = reynolds * NuAir / (double)numMeters.Value;
                        text = "|u| / U∞ = " + F(u, "0.00") + "   ·   cp = " + F(cp, "0.00") + "   ·   ≈ " + Sig(u * vInf) + " m/s";
                    }
                }
            }
            if (warning != null && clock.Elapsed.TotalMilliseconds < warningUntil)
            {
                statusWarn = true;
                statusText = warning;
            }
            else
            {
                statusWarn = false;
                statusText = text.Length > 0 ? text : "Mit der Maus ins Bild zeichnen: links = Wand, rechts = radieren   ·   Leertaste = Start/Pause";
            }
            cardView.Invalidate(new Rectangle(0, cardView.Height - 46, cardView.Width, 46));
        }

        void OnViewCardPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            int W = cardView.Width, H = cardView.Height;

            // Kopf: Zustand und Rechenwerk
            if (solver != null)
            {
                int x = Card.Pad + Theme.Width(cardView.Title, Theme.Title) + 12;
                Color c = running ? Theme.Green : Theme.Orange;
                x += Theme.Chip(g, running ? "Läuft" : "Pausiert", x, 18, c, Theme.Tint(c), 24, true) + 6;
                x += Theme.Chip(g, solver.Backend, x, 18, Theme.Muted, Theme.Ctl) + 6;
                if (running && mlups > 0)
                    Theme.Chip(g, F(mlups, "#,0") + " MLUPS  ·  " + F(fps, "0") + " FPS", x, 18, Theme.Muted, Theme.Ctl);
            }

            // Fuß: Farbskala links, Hinweis bzw. Cursorwerte rechts
            int fy = H - 46;
            int legendRight = Card.Pad;
            if (viewMode != ViewMode.Rauch)
            {
                string title, lo, hi;
                switch (viewMode)
                {
                    case ViewMode.Druck: title = "Druckbeiwert cp"; lo = "−2"; hi = "+1"; break;
                    case ViewMode.Wirbel: title = "Wirbelstärke ω·L/U∞"; lo = "−6"; hi = "+6"; break;
                    default: title = "|u| / U∞"; lo = "0"; hi = "2"; break;
                }
                int x = Card.Pad;
                Theme.Draw(g, title, Theme.Small, Theme.Muted, new Rectangle(x, fy, 200, 30), TextFormatFlags.VerticalCenter);
                x += Theme.Width(title, Theme.Small) + 12;
                Theme.Draw(g, lo, Theme.Small, Theme.Muted, new Rectangle(x, fy, 40, 30), TextFormatFlags.VerticalCenter);
                x += Theme.Width(lo, Theme.Small) + 8;
                int bw = 150;
                using (var bmp = new Bitmap(bw, 1))
                {
                    for (int i = 0; i < bw; i++) bmp.SetPixel(i, 0, Renderer.LutColor(viewMode, i / (float)(bw - 1), renderer));
                    using (var path = Theme.Round(new RectangleF(x, fy + 12, bw, 7), 3.5f))
                    using (var tb = new TextureBrush(bmp, WrapMode.TileFlipY))
                    {
                        tb.TranslateTransform(x, fy + 12);
                        g.FillPath(tb, path);
                    }
                }
                x += bw + 8;
                Theme.Draw(g, hi, Theme.Small, Theme.Muted, new Rectangle(x, fy, 40, 30), TextFormatFlags.VerticalCenter);
                legendRight = x + Theme.Width(hi, Theme.Small) + 24;
            }
            else
            {
                const string hint = "Rauchfäden vom Rechen am Einlass, mit der Strömung mitgeführt";
                Theme.Draw(g, hint, Theme.Small, Theme.Muted, new Rectangle(Card.Pad, fy, W / 2, 30), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                legendRight = Card.Pad + Math.Min(W / 2, Theme.Width(hint, Theme.Small)) + 24;
            }
            var sr = new Rectangle(legendRight, fy, W - Card.Pad - legendRight, 30);
            if (statusWarn)
            {
                int tw = Theme.ChipWidth(statusText);
                Theme.Chip(g, statusText, Math.Max(legendRight, W - Card.Pad - tw), fy + 3, Theme.Orange, Theme.Tint(Theme.Orange));
            }
            else
                Theme.Draw(g, statusText, Theme.Small, Theme.Muted, sr, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }

        void OnViewPaint(object sender, PaintEventArgs e)
        {
            if (renderer.Bitmap == null || solver == null) return;
            var g = e.Graphics;
            g.DrawImageUnscaled(renderer.Bitmap, 0, 0);
            // Tunnelbild mit runden Ecken
            var tr = new RectangleF(renderer.OffX, renderer.OffY, solver.VisibleNX * renderer.Scale, solver.NY * renderer.Scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath(FillMode.Alternate))
            using (var b = new SolidBrush(Theme.Card))
            {
                path.AddRectangle(RectangleF.Inflate(tr, 2, 2));
                path.AddPath(Theme.Round(tr, 14), false);
                g.FillPath(b, path);
            }
        }

        /// <summary>Karte für cw bzw. ca: großer Wert, Mittelwert und Verlauf als Kapseln.</summary>
        void PaintForceCard(Graphics g, Card card, bool cd)
        {
            Theme.Prepare(g);
            int W = card.Width;
            Color col = cd ? Theme.Accent : Theme.Pink;
            string sym = cd ? "cw" : "ca";
            int tw = Theme.Width(card.Title, Theme.Title);
            Theme.Chip(g, sym, Card.Pad + tw + 10, 18, col, Theme.Tint(col));
            if (solver == null) return;

            double v = cd ? curCd : curCl;
            Theme.Draw(g, stats.Count > 0 ? F(v, "0.000") : "–", Theme.Big, Theme.Text, new Rectangle(Card.Pad - 2, 54, W - 30, 44), TextFormatFlags.VerticalCenter);
            if (settled)
            {
                int cw = Theme.Chip(g, "Ø " + F(cd ? meanCd : meanCl, "0.000"), Card.Pad, 104, Theme.Text, Theme.Ctl);
                Theme.Draw(g, "Mittel", Theme.Small, Theme.Muted, new Rectangle(Card.Pad + cw + 8, 104, 80, 24), TextFormatFlags.VerticalCenter);
            }
            else
                Theme.Chip(g, "Mittelwert läuft ein …", Card.Pad, 104, Theme.Orange, Theme.Tint(Theme.Orange));

            // Verlauf der letzten ~12 Zeiteinheiten als Kapseln (eine Kapsel = Mittel über ein Stück)
            var area = new RectangleF(Card.Pad, 142, W - 2 * Card.Pad, BottomH - 142 - 18);
            const int n = 12;
            double T = ConvectiveTime;
            int span = Math.Min(stats.Count, (int)(12 * T));
            float gap = 5, bw = (area.Width - gap * (n - 1)) / n;
            if (span < n * 2)
            {
                for (int i = 0; i < n; i++)
                    Theme.FillRound(g, Theme.Ctl, new RectangleF(area.X + i * (bw + gap), area.Bottom - 10, bw, 10), bw / 2);
                return;
            }
            var m = new double[n];
            double lo = double.MaxValue, hi = double.MinValue;
            int chunk = span / n;
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int k = 0; k < chunk; k++)
                {
                    int back = (n - 1 - i) * chunk + k;
                    s += cd ? stats.Cd(back) : stats.Cl(back);
                }
                m[i] = s / chunk;
                lo = Math.Min(lo, m[i]); hi = Math.Max(hi, m[i]);
            }
            double range = Math.Max(hi - lo, 1e-3);
            for (int i = 0; i < n; i++)
            {
                float h = (float)(bw + (area.Height - bw) * (0.25 + 0.75 * (m[i] - lo) / range));
                var r = new RectangleF(area.X + i * (bw + gap), area.Bottom - h, bw, h);
                Theme.FillRound(g, i == n - 1 ? col : Theme.Mix(Theme.Card, col, Theme.Dark ? 0.35f : 0.22f), r, bw / 2);
            }
        }

        void OnKennPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            if (solver == null) return;
            int W = cardKenn.Width, half = (W - 2 * Card.Pad) / 2;
            Theme.Draw(g, "Strouhal St", Theme.Small, Theme.Muted, new Rectangle(Card.Pad, 56, half, 18), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, tSt, Theme.Mid, Theme.Text, new Rectangle(Card.Pad - 1, 74, half, 32), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "Gleitzahl ca/cw", Theme.Small, Theme.Muted, new Rectangle(Card.Pad + half, 56, half, 18), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, tLd, Theme.Mid, Theme.Text, new Rectangle(Card.Pad + half - 1, 74, half, 32), TextFormatFlags.VerticalCenter);

            using (var p = new Pen(Theme.Border)) g.DrawLine(p, Card.Pad, 120, W - Card.Pad, 120);
            Theme.Draw(g, "Einlauf", Theme.SmallMed, Theme.Text, new Rectangle(Card.Pad, 130, 100, 18), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "t·U/L = " + F(tStar, "0"), Theme.Small, Theme.Muted, new Rectangle(Card.Pad, 130, W - 2 * Card.Pad, 18),
                       TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            Color on = settled ? Theme.Green : Theme.Accent;
            Theme.Capsules(g, new RectangleF(Card.Pad, 154, W - 2 * Card.Pad, 22), 16, (float)settleFrac, on, Theme.Ctl);
            using (var b = new SolidBrush(on)) g.FillEllipse(b, Card.Pad, BottomH - 22, 7, 7);
            Theme.Draw(g, settled ? "Mittelwerte stehen fest" : "Strömung läuft noch ein", Theme.Small, Theme.Muted,
                       new Rectangle(Card.Pad + 13, BottomH - 28, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter);
        }

        void OnChartPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            var r = cardChart.ClientRectangle;
            if (r.Width < 160) return;
            int lx = r.Width - Card.Pad;
            lx -= Theme.ChipWidth("ca  Auftrieb", true);
            Theme.Chip(g, "ca  Auftrieb", lx, 18, Theme.Pink, Theme.Tint(Theme.Pink), 24, true);
            int w2 = Theme.ChipWidth("cw  Widerstand", true);
            Theme.Chip(g, "cw  Widerstand", lx - 6 - w2, 18, Theme.Accent, Theme.Tint(Theme.Accent), 24, true);

            var plot = new Rectangle(r.Left + Card.Pad + 40, r.Top + Card.Head + 4, r.Width - 2 * Card.Pad - 40, r.Height - Card.Head - 4 - 34);
            if (plot.Width < 20 || plot.Height < 20 || solver == null) return;
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

            using (var grid = new Pen(Theme.Dark ? Theme.Grid : Theme.Track) { DashStyle = DashStyle.Dot })
                for (int i = 0; i <= 3; i++)
                {
                    double v = lo + (hi - lo) * i / 3;
                    float yy = (float)(plot.Bottom - (v - lo) / (hi - lo) * plot.Height);
                    g.DrawLine(grid, plot.Left, yy, plot.Right, yy);
                    Theme.Draw(g, F(v, "0.00"), Theme.Small, Theme.Faint, new Rectangle(r.Left + Card.Pad - 4, (int)yy - 9, 40, 18),
                               TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                }
            float y0 = (float)(plot.Bottom - (0 - lo) / (hi - lo) * plot.Height);
            using (var zero = new Pen(Theme.Faint)) g.DrawLine(zero, plot.Left, y0, plot.Right, y0);

            DrawSeries(g, plot, span, lo, hi, true);
            DrawSeries(g, plot, span, lo, hi, false);

            double spanT = span / T;
            Theme.Chip(g, "letzte " + F(spanT, "0") + " Zeiteinheiten", plot.Left, plot.Bottom + 8, Theme.Muted, Theme.Ctl, 22);
            int jw = Theme.ChipWidth("jetzt");
            Theme.Chip(g, "jetzt", plot.Right - jw, plot.Bottom + 8, Theme.Text, Theme.Ctl, 22);
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
            using (var p = new Pen(cd ? Theme.Accent : Theme.Pink, 2f) { LineJoin = LineJoin.Round })
            {
                if (!cd) p.DashPattern = new[] { 3f, 2f };
                g.DrawLines(p, pts);
            }
            var last = pts[n - 1];
            using (var b = new SolidBrush(Theme.Card)) g.FillEllipse(b, last.X - 5, last.Y - 5, 10, 10);
            using (var p = new Pen(cd ? Theme.Accent : Theme.Pink, 2f)) g.DrawEllipse(p, last.X - 4, last.Y - 4, 8, 8);
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
