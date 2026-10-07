using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windkanal;

[assembly: AssemblyTitle("Windkanal – Setup")]
[assembly: AssemblyProduct("Windkanal")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WindkanalSetup
{
    /// <summary>
    /// Eine Datei für alles: Installer und Deinstaller in einem. Enthält das Programm als Ressource.
    /// Installieren mit frei wählbarem Ordner (Ordner, die Adminrechte brauchen, z. B. „Programme“: Neustart mit Adminrechten).
    /// Nach der Installation liegt eine Kopie dieser Datei als „Deinstallieren.exe“ im Programmordner; darauf zeigt der
    /// Eintrag in den Windows-Apps.
    /// Aufruf: ohne Argumente = Fenster; /uninstall = Entfernen; /silent = ohne Fenster;
    /// /install /dir="…" /desktop=0|1 /startmenu=0|1 /launch=0|1 = Installation sofort starten (für den Neustart mit Adminrechten).
    /// </summary>
    static class Setup
    {
        const string AppExe = "Windkanal2D.exe";
        const string UninstallExe = "Deinstallieren.exe";
        // Registry-Schlüssel und Programmdatei behalten ihre alten Namen, damit Updates die alte Installation finden
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Windkanal2D";
        static readonly string[] OldLinkNames = { "Windkanal 2D" };

        /// <summary>Ordner der früheren Versionen (fester Ort).</summary>
        static string LegacyDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Windkanal2D"); }
        }

        /// <summary>Ordner der bestehenden Installation laut Windows-Apps-Eintrag (oder alter fester Ort), sonst null.</summary>
        public static string InstalledDir
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RegKey))
                    {
                        var d = k != null ? k.GetValue("InstallLocation") as string : null;
                        if (!string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, AppExe))) return d;
                    }
                }
                catch { }
                return File.Exists(Path.Combine(LegacyDir, AppExe)) ? LegacyDir : null;
            }
        }

        /// <summary>Vorschlag: bestehende Installation, sonst für den eigenen Benutzer ohne Adminrechte.</summary>
        public static string DefaultDir
        {
            get
            {
                return InstalledDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);
            }
        }

        public static bool IsInstalled { get { return InstalledDir != null; } }

        static string Link(Environment.SpecialFolder where, string name)
        {
            return Path.Combine(Environment.GetFolderPath(where), name + ".lnk");
        }

        static string StartMenuLink { get { return Link(Environment.SpecialFolder.Programs, AppInfo.Name); } }
        static string DesktopLink { get { return Link(Environment.SpecialFolder.DesktopDirectory, AppInfo.Name); } }

        public static bool IsAdmin
        {
            get
            {
                try { return new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                                 .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator); }
                catch { return false; }
            }
        }

        /// <summary>
        /// Darf dieser Prozess in den Ordner schreiben? Geprüft wird im Ordner selbst oder, wenn er noch nicht existiert,
        /// im nächsten vorhandenen Elternordner – ohne Ordner anzulegen; die Probedatei wird sofort wieder gelöscht.
        /// </summary>
        public static bool CanWrite(string dir)
        {
            try
            {
                string d = Path.GetFullPath(dir);
                while (d != null && !Directory.Exists(d)) d = Path.GetDirectoryName(d);
                if (d == null) return false;
                string probe = Path.Combine(d, ".schreibtest-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Func<string, bool> has = a => Array.Exists(args, x => x.Equals(a, StringComparison.OrdinalIgnoreCase));
            Func<string, string> val = key =>
            {
                foreach (var x in args)
                    if (x.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) return x.Substring(key.Length + 1).Trim('"');
                return null;
            };
            string self = Path.GetFileName(Application.ExecutablePath);
            bool uninstallMode = has("/uninstall") || self.IndexOf("deinstall", StringComparison.OrdinalIgnoreCase) >= 0;
            var opts = new Options
            {
                Dir = val("/dir") ?? DefaultDir,
                Desktop = val("/desktop") != "0",
                StartMenu = val("/startmenu") != "0",
                Launch = val("/launch") != "0",
                AutoStart = has("/install") || (uninstallMode && has("/now"))
            };

            if (has("/silent"))
            {
                try
                {
                    if (uninstallMode) Uninstall(); else Install(opts.Dir, opts.Desktop, opts.StartMenu);
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            }
            Application.Run(new SetupForm(uninstallMode, opts));
            return 0;
        }

        public sealed class Options
        {
            public string Dir;
            public bool Desktop, StartMenu, Launch, AutoStart;
        }

        /// <summary>Diese Datei mit Adminrechten neu starten (Windows fragt nach). false, wenn abgelehnt.</summary>
        public static bool RestartAsAdmin(string args)
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath, args) { Verb = "runas", UseShellExecute = true });
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------ Installation

        public static void Install(string dir, bool desktop, bool startMenu)
        {
            CloseRunningApp();
            dir = Path.GetFullPath(dir);
            // Wechsel des Ordners: alte Installation vorher entfernen (sonst bliebe sie verwaist liegen)
            string old = InstalledDir;
            if (old != null && !string.Equals(Path.GetFullPath(old).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                try { RemoveFiles(old); } catch { }   // alter Ordner nicht löschbar (z. B. Rechte): neue Installation trotzdem anlegen
            }
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
            foreach (var o in OldLinkNames)
            {
                TryDelete(Link(Environment.SpecialFolder.Programs, o));
                TryDelete(Link(Environment.SpecialFolder.DesktopDirectory, o));
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
            string dir = InstalledDir;
            TryDelete(StartMenuLink);
            TryDelete(DesktopLink);
            foreach (var o in OldLinkNames)
            {
                TryDelete(Link(Environment.SpecialFolder.Programs, o));
                TryDelete(Link(Environment.SpecialFolder.DesktopDirectory, o));
            }
            Registry.CurrentUser.DeleteSubKeyTree(RegKey, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Windkanal2D", false);   // gemerktes Design
            if (dir != null) RemoveFiles(dir);
        }

        /// <summary>Programmdateien und Ordner löschen; läuft diese Datei selbst aus dem Ordner, verschwindet er kurz nach dem Beenden.</summary>
        static void RemoveFiles(string dir)
        {
            if (!Directory.Exists(dir)) return;
            string selfPath = Path.GetFullPath(Application.ExecutablePath);
            bool runningFromDir = selfPath.StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            foreach (var f in new[] { AppExe, UninstallExe })
            {
                string p = Path.Combine(dir, f);
                if (runningFromDir && string.Equals(Path.GetFullPath(p), selfPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(p)) File.Delete(p);   // Fehler (z. B. fehlende Rechte) bis nach oben melden
            }
            // nur unseren eigenen Ordner löschen, und nur wenn sonst nichts darin liegt
            bool empty = Directory.GetFileSystemEntries(dir).Length == (runningFromDir ? 1 : 0);
            if (!empty) return;
            if (runningFromDir)
            {
                var psi = new ProcessStartInfo("cmd.exe", "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
                {
                    CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(psi);
            }
            else
            {
                try { Directory.Delete(dir, false); } catch { }
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

        /// <summary>Programm starten – bei Adminrechten über den Explorer, damit es als normaler Benutzer läuft.</summary>
        public static void Launch(string dir)
        {
            string app = Path.Combine(dir, AppExe);
            try
            {
                if (IsAdmin) Process.Start("explorer.exe", "\"" + app + "\"");
                else Process.Start(new ProcessStartInfo(app) { WorkingDirectory = dir });
            }
            catch { }
        }
    }

    /// <summary>
    /// Installer-Fenster im Design des Programms: oben die Strömungsbühne, darunter Name und Version, der Zielordner
    /// (mit „Ändern …“), Optionen als Schalter, Fortschritt als Balken, Rückfragen direkt im Fenster.
    /// </summary>
    sealed class SetupForm : Form
    {
        const int W = 600, Pad = 16, StageH = 196;
        readonly bool uninstallMode;
        readonly Setup.Options opts;
        readonly FlowStage stage = new FlowStage(320);
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 16 };
        readonly Toggle chkStart, chkDesktop, chkLaunch;
        readonly FlatButton btnMain, btnRemove, btnClose, btnDir;
        readonly int dirTop, optionsTop, statusTop;
        string dir, status = "", detail = "";
        Color statusColor;
        float progress = -1, progressTarget;
        bool busy, confirmRemove, done, needsAdmin;
        double confirmUntil;

        public SetupForm(bool uninstallMode, Setup.Options opts)
        {
            this.uninstallMode = uninstallMode;
            this.opts = opts;
            dir = uninstallMode ? (Setup.InstalledDir ?? opts.Dir) : opts.Dir;
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
            dirTop = y;
            // Zielordner mit „Ändern …“
            btnDir = new FlatButton { Text = "Ändern …", BackColor = Theme.Card };
            btnDir.SetBounds(W - Pad - 110, y + 14, 110, 34);
            btnDir.Click += delegate { ChooseDir(); };
            Controls.Add(btnDir);
            y += 60;
            optionsTop = y;
            chkStart = MakeToggle("Verknüpfung im Startmenü", opts.StartMenu, ref y);
            chkDesktop = MakeToggle("Verknüpfung auf dem Desktop", opts.Desktop, ref y);
            chkLaunch = MakeToggle("Nach der Installation starten", opts.Launch, ref y);
            if (uninstallMode)
            {
                chkStart.Visible = chkDesktop.Visible = chkLaunch.Visible = btnDir.Visible = false;
                y = optionsTop;
            }
            statusTop = y + 10;
            int by = statusTop + 64;
            ClientSize = new Size(W, by + 40 + Pad + 4);

            btnMain = new FlatButton { Primary = true, BackColor = Theme.Card };
            btnMain.SetBounds(W - Pad - 210, by, 210, 40);
            btnRemove = new FlatButton { Text = "Deinstallieren", BackColor = Theme.Card };
            btnRemove.SetBounds(Pad, by, 150, 40);
            btnClose = new FlatButton { Text = "Schließen", BackColor = Theme.Card };
            btnClose.SetBounds(W - Pad - 210 - 10 - 120, by, 120, 40);
            btnMain.Click += delegate { OnMain(); };
            btnRemove.Click += delegate { OnRemove(); };
            btnClose.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { btnMain, btnRemove, btnClose });

            timer.Tick += delegate { OnTick(); };
            Shown += delegate
            {
                timer.Start();
                // nach dem Neustart mit Adminrechten gleich weitermachen
                if (opts.AutoStart) { if (uninstallMode) { confirmRemove = true; OnRemove(); } else OnMain(); }
            };
            FormClosed += delegate { timer.Stop(); };
            CheckDir();
            RefreshState();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        Toggle MakeToggle(string text, bool on, ref int y)
        {
            var t = new Toggle { Text = text, Checked = on, BackColor = Theme.Card };
            t.SetBounds(Pad + 4, y, W - 2 * Pad - 8, 28);
            Controls.Add(t);
            y += 34;
            return t;
        }

        /// <summary>Braucht der gewählte Ordner Adminrechte? (legt dabei nichts an)</summary>
        void CheckDir()
        {
            needsAdmin = !Setup.IsAdmin && !Setup.CanWrite(dir);
        }

        void ChooseDir()
        {
            string picked = FolderPicker.Pick(this, "Ordner für " + AppInfo.Name + " wählen", Directory.Exists(dir) ? dir : Path.GetDirectoryName(dir));
            if (picked == null) return;
            // in einen eigenen Unterordner installieren, außer der gewählte Ordner heißt schon so
            if (!Path.GetFileName(picked.TrimEnd('\\')).Equals(AppInfo.Name, StringComparison.OrdinalIgnoreCase))
                picked = Path.Combine(picked, AppInfo.Name);
            dir = picked;
            CheckDir();
            status = "";
            RefreshState();
        }

        void RefreshState()
        {
            bool inst = Setup.IsInstalled;
            if (uninstallMode)
            {
                needsAdmin = inst && !Setup.IsAdmin && !Setup.CanWrite(Setup.InstalledDir);
                btnMain.Text = confirmRemove ? "Wirklich entfernen" : needsAdmin ? "Als Admin entfernen" : "Deinstallieren";
                btnMain.Enabled = inst && !busy && !done;
                btnRemove.Visible = false;
            }
            else
            {
                btnMain.Text = done && !chkLaunch.Checked ? "Starten" : needsAdmin ? "Als Admin installieren"
                             : inst ? "Neu installieren" : "Installieren";
                btnRemove.Visible = inst && !busy;
                btnRemove.Text = confirmRemove ? "Wirklich entfernen?" : "Deinstallieren";
                btnMain.Enabled = !busy;
                btnDir.Enabled = !busy && !done;
            }
            if (!busy && !done && status.Length == 0)
            {
                if (uninstallMode && !inst) SetStatus(AppInfo.Name + " ist nicht installiert.", "", Theme.Muted);
                else if (needsAdmin) SetStatus("Für diesen Ordner braucht es Administratorrechte.", "Windows fragt beim Installieren nach. Oder einen anderen Ordner wählen.", Theme.Orange);
                else if (inst) SetStatus(AppInfo.Name + " ist installiert.", "Neu installieren ersetzt das Programm durch diese Version " + AppInfo.Version + ".", Theme.Green);
                else SetStatus("Bereit zur Installation.", "Ohne Adminrechte, solange der Ordner dir gehört.", Theme.Muted);
            }
            foreach (var b in new[] { btnMain, btnRemove, btnClose, btnDir }) b.Invalidate();
            Invalidate();
        }

        void SetStatus(string text, string sub, Color c)
        {
            status = text; detail = sub; statusColor = c;
            Invalidate(new Rectangle(0, statusTop, W, 60));
        }

        string AdminArgs(bool uninstall)
        {
            if (uninstall) return "/uninstall /now";
            return "/install /dir=\"" + dir + "\" /desktop=" + (chkDesktop.Checked ? 1 : 0) + " /startmenu=" + (chkStart.Checked ? 1 : 0)
                   + " /launch=" + (chkLaunch.Checked ? 1 : 0);
        }

        void OnMain()
        {
            if (uninstallMode) { OnRemove(); return; }
            if (done && !chkLaunch.Checked) { Setup.Launch(dir); Close(); return; }
            if (needsAdmin)
            {
                if (Setup.RestartAsAdmin(AdminArgs(false))) Close();
                else SetStatus("Ohne Administratorrechte geht dieser Ordner nicht.", "Einen anderen Ordner wählen, z. B. den Vorschlag im eigenen Benutzerordner.", Theme.Orange);
                return;
            }
            string target = dir;
            Run("Wird installiert …", () => Setup.Install(target, chkDesktop.Checked, chkStart.Checked), () =>
            {
                if (chkLaunch.Checked) { Setup.Launch(target); Close(); return; }
                SetStatus("Installiert.", target, Theme.Green);
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
            string inst = Setup.InstalledDir;
            if (inst != null && !Setup.IsAdmin && !Setup.CanWrite(inst))
            {
                if (Setup.RestartAsAdmin(AdminArgs(true))) Close();
                else SetStatus("Ohne Administratorrechte lässt sich der Ordner nicht löschen.", inst, Theme.Orange);
                return;
            }
            Run("Wird entfernt …", Setup.Uninstall, () => SetStatus(AppInfo.Name + " wurde vollständig entfernt.", "", Theme.Green));
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

            using (var p = new Pen(Theme.Border)) g.DrawLine(p, Pad, dirTop - 12, W - Pad, dirTop - 12);
            // Zielordner (beim Entfernen: der installierte Ordner)
            if (!uninstallMode)
            {
                Theme.Draw(g, "INSTALLIEREN NACH", Theme.SmallMed, Theme.Muted, new Rectangle(Pad + 4, dirTop, 300, 16), TextFormatFlags.VerticalCenter);
                Theme.Draw(g, dir, Theme.Base, Theme.Text, new Rectangle(Pad + 4, dirTop + 18, W - 2 * Pad - 130, 28),
                           TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
            }

            // Zustand: Text, darunter Details oder Fortschritt
            Theme.Draw(g, status, Theme.Label, statusColor, new Rectangle(Pad + 4, statusTop, W - 2 * Pad, 22), TextFormatFlags.VerticalCenter);
            if (progress >= 0)
                Theme.ProgressBar(g, new RectangleF(Pad + 4, statusTop + 32, W - 2 * Pad - 8, 8), Math.Min(1, progress), Theme.Accent, Theme.Track,
                                  (float)clock.Elapsed.TotalSeconds);
            else if (detail.Length > 0)
                Theme.Draw(g, detail, Theme.Small, Theme.Muted, new Rectangle(Pad + 4, statusTop + 24, W - 2 * Pad - 8, 34),
                           TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>Moderne Windows-Ordnerauswahl (wie im Explorer) über IFileOpenDialog mit „nur Ordner“.</summary>
    static class FolderPicker
    {
        public static string Pick(IWin32Window owner, string title, string start)
        {
            IFileOpenDialog dlg = null;
            try
            {
                dlg = (IFileOpenDialog)new FileOpenDialogCom();
                dlg.SetOptions(0x20 | 0x40 | 0x800);   // Ordner wählen, nur Dateisystem, Pfad muss existieren
                dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(start) && Directory.Exists(start))
                {
                    IShellItem folder;
                    if (SHCreateItemFromParsingName(start, IntPtr.Zero, typeof(IShellItem).GUID, out folder) == 0) dlg.SetFolder(folder);
                }
                if (dlg.Show(owner != null ? owner.Handle : IntPtr.Zero) != 0) return null;   // abgebrochen
                IShellItem item;
                dlg.GetResult(out item);
                IntPtr p;
                item.GetDisplayName(0x80058000, out p);   // SIGDN_FILESYSPATH
                string path = Marshal.PtrToStringUni(p);
                Marshal.FreeCoTaskMem(p);
                return path;
            }
            catch
            {
                // Rückfall: klassische Ordnerauswahl
                using (var f = new FolderBrowserDialog { Description = title, SelectedPath = start ?? "" })
                    return f.ShowDialog(owner) == DialogResult.OK ? f.SelectedPath : null;
            }
            finally { if (dlg != null) Marshal.ReleaseComObject(dlg); }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr bindCtx, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IShellItem item);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        // nur die benutzten Methoden haben Parameter; die übrigen halten die Reihenfolge der vtable
        [ComImport, Guid("d57c7288-d4ad-4768-be02-9d969532d960"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void _SetFileTypes(); void _SetFileTypeIndex(); void _GetFileTypeIndex(); void _Advise(); void _Unadvise();
            void SetOptions(uint options);
            void _GetOptions(); void _SetDefaultFolder();
            void SetFolder(IShellItem item);
            void _GetFolder(); void _GetCurrentSelection(); void _SetFileName(); void _GetFileName();
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void _SetOkButtonLabel(); void _SetFileNameLabel();
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void _BindToHandler(); void _GetParent();
            void GetDisplayName(uint sigdn, out IntPtr name);
        }
    }
}
