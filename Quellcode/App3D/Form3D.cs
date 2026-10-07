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
        static readonly int[,] Res = { { 160, 64, 64 }, { 256, 112, 112 }, { 384, 160, 160 }, { 512, 224, 224 } };
        static readonly string[] ResNames = { "Niedrig", "Mittel", "Hoch", "Sehr hoch" };
        static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");
        const float UIn = 0.05f;
        const double TauMin = 0.52;   // darunter wird der jetzige Rechenkern instabil

        const int Outer = 18, Gap = 14, TopH = 76, RightW = 344, BottomH = 206;

        /// <summary>Wird ausgelöst, wenn im Kopf "2D" gewählt wird.</summary>
        public event EventHandler SwitchTo2D;

        // Zustand
        readonly List<Mesh> meshes = new List<Mesh>(Mesh.BuiltIn());
        static readonly int BuiltInCount = Mesh.BuiltIn().Length;
        Mesh mesh;
        Placement place;
        Lbm3D lbm;
        string gpuError;
        int resIndex = 1, sizePercent = 25, yaw, pitch, sliceAxis, slicePos = 50;
        bool upIsY, onGround, running = true, suppress;
        double reynolds = 100, reUsed, tauUsed;
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
        readonly Timer timer = new Timer();
        readonly Timer settleRender = new Timer { Interval = 140 };
        readonly Stopwatch clock = Stopwatch.StartNew();
        double lastSlice, rateClock;
        long rateSteps;

        // Oberfläche
        Segmented segMode, segUp, segSlice;
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
            BuildUi();
            CreateSolver();
            Rebuild();

            timer.Interval = 1;
            timer.Tick += OnTick;
            settleRender.Tick += delegate { settleRender.Stop(); sceneDirty = true; view.Invalidate(); };
            VisibleChanged += delegate { if (Visible) timer.Start(); else timer.Stop(); };
            FormClosed += delegate { timer.Stop(); if (lbm != null) lbm.Dispose(); };
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
            Controls.AddRange(new Control[] { segMode, btnRun, btnReset, btnTheme });

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
            segUp = (Segmented)add(new Segmented(), 40, 8);
            segUp.Items.AddRange(new[] { "Oben = Z-Achse", "Oben = Y-Achse" });
            segUp.SelectedIndexChanged += delegate { upIsY = segUp.SelectedIndex == 1; if (!suppress) Rebuild(); };
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
                cbRes.Items.Add(ResNames[i] + " (" + Res[i, 0] + " × " + Res[i, 1] + " × " + Res[i, 2] + ")");
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
            if (meshes.IndexOf(mesh) < BuiltInCount) { segUp.SelectedIndex = 0; upIsY = false; }   // geladene Dateien behalten ihre Ausrichtung
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
                hintRe.Text = limited ? "Begrenzt auf Re " + FormatRe(reUsed) + " (höher wird der Rechenkern noch instabil)"
                                      : "Bezugslänge: Länge des Körpers in Strömungsrichtung";
                hintRe.Invalidate();
            }
        }

        static string FormatRe(double re) { return re.ToString(re >= 100 ? "N0" : "0.#", De); }

        static double SliderToRe(int v)
        {
            double re = 10 * Math.Pow(100, v / 200.0);   // 10 … 1000
            double mag = Math.Pow(10, Math.Floor(Math.Log10(re)) - 1);
            return Math.Round(re / mag) * mag;
        }

        static int ReToSlider(double re) { return (int)Math.Round(200 * Math.Log(re / 10) / Math.Log(100)); }

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
                suppress = false;
                UpdateLabels();
                Rebuild();
            }
        }

        void CreateSolver()
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
            place = Placement.Build(mesh, upIsY, yaw, pitch, sizePercent / 100.0, onGround, scene.NX, scene.NY, scene.NZ);
            Cursor = Cursors.Default;
            scene.Triangles = place.World;
            if (lbm != null) lbm.SetSolid(place.Solid);
            UpdateFlowParams();
            ResetFlow();
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
            if (lbm != null) lbm.Reset(UIn);
            steps = 0;
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
            if (!running || lbm == null || place == null) return;

            // so viele Schritte, wie in ~30 ms passen
            double msPer = mlups > 0 ? lbm.N / (mlups * 1000.0) : 2;
            int n = Math.Max(1, Math.Min(400, (int)(30 / Math.Max(0.01, msPer))));
            var sw = Stopwatch.StartNew();
            double fx, fy, fz;
            lbm.Step(n, (float)(1 / tauUsed), UIn, out fx, out fy, out fz);
            sw.Stop();
            steps += n;
            rateSteps += n;
            if (now - rateClock > 500)
            {
                mlups = (double)lbm.N * rateSteps / ((now - rateClock) * 1000.0);
                rateClock = now; rateSteps = 0;
            }
            if (double.IsNaN(fx) || double.IsInfinity(fx))
            {
                ShowWarning("Die Rechnung ist instabil geworden und wurde neu gestartet. Kleinere Reynoldszahl oder höhere Auflösung wählen.");
                ResetFlow();
                return;
            }
            double q = 0.5 * UIn * UIn * Math.Max(1, place.FrontalCells);
            cw = fx / q; cs = fy / q; ca = fz / q;
            history.Enqueue(new[] { cw, ca });
            int keep = (int)Math.Max(20, 3 * place.LengthX / UIn / Math.Max(1, n));   // etwa drei Überströmzeiten
            while (history.Count > keep) history.Dequeue();
            double sCw = 0, sCa = 0, lo = double.MaxValue, hi = double.MinValue;
            foreach (var h in history) { sCw += h[0]; sCa += h[1]; lo = Math.Min(lo, h[0]); hi = Math.Max(hi, h[0]); }
            cwMean = sCw / history.Count; caMean = sCa / history.Count;
            double through = steps * UIn / Math.Max(1, scene.NX);   // wie oft die Luft schon durch den Kanal ist
            settled = through > 1.0 && history.Count >= keep && (hi - lo) < 0.03 * Math.Abs(cwMean) + 1e-6;

            if (now - lastSlice > 120) { lastSlice = now; RenderSlice(); }
            InvalidateCards();
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
                sceneBmp = scene.Render(view.Width, view.Height, dragging ? 1 : 2);
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
            float[] d = lbm.ReadSlice(sliceAxis, scene.SliceIndex, UIn);
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
                    px[(h - 1 - r) * w + x] = v < 0 ? solid : sliceLut[Math.Max(0, Math.Min(255, (int)(v / 2f * 255)))];
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
            Theme.Draw(g, "2 · U∞", Theme.Small, Theme.Muted, new Rectangle(lx, ly + 8, lw, 18), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
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
