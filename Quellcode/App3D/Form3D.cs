using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
using Windkanal;

namespace Windkanal3D
{
    /// <summary>
    /// Hauptfenster der 3D-Version. Nutzt Design und Bedienelemente der 2D-Version (Theme, Card, FlatButton …)
    /// nur lesend; der 2D-Code wird dadurch nicht verändert.
    /// </summary>
    public sealed class Form3D : Form
    {
        static readonly int[,] Res = { { 160, 64, 64 }, { 256, 112, 112 }, { 384, 160, 160 }, { 512, 224, 224 }, { 640, 288, 288 } };
        static readonly string[] ResNames = { "Niedrig", "Mittel", "Hoch", "Sehr hoch", "Ultra" };
        static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        const float UIn = 0.05f;
        const double TauMin = 0.50001;   // mit Turbulenzmodell (LES) bis Re ~ 1 Mio. stabil (getestet an der Kugel)

        const int Outer = 18, Gap = 14, TopH = 76, RightW = 344, BottomH = 206;

        /// <summary>Wird ausgelöst, wenn im Kopf "2D" gewählt wird.</summary>
        public event EventHandler SwitchTo2D;

        // Zustand
        readonly List<Mesh> meshes = new List<Mesh>(Mesh.BuiltIn());
        Mesh mesh;
        Placement place;
        Lbm3D lbm;
        string gpuError;
        int resIndex = 1, sizePercent = 25, yaw, pitch, sliceAxis, slicePos = 50;
        bool onGround, running = true, suppress;
        /// <summary>Darstellung in der 3D-Ansicht: 0 = Stromlinien, 1 = Rauch, 2 = Schnittebene, 3 = nur Körper.</summary>
        int vizMode;
        FlowField field;
        List<float[]> rakeLines, rakeSmoke;
        double lastField, lastLines;

        double reynolds = 20000, reUsed, tauUsed;
        float[] bodyMin, bodyMax;
        long steps;

        // Messwerte
        readonly Queue<double[]> history = new Queue<double[]>();
        double cw, ca, cs, cwMean, caMean, mlups;
        bool settled;
        string warning;
        int[] sliceLut;
        Bitmap sliceBmp, sceneBmp;
        bool sceneDirty = true, dragging;
        Point dragStart;
        MouseButtons dragButton;
        double dragYaw, dragPitch, dragPanX, dragPanY;

        readonly Scene scene = new Scene();

        // Rechen-Thread: rechnet ununterbrochen, während der Haupt-Thread zeichnet.
        // Alle Zugriffe auf 'lbm' laufen unter dieser Sperre.
        readonly object gpu = new object();
        System.Threading.Thread simThread;
        volatile bool simStop;
        volatile int uiWaiting;
        readonly Queue<double[]> simForces = new Queue<double[]>();   // je Paket: fx, fy, fz, Schritte
        bool simUnstable;
        double renderMs = 10, smokeMs = 5;
        int smokeScale = 1;
        /// <summary>Gemessene Leistung der Grafikkarte (Mio. Zellen-Schritte pro Sekunde), 0 = unbekannt.</summary>
        double gpuMlups;
        readonly Timer timer = new Timer();
        readonly Timer settleRender = new Timer { Interval = 140 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastSlice, rateClock, lastRender;
        long rateSteps;

        // Oberfläche
        Segmented segMode, segViz, segSlice;
        FlatButton btnRotX, btnRotY, btnRotZ, btnFlip, btnAuto;
        FlatButton btnRun, btnReset, btnTheme, btnLoad;
        Card cardView, cardCd, cardCl, cardKenn, cardSlice, cardSet;
        ViewCanvas view;
        Canvas sliceView;
        DropDown cbModel, cbRes;
        FlatSlider tbYaw, tbPitch, tbSize, tbRe, tbSlice;
        Toggle chkGround;
        HintLabel hintModel, hintRe;
        readonly ToolTip tips = new ToolTip();

        public Form3D()
        {
            Text = "Windkanal 3D";
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

            sliceLut = BuildSpeedLut();
            mesh = meshes[0];
            resIndex = PickResolution();
            BuildUi();
            CreateSolver();
            Rebuild();
            simThread = new System.Threading.Thread(SimLoop) { IsBackground = true, Name = "Windkanal3D-Rechnung" };
            simThread.Start();

            timer.Interval = 1;
            timer.Tick += OnTick;
            settleRender.Tick += delegate { settleRender.Stop(); sceneDirty = true; view.Invalidate(); };
            VisibleChanged += delegate { if (Visible) timer.Start(); else timer.Stop(); };
            FormClosed += delegate
            {
                timer.Stop();
                simStop = true;
                if (simThread != null) simThread.Join(2000);
                lock (gpu) { if (lbm != null) lbm.Dispose(); lbm = null; }
            };
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
            segMode = new Segmented { BackColor = Theme.Bg };
            segMode.Items.AddRange(new[] { "2D", "3D" });
            segMode.SelectedIndex = 1;
            segMode.SelectedIndexChanged += delegate
            {
                if (segMode.SelectedIndex != 0) return;
                segMode.SelectedIndex = 1;
                if (SwitchTo2D != null) SwitchTo2D(this, EventArgs.Empty);
            };
            btnRun = new FlatButton { Text = "Pause", Icon = "", Primary = true, BackColor = Theme.Bg };
            btnReset = new FlatButton { Icon = "", BackColor = Theme.Bg };
            btnTheme = new FlatButton { BackColor = Theme.Bg };
            if (Theme.Icons == null) btnReset.Text = "Neu";
            tips.SetToolTip(btnReset, "Strömung neu starten");
            btnRun.Click += delegate { SetRunning(!running); };
            btnReset.Click += delegate { ResetFlow(); };
            btnTheme.Click += delegate { SwitchTheme(); };
            UpdateThemeButton();
            segViz = new Segmented { BackColor = Theme.Bg };
            segViz.Items.AddRange(new[] { "Stromlinien", "Rauch", "Schnittebene", "Nur Körper" });
            segViz.SelectedIndexChanged += delegate
            {
                vizMode = segViz.SelectedIndex;
                ApplySmokeMode();
                lastLines = lastField = 0;
                UpdateSceneFlow();
            };
            Controls.AddRange(new Control[] { segViz, segMode, btnRun, btnReset, btnTheme });

            // --- 3D-Ansicht
            cardView = new Card("3D-Ansicht");
            cardView.Paint += OnViewCardPaint;
            view = new ViewCanvas { BackColor = Theme.Card, Cursor = Cursors.SizeAll };
            view.Paint += OnViewPaint;
            view.Resize += delegate { sceneDirty = true; };
            view.MouseDown += OnViewDown;
            view.MouseMove += OnViewMove;
            view.MouseUp += OnViewUp;
            view.MouseWheel += OnViewWheel;
            view.MouseEnter += delegate { view.Focus(); };
            view.DoubleClick += delegate { scene.ResetCamera(); sceneDirty = true; view.Invalidate(); };
            cardView.Controls.Add(view);

            // --- Messwerte
            cardCd = new Card("Widerstand");
            cardCd.Paint += (s, e) => PaintForce(e.Graphics, cardCd, "cw", Theme.Accent, cw, cwMean);
            cardCl = new Card("Auftrieb");
            cardCl.Paint += (s, e) => PaintForce(e.Graphics, cardCl, "ca", Theme.Pink, ca, caMean);
            cardKenn = new Card("Kennzahlen");
            cardKenn.Paint += OnKennPaint;
            cardSlice = new Card("Schnitt · Geschwindigkeit");
            cardSlice.Paint += OnSliceCardPaint;
            sliceView = new Canvas { BackColor = Theme.Card };
            sliceView.Paint += OnSlicePaint;
            cardSlice.Controls.Add(sliceView);

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
            cbModel = (DropDown)add(new DropDown(), 40, 8);
            FillModelList();
            cbModel.SelectedIndexChanged += delegate
            {
                if (suppress) return;
                mesh = meshes[cbModel.SelectedIndex];
                ApplyModelDefaults();
                Rebuild();
            };
            btnLoad = (FlatButton)add(new FlatButton { Text = "3D-Datei laden (STL, OBJ)", Icon = "" }, 40, 6);
            btnLoad.Click += delegate { LoadFile(); };
            hintModel = (HintLabel)add(new HintLabel(""), 32, 4);
            // Ausrichtung: um 90° kippen (x = Strömungsrichtung, y = quer, z = oben) oder automatisch
            btnRotX = new FlatButton { Text = "X", Icon = "\uE7AD" };
            btnRotY = new FlatButton { Text = "Y", Icon = "\uE7AD" };
            btnRotZ = new FlatButton { Text = "Z", Icon = "\uE7AD" };
            btnFlip = new FlatButton { Text = "180°" };
            btnAuto = new FlatButton { Text = "Auto" };
            var rotBtns = new[] { btnRotX, btnRotY, btnRotZ, btnFlip, btnAuto };
            int[] bws = { 52, 52, 52, 0, 0 };
            int rest = (w - 3 * 52 - 4 * 6) / 2, bx = Card.Pad;
            bws[3] = rest; bws[4] = rest;
            for (int i = 0; i < 5; i++)
            {
                rotBtns[i].BackColor = Theme.Card;
                rotBtns[i].SetBounds(bx, y, bws[i], 40);
                bx += bws[i] + 6;
                cardSet.Controls.Add(rotBtns[i]);
            }
            y += 48;
            tips.SetToolTip(btnRotX, "Um die Strömungsrichtung kippen (90°)");
            tips.SetToolTip(btnRotY, "Nase hoch/runter kippen (90°)");
            tips.SetToolTip(btnRotZ, "Um die Hochachse drehen (90°)");
            tips.SetToolTip(btnFlip, "Vorne und hinten tauschen");
            tips.SetToolTip(btnAuto, "Automatisch: längste Seite in Strömungsrichtung, flachste Seite nach oben, höheres Ende nach hinten");
            btnRotX.Click += delegate { mesh.Rotate90(0); Rebuild(); };
            btnRotY.Click += delegate { mesh.Rotate90(1); Rebuild(); };
            btnRotZ.Click += delegate { mesh.Rotate90(2); Rebuild(); };
            btnFlip.Click += delegate { mesh.Rotate180(); Rebuild(); };
            btnAuto.Click += delegate { mesh.AutoOrient(); Rebuild(); };
            tbYaw = (FlatSlider)add(new FlatSlider { Text = "Drehung um die Hochachse", Minimum = -180, Maximum = 180 }, 46, 4);
            tbYaw.ValueChanged += delegate { yaw = tbYaw.Value; UpdateLabels(); if (!suppress) RebuildSoon(); };
            tbPitch = (FlatSlider)add(new FlatSlider { Text = "Anstellwinkel", Minimum = -45, Maximum = 45 }, 46, 4);
            tbPitch.ValueChanged += delegate { pitch = tbPitch.Value; UpdateLabels(); if (!suppress) RebuildSoon(); };
            tbSize = (FlatSlider)add(new FlatSlider { Text = "Größe (Anteil der Kanalbreite)", Minimum = 5, Maximum = 80, Value = sizePercent }, 46, 6);
            tbSize.ValueChanged += delegate { sizePercent = tbSize.Value; UpdateLabels(); if (!suppress) RebuildSoon(); };
            chkGround = (Toggle)add(new Toggle { Text = "Steht auf dem Boden" }, 28, 12);
            chkGround.CheckedChanged += delegate { onGround = chkGround.Checked; if (!suppress) Rebuild(); };

            add(new Section("Strömung"), 20, 10);
            tbRe = (FlatSlider)add(new FlatSlider { Text = "Reynoldszahl Re", Minimum = 0, Maximum = 200, Value = ReToSlider(reynolds) }, 46, 2);
            tbRe.ValueChanged += delegate { reynolds = SliderToRe(tbRe.Value); UpdateFlowParams(); UpdateLabels(); };
            hintRe = (HintLabel)add(new HintLabel(""), 18, 6);
            cbRes = (DropDown)add(new DropDown(), 40, 12);
            for (int i = 0; i < ResNames.Length; i++)
            {
                long cells = (long)Res[i, 0] * Res[i, 1] * Res[i, 2];
                bool fits = Lbm3D.DeviceName == null || cells <= Lbm3D.MaxCells;
                cbRes.Items.Add(ResNames[i] + " (" + Res[i, 0] + " × " + Res[i, 1] + " × " + Res[i, 2] + ")" + (fits ? "" : " – zu groß"));
            }
            cbRes.SelectedIndex = resIndex;
            cbRes.SelectedIndexChanged += delegate
            {
                if (suppress) return;
                resIndex = cbRes.SelectedIndex;
                CreateSolver();
                Rebuild();
            };

            add(new Section("Schnittebene"), 20, 10);
            segSlice = (Segmented)add(new Segmented(), 40, 8);
            segSlice.Items.AddRange(new[] { "Seitenschnitt", "Draufsicht" });
            segSlice.SelectedIndexChanged += delegate { sliceAxis = segSlice.SelectedIndex; UpdateSlicePlane(); };
            tbSlice = (FlatSlider)add(new FlatSlider { Text = "Lage der Ebene", Minimum = 0, Maximum = 100, Value = slicePos }, 46, 0);
            tbSlice.ValueChanged += delegate { slicePos = tbSlice.Value; UpdateLabels(); UpdateSlicePlane(); };

            Controls.AddRange(new Control[] { cardView, cardCd, cardCl, cardKenn, cardSlice, cardSet });
            ResumeLayout();
            LayoutAll();
            UpdateLabels();
        }

        void FillModelList()
        {
            suppress = true;
            cbModel.Items.Clear();
            foreach (var m in meshes) cbModel.Items.Add(m.Name);
            cbModel.SelectedIndex = Math.Max(0, meshes.IndexOf(mesh));
            cbModel.Invalidate();
            suppress = false;
        }

        /// <summary>Sinnvolle Startwerte je Körper (Autos auf den Boden, Kugeln in die Mitte).</summary>
        void ApplyModelDefaults()
        {
            suppress = true;
            bool car = mesh.Name.StartsWith("Ahmed");
            chkGround.Checked = onGround = car;
            tbSize.Value = sizePercent = car ? 50 : mesh.Name == "Kugel" ? 25 : 30;
            tbYaw.Value = yaw = 0;
            tbPitch.Value = pitch = 0;
            suppress = false;
            UpdateLabels();
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

            int bh = 40, by0 = (TopH - bh) / 2 + 2, x = W - Outer;
            x -= 116; btnRun.SetBounds(x, by0, 116, bh);
            foreach (var b in new[] { btnReset, btnTheme })
            {
                int bw = b.Text.Length > 0 ? Theme.Width(b.Text, b.Font) + 32 : bh;
                x -= 10 + bw;
                b.SetBounds(x, by0, bw, bh);
            }
            int mw = segMode.PreferredWidth;
            segMode.SetBounds(x - 18 - mw, (TopH - 44) / 2 + 2, mw, 44);
            int vw = segViz.PreferredWidth;
            segViz.SetBounds(Math.Max(240, Outer + (lw - vw) / 2 + 40), (TopH - 44) / 2 + 2, vw, 44);

            cardView.SetBounds(Outer, TopH, lw, by - Gap - TopH);
            view.SetBounds(12, Card.Head, cardView.Width - 24, cardView.Height - Card.Head - 40);

            int cw = 196, kw = 236;
            cardCd.SetBounds(Outer, by, cw, BottomH);
            cardCl.SetBounds(Outer + cw + Gap, by, cw, BottomH);
            cardKenn.SetBounds(Outer + 2 * (cw + Gap), by, kw, BottomH);
            int sx = Outer + 2 * (cw + Gap) + kw + Gap;
            cardSlice.SetBounds(sx, by, Math.Max(100, Outer + lw - sx), BottomH);
            sliceView.SetBounds(12, Card.Head - 6, cardSlice.Width - 24, BottomH - Card.Head - 6);

            cardSet.SetBounds(rx, TopH, RightW, H - Outer - TopH);
            sceneDirty = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Bg);
            foreach (Control c in Controls)
                if (c is Card) Theme.SoftShadow(g, new RectangleF(c.Left + 4, c.Top + 4, c.Width - 8, c.Height - 8), Card.Radius);
            int ly = (TopH - 40) / 2 + 2;
            Theme.DrawLogo(g, Outer, ly, 40, Theme.Ink, Theme.OnInk);
            Theme.Draw(g, "windkanal", Theme.Word, Theme.Text, new Rectangle(Outer + 52, ly - 2, 200, 28), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "3D-Strömungssimulation", Theme.Small, Theme.Muted, new Rectangle(Outer + 53, ly + 23, 200, 18), TextFormatFlags.VerticalCenter);
        }

        void UpdateThemeButton()
        {
            btnTheme.Icon = Theme.Dark ? "" : "";   // Sonne bzw. Mond
            if (Theme.Icons == null) btnTheme.Text = Theme.Dark ? "Hell" : "Dunkel";
            tips.SetToolTip(btnTheme, Theme.Dark ? "Helles Design" : "Dunkles Design");
            btnTheme.Invalidate();
        }

        void SwitchTheme()
        {
            Theme.Apply(!Theme.Dark);
            Theme.SaveDark(Theme.Dark);
            RefreshTheme();
        }

        /// <summary>Farben neu anwenden (auch nach einem Wechsel im 2D-Fenster).</summary>
        public void RefreshTheme()
        {
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Recolor(this);
            UpdateThemeButton();
            Theme.DarkTitleBar(Handle);
            Theme.RefreshFrame(this);
            sceneDirty = true;
            RenderSlice();
            Invalidate(true);
        }

        void Recolor(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                c.BackColor = parent is Card ? Theme.Card : Theme.Bg;
                if (c is Painted) c.ForeColor = Theme.Text;
                Recolor(c);
            }
        }

        void UpdateLabels()
        {
            tbYaw.ValueText = yaw + "°";
            tbPitch.ValueText = pitch + "°";
            tbSize.ValueText = sizePercent + " %";
            tbRe.ValueText = FormatRe(reynolds);
            tbSlice.ValueText = slicePos + " %";
            if (place != null)
            {
                string leak = place.LeakyRays > 0 ? " · Netz nicht ganz geschlossen" : "";
                hintModel.Text = mesh.Triangles.ToString("N0", De) + " Dreiecke · " + place.SolidCells.ToString("N0", De) + " Zellen" + leak;
            }
            if (hintRe != null)
            {
                bool limited = reUsed < reynolds * 0.999;
                hintRe.Text = limited ? "Begrenzt auf Re " + FormatRe(reUsed) + " (höher wird der Rechenkern instabil)"
                                      : reynolds > 2000 ? "Mit Turbulenzmodell (LES) · Bezug: Länge des Körpers"
                                                        : "Bezugslänge: Länge des Körpers in Strömungsrichtung";
                hintRe.Invalidate();
            }
        }

        static string FormatRe(double re) { return re.ToString(re >= 100 ? "N0" : "0.#", De); }

        static double SliderToRe(int v)
        {
            double re = 10 * Math.Pow(100000, v / 200.0);   // 10 … 1 000 000
            double mag = Math.Pow(10, Math.Floor(Math.Log10(re)) - 1);
            return Math.Round(re / mag) * mag;
        }

        static int ReToSlider(double re) { return (int)Math.Round(200 * Math.Log(re / 10) / Math.Log(100000)); }

        // ------------------------------------------------------------------ Modell und Rechnung

        void LoadFile()
        {
            using (var dlg = new OpenFileDialog { Filter = "3D-Modelle (*.stl;*.obj)|*.stl;*.obj|Alle Dateien (*.*)|*.*", Title = "3D-Modell laden" })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Mesh m;
                Cursor = Cursors.WaitCursor;
                try { m = Mesh.Load(dlg.FileName); }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    ShowWarning("Datei konnte nicht geladen werden: " + ex.Message);
                    return;
                }
                Cursor = Cursors.Default;
                meshes.Add(m);
                mesh = m;
                FillModelList();
                suppress = true;
                tbSize.Value = sizePercent = 40;
                tbYaw.Value = yaw = 0; tbPitch.Value = pitch = 0;
                chkGround.Checked = onGround = LooksLikeVehicle(m);
                suppress = false;
                UpdateLabels();
                Rebuild();
            }
        }

        /// <summary>Flach und lang (nach dem automatischen Ausrichten) = vermutlich ein Fahrzeug, das auf dem Boden steht.</summary>
        static bool LooksLikeVehicle(Mesh m)
        {
            var p = Placement.Build(m, 0, 0, 0.5, false, 64, 64, 64);
            return p.HeightZ < 0.5 * p.LengthX && p.HeightZ <= p.WidthY * 1.2;
        }

        void CreateSolver()
        {
            EnterGpu();
            try
            {
                if (lbm != null) { lbm.Dispose(); lbm = null; }
                int nx = Res[resIndex, 0], ny = Res[resIndex, 1], nz = Res[resIndex, 2];
                scene.NX = nx; scene.NY = ny; scene.NZ = nz;
                try
                {
                    lbm = new Lbm3D(nx, ny, nz);
                    gpuError = null;
                }
                catch (Exception ex)
                {
                    gpuError = ex.Message;
                    ShowWarning("3D-Rechnung nicht möglich: " + ex.Message);
                }
            }
            finally { LeaveGpu(); }
        }

        /// <summary>
        /// Größtes Gitter, das in den Grafikspeicher passt und noch mindestens etwa 300 Schritte pro Sekunde schafft.
        /// Dafür rechnet die Grafikkarte einmal kurz zur Probe (unter einer Zehntelsekunde).
        /// </summary>
        int PickResolution()
        {
            if (Lbm3D.DeviceName == null) return 1;
            try
            {
                int bi = (long)Res[1, 0] * Res[1, 1] * Res[1, 2] <= Lbm3D.MaxCells ? 1 : 0;
                using (var probe = new Lbm3D(Res[bi, 0], Res[bi, 1], Res[bi, 2]))
                {
                    probe.SetSolid(new byte[probe.N]);
                    probe.Reset(UIn);
                    double fx, fy, fz;
                    probe.Step(10, 1.5f, UIn, out fx, out fy, out fz);
                    var sw = Stopwatch.StartNew();
                    probe.Step(40, 1.5f, UIn, out fx, out fy, out fz);
                    gpuMlups = probe.N * 40.0 / sw.Elapsed.TotalSeconds / 1e6;
                }
                int best = 0;
                for (int i = 0; i < ResNames.Length; i++)
                {
                    long cells = (long)Res[i, 0] * Res[i, 1] * Res[i, 2];
                    if (cells <= Lbm3D.MaxCells && cells <= gpuMlups * 1e6 / 300) best = i;
                }
                return best;
            }
            catch { return 0; }
        }

        void EnterGpu()
        {
            System.Threading.Interlocked.Increment(ref uiWaiting);
            System.Threading.Monitor.Enter(gpu);
        }

        void LeaveGpu()
        {
            System.Threading.Monitor.Exit(gpu);
            System.Threading.Interlocked.Decrement(ref uiWaiting);
        }

        /// <summary>
        /// Rechnet in Paketen von etwa 12 ms, solange "Läuft". Zwischen den Paketen kommt der Haupt-Thread an die
        /// Grafikkarte (Schnittbild, Rauchbild), ohne dass die Rechnung auf das Zeichnen warten muss.
        /// </summary>
        void SimLoop()
        {
            double msPerStep = 1;
            while (!simStop)
            {
                bool did = false;
                lock (gpu)
                {
                    if (lbm != null && running && place != null && !simUnstable)
                    {
                        int n = Math.Max(1, Math.Min(500, (int)(12 / Math.Max(0.002, msPerStep))));
                        var sw = Stopwatch.StartNew();
                        double fx, fy, fz;
                        lbm.Step(n, (float)(1 / tauUsed), UIn, out fx, out fy, out fz);
                        msPerStep = 0.7 * msPerStep + 0.3 * sw.Elapsed.TotalMilliseconds / n;
                        steps += n;
                        if (double.IsNaN(fx) || double.IsInfinity(fx)) simUnstable = true;
                        lock (simForces) simForces.Enqueue(new[] { fx, fy, fz, n });
                        did = true;
                    }
                }
                if (!did) System.Threading.Thread.Sleep(5);
                // der Haupt-Thread will an die Grafikkarte: kurz Vortritt lassen
                while (uiWaiting > 0 && !simStop) System.Threading.Thread.Yield();
            }
        }

        void RebuildSoon()
        {
            // beim Ziehen am Regler erst neu bauen, wenn er kurz stillsteht
            rebuildPending = true;
            rebuildAt = clock.Elapsed.TotalMilliseconds + 180;
        }
        bool rebuildPending;
        double rebuildAt;

        void Rebuild()
        {
            rebuildPending = false;
            Cursor = Cursors.WaitCursor;
            var np = Placement.Build(mesh, yaw, pitch, sizePercent / 100.0, onGround, scene.NX, scene.NY, scene.NZ);
            Cursor = Cursors.Default;
            EnterGpu();
            try
            {
                place = np;
                scene.Triangles = place.World;
                if (lbm != null) lbm.SetSolid(place.Solid);
                BuildRakes();
                UpdateFlowParams();
                ResetFlow();
            }
            finally { LeaveGpu(); }
            UpdateSlicePlane();
            UpdateLabels();
            sceneDirty = true;
            view.Invalidate();
        }

        void UpdateFlowParams()
        {
            double L = Math.Max(1, place == null ? 20 : place.LengthX);
            double nu = UIn * L / reynolds;
            double tau = 3 * nu + 0.5;
            if (tau < TauMin) { tau = TauMin; nu = (tau - 0.5) / 3; }
            tauUsed = tau;
            reUsed = UIn * L / nu;
            history.Clear();
            settled = false;
        }

        void ResetFlow()
        {
            EnterGpu();
            try
            {
                if (lbm != null) { lbm.Reset(UIn); lbm.SmokeClear(); }
                steps = 0;
                simUnstable = false;
                lock (simForces) simForces.Clear();
            }
            finally { LeaveGpu(); }
            field = null;
            scene.Lines = null;
            lastField = lastLines = 0;
            history.Clear();
            cw = ca = cs = cwMean = caMean = 0;
            settled = false;
            RenderSlice();
            InvalidateCards();
        }

        void UpdateSlicePlane()
        {
            scene.SliceAxis = sliceAxis;
            int dim = sliceAxis == 0 ? scene.NY : scene.NZ;
            scene.SliceIndex = Math.Max(0, Math.Min(dim - 1, (int)Math.Round(slicePos / 100.0 * (dim - 1))));
            sceneDirty = true;
            view.Invalidate();
            RenderSlice();
        }

        void SetRunning(bool r)
        {
            running = r;
            btnRun.Text = r ? "Pause" : "Start";
            btnRun.Icon = r ? "" : "";
            btnRun.Invalidate();
            LayoutAll();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                SetRunning(!running);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        void ShowWarning(string text)
        {
            warning = text;
            warnUntil = clock.Elapsed.TotalSeconds + 8;
            if (cardView != null) cardView.Invalidate();
        }
        double warnUntil;

        void OnTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            if (rebuildPending && now >= rebuildAt && !dragging) Rebuild();
            if (warning != null && clock.Elapsed.TotalSeconds > warnUntil) { warning = null; cardView.Invalidate(); }
            if (lbm == null || place == null) return;
            if (simUnstable)
            {
                ShowWarning("Die Rechnung ist instabil geworden und wurde neu gestartet. Kleinere Reynoldszahl oder höhere Auflösung wählen.");
                ResetFlow();
                return;
            }
            double[][] got;
            lock (simForces) { got = simForces.ToArray(); simForces.Clear(); }
            if (!running || got.Length == 0) { UpdateFlowView(now); return; }

            int n = 0;
            foreach (var f in got) n += (int)f[3];
            rateSteps += n;
            if (now - rateClock > 500)
            {
                mlups = (double)lbm.N * rateSteps / ((now - rateClock) * 1000.0);
                rateClock = now; rateSteps = 0;
            }
            double q = 0.5 * UIn * UIn * Math.Max(1, place.FrontalCells);
            var last = got[got.Length - 1];
            cw = last[0] / q; cs = last[1] / q; ca = last[2] / q;
            foreach (var f in got) history.Enqueue(new[] { f[0] / q, f[2] / q, f[3] });
            // etwa drei Überströmzeiten mitteln
            double span = 3 * place.LengthX / UIn, sum = 0;
            foreach (var h in history) sum += h[2];
            while (history.Count > 20 && sum - history.Peek()[2] > span) sum -= history.Dequeue()[2];
            bool full = sum >= 0.95 * span;
            double sCw = 0, sCa = 0, lo = double.MaxValue, hi = double.MinValue;
            foreach (var h in history) { sCw += h[0]; sCa += h[1]; lo = Math.Min(lo, h[0]); hi = Math.Max(hi, h[0]); }
            cwMean = sCw / history.Count; caMean = sCa / history.Count;
            double through = steps * UIn / Math.Max(1, scene.NX);   // wie oft die Luft schon durch den Kanal ist
            settled = through > 1.0 && full && (hi - lo) < 0.03 * Math.Abs(cwMean) + 1e-6;

            if (now - lastSlice > 120) { lastSlice = now; RenderSlice(); }
            UpdateFlowView(now);
            InvalidateCards();
        }

        void BuildRakes()
        {
            if (place == null) return;
            var w = place.World;
            var bmin = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            var bmax = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < w.Length; i++) { int a = i % 3; bmin[a] = Math.Min(bmin[a], w[i]); bmax[a] = Math.Max(bmax[a], w[i]); }
            bodyMin = bmin; bodyMax = bmax;
            // Kamera auf Körper und Nachlauf richten (etwa 2,5 Körperlängen Bildbreite)
            float blen = bmax[0] - bmin[0], bdia = (float)Math.Sqrt(blen * blen + Math.Pow(bmax[1] - bmin[1], 2) + Math.Pow(bmax[2] - bmin[2], 2));
            scene.Focus = new double[] { (bmin[0] + bmax[0]) / 2 + 0.5 * blen, (bmin[1] + bmax[1]) / 2, (bmin[2] + bmax[2]) / 2, 2.2 * bdia };
            rakeLines = Rake.Build(bmin, bmax, scene.NX, scene.NY, scene.NZ, 11, 5, onGround);
            rakeSmoke = SmokeRake.Build(bmin, bmax, scene.NX, scene.NY, scene.NZ, onGround);
            ApplySmokeMode();
        }

        /// <summary>
        /// Echter Rauch: ein Dichtefeld in voller Auflösung, das die Grafikkarte mit der Strömung mitträgt.
        /// Aus jeder Düse des Rauchrechens strömt ein dünner Faden (Radius knapp eine Zelle bei mittlerer Auflösung).
        /// </summary>
        void ApplySmokeMode()
        {
            EnterGpu();
            try { ApplySmokeModeLocked(); }
            finally { LeaveGpu(); }
        }

        void ApplySmokeModeLocked()
        {
            if (lbm == null) return;
            bool on = vizMode == 1;
            if (on && rakeSmoke != null && bodyMin != null)
            {
                // Rauch nur dort rechnen, wo er zu sehen ist: vom Rauchrechen bis weit hinter den Körper, mit Rand.
                // Dort in doppelter Auflösung (achtmal so viele Rauchzellen), solange das in etwa 2,5-mal so viele
                // Rauchzellen wie Strömungszellen passt; sonst in einfacher Auflösung.
                float len = bodyMax[0] - bodyMin[0], wid = bodyMax[1] - bodyMin[1], hgt = bodyMax[2] - bodyMin[2];
                float rx = rakeSmoke[0][0];
                int x0 = (int)(rx - 6), x1 = (int)Math.Min(scene.NX, bodyMax[0] + 3 * Math.Max(len, 2 * hgt));
                int y0 = (int)(bodyMin[1] - 0.9f * wid - 6), y1 = (int)Math.Ceiling(bodyMax[1] + 0.9f * wid + 6);
                int z0 = onGround ? 0 : (int)(bodyMin[2] - 0.8f * hgt - 6), z1 = (int)Math.Ceiling(bodyMax[2] + 1.0f * hgt + 6);
                x0 = Math.Max(0, x0); y0 = Math.Max(0, y0); z0 = Math.Max(0, z0);
                x1 = Math.Min(scene.NX, x1); y1 = Math.Min(scene.NY, y1); z1 = Math.Min(scene.NZ, z1);
                long box = (long)(x1 - x0) * (y1 - y0) * (z1 - z0);
                int refine = box * 8 <= 2.5 * lbm.N ? 2 : 1;
                lbm.SetSmokeBox(x0, y0, z0, x1 - x0, y1 - y0, z1 - z0, refine);
                var xyz = new float[rakeSmoke.Count * 3];
                for (int i = 0; i < rakeSmoke.Count; i++) { xyz[3 * i] = rakeSmoke[i][0]; xyz[3 * i + 1] = rakeSmoke[i][1]; xyz[3 * i + 2] = rakeSmoke[i][2]; }
                // dünne Fäden: bei doppelter Auflösung gut eine Rauchzelle Radius
                lbm.SetSmokeSources(xyz, refine == 2 ? 0.6f : 0.9f);
            }
            if (on && !lbm.SmokeOn) lbm.SmokeClear();
            lbm.SmokeOn = on;
            scene.Overlay = on ? (Func<float[], int, int, float[], byte[]>)SmokeOverlay : null;
        }

        byte[] SmokeOverlay(float[] invDepth, int w, int h, float[] cam)
        {
            // weißer Rauch auf dunklem Grund, im hellen Design dunkelgrauer Rauch
            float r = Theme.Dark ? 0.93f : 0.22f, g = Theme.Dark ? 0.94f : 0.25f, b = Theme.Dark ? 0.96f : 0.30f;
            // schwächere Grafikkarten: Rauch in halber Auflösung zeichnen und weich hochrechnen
            int k = smokeScale, sw = Math.Max(1, w / k), sh = Math.Max(1, h / k);
            float[] d = invDepth;
            if (k > 1)
            {
                d = new float[sw * sh];
                for (int y = 0; y < sh; y++)
                    for (int x = 0; x < sw; x++)
                    {
                        float m = 0;
                        for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) m = Math.Max(m, invDepth[(y * k + j) * w + x * k + i]);
                        d[y * sw + x] = m;
                    }
                cam = (float[])cam.Clone();
                cam[12] /= k;
            }
            byte[] img;
            var t = Stopwatch.StartNew();
            EnterGpu();
            try { img = lbm == null ? new byte[sw * sh * 4] : lbm.RenderSmoke(sw, sh, cam, d, r, g, b, 0.9f); }
            finally { LeaveGpu(); }
            smokeMs = 0.8 * smokeMs + 0.2 * t.Elapsed.TotalMilliseconds;
            if (smokeScale == 1 && smokeMs > 22) smokeScale = 2;
            else if (smokeScale == 2 && smokeMs < 4) smokeScale = 1;
            if (k == 1) return img;
            // bilinear auf volle Größe
            var full = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
            {
                float gy = Math.Max(0, Math.Min(sh - 1.001f, (y + 0.5f) / k - 0.5f));
                int y0 = (int)gy; float fy = gy - y0;
                for (int x = 0; x < w; x++)
                {
                    float gx = Math.Max(0, Math.Min(sw - 1.001f, (x + 0.5f) / k - 0.5f));
                    int x0 = (int)gx; float fx = gx - x0;
                    int a = 4 * (y0 * sw + x0), b2 = a + 4, c = a + 4 * sw, e = c + 4, o = 4 * (y * w + x);
                    for (int ch = 0; ch < 4; ch++)
                        full[o + ch] = (byte)((img[a + ch] * (1 - fx) + img[b2 + ch] * fx) * (1 - fy) + (img[c + ch] * (1 - fx) + img[e + ch] * fx) * fy);
                }
            }
            return full;
        }

        /// <summary>Geschwindigkeitsfeld holen, Stromlinien bzw. Rauch nachführen und die 3D-Ansicht neu zeichnen.</summary>
        void UpdateFlowView(double now)
        {
            if (lbm == null || vizMode == 3) return;
            bool redraw = false;
            if (vizMode == 2)
            {
                if (now - lastField > 150) { lastField = now; redraw = true; }   // Schnittebene kommt aus RenderSlice
            }
            else
            {
                if (vizMode == 0 && (field == null || now - lastField > 350))
                {
                    int stride = Math.Max(1, (int)Math.Ceiling(Math.Pow(lbm.N / 600000.0, 1.0 / 3)));
                    int cnx, cny, cnz;
                    float[] u;
                    EnterGpu();
                    try { u = lbm.ReadVelocity(stride, out cnx, out cny, out cnz); }
                    finally { LeaveGpu(); }
                    field = new FlowField(u, stride, cnx, cny, cnz, scene.NX, scene.NY, scene.NZ, place.Solid);
                    lastField = now;
                }
                if (vizMode == 0 && now - lastLines > 350 && rakeLines != null)
                {
                    lastLines = now;
                    scene.Lines = Streamlines.Trace(field, rakeLines, UIn);
                    redraw = true;
                }
                if (vizMode == 1) redraw = true;   // Rauch wird beim Zeichnen von der Grafikkarte geholt
            }
            // höchstens etwa 30 Bilder pro Sekunde, damit die Rechnung Vorrang hat
            if (redraw && now - lastRender > 33) { lastRender = now; UpdateSceneFlow(); }
        }

        void UpdateSceneFlow()
        {
            scene.Lut = sliceLut;
            scene.Lines = vizMode == 0 ? scene.Lines : null;
            scene.ShowSlicePlane = vizMode == 2;
            sceneDirty = true;
            view.Invalidate();
        }

        void InvalidateCards() { cardCd.Invalidate(); cardCl.Invalidate(); cardKenn.Invalidate(); }

        // ------------------------------------------------------------------ 3D-Ansicht

        void OnViewPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            if (sceneDirty || sceneBmp == null || sceneBmp.Width != view.Width || sceneBmp.Height != view.Height)
            {
                scene.Background = Theme.Card;
                scene.Body = Theme.Dark ? Color.FromArgb(206, 211, 219) : Color.FromArgb(150, 157, 168);
                scene.Frame = Theme.Faint;
                scene.Floor = Theme.Grid;
                scene.SliceColor = Theme.Accent;
                if (sceneBmp != null) sceneBmp.Dispose();
                // Kantenglättung nur, wenn der Prozessor schnell genug ist (sonst ruckelt die Ansicht)
                var t = Stopwatch.StartNew();
                sceneBmp = scene.Render(view.Width, view.Height, dragging || renderMs > 30 ? 1 : 2);
                renderMs = 0.8 * renderMs + 0.2 * t.Elapsed.TotalMilliseconds;
                sceneDirty = false;
                if (dragging) settleRender.Stop();
            }
            g.DrawImageUnscaled(sceneBmp, 0, 0);
            Theme.Prepare(g);
            if (gpuError != null)
                Theme.Chip(g, "Nur Vorschau: " + gpuError, 10, 10, Theme.Orange, Theme.Tint(Theme.Orange));
        }

        void OnViewCardPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            int tw = Theme.Width(cardView.Title, Theme.Title);
            int x = Card.Pad + tw + 10;
            x += Theme.Chip(g, running && lbm != null ? "Läuft" : "Pausiert", x, 18, running && lbm != null ? Theme.Green : Theme.Muted,
                            Theme.Tint(running && lbm != null ? Theme.Green : Theme.Muted), 24, true) + 8;
            if (warning != null)
                Theme.Chip(g, warning, x, 18, Theme.Orange, Theme.Tint(Theme.Orange));
            Theme.Draw(g, "Linke Maustaste: drehen  ·  Rechte Maustaste: verschieben  ·  Mausrad: zoomen  ·  Doppelklick: Ansicht zurücksetzen",
                       Theme.Small, Theme.Muted, new Rectangle(Card.Pad, cardView.Height - 34, cardView.Width - 2 * Card.Pad, 20), TextFormatFlags.VerticalCenter);
        }

        void OnViewDown(object sender, MouseEventArgs e)
        {
            dragging = true;
            dragButton = e.Button;
            dragStart = e.Location;
            dragYaw = scene.Yaw; dragPitch = scene.Pitch; dragPanX = scene.PanX; dragPanY = scene.PanY;
            view.Capture = true;
        }

        void OnViewMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            double dx = e.X - dragStart.X, dy = e.Y - dragStart.Y;
            if (dragButton == MouseButtons.Left)
            {
                scene.Yaw = dragYaw - dx * 0.008;
                scene.Pitch = Math.Max(-1.45, Math.Min(1.45, dragPitch + dy * 0.008));
            }
            else
            {
                double k = scene.Distance / Math.Max(200, view.Width) * 1.1;
                scene.PanX = dragPanX - dx * k;
                scene.PanY = dragPanY + dy * k;
            }
            sceneDirty = true;
            view.Invalidate();
        }

        void OnViewUp(object sender, MouseEventArgs e)
        {
            dragging = false;
            view.Capture = false;
            sceneDirty = true;   // jetzt in voller Qualität
            view.Invalidate();
        }

        void OnViewWheel(object sender, MouseEventArgs e)
        {
            scene.Distance = Math.Max(0.25, Math.Min(4, scene.Distance * Math.Pow(0.88, e.Delta / 120.0)));
            dragging = true;   // schnelle Vorschau, gleich danach in voller Qualität
            sceneDirty = true;
            view.Invalidate();
            dragging = false;
            settleRender.Stop(); settleRender.Start();
        }

        // ------------------------------------------------------------------ Schnittbild

        static int[] BuildSpeedLut()
        {
            // dieselben Farben wie die Geschwindigkeitsansicht in 2D
            float[,] s = {
                { 0.00f, 48, 18, 59 }, { 0.13f, 70, 107, 227 }, { 0.25f, 40, 170, 250 }, { 0.38f, 26, 228, 182 },
                { 0.50f, 106, 253, 98 }, { 0.63f, 196, 240, 52 }, { 0.75f, 251, 185, 56 }, { 0.88f, 237, 97, 23 },
                { 1.00f, 122, 4, 3 } };
            var lut = new int[256];
            int n = s.GetLength(0);
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                int k = 0;
                while (k < n - 2 && t > s[k + 1, 0]) k++;
                float f = Math.Max(0, Math.Min(1, (t - s[k, 0]) / (s[k + 1, 0] - s[k, 0])));
                int r = (int)(s[k, 1] + f * (s[k + 1, 1] - s[k, 1]));
                int g = (int)(s[k, 2] + f * (s[k + 1, 2] - s[k, 2]));
                int b = (int)(s[k, 3] + f * (s[k + 1, 3] - s[k, 3]));
                lut[i] = (255 << 24) | (r << 16) | (g << 8) | b;
            }
            return lut;
        }

        void RenderSlice()
        {
            if (lbm == null || sliceView == null) return;
            int w = scene.NX, h = sliceAxis == 0 ? scene.NZ : scene.NY;
            float[] d;
            EnterGpu();
            try { if (lbm == null) return; d = lbm.ReadSlice(sliceAxis, scene.SliceIndex, UIn); }
            finally { LeaveGpu(); }
            scene.SliceData = d;
            if (sliceBmp == null || sliceBmp.Width != w || sliceBmp.Height != h)
            {
                if (sliceBmp != null) sliceBmp.Dispose();
                sliceBmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            }
            int solid = Theme.Dark ? Color.FromArgb(205, 209, 216).ToArgb() : Color.FromArgb(120, 127, 138).ToArgb();
            var px = new int[w * h];
            for (int r = 0; r < h; r++)
                for (int x = 0; x < w; x++)
                {
                    float v = d[r * w + x];
                    // oben im Bild = oben im Kanal (Seitenschnitt) bzw. links in Strömungsrichtung (Draufsicht)
                    px[(h - 1 - r) * w + x] = v < 0 ? solid : sliceLut[Math.Max(0, Math.Min(255, (int)(v / 1.5f * 255)))];
                }
            var data = sliceBmp.LockBits(new Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, sliceBmp.PixelFormat);
            for (int r = 0; r < h; r++) System.Runtime.InteropServices.Marshal.Copy(px, r * w, data.Scan0 + r * data.Stride, w);
            sliceBmp.UnlockBits(data);
            sliceView.Invalidate();
        }

        void OnSlicePaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            if (sliceBmp == null) return;
            float s = Math.Min(sliceView.Width / (float)sliceBmp.Width, (sliceView.Height - 4) / (float)sliceBmp.Height);
            float w = sliceBmp.Width * s, h = sliceBmp.Height * s;
            var r = new RectangleF((sliceView.Width - w) / 2, (sliceView.Height - h) / 2, w, h);
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            Theme.Prepare(g);
            using (var path = Theme.Round(r, 10))
            {
                g.SetClip(path);
                g.DrawImage(sliceBmp, r);
                g.ResetClip();
            }
        }

        void OnSliceCardPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            int W = cardSlice.Width;
            // Farbskala rechts oben
            int lw = 120, lx = W - Card.Pad - lw, ly = 26;
            for (int i = 0; i < lw; i++)
                using (var p = new Pen(Color.FromArgb(sliceLut[i * 255 / (lw - 1)]))) g.DrawLine(p, lx + i, ly, lx + i, ly + 8);
            Theme.Draw(g, "0", Theme.Small, Theme.Muted, new Rectangle(lx - 40, ly - 5, 34, 18), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "1,5 · U∞", Theme.Small, Theme.Muted, new Rectangle(lx, ly + 8, lw, 18), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        // ------------------------------------------------------------------ Messwert-Karten

        void PaintForce(Graphics g, Card card, string sym, Color col, double v, double mean)
        {
            Theme.Prepare(g);
            int W = card.Width;
            int tw = Theme.Width(card.Title, Theme.Title);
            Theme.Chip(g, sym, Card.Pad + tw + 10, 18, col, Theme.Tint(col));
            bool any = lbm != null && history.Count > 0;
            Theme.Draw(g, any ? v.ToString("0.000", De) : "–", Theme.Big, Theme.Text, new Rectangle(Card.Pad - 2, 54, W - 30, 44), TextFormatFlags.VerticalCenter);
            if (!any) return;
            if (settled)
            {
                int cwid = Theme.Chip(g, "Ø " + mean.ToString("0.000", De), Card.Pad, 104, Theme.Text, Theme.Ctl);
                Theme.Draw(g, "Mittel", Theme.Small, Theme.Muted, new Rectangle(Card.Pad + cwid + 8, 104, 80, 24), TextFormatFlags.VerticalCenter);
            }
            else
                Theme.Chip(g, "Strömung läuft ein …", Card.Pad, 104, Theme.Orange, Theme.Tint(Theme.Orange));
            Theme.Draw(g, sym == "cw" ? "bezogen auf die Stirnfläche" : "positiv = nach oben", Theme.Small, Theme.Muted,
                       new Rectangle(Card.Pad, BottomH - 40, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter);
        }

        void OnKennPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            int W = cardKenn.Width, half = (W - 2 * Card.Pad) / 2;
            Theme.Draw(g, "Reynolds Re", Theme.Small, Theme.Muted, new Rectangle(Card.Pad, 56, half, 18), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, FormatRe(reUsed), Theme.Mid, Theme.Text, new Rectangle(Card.Pad - 1, 74, half, 32), TextFormatFlags.VerticalCenter);
            double block = place == null ? 0 : 100.0 * place.FrontalCells / ((double)scene.NY * scene.NZ);
            Theme.Draw(g, "Versperrung", Theme.Small, Theme.Muted, new Rectangle(Card.Pad + half, 56, half, 18), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, block.ToString("0.0", De) + " %", Theme.Mid, block > 10 ? Theme.Orange : Theme.Text,
                       new Rectangle(Card.Pad + half - 1, 74, half, 32), TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, Card.Pad, 120, W - Card.Pad, 120);
            string cells = (scene.NX * (long)scene.NY * scene.NZ / 1e6).ToString("0.0", De) + " Mio. Zellen";
            Theme.Draw(g, cells, Theme.SmallMed, Theme.Text, new Rectangle(Card.Pad, 130, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter);
            string speed = lbm == null ? "keine GPU" : mlups > 0 ? (mlups * 1e6 / lbm.N).ToString("0", De) + " Schritte/s" : "–";
            Theme.Draw(g, speed, Theme.Small, Theme.Muted, new Rectangle(Card.Pad, 130, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            Theme.Draw(g, "Seitenkraft cs " + (history.Count > 0 ? cs.ToString("0.000", De) : "–"), Theme.Small, Theme.Muted,
                       new Rectangle(Card.Pad, 152, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter);
            Color on = settled ? Theme.Green : Theme.Accent;
            using (var b = new SolidBrush(on)) g.FillEllipse(b, Card.Pad, BottomH - 22, 7, 7);
            Theme.Draw(g, settled ? "Mittelwerte stehen fest" : "Strömung läuft noch ein", Theme.Small, Theme.Muted,
                       new Rectangle(Card.Pad + 13, BottomH - 28, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter);
        }
    }

    /// <summary>Zeichenfläche, die auch das Mausrad bekommt (dafür muss sie den Fokus annehmen können).</summary>
    sealed class ViewCanvas : Control
    {
        public ViewCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = false;
        }
    }
}
