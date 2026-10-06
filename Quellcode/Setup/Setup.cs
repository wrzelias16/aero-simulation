using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Windkanal 2D – Setup")]
[assembly: AssemblyProduct("Windkanal 2D")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace WindkanalSetup
{
    /// <summary>
    /// Installiert/deinstalliert Windkanal 2D für den aktuellen Benutzer (keine Admin-Rechte nötig).
    /// Start mit /uninstall oder als "...Deinstallieren.exe" öffnet direkt die Deinstallation.
    /// </summary>
    static class Setup
    {
        const string AppName = "Windkanal 2D";
        const string AppExe = "Windkanal2D.exe";
        const string UninstallExe = "Deinstallieren.exe";
        const string Version = "1.0.0";
        const string RegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Windkanal2D";

        static readonly Color CBg = Color.FromArgb(29, 33, 40);
        static readonly Color CCtl = Color.FromArgb(42, 47, 56);
        static readonly Color CBorder = Color.FromArgb(62, 68, 80);
        static readonly Color CText = Color.FromArgb(226, 229, 234);
        static readonly Color CMuted = Color.FromArgb(145, 152, 164);
        static readonly Color CAccent = Color.FromArgb(77, 163, 255);

        static string InstallDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Programs", "Windkanal2D");
            }
        }

        static string StartMenuLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppName + ".lnk"); }
        }

        static string DesktopLink
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), AppName + ".lnk"); }
        }

        static bool IsInstalled
        {
            get { return File.Exists(Path.Combine(InstallDir, AppExe)); }
        }

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

            if (uninstallMode)
            {
                if (!IsInstalled && Registry.CurrentUser.OpenSubKey(RegKey) == null)
                {
                    MessageBox.Show(AppName + " ist nicht installiert.", AppName + " deinstallieren",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                if (MessageBox.Show(AppName + " wirklich vom Computer entfernen?\n\nProgramm, Verknüpfungen und Eintrag in den Windows-Apps werden gelöscht.",
                                    AppName + " deinstallieren", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return 0;
                try
                {
                    Uninstall();
                    MessageBox.Show(AppName + " wurde vollständig entfernt.", AppName + " deinstallieren",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Deinstallation fehlgeschlagen:\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                return 0;
            }

            Application.Run(new SetupForm());
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

            if (startMenu) CreateShortcut(StartMenuLink, target, dir);
            else TryDelete(StartMenuLink);
            if (desktop) CreateShortcut(DesktopLink, target, dir);
            else TryDelete(DesktopLink);

            long sizeKb = new FileInfo(target).Length / 1024 + new FileInfo(uninst).Length / 1024;
            using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
            {
                k.SetValue("DisplayName", AppName);
                k.SetValue("DisplayVersion", Version);
                k.SetValue("Publisher", AppName);
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
                var psi = new ProcessStartInfo("cmd.exe",
                    "/c timeout /t 2 /nobreak >nul & rmdir /s /q \"" + dir + "\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
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
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { "2D-Windkanal-Simulation" });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
        }

        // ------------------------------------------------------------ Oberfläche

        sealed class SetupForm : Form
        {
            readonly CheckBox chkDesktop, chkStart, chkLaunch;
            readonly Button btnInstall, btnUninstall;
            readonly Label lblState;

            public SetupForm()
            {
                Text = AppName + " – Setup";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(520, 330);
                BackColor = CBg;
                ForeColor = CText;
                Font = new Font("Segoe UI", 9.5f);
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

                var title = new Label { Text = AppName, Font = new Font("Segoe UI Semibold", 18f), AutoSize = true, Location = new Point(28, 22) };
                var sub = new Label
                {
                    Text = "Strömungssimulation im virtuellen Windkanal · Version " + Version,
                    ForeColor = CMuted, AutoSize = true, Location = new Point(30, 62)
                };
                var path = new Label
                {
                    Text = "Installationsordner (nur für dich, keine Admin-Rechte nötig):\n" + InstallDir,
                    ForeColor = CMuted, Location = new Point(30, 100), Size = new Size(470, 40)
                };
                chkStart = MakeCheck("Verknüpfung im Startmenü", 152);
                chkDesktop = MakeCheck("Verknüpfung auf dem Desktop", 180);
                chkLaunch = MakeCheck("Nach der Installation starten", 208);

                lblState = new Label { ForeColor = CAccent, Location = new Point(30, 244), Size = new Size(470, 20) };

                btnInstall = MakeButton("Installieren", new Point(30, 276), 160);
                btnInstall.BackColor = Color.FromArgb(38, 98, 170);
                btnInstall.FlatAppearance.BorderColor = CAccent;
                btnUninstall = MakeButton("Deinstallieren", new Point(200, 276), 150);
                var btnClose = MakeButton("Schließen", new Point(360, 276), 130);

                btnInstall.Click += delegate { DoInstall(); };
                btnUninstall.Click += delegate { DoUninstall(); };
                btnClose.Click += delegate { Close(); };

                Controls.AddRange(new Control[] { title, sub, path, chkStart, chkDesktop, chkLaunch, lblState, btnInstall, btnUninstall, btnClose });
                RefreshState();
            }

            CheckBox MakeCheck(string text, int y)
            {
                return new CheckBox { Text = text, Checked = true, Location = new Point(30, y), AutoSize = true, ForeColor = CText };
            }

            Button MakeButton(string text, Point loc, int w)
            {
                var b = new Button
                {
                    Text = text, Location = loc, Size = new Size(w, 34), FlatStyle = FlatStyle.Flat,
                    BackColor = CCtl, ForeColor = CText, Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderColor = CBorder;
                return b;
            }

            void RefreshState()
            {
                bool inst = IsInstalled;
                btnUninstall.Enabled = inst;
                btnInstall.Text = inst ? "Neu installieren" : "Installieren";
                lblState.Text = inst ? "✓ " + AppName + " ist bereits installiert." : "";
            }

            void DoInstall()
            {
                try
                {
                    UseWaitCursor = true;
                    Install(chkDesktop.Checked, chkStart.Checked);
                    UseWaitCursor = false;
                    if (chkLaunch.Checked)
                    {
                        Process.Start(new ProcessStartInfo(Path.Combine(InstallDir, AppExe)) { WorkingDirectory = InstallDir });
                        Close();
                        return;
                    }
                    RefreshState();
                    MessageBox.Show(this, AppName + " wurde erfolgreich installiert.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    UseWaitCursor = false;
                    MessageBox.Show(this, "Installation fehlgeschlagen:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            void DoUninstall()
            {
                if (MessageBox.Show(this, AppName + " wirklich vom Computer entfernen?", Text,
                                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try
                {
                    Uninstall();
                    RefreshState();
                    MessageBox.Show(this, AppName + " wurde vollständig entfernt.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Deinstallation fehlgeschlagen:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
