using System;
using System.Collections.Generic;
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

        Model model = ModelLibrary.Find("zylinder") ?? ModelLibrary.All[0];
        bool[] customMask;
        int resIndex = 1, sizePercent = 10, angleDeg = 0;
        double reynolds = 100;
        float refLen = 24, frontalCells;
        bool running = true, showSmoke = true, dirty = true, suppress;
        ViewMode viewMode = ViewMode.Geschwindigkeit;

        // Oberfläche: Karten auf hellem bzw. dunklem Grund
        Card cardView, cardCd, cardCl, cardKenn, cardChart, cardSet, cardAir;
        Canvas view;
        Segmented segView, segMode;
        // Bewegliche Teile (DRS, Macarena): Stand 0 = Ausgangslage, 1 = Ende; Richtung +1 öffnet, -1 schließt
        FlatButton btnMotion;
        float motion;
        int motionDir;
        Model motionModel;
        // Tempo der Bewegung: Echtzeit-Faktoren (1 = echte Dauer, z. B. 0,4 s) oder 0 = physikalisch (nach Rechenschritten)
        static readonly double[] MotionSpeeds = { 0.25, 0.5, 1, 2, 4, 0 };
        static readonly string[] MotionSpeedNames = { "Zeitlupe 0,25×", "Zeitlupe 0,5×", "Echtzeit 1×", "Schneller 2×", "Schneller 4×", "Physikalisch" };
        DropDown cbMotionSpeed;
        int motionSpeed = 2;
        double motionWall, motionAir, lastTickMs = -1, lastMotionSeconds = -1, lastRecPaint;
        /// <summary>Wird ausgelöst, wenn im Kopf "3D" gewählt wird.</summary>
        public event EventHandler SwitchTo3D;
        DropDown cbRes;
        ModelPicker cbShape;
        FlatSlider tbAngle, tbSize, tbRe;
        Toggle chkSmoke, chkWalls;
        FlatButton btnRun, btnReset, btnInfo, btnTheme, btnFile;

        // Vergleich: zweite Strömung B mit denselben Einstellungen (Re, Größe, Winkel, Auflösung),
        // aber anderem Modell bzw. anderer Klappenstellung. A oben, B unten im Strömungsbild.
        bool compare;
        Solver solverB;
        Particles particlesB;
        readonly Renderer rendererB = new Renderer();
        readonly ForceStats statsB = new ForceStats(400000);
        readonly double[] batchFxB = new double[500], batchFyB = new double[500];
        Model modelB;
        float motionB, refLenB, frontalB;
        double curCdB, curClB, meanCdB, meanClB;
        bool settledB;
        Toggle chkCompare, chkMotionB;
        ModelPicker cbShapeB;
        const int CompareGap = 10;
        int settingsBottom;
        FileMenu fileMenu;
        /// <summary>Eine 3D-Sitzung wurde hier geöffnet: im 3D-Fenster laden.</summary>
        public event Action<string> OpenIn3D;
        NumberBox numMeters, numKmh;
        InfoRows rowsGeo, rowsPhys;
        HintLabel hintModel;
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
            Text = "Windkanal · 2D";
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
            // solange das 3D-Fenster offen ist, rechnet 2D nicht im Hintergrund weiter
            VisibleChanged += delegate { if (Visible) timer.Start(); else timer.Stop(); };
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
            segMode = new Segmented { BackColor = Theme.Bg };
            segMode.Items.AddRange(new[] { "2D", "3D" });
            segMode.SelectedIndexChanged += delegate
            {
                if (segMode.SelectedIndex != 1) return;
                segMode.SelectedIndex = 0;
                if (SwitchTo3D != null) SwitchTo3D(this, EventArgs.Empty);
            };
            btnFile = new FlatButton { Icon = "\uE712", BackColor = Theme.Bg };
            if (Theme.Icons == null) btnFile.Text = "Datei";
            tips.SetToolTip(btnFile, "Datei: Sitzung speichern und öffnen, Bild speichern, Video oder GIF aufnehmen");
            btnFile.Click += delegate { fileMenu.Show(btnFile); };
            Controls.AddRange(new Control[] { segView, segMode, btnRun, btnReset, btnInfo, btnTheme, btnFile });

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
            cbMotionSpeed = new DropDown { BackColor = Theme.Card, Visible = false };
            foreach (var n in MotionSpeedNames) cbMotionSpeed.Items.Add(n);
            cbMotionSpeed.SelectedIndex = motionSpeed;
            cbMotionSpeed.SelectedIndexChanged += delegate { motionSpeed = cbMotionSpeed.SelectedIndex; cardView.Invalidate(); };
            tips.SetToolTip(cbMotionSpeed, "Echtzeit: die Bewegung dauert so lange wie am echten Auto (DRS 0,4 s), die Strömung rechnet dabei so schnell "
                                           + "die Grafikkarte kann. Physikalisch: Bewegung und Strömung im echten Verhältnis (Zeitlupe).");
            cardView.Controls.Add(cbMotionSpeed);
            btnMotion = new FlatButton { Primary = true, BackColor = Theme.Card, Visible = false };
            btnMotion.Click += delegate { ToggleMotion(); };
            tips.SetToolTip(btnMotion, "Bewegliches Teil auf- bzw. zufahren (Taste D)");
            cardView.Controls.Add(btnMotion);

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
            cbShape = (ModelPicker)add(new ModelPicker(ModelLibrary.ByCategory()), 48, 4);
            cbShape.Selected = model;
            cbShape.SelectedChanged += OnShapeChanged;
            hintModel = (HintLabel)add(new HintLabel(""), 30, 4);
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
            rowsGeo = (InfoRows)add(new InfoRows(), 3 * InfoRows.Row, 10);
            var btnClear = (FlatButton)add(new FlatButton { Text = "Leere Fläche zum Zeichnen", Icon = "" }, 40, 14);
            btnClear.Click += delegate
            {
                customMask = new bool[solver.N];
                SelectShape(ModelLibrary.Custom());
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
            chkWalls = (Toggle)add(new Toggle { Text = "Wände mit Reibung (Haftbedingung)" }, 28, 14);
            chkWalls.CheckedChanged += delegate
            {
                solver.NoSlipWalls = chkWalls.Checked; stats.Clear();
                if (solverB != null) { solverB.NoSlipWalls = chkWalls.Checked; statsB.Clear(); }
            };

            add(new Section("Vergleich"), 20, 10);
            chkCompare = (Toggle)add(new Toggle { Text = "Zweite Strömung B daneben rechnen" }, 28, 8);
            chkCompare.CheckedChanged += delegate { SetCompare(chkCompare.Checked); };
            cbShapeB = (ModelPicker)add(new ModelPicker(ModelLibrary.ByCategory()), 48, 6);
            cbShapeB.Selected = model;
            cbShapeB.SelectedChanged += delegate
            {
                if (suppress || cbShapeB.Selected.IsCustom) return;
                modelB = cbShapeB.Selected;
                motionB = 0;
                UpdateCompareControls();
                if (compare) RebuildGeometryB(true);
            };
            chkMotionB = (Toggle)add(new Toggle { Text = "B mit offener Klappe (DRS offen)" }, 28, 0);
            chkMotionB.CheckedChanged += delegate
            {
                if (suppress) return;
                motionB = chkMotionB.Checked ? 1 : 0;
                if (compare) RebuildGeometryB(false);
            };
            settingsBottom = y;
            UpdateCompareControls();

            // --- Umrechnung auf Luft
            cardAir = new Card("Echte Größe und Tempo");
            cardAir.Paint += (s, e) =>
            {
                Theme.Prepare(e.Graphics);
                int tw = Theme.Width(cardAir.Title, Theme.Title);
                Theme.Chip(e.Graphics, "20 °C", Card.Pad + tw + 10, 18, Theme.Muted, Theme.Ctl);
            };
            // Länge und Tempo des echten Objekts: daraus folgt die echte Reynoldszahl. Die Simulation übernimmt sie,
            // soweit das Gitter sie schafft (darüber ändern sich die Beiwerte meist nur noch wenig); die Kräfte gelten fürs echte Tempo.
            numMeters = new NumberBox { Caption = "Echte Länge", Minimum = 0.001m, Maximum = 100m, Increment = 0.01m, Decimals = 3, Unit = "m", BackColor = Theme.Card };
            numMeters.SetBounds(Card.Pad, Card.Head, w, 40);
            numMeters.Value = 0.1m;
            numMeters.ValueChanged += delegate { ApplyRealSpeed(); };
            numKmh = new NumberBox { Caption = "Tempo", Minimum = 0.1m, Maximum = 1500m, Increment = 10m, Decimals = 0, Unit = "km/h", BackColor = Theme.Card };
            numKmh.SetBounds(Card.Pad, Card.Head + 48, w, 40);
            numKmh.Value = 100m;
            numKmh.ValueChanged += delegate { ApplyRealSpeed(); };
            tips.SetToolTip(numKmh, "Geschwindigkeit des echten Objekts. Daraus folgt die Reynoldszahl; Widerstand und Auftrieb in Newton gelten für dieses Tempo.");
            rowsPhys = new InfoRows { BackColor = Theme.Card };
            rowsPhys.SetBounds(Card.Pad, Card.Head + 100, w, 3 * InfoRows.Row);
            var note = new HintLabel("Kräfte je Meter Spannweite (2D-Profil)") { BackColor = Theme.Card };
            note.SetBounds(Card.Pad, Card.Head + 100 + 3 * InfoRows.Row + 6, w, 18);
            cardAir.Controls.AddRange(new Control[] { numMeters, numKmh, rowsPhys, note });

            Controls.AddRange(new Control[] { cardView, cardCd, cardCl, cardKenn, cardChart, cardSet, cardAir });
            fileMenu = new FileMenu(this, cardView, SaveSession, LoadSession, ShowMessage);
            // Einstellungen: bei niedrigen Fenstern mit Bildlaufleiste statt abgeschnitten
            cardSet.AutoScroll = true;
            cardSet.AutoScrollMargin = new Size(0, Card.Pad);
            cardSet.Scroll += delegate { cardSet.Invalidate(); };
            cardSet.HandleCreated += delegate { Theme.ThemeScrollbars(cardSet.Handle); };
            ResumeLayout();
            LayoutAll();
            UpdateModelHint();
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
            foreach (var b in new[] { btnReset, btnInfo, btnTheme, btnFile })
            {
                int bw = b.Text.Length > 0 ? Theme.Width(b.Text, b.Font) + 32 : bh;
                x -= 10 + bw;
                b.SetBounds(x, by0, bw, bh);
            }
            int mw = segMode.PreferredWidth;
            segMode.SetBounds(x - 18 - mw, (TopH - 44) / 2 + 2, mw, 44);
            int sw = segView.PreferredWidth;
            segView.SetBounds(Math.Max(240, Outer + (lw - sw) / 2 + 40), (TopH - 44) / 2 + 2, sw, 44);

            cardView.SetBounds(Outer, TopH, lw, by - Gap - TopH);
            view.SetBounds(12, Card.Head, cardView.Width - 24, cardView.Height - Card.Head - 46);
            chkSmoke.SetBounds(cardView.Width - Card.Pad - 140, 16, 140, 28);
            int mw2 = Math.Max(150, Theme.Width(btnMotion.Text, btnMotion.Font) + 40);
            btnMotion.SetBounds(chkSmoke.Left - 14 - mw2, 12, mw2, 36);
            cbMotionSpeed.SetBounds(btnMotion.Left - 10 - 170, 12, 170, 36);

            int cw = 196, kw = 236;
            cardCd.SetBounds(Outer, by, cw, BottomH);
            cardCl.SetBounds(Outer + cw + Gap, by, cw, BottomH);
            cardKenn.SetBounds(Outer + 2 * (cw + Gap), by, kw, BottomH);
            int chx = Outer + 2 * (cw + Gap) + kw + Gap;
            cardChart.SetBounds(chx, by, Math.Max(100, Outer + lw - chx), BottomH);
            cardChart.Title = cardChart.Width < 460 ? "Verlauf" : "Kraftbeiwerte über der Zeit";   // kleines Fenster: kurzer Titel

            int airH = Card.Head + 100 + 3 * InfoRows.Row + 6 + 18 + 18;
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
            RefreshTheme();
        }

        /// <summary>Farben neu anwenden (auch nach einem Wechsel im 3D-Fenster).</summary>
        public void RefreshTheme()
        {
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

        static string ReText(double re)
        {
            return re >= 1e6 ? F(re / 1e6, "0.0") + " Mio." : F(re, re >= 100 ? "#,0" : "0.#");
        }

        /// <summary>Echte Länge und echtes Tempo -> Reynoldszahl; die Simulation nimmt sie, höchstens bis zum Reglerende.</summary>
        void ApplyRealSpeed()
        {
            if (tbRe == null) return;
            double re = (double)numKmh.Value / 3.6 * (double)numMeters.Value / NuAir;
            int v = Math.Max(tbRe.Minimum, Math.Min(tbRe.Maximum, ReToSlider(Math.Max(10, re))));
            if (v != tbRe.Value) tbRe.Value = v;   // löst die übliche Re-Änderung aus
            UpdateReadouts();
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
            bool custom = model.IsCustom;
            tbAngle.Enabled = !custom;
            tbSize.Enabled = !custom;
        }

        void SelectShape(Model m)
        {
            suppress = true;
            model = m;
            cbShape.Selected = m;
            suppress = false;
            UpdateModelHint();
            UpdateSliderLabels();
        }

        /// <summary>Kurzbeschreibung des Modells unter der Auswahl; Quelle und Lizenz im Tooltip.</summary>
        void UpdateModelHint()
        {
            if (hintModel == null) return;
            hintModel.Text = model.IsCustom ? "Mit der Maus ins Strömungsbild zeichnen." : model.Description;
            hintModel.Invalidate();
            string tip = model.IsCustom ? "" : model.Name + "\n" + model.Description + "\n\nQuelle: " + model.Source
                + (model.License.Length > 0 ? "\nLizenz: " + model.License : "");
            tips.SetToolTip(hintModel, tip);
            tips.SetToolTip(cbShape, tip);
        }

        void OnShapeChanged(object sender, EventArgs e)
        {
            if (suppress) return;
            var k = cbShape.Selected;
            if (k.IsCustom)
            {
                // aktuelle Form als Ausgangspunkt zum Weiterzeichnen übernehmen
                customMask = (bool[])solver.Solid.Clone();
                model = k;
                UpdateModelHint();
                UpdateSliderLabels();
                RebuildGeometry(false);
                return;
            }
            model = k;
            UpdateModelHint();
            suppress = true;
            sizePercent = Math.Max(tbSize.Minimum, Math.Min(tbSize.Maximum, k.SizePercent));
            tbSize.Value = sizePercent;
            angleDeg = k.Angle;
            tbAngle.Value = angleDeg;
            reynolds = k.Reynolds;
            tbRe.Value = ReToSlider(reynolds);
            reynolds = SliderToRe(tbRe.Value);
            suppress = false;
            UpdateSliderLabels();
            RebuildGeometry(true);
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (!(ActiveControl is TextBox) && fileMenu.HandleKey(e)) { e.Handled = true; e.SuppressKeyPress = true; return; }
            if (e.KeyCode == Keys.Space && !(ActiveControl is TextBox))
            {
                SetRunning(!running);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.D && !(ActiveControl is TextBox) && model != null && model.HasMotion)
            {
                ToggleMotion();
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
            if (compare) CreateSolverB();

            // eigene Zeichnung auf das neue Gitter übertragen
            if (model.IsCustom && oldMask != null)
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
            if (model != motionModel)
            {
                motionModel = model;
                motion = 0; motionDir = 0;
                UpdateMotionButton();
            }
            int nx = solver.NX, ny = solver.NY;
            var mask = new bool[nx * ny];
            float sizeCells = sizePercent / 100f * ny;
            if (model.IsCustom)
            {
                if (customMask != null && customMask.Length == mask.Length) Array.Copy(customMask, mask, mask.Length);
            }
            else
            {
                // Fahrzeuge und Bauwerke stehen am (reibungsfreien = mitbewegten) Boden, alles andere mittig im Kanal
                float px = (model.OnGround ? 0.3f : 0.25f) * solver.VisibleNX;
                mask = ModelLibrary.Rasterize(model, nx, ny, px, sizeCells, angleDeg, motion);
            }

            int rows = 0;
            for (int y = 0; y < ny; y++)
                for (int x = 2; x < nx - 2; x++)
                    if (mask[y * nx + x]) { rows++; break; }
            frontalCells = rows;
            refLen = model.RefIsSize ? sizeCells : Math.Max(rows, 0);
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
            if (compare) RebuildGeometryB(resetFlow);
        }

        // ------------------------------------------------------------ Vergleich

        void UpdateCompareControls()
        {
            if (cbShapeB == null) return;
            cbShapeB.Enabled = compare;
            var mb = modelB ?? model;
            bool has = mb != null && mb.HasMotion && !mb.IsCustom;
            chkMotionB.Enabled = compare && has;
            chkMotionB.Text = has ? "B mit " + (mb.MotionName == "DRS" ? "offenem DRS" : mb.MotionName + " an") : "B mit offener Klappe (Modell hat keine)";
            suppress = true;
            chkMotionB.Checked = motionB >= 0.5f;
            suppress = false;
            chkMotionB.Invalidate();
        }

        void SetCompare(bool on)
        {
            if (on == compare) return;
            compare = on;
            if (on)
            {
                // Vorgabe: dasselbe Modell; hat es eine Klappe, steht sie bei B offen (z. B. DRS zu gegen offen)
                if (modelB == null || modelB.IsCustom) modelB = model.IsCustom ? ModelLibrary.All[0] : model;
                if (modelB == model && model.HasMotion) motionB = 1;
                suppress = true;
                cbShapeB.Selected = modelB;
                suppress = false;
                CreateSolverB();
                RebuildGeometryB(true);
            }
            else
            {
                if (solverB != null) solverB.Dispose();
                solverB = null; particlesB = null;
                statsB.Clear();
            }
            UpdateCompareControls();
            dirty = true;
            foreach (var c in new[] { cardCd, cardCl, cardChart, cardView }) c.Invalidate();
        }

        void CreateSolverB()
        {
            if (solverB != null) solverB.Dispose();
            solverB = new Solver(ResNX[resIndex], ResNY[resIndex]);
            solverB.NoSlipWalls = chkWalls != null && chkWalls.Checked;
            particlesB = new Particles(solverB.NX, solverB.NY);
        }

        /// <summary>Form B mit denselben Größen- und Winkeleinstellungen wie A rastern.</summary>
        void RebuildGeometryB(bool resetFlow)
        {
            if (!compare || solverB == null || modelB == null) return;
            int nx = solverB.NX, ny = solverB.NY;
            float sizeCells = sizePercent / 100f * ny;
            float px = (modelB.OnGround ? 0.3f : 0.25f) * solverB.VisibleNX;
            var mask = ModelLibrary.Rasterize(modelB, nx, ny, px, sizeCells, angleDeg, modelB.HasMotion ? motionB : 0);
            int rows = 0;
            for (int y = 0; y < ny; y++)
                for (int x = 2; x < nx - 2; x++)
                    if (mask[y * nx + x]) { rows++; break; }
            frontalB = rows;
            refLenB = modelB.RefIsSize ? sizeCells : Math.Max(rows, 0);
            if (refLenB < 2) refLenB = 0.1f * ny;
            solverB.ApplyMask(mask);
            UpdateFlowParamsB();
            if (resetFlow) { solverB.Reset(); particlesB.Clear(); }
            statsB.Clear();
            dirty = true;
        }

        void UpdateFlowParamsB()
        {
            if (solverB == null) return;
            double L = Math.Max(2.0, refLenB);
            solverB.U0 = Solver.ChooseU0(reynolds, L, frontalB / (double)solverB.NY);
            solverB.Nu = (float)(solverB.U0 * L / reynolds);
        }

        void UpdateReadoutsB()
        {
            if (!compare || solverB == null) return;
            double T = refLenB / solverB.U0;
            int transient = (int)(15 * T);
            int window = Math.Min((int)(40 * T), statsB.Count - transient);
            settledB = window > 5 * T;
            curCdB = statsB.Count > 0 ? statsB.Cd(0) : 0;
            curClB = statsB.Count > 0 ? statsB.Cl(0) : 0;
            double freq;
            meanCdB = meanClB = 0;
            if (settledB) statsB.Analyze(window, (int)(0.5 * T), out meanCdB, out meanClB, out freq);
        }

        /// <summary>Unterschied B gegenüber A in Prozent, als Text mit Vorzeichen.</summary>
        static string Delta(double a, double b)
        {
            if (Math.Abs(a) < 1e-6) return "";
            double d = (b - a) / Math.Abs(a) * 100;
            return (d >= 0 ? "+" : "−") + F(Math.Abs(d), "0") + " %";
        }

        // ------------------------------------------------------------ Sitzung

        void ShowMessage(string text)
        {
            warning = text;
            warningUntil = clock.Elapsed.TotalMilliseconds + 7000;
            UpdateStatus();
        }

        Dictionary<string, string> SaveSession()
        {
            var d = new Dictionary<string, string>();
            d["modus"] = "2D";
            if (model.IsCustom) ShowMessage("Hinweis: eine eigene Zeichnung wird nicht mitgespeichert, nur die Einstellungen.");
            else d["modell"] = model.Id;
            d["groesse"] = sizePercent.ToString();
            d["winkel"] = angleDeg.ToString();
            d["reynolds"] = Session.F(reynolds);
            d["aufloesung"] = resIndex.ToString();
            d["ansicht"] = ((int)viewMode).ToString();
            d["rauchlinien"] = Session.Yes(showSmoke);
            d["waende-reibung"] = Session.Yes(chkWalls.Checked);
            d["laenge-m"] = Session.F((double)numMeters.Value);
            d["tempo-kmh"] = Session.F((double)numKmh.Value);
            if (model.HasMotion)
            {
                d["bewegung"] = Session.F(motion >= 0.5f ? 1 : 0);
                d["bewegung-tempo"] = motionSpeed.ToString();
            }
            d["vergleich"] = Session.Yes(compare);
            if (modelB != null && !modelB.IsCustom)
            {
                d["modell-b"] = modelB.Id;
                d["bewegung-b"] = Session.F(motionB >= 0.5f ? 1 : 0);
            }
            return d;
        }

        void LoadSession(Dictionary<string, string> d)
        {
            if (d["modus"] == "3D")
            {
                if (OpenIn3D != null) OpenIn3D(fileMenu.LastPath);
                return;
            }
            string id;
            Model m = d.TryGetValue("modell", out id) ? ModelLibrary.Find(id) : null;
            if (m != null && m != model) cbShape.Selected = m;   // setzt die Startwerte des Modells
            int r = Session.I(d, "aufloesung", resIndex);
            if (r != resIndex && r >= 0 && r < cbRes.Items.Count) cbRes.SelectedIndex = r;
            suppress = true;
            tbSize.Value = sizePercent = Math.Max(tbSize.Minimum, Math.Min(tbSize.Maximum, Session.I(d, "groesse", sizePercent)));
            tbAngle.Value = angleDeg = Math.Max(tbAngle.Minimum, Math.Min(tbAngle.Maximum, Session.I(d, "winkel", angleDeg)));
            tbRe.Value = ReToSlider(Session.D(d, "reynolds", reynolds));
            reynolds = SliderToRe(tbRe.Value);
            numMeters.Value = (decimal)Session.D(d, "laenge-m", (double)numMeters.Value);
            numKmh.Value = (decimal)Session.D(d, "tempo-kmh", (double)numKmh.Value);
            suppress = false;
            segView.SelectedIndex = Math.Max(0, Math.Min(3, Session.I(d, "ansicht", (int)viewMode)));
            chkSmoke.Checked = Session.B(d, "rauchlinien", showSmoke);
            chkWalls.Checked = Session.B(d, "waende-reibung", chkWalls.Checked);
            UpdateSliderLabels();
            RebuildGeometry(true);
            if (model.HasMotion)
            {
                cbMotionSpeed.SelectedIndex = Math.Max(0, Math.Min(MotionSpeeds.Length - 1, Session.I(d, "bewegung-tempo", motionSpeed)));
                motion = Session.D(d, "bewegung", 0) >= 0.5 ? 1 : 0;
                motionDir = 0;
                ApplyMotionMask();
                UpdateMotionButton();
            }
            string idB;
            Model mb = d.TryGetValue("modell-b", out idB) ? ModelLibrary.Find(idB) : null;
            if (mb != null) { modelB = mb; motionB = Session.D(d, "bewegung-b", 0) >= 0.5 ? 1 : 0; suppress = true; cbShapeB.Selected = mb; suppress = false; }
            bool cmp = Session.B(d, "vergleich", false);
            if (cmp && compare) RebuildGeometryB(true);
            chkCompare.Checked = cmp;
            UpdateCompareControls();
            ShowMessage("Sitzung geöffnet");
        }

        /// <summary>Sitzung aus einer Datei öffnen (auch aus dem 3D-Fenster heraus).</summary>
        public void LoadSessionFile(string path) { fileMenu.OpenSession(path); }

        // ------------------------------------------------------------ bewegliche Teile

        void ToggleMotion()
        {
            if (model == null || !model.HasMotion) return;
            // Richtung umkehren; aus der Ruhe heraus: offen -> zu, sonst zu -> offen
            motionDir = motionDir != 0 ? -motionDir : (motion >= 1 ? -1 : 1);
            motionWall = 0; motionAir = 0; lastMotionSeconds = -1;
            UpdateMotionButton();
        }

        void UpdateMotionButton()
        {
            if (btnMotion == null) return;
            bool has = model != null && model.HasMotion && !model.IsCustom;
            btnMotion.Visible = has;
            cbMotionSpeed.Visible = has;
            if (!has) return;
            string n = model.MotionName;
            bool openNext = motionDir == 0 ? motion < 1 : motionDir < 0;
            btnMotion.Text = n == "DRS" ? (openNext ? "DRS öffnen" : "DRS schließen") : (openNext ? n + " an" : n + " aus");
            btnMotion.Invalidate();
            if (cardView != null) LayoutAll();
        }

        /// <summary>
        /// Bewegung um 'steps' Rechenschritte weiterführen. Die Dauer entspricht gleich vielen Überströmungen
        /// der Bezugslänge wie am echten Auto (z. B. 0,4 s bei 300 km/h und 0,5 m Flügeltiefe).
        /// </summary>
        void AdvanceMotion(int steps, double wallMs)
        {
            if (motionDir == 0 || model == null || !model.HasMotion) return;
            double total = model.MotionConvective * refLen / solver.U0;   // Rechenschritte für die echte Dauer
            double speed = MotionSpeeds[motionSpeed];
            double delta = speed > 0
                ? wallMs / (1000.0 * model.MotionSeconds / speed)          // Echtzeit: nach der Uhr
                : steps / Math.Max(1.0, total);                            // physikalisch: nach Rechenschritten
            motionWall += wallMs;
            motionAir += steps / Math.Max(1.0, total) * model.MotionSeconds;   // so viel echte Zeit ist die Luft weitergekommen
            float before = motion;
            motion = (float)Math.Max(0, Math.Min(1, motion + motionDir * delta));
            if (motion <= 0 || motion >= 1)
            {
                motionDir = 0;
                lastMotionSeconds = motionWall / 1000.0;
                UpdateMotionButton();
            }
            if (motion != before) ApplyMotionMask();
        }

        bool MotionActive { get { return motionDir != 0 && model != null && model.HasMotion; } }

        /// <summary>Nur die Form neu rastern; Strömung, Messreihe und Rechenparameter bleiben (sonst sähe man den Übergang nicht).</summary>
        void ApplyMotionMask()
        {
            int nx = solver.NX, ny = solver.NY;
            float sizeCells = sizePercent / 100f * ny;
            float px = (model.OnGround ? 0.3f : 0.25f) * solver.VisibleNX;
            solver.ApplyMask(ModelLibrary.Rasterize(model, nx, ny, px, sizeCells, angleDeg, motion));
            dirty = true;
        }

        /// <summary>Stand als Zeit im echten Maßstab (Sekunden bei der Modellgeschwindigkeit).</summary>
        string MotionText()
        {
            string s = model.MotionName + " " + F(motion * 100, "0") + " %";
            double speed = MotionSpeeds[motionSpeed];
            if (motionDir != 0)
            {
                s += "  ·  " + F(motionWall / 1000.0, "0.00") + " s";
                // wie weit die Luft im Verhältnis zur echten Zeit mitkommt (Echtzeit-Modus: Grafikkarte begrenzt)
                if (speed > 0 && motionWall > 50) s += "  ·  Luft " + F(100 * motionAir / (motionWall / 1000.0 * speed), "0") + " % Echtzeit";
            }
            else if (lastMotionSeconds >= 0) s += "  ·  in " + F(lastMotionSeconds, "0.00") + " s";
            return s;
        }

        void UpdateFlowParams()
        {
            double L = Math.Max(2.0, refLen);
            solver.U0 = Solver.ChooseU0(reynolds, L, frontalCells / (double)solver.NY);
            solver.Nu = (float)(solver.U0 * L / reynolds);
            if (compare) { UpdateFlowParamsB(); statsB.Clear(); }
        }

        void ResetFlow()
        {
            solver.Reset();
            particles.Clear();
            stats.Clear();
            if (compare && solverB != null) { solverB.Reset(); particlesB.Clear(); statsB.Clear(); }
            dirty = true;
        }

        double ConvectiveTime { get { return refLen / solver.U0; } }

        void OnTick(object sender, EventArgs e)
        {
            double now = clock.Elapsed.TotalMilliseconds;
            double prevTick = lastTickMs;
            lastTickMs = now;
            if (running)
            {
                var sw = Stopwatch.StartNew();
                int steps = 0;
                float q = 0.5f * solver.U0 * solver.U0 * refLen;
                // während einer Bewegung so viel rechnen, wie ins Bild passt (volle Grafikkarte), sonst wie bisher
                bool full = MotionActive;
                double budget = full ? 12 : 22;   // kurze Bilder: Bewegung trifft die Uhr auf etwa 15 ms genau
                int maxSteps = full ? 1000000 : 500;
                do
                {
                    // Pakete so groß wählen, dass sie ins Zeitbudget passen (GPU rechnet ein Paket ohne Pause durch)
                    double left = budget - sw.Elapsed.TotalMilliseconds;
                    int batch = solver.OnGpu ? (int)Math.Max(1, Math.Min(batchFx.Length, 0.8 * left / Math.Max(1e-4, msPerStep))) : 1;
                    batch = Math.Min(batch, maxSteps - steps);
                    double t0 = sw.Elapsed.TotalMilliseconds;
                    solver.StepMany(batch, batchFx, batchFy);
                    if (compare && solverB != null)
                    {
                        solverB.StepMany(batch, batchFxB, batchFyB);
                        float qB = 0.5f * solverB.U0 * solverB.U0 * refLenB;
                        for (int k = 0; k < batch; k++) statsB.Add((float)(batchFxB[k] / qB), (float)(batchFyB[k] / qB));
                    }
                    double dt = sw.Elapsed.TotalMilliseconds - t0;
                    msPerStep = 0.7 * msPerStep + 0.3 * dt / batch;
                    for (int k = 0; k < batch; k++) stats.Add((float)(batchFx[k] / q), (float)(batchFy[k] / q));
                    steps += batch;
                } while (sw.Elapsed.TotalMilliseconds < budget && steps < maxSteps);
                stepMsAcc += sw.Elapsed.TotalMilliseconds;
                stepsAcc += steps;

                if (!solver.IsStable())
                {
                    ResetFlow();
                    warning = "Simulation wurde instabil und neu gestartet. Tipp: Reynoldszahl senken oder Auflösung erhöhen.";
                    warningUntil = now + 9000;
                    return;
                }
                double tickMs = prevTick < 0 ? 16 : Math.Min(100, now - prevTick);
                AdvanceMotion(steps, tickMs);
                solver.AdvectSmoke(steps);
                if (showSmoke && viewMode != ViewMode.Rauch) particles.Update(solver, steps, 2.5f * solver.NX / solver.U0);
                if (compare && solverB != null)
                {
                    if (!solverB.IsStable())
                    {
                        solverB.Reset(); particlesB.Clear(); statsB.Clear();
                        warning = "Strömung B wurde instabil und neu gestartet.";
                        warningUntil = now + 9000;
                    }
                    solverB.AdvectSmoke(steps);
                    if (showSmoke && viewMode != ViewMode.Rauch) particlesB.Update(solverB, steps, 2.5f * solverB.NX / solverB.U0);
                }
                dirty = true;
            }

            if (dirty && view.ClientSize.Width > 0 && view.ClientSize.Height > 0)
            {
                int vh = compare && solverB != null ? (view.ClientSize.Height - CompareGap) / 2 : view.ClientSize.Height;
                renderer.EnsureSize(view.ClientSize.Width, vh);
                if (viewMode == ViewMode.Rauch) solver.ReadSmoke();
                renderer.Render(solver, particles, viewMode, showSmoke, refLen);
                if (compare && solverB != null)
                {
                    rendererB.EnsureSize(view.ClientSize.Width, vh);
                    if (viewMode == ViewMode.Rauch) solverB.ReadSmoke();
                    rendererB.Render(solverB, particlesB, viewMode, showSmoke, refLenB);
                }
                view.Invalidate();
                dirty = false;
                frames++;
            }

            if (fileMenu.Recording)
            {
                fileMenu.Capture();
                if (now - lastRecPaint > 500) { lastRecPaint = now; cardView.Invalidate(new Rectangle(0, 0, cardView.Width, Card.Head)); }
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

            // feinstes Detail (dünnstes Teil, engster Spalt) in Zellen: unter 2 Zellen erfasst das Gitter es nur grob
            float detailCells = model.Detail * sizePercent / 100f * solver.NY;
            bool coarse = model.Detail > 0 && detailCells < 2;
            rowsGeo.Set(new[] { "Versperrung", "Bezugslänge", "Feinstes Detail" },
                        new[] { F(100.0 * frontalCells / solver.NY, "0.0") + " %", F(refLen, "0") + " Zellen (" + model.RefName + ")",
                                model.Detail > 0 ? F(detailCells, "0.0") + " Zellen" + (coarse ? " · Auflösung erhöhen" : "") : "–" });
            rowsGeo.WarnRow = coarse ? 2 : -1;

            double Lm = (double)numMeters.Value;
            double v = (double)numKmh.Value / 3.6;
            double reReal = v * Lm / NuAir;
            double qd = 0.5 * RhoAir * v * v;
            double useCd = settled ? meanCd : curCd, useCl = settled ? meanCl : curCl;
            string reText = ReText(reReal) + (Math.Abs(reReal - reynolds) > 0.02 * reReal ? "  ·  Simulation " + ReText(reynolds) : "");
            rowsPhys.Set(new[] { "Reynoldszahl echt", "Widerstand FW", "Auftrieb FA" },
                         new[] { reText, Sig(useCd * qd * Lm) + " N/m", Sig(useCl * qd * Lm) + " N/m" });

            UpdateReadoutsB();
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
                        double vInf = (double)numKmh.Value / 3.6;
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
                if (fileMenu != null && fileMenu.Recording)
                    x += Theme.Chip(g, fileMenu.RecordingText, x, 18, Theme.Pink, Theme.Tint(Theme.Pink), 24, true) + 6;
                bool movable = model != null && model.HasMotion && !model.IsCustom;
                if (!movable) x += Theme.Chip(g, solver.Backend, x, 18, Theme.Muted, Theme.Ctl) + 6;
                if (movable && (motionDir != 0 || motion > 0 || lastMotionSeconds >= 0))
                {
                    Color mc = motionDir != 0 ? Theme.Accent : Theme.Muted;
                    x += Theme.Chip(g, MotionText(), x, 18, mc, Theme.Tint(mc)) + 6;
                }
                else if (running && mlups > 0)
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
            bool two = compare && solverB != null && rendererB.Bitmap != null;
            DrawTunnel(g, renderer, solver, 0, two ? "A · " + model.Name + (model.HasMotion && motion >= 0.5f ? " (offen)" : "") : null);
            if (two)
                DrawTunnel(g, rendererB, solverB, renderer.Bitmap.Height + CompareGap,
                           "B · " + modelB.Name + (modelB.HasMotion && motionB >= 0.5f ? " (offen)" : ""));
        }

        /// <summary>Tunnelbild mit runden Ecken, bei Vergleich mit Beschriftung oben links.</summary>
        void DrawTunnel(Graphics g, Renderer r, Solver s, int top, string label)
        {
            g.DrawImageUnscaled(r.Bitmap, 0, top);
            var tr = new RectangleF(r.OffX, top + r.OffY, s.VisibleNX * r.Scale, s.NY * r.Scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = new GraphicsPath(FillMode.Alternate))
            using (var b = new SolidBrush(Theme.Card))
            {
                path.AddRectangle(RectangleF.Inflate(tr, 2, 2));
                path.AddPath(Theme.Round(tr, 14), false);
                g.FillPath(b, path);
            }
            if (label != null)
            {
                Theme.Prepare(g);
                Theme.Chip(g, label, tr.X + 10, tr.Y + 10, Color.White, Color.FromArgb(150, 12, 14, 18));
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

            // Vergleich: statt des Verlaufs den Wert von B und den Unterschied zu A
            if (compare && solverB != null)
            {
                using (var p = new Pen(Theme.Border)) g.DrawLine(p, Card.Pad, 138, W - Card.Pad, 138);
                double a = cd ? (settled ? meanCd : curCd) : (settled ? meanCl : curCl);
                double b = cd ? (settledB ? meanCdB : curCdB) : (settledB ? meanClB : curClB);
                int bx = Card.Pad + Theme.Chip(g, "B", Card.Pad, 150, col, Theme.Tint(col)) + 8;
                Theme.Draw(g, statsB.Count > 0 ? F(b, "0.000") : "–", Theme.Mid, Theme.Text, new Rectangle(bx, 144, W - bx - Card.Pad, 34), TextFormatFlags.VerticalCenter);
                if (statsB.Count > 0 && stats.Count > 0)
                    Theme.Draw(g, Delta(a, b) + " zu A" + (settled && settledB ? "" : " (läuft ein)"), Theme.Small, Theme.Muted,
                               new Rectangle(Card.Pad, 178, W - 2 * Card.Pad, 18), TextFormatFlags.VerticalCenter);
                return;
            }

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
            // Legende rechts im Kopf: lang, wenn Platz ist, sonst kurz; „B blass“ nur, wenn es noch passt
            int titleEnd = Card.Pad + Theme.Width(cardChart.Title, Theme.Title) + 12;
            bool withLegendB = compare && solverB != null;
            string tCd = "cw  Widerstand", tCl = "ca  Auftrieb";
            int need = Theme.ChipWidth(tCd, true) + Theme.ChipWidth(tCl, true) + 6;
            if (r.Width - Card.Pad - need < titleEnd) { tCd = "cw"; tCl = "ca"; need = Theme.ChipWidth(tCd, true) + Theme.ChipWidth(tCl, true) + 6; }
            int wB = Theme.ChipWidth("B blass");
            if (r.Width - Card.Pad - need - 6 - wB < titleEnd) withLegendB = false;
            int lx = r.Width - Card.Pad - Theme.ChipWidth(tCl, true);
            Theme.Chip(g, tCl, lx, 18, Theme.Pink, Theme.Tint(Theme.Pink), 24, true);
            int w2 = Theme.ChipWidth(tCd, true);
            Theme.Chip(g, tCd, lx - 6 - w2, 18, Theme.Accent, Theme.Tint(Theme.Accent), 24, true);
            if (withLegendB) Theme.Chip(g, "B blass", lx - 12 - w2 - wB, 18, Theme.Muted, Theme.Ctl);

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
            bool withB = compare && solverB != null && statsB.Count > 2;
            int spanB = withB ? Math.Min(statsB.Count, span) : 0;
            for (int k = 0; k < spanB - Math.Min(spanB - 1, skip); k += stride)
            {
                double a = statsB.Cd(k), b = statsB.Cl(k);
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

            if (withB)
            {
                DrawSeries(g, plot, spanB, lo, hi, true, statsB, true);
                DrawSeries(g, plot, spanB, lo, hi, false, statsB, true);
            }
            DrawSeries(g, plot, span, lo, hi, true, stats, false);
            DrawSeries(g, plot, span, lo, hi, false, stats, false);

            double spanT = span / T;
            string spanText = "letzte " + F(spanT, "0") + " Zeiteinheiten";
            if (Theme.ChipWidth(spanText) + Theme.ChipWidth("jetzt") + 12 < plot.Width)
                Theme.Chip(g, spanText, plot.Left, plot.Bottom + 8, Theme.Muted, Theme.Ctl, 22);
            int jw = Theme.ChipWidth("jetzt");
            Theme.Chip(g, "jetzt", plot.Right - jw, plot.Bottom + 8, Theme.Text, Theme.Ctl, 22);
        }

        void DrawSeries(Graphics g, Rectangle plot, int span, double lo, double hi, bool cd, ForceStats st, bool faded)
        {
            int n = Math.Min(plot.Width, span);
            if (n < 2) return;
            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                int back = (int)((long)(n - 1 - i) * (span - 1) / (n - 1));
                double v = cd ? st.Cd(back) : st.Cl(back);
                v = Math.Max(lo, Math.Min(hi, v));
                float x = plot.Right - (float)back / (span - 1) * plot.Width;
                pts[i] = new PointF(x, (float)(plot.Bottom - (v - lo) / (hi - lo) * plot.Height));
            }
            Color c = cd ? Theme.Accent : Theme.Pink;
            if (faded) c = Theme.Mix(Theme.Card, c, 0.45f);   // B: blasser und dünner
            using (var p = new Pen(c, faded ? 1.5f : 2f) { LineJoin = LineJoin.Round })
            {
                if (!cd) p.DashPattern = new[] { 3f, 2f };
                g.DrawLines(p, pts);
            }
            if (faded) return;
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
            if (!model.IsCustom)
            {
                customMask = (bool[])solver.Solid.Clone();
                SelectShape(ModelLibrary.Custom());
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
