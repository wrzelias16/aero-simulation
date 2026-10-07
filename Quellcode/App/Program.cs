using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Windkanal")]
[assembly: AssemblyProduct("Windkanal")]
[assembly: AssemblyDescription("2D- und 3D-Windkanal-Simulation (Lattice-Boltzmann)")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Windkanal
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var splash = Splash.Start();
            Action<string, float> report = null;
            if (splash != null) report = splash.Report;
            var form = new MainForm(report);
            form.SwitchTo3D += delegate { Show3D(form); };
            form.OpenIn3D += path => { Show3D(form); if (form3D != null && form3D.Visible) form3D.LoadSessionFile(path); };
            if (splash != null)
            {
                splash.WaitReady(1800);
                form.Shown += delegate { form.Activate(); splash.FadeOut(); };
            }
            Application.Run(form);
        }

        static Windkanal3D.Form3D form3D;

        /// <summary>2D-Fenster verstecken (es pausiert dann) und das 3D-Fenster an derselben Stelle zeigen.</summary>
        static void Show3D(MainForm form2D)
        {
            if (form3D == null || form3D.IsDisposed)
            {
                form2D.Cursor = Cursors.WaitCursor;
                try { form3D = new Windkanal3D.Form3D(); }
                catch (Exception ex)
                {
                    form2D.Cursor = Cursors.Default;
                    MessageBox.Show(form2D, "Die 3D-Ansicht konnte nicht geöffnet werden: " + ex.Message, "Windkanal 3D",
                                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                form2D.Cursor = Cursors.Default;
                form3D.SwitchTo2D += delegate { Swap(form3D, form2D); form2D.RefreshTheme(); };
                form3D.OpenIn2D += path => { Swap(form3D, form2D); form2D.RefreshTheme(); form2D.LoadSessionFile(path); };
                form3D.FormClosed += delegate { form2D.Close(); };   // 3D schließen beendet das Programm
            }
            else form3D.RefreshTheme();
            Swap(form2D, form3D);
        }

        static void Swap(Form from, Form to)
        {
            // weich überblenden: das alte Fensterbild blendet aus, darunter steht schon das neue Fenster
            Transition.CrossFade(from, () =>
            {
                to.StartPosition = FormStartPosition.Manual;
                if (from.WindowState == FormWindowState.Normal) to.Bounds = from.Bounds;
                to.WindowState = from.WindowState == FormWindowState.Minimized ? FormWindowState.Normal : from.WindowState;
                to.Show();
                to.Activate();
                from.Hide();
            }, to);
        }
    }
}
