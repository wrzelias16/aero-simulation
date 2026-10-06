using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("Windkanal 2D")]
[assembly: AssemblyProduct("Windkanal 2D")]
[assembly: AssemblyDescription("2D-Windkanal-Simulation (Lattice-Boltzmann)")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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
            if (splash != null)
            {
                splash.WaitReady(1800);
                form.Shown += delegate { form.Activate(); splash.FadeOut(); };
            }
            Application.Run(form);
        }
    }
}
