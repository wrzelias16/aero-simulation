using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windkanal;

[assembly: AssemblyTitle("Windkanal – Setup")]
[assembly: AssemblyProduct("Windkanal")]
[assembly: AssemblyVersion("0.0.2.0")]
[assembly: AssemblyFileVersion("0.0.2.0")]

namespace WindkanalSetup
{
    /// <summary>
    /// Installiert/deinstalliert Windkanal für den aktuellen Benutzer (keine Admin-Rechte nötig).
    /// Start mit /uninstall oder als "...Deinstallieren.exe" öffnet direkt die Deinstallation, /silent ohne Fenster.
    /// Oberfläche im Design des Programms (Theme, Schrift Outfit, Hell/Dunkel wie gemerkt).
    /// </summary>
    static class Setup
    {
        const string AppExe = "Windkanal2D.exe";
        const string UninstallExe = "Deinstallieren.exe";
        // Ordner, Registry-Schlüssel und Programmdatei behalten ihre alten Namen, damit Updates die alte Installation finden
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Windkanal2D";
        static readonly string[] OldLinkNames = { "Windkanal 2D" };

        static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Windkanal2D"); }
        }

        static string Link(Environment.SpecialFolder where, string name)
        {
            return Path.Combine(Environment.GetFolderPath(where), name + ".lnk");
        }

        static string StartMenuLink { get { return Link(Environment.SpecialFolder.Programs, AppInfo.Name); } }
        static string DesktopLink { get { return Link(Environment.SpecialFolder.DesktopDirectory, AppInfo.Name); } }

        public static bool IsInstalled { get { return File.Exists(Path.Combine(InstallDir, AppExe)); } }
        public static string AppPath { get { return Path.Combine(InstallDir, AppExe); } }
        public static string Folder { get { return InstallDir; } }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool silent = Array.Exists(args, a => a.Equals("/silent", StringComparison.OrdinalIgnoreCase));
            string self = Path.GetFileName(Application.ExecutablePath);
            bool uninstallMode = Array.Exists(args, a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase))
                                 || self.IndexOf("deinstall", StringComparison.OrdinalIgnoreCase) >= 0;

            if (silent)
            {
                try
                {
                    if (uninstallMode) Uninstall(); else Install(true, true);
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            Application.Run(new SetupForm(uninstallMode));
            return 0;
        }

        // ------------------------------------------------------------ Installation

        public static void Install(bool desktop, bool startMenu)
        {
            CloseRunningApp();
            string dir = InstallDir;
            Directory.CreateDirectory(dir);

            string target = Path.Combine(dir, AppExe);
            using (var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("Payload.Windkanal2D.exe"))
            {
                if (res == null) throw new InvalidOperationException("Programmdaten fehlen im Installer.");
                using (var fs = File.Create(target)) res.CopyTo(fs);
            }

            string uninst = Path.Combine(dir, UninstallExe);
            if (!string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(uninst), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, uninst, true);

            // alte Verknüpfungen („Windkanal 2D“) durch neue ersetzen
            foreach (var old in OldLinkNames)
            {
                TryDelete(Link(Environment.SpecialFolder.Programs, old));
                TryDelete(Link(Environment.SpecialFolder.DesktopDirectory, old));
            }
            if (startMenu) CreateShortcut(StartMenuLink, target, dir);
            else TryDelete(StartMenuLink);
            if (desktop) CreateShortcut(DesktopLink, target, dir);
            else TryDelete(DesktopLink);

            long sizeKb = new FileInfo(target).Length / 1024 + new FileInfo(uninst).Length / 1024;
            using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", AppInfo.Name);
                k.SetValue("DisplayVersion", AppInfo.Version);
                k.SetValue("Publisher", AppInfo.Name);
                k.SetValue("DisplayIcon", target + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", "\"" + uninst + "\" /uninstall");
                k.SetValue("QuietUninstallString", "\"" + uninst + "\" /uninstall /silent");
                k.SetValue("EstimatedSize", (int)sizeKb, RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
        }

        public static void Uninstall()
        {
            CloseRunningApp();
            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);
            foreach (var old in OldLinkNames)
            {
                TryDelete(Link(Environment.SpecialFolder.Programs, old));
                TryDelete(Link(Environment.SpecialFolder.DesktopDirectory, old));
            }
            Registry.CurrentUser.DeleteSubKeyTree(RegKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Windkanal2D", false);   // gemerktes Design

            string dir = InstallDir;
            if (!Directory.Exists(dir)) return;

            string selfPath = Path.GetFullPath(Application.ExecutablePath);
            bool runningFromInstallDir = selfPath.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                if (runningFromInstallDir && string.Equals(Path.GetFullPath(f), selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                TryDelete(f);
            }

            if (runningFromInstallDir)
            {
                // Das laufende Deinstallationsprogramm kann sich nicht selbst löschen:
                // Ordner wird kurz nach dem Beenden entfernt.
                var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            else
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        static void CloseRunningApp()
        {
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(AppExe)))
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(3000)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
            }
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        static void CreateShortcut(string lnk, string target, string workDir)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "Strömungssimulation in 2D und 3D" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }
    }

    /// <summary>
    /// Installer-Fenster im Design des Programms: oben die Strömungsbühne, darunter Name und Version,
    /// Optionen als Schalter, Fortschritt als Kapseln, Rückfragen direkt im Fenster (keine Windows-Meldungsfenster).
    /// </summary>
    sealed class SetupForm : Form
    {
        const int W = 600, Pad = 16, StageH = 196;
        readonly bool uninstallMode;
        readonly FlowStage stage = new FlowStage(320);
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
        readonly Toggle chkStart, chkDesktop, chkLaunch;
        readonly FlatButton btnMain, btnRemove, btnClose;
        readonly int optionsTop, statusTop;
        string status = "", detail = "";
        Color statusColor;
        float progress = -1, progressTarget;
        bool busy, confirmRemove, done;
        double confirmUntil;

        public SetupForm(bool uninstallMode)
        {
            this.uninstallMode = uninstallMode;
            Text = AppInfo.Name + (uninstallMode ? " – entfernen" : " – Installation");
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            Font = Theme.Base;
            DoubleBuffered = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int y = Pad + StageH + 92;
            optionsTop = y;
            chkStart = MakeToggle("Verknüpfung im Startmenü", ref y);
            chkDesktop = MakeToggle("Verknüpfung auf dem Desktop", ref y);
            chkLaunch = MakeToggle("Nach der Installation starten", ref y);
            if (uninstallMode) { chkStart.Visible = chkDesktop.Visible = chkLaunch.Visible = false; y = optionsTop + 20; }
            statusTop = y + 10;
            int by = statusTop + 64;
            ClientSize = new Size(W, by + 40 + Pad + 4);

            btnMain = new FlatButton { Primary = true, BackColor = Theme.Card };
            btnMain.SetBounds(W - Pad - 170, by, 170, 40);
            btnRemove = new FlatButton { Text = "Deinstallieren", BackColor = Theme.Card };
            btnRemove.SetBounds(Pad, by, 150, 40);
            btnClose = new FlatButton { Text = "Schließen", BackColor = Theme.Card };
            btnClose.SetBounds(W - Pad - 170 - 10 - 120, by, 120, 40);
            btnMain.Click += delegate { OnMain(); };
            btnRemove.Click += delegate { OnRemove(); };
            btnClose.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { btnMain, btnRemove, btnClose });

            timer.Tick += delegate { OnTick(); };
            Shown += delegate { timer.Start(); };
            FormClosed += delegate { timer.Stop(); };
            RefreshState();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        Toggle MakeToggle(string text, ref int y)
        {
            var t = new Toggle { Text = text, Checked = true, BackColor = Theme.Card };
            t.SetBounds(Pad + 4, y, W - 2 * Pad - 8, 28);
            Controls.Add(t);
            y += 34;
            return t;
        }

        void RefreshState()
        {
            bool inst = Setup.IsInstalled;
            if (uninstallMode)
            {
                btnMain.Text = confirmRemove ? "Wirklich entfernen" : "Deinstallieren";
                btnMain.Enabled = inst && !busy && !done;
                btnRemove.Visible = false;
            }
            else
            {
                btnMain.Text = done && !chkLaunch.Checked ? "Starten" : inst ? "Neu installieren" : "Installieren";
                btnRemove.Visible = inst && !busy;
                btnRemove.Text = confirmRemove ? "Wirklich entfernen?" : "Deinstallieren";
                btnMain.Enabled = !busy;
            }
            if (!busy && !done && status.Length == 0)
            {
                if (uninstallMode && !inst) SetStatus(AppInfo.Name + " ist nicht installiert.", "", Theme.Muted);
                else if (inst) SetStatus(AppInfo.Name + " ist installiert.", Setup.Folder, Theme.Green);
                else SetStatus("Bereit zur Installation.", "Für dich allein, ohne Admin-Rechte: " + Setup.Folder, Theme.Muted);
            }
            foreach (var b in new[] { btnMain, btnRemove, btnClose }) b.Invalidate();
            Invalidate();
        }

        void SetStatus(string text, string sub, Color c)
        {
            status = text; detail = sub; statusColor = c;
            Invalidate(new Rectangle(0, statusTop, W, 60));
        }

        void OnMain()
        {
            if (uninstallMode) { OnRemove(); return; }
            if (done && !chkLaunch.Checked) { Launch(); Close(); return; }
            Run("Wird installiert …", () => Setup.Install(chkDesktop.Checked, chkStart.Checked), () =>
            {
                if (chkLaunch.Checked) { Launch(); Close(); return; }
                SetStatus("Installiert.", "Starten über das Startmenü oder direkt hier.", Theme.Green);
            });
        }

        /// <summary>Entfernen mit Rückfrage im Fenster: erster Klick fragt, zweiter Klick (innerhalb von 4 s) entfernt.</summary>
        void OnRemove()
        {
            if (!confirmRemove)
            {
                confirmRemove = true;
                confirmUntil = clock.Elapsed.TotalSeconds + 4;
                SetStatus("Wirklich entfernen?", "Programm, Verknüpfungen und Eintrag in den Windows-Apps werden gelöscht. Zum Bestätigen nochmal klicken.", Theme.Orange);
                RefreshState();
                return;
            }
            confirmRemove = false;
            Run("Wird entfernt …", Setup.Uninstall, () => SetStatus(AppInfo.Name + " wurde vollständig entfernt.", "", Theme.Green));
        }

        void Launch()
        {
            try { Process.Start(new ProcessStartInfo(Setup.AppPath) { WorkingDirectory = Setup.Folder }); } catch { }
        }

        /// <summary>Arbeit im Hintergrund, dazu ein Fortschritt, der mindestens eine knappe Sekunde läuft (sonst wirkt es abgehackt).</summary>
        void Run(string text, Action work, Action finished)
        {
            busy = true; done = false;
            progress = 0; progressTarget = 0.85f;
            SetStatus(text, "", Theme.Accent);
            RefreshState();
            Exception error = null;
            var started = clock.Elapsed.TotalSeconds;
            var th = new Thread(() => { try { work(); } catch (Exception ex) { error = ex; } }) { IsBackground = true };
            th.SetApartmentState(ApartmentState.STA);   // Verknüpfungen über WScript.Shell brauchen STA
            th.Start();
            var wait = new System.Windows.Forms.Timer { Interval = 30 };
            wait.Tick += delegate
            {
                if (th.IsAlive || clock.Elapsed.TotalSeconds - started < 0.9) return;
                wait.Stop(); wait.Dispose();
                busy = false;
                if (error != null)
                {
                    progress = -1;
                    SetStatus("Das hat nicht geklappt.", error.Message, Theme.Orange);
                }
                else
                {
                    progressTarget = 1;
                    done = true;
                    finished();
                }
                RefreshState();
            };
            wait.Start();
        }

        void OnTick()
        {
            if (confirmRemove && clock.Elapsed.TotalSeconds > confirmUntil)
            {
                confirmRemove = false;
                status = "";
                RefreshState();
            }
            if (progress >= 0) progress += (progressTarget - progress) * 0.12f;
            Invalidate(new Rectangle(Pad, Pad, W - 2 * Pad, StageH));
            if (progress >= 0) Invalidate(new Rectangle(0, statusTop, W, 60));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Prepare(g);
            g.Clear(Theme.Card);
            var sr = new RectangleF(Pad, Pad, W - 2 * Pad, StageH);
            stage.Draw(g, sr, (float)clock.Elapsed.TotalSeconds, 12, Theme.Dark ? Color.FromArgb(205, 209, 216) : Color.FromArgb(70, 76, 86));

            int ty = Pad + StageH + 22;
            Theme.DrawLogo(g, Pad + 2, ty, 42, Theme.Ink, Theme.OnInk);
            Theme.Draw(g, "windkanal", Theme.Word, Theme.Text, new Rectangle(Pad + 56, ty - 2, 260, 28), TextFormatFlags.VerticalCenter);
            Theme.Draw(g, uninstallMode ? "Vom Computer entfernen" : "Strömungssimulation in 2D und 3D", Theme.Small, Theme.Muted,
                       new Rectangle(Pad + 57, ty + 24, 300, 18), TextFormatFlags.VerticalCenter);
            string ver = "Version " + AppInfo.Version;
            int vw = Theme.ChipWidth(ver);
            Theme.Chip(g, ver, W - Pad - vw, ty + 9, Theme.Muted, Theme.Ctl);

            // Trennlinie über den Optionen
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, Pad, optionsTop - 12, W - Pad, optionsTop - 12);

            // Zustand: Text, darunter Details oder Fortschritt
            Theme.Draw(g, status, Theme.Label, statusColor, new Rectangle(Pad + 4, statusTop, W - 2 * Pad, 22), TextFormatFlags.VerticalCenter);
            if (progress >= 0)
                Theme.Capsules(g, new RectangleF(Pad + 4, statusTop + 30, W - 2 * Pad - 8, 10), 44, Math.Min(1, progress), Theme.Accent, Theme.Track);
            else if (detail.Length > 0)
                Theme.Draw(g, detail, Theme.Small, Theme.Muted, new Rectangle(Pad + 4, statusTop + 24, W - 2 * Pad - 8, 34),
                           TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }
}
