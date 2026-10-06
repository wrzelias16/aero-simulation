using System;
using System.Diagnostics;

namespace Windkanal
{
    // Validierung gegen Literaturwerte (Zylinderumströmung).
    static class ValidationTest
    {
        static void Main(string[] args)
        {
            using (var probe = new Solver(40, 16))
                Console.WriteLine("Rechnet auf: " + probe.Backend
                    + (probe.GpuUnavailableReason != null ? " (GPU nicht genutzt: " + probe.GpuUnavailableReason + ")" : ""));
            if (args.Length == 5)
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                Run("Zylinder nx=" + args[0] + " ny=" + args[1] + " D=" + args[2] + " Re=" + args[3],
                    int.Parse(args[0]), int.Parse(args[1]), float.Parse(args[2], ci), double.Parse(args[3], ci), int.Parse(args[4]));
                return;
            }
            ModelCheck();
            SmokeCheck();
            Run("Stabilität Zylinder 40 % Versperrung Re=20000", 400, 160, 64, 20000, 25000);
            Run("Stabilität Quadrat 60 % Versperrung Re=20000", 400, 160, 96, 20000, 25000, ShapeKind.Quadrat);
            Run("Stabilität Zylinder Re=20000", 600, 240, 24, 20000, 20000);
            Run("Stabilität NACA4412 a=15 Re=20000", 600, 240, 60, 20000, 20000, ShapeKind.Naca4412, 15);
            Run("Zylinder Re=100, Versperrung 8 %", 900, 360, 30, 100, 40000);
            Run("Zylinder Re=100, Versperrung 4 %", 1200, 480, 20, 100, 30000);
            Run("Zylinder Re=20, Versperrung 8 %", 900, 360, 30, 20, 30000);
        }

        /// <summary>Alle Modelldateien müssen fehlerfrei laden und auf dem mittleren Gitter mindestens eine Zelle belegen.</summary>
        static void ModelCheck()
        {
            int empty = 0;
            foreach (var m in ModelLibrary.All)
            {
                if (m.IsCustom) continue;
                var mask = ModelLibrary.Rasterize(m, 600, 240, m.OnGround ? 180 : 150, m.SizePercent / 100f * 240, m.Angle);
                if (Array.IndexOf(mask, true) < 0) { empty++; Console.WriteLine("  leer: " + m.Id); }
            }
            foreach (var e in ModelLibrary.Errors) Console.WriteLine("  " + e);
            Console.WriteLine("Modelle: " + ModelLibrary.All.Count + " geladen, " + ModelLibrary.Errors.Count + " Fehler"
                + (ModelLibrary.Errors.Count == 0 && empty == 0 ? " -> OK" : " -> FEHLER"));
        }

        /// <summary>
        /// Rauch-Mitführung: der Rauch muss den Auslass erreichen, im Körper null bleiben und
        /// auf GPU und CPU (gleiche Strömung, gleiche Formeln) fast dasselbe Bild ergeben.
        /// </summary>
        static void SmokeCheck()
        {
            const int nx = 300, ny = 120, steps = 6000;
            float[] gpuSmoke = null, cpuSmoke = null;
            bool[] solid = null;
            int fullNx = 0;
            string prev = Environment.GetEnvironmentVariable("WK_CPU");
            for (int pass = 0; pass < 2; pass++)
            {
                if (pass == 1) Environment.SetEnvironmentVariable("WK_CPU", "1");
                using (var s = new Solver(nx, ny))
                {
                    if (pass == 0 && !s.OnGpu) continue;
                    var mask = new bool[s.N];
                    Shapes.Fill(Shapes.Transform(Shapes.Polygon(ShapeKind.Zylinder), 20, 0, nx * 0.25f, (ny - 1) / 2f), mask, s.NX, ny);
                    s.U0 = Solver.ChooseU0(100, 20, 20.0 / ny);
                    s.Nu = (float)(s.U0 * 20 / 100);
                    s.ApplyMask(mask);
                    s.Reset();
                    for (int t = 0; t < steps; t += 50) { s.StepMany(50, null, null); s.AdvectSmoke(50); }
                    s.ReadSmoke();
                    if (pass == 0) gpuSmoke = (float[])s.Smoke.Clone(); else cpuSmoke = (float[])s.Smoke.Clone();
                    solid = s.Solid; fullNx = s.NX;
                }
            }
            Environment.SetEnvironmentVariable("WK_CPU", prev);

            double outlet = 0, inSolid = 0;
            for (int y = 0; y < ny; y++) outlet += cpuSmoke[y * fullNx + nx - 5];
            for (int c = 0; c < cpuSmoke.Length; c++) if (solid[c]) inSolid += cpuSmoke[c];
            string cmp = "keine GPU";
            if (gpuSmoke != null)
            {
                double diff = 0;
                for (int c = 0; c < cpuSmoke.Length; c++) diff += Math.Abs(gpuSmoke[c] - cpuSmoke[c]);
                cmp = "mittlere Abweichung GPU/CPU " + (diff / cpuSmoke.Length).ToString("0.0000");
            }
            Console.WriteLine("Rauch: Dichte am Auslass " + (outlet / ny).ToString("0.000") + ", im Körper " + inSolid.ToString("0") + ", " + cmp
                + (outlet / ny > 0.05 && inSolid == 0 ? " -> OK" : " -> FEHLER"));
        }

        static void Run(string name, int nx, int ny, float d, double re, int steps,
                        ShapeKind kind = ShapeKind.Zylinder, float angle = 0)
        {
            using (var s = new Solver(nx, ny)) RunCase(s, name, nx, ny, d, re, steps, kind, angle);
        }

        static void RunCase(Solver s, string name, int nx, int ny, float d, double re, int steps, ShapeKind kind, float angle)
        {
            float pivotX = nx * 0.25f;
            nx = s.NX;
            var mask = new bool[nx * ny];
            Shapes.Fill(Shapes.Transform(Shapes.Polygon(kind), d, angle, pivotX, (ny - 1) / 2f), mask, nx, ny);
            int rows = 0;
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    if (mask[y * nx + x]) { rows++; break; }
            s.U0 = Solver.ChooseU0(re, d, rows / (double)ny);
            s.Nu = (float)(s.U0 * d / re);
            s.ApplyMask(mask);
            s.Reset();

            double q = 0.5 * s.U0 * s.U0 * d;
            var cd = new double[steps];
            var cl = new double[steps];
            var fxs = new double[100];
            var fys = new double[100];
            var sw = Stopwatch.StartNew();
            for (int t0 = 0; t0 < steps; t0 += 100)
            {
                // in Paketen zu 100 Schritten rechnen (auf der GPU ohne Warten zwischen den Schritten)
                int m = Math.Min(100, steps - t0);
                s.StepMany(m, fxs, fys);
                int t = t0 + m - 1;
                for (int k = 0; k < m; k++)
                {
                    cd[t0 + k] = fxs[k] / q; cl[t0 + k] = fys[k] / q;
                    if (double.IsNaN(fxs[k]) || double.IsInfinity(fxs[k])) { t = t0 + k; break; }
                }
                {
                    float umax = 0; int at = 0;
                    for (int c = 0; c < s.N; c++)
                    {
                        float u = (float)Math.Sqrt(s.Ux[c] * s.Ux[c] + s.Uy[c] * s.Uy[c]);
                        if (!(u <= umax)) { umax = u; at = c; if (float.IsNaN(u)) break; }
                    }
                    if (Environment.GetEnvironmentVariable("WK_DIAG") == "1")
                        Console.WriteLine("  t=" + t + " umax=" + umax.ToString("0.000") + " bei x=" + (at % nx) + " y=" + (at / nx));
                    if (!s.IsStable())
                    {
                        Console.WriteLine(name + ": INSTABIL nach " + t + " Schritten, umax=" + umax + " bei x=" + (at % nx) + " y=" + (at / nx) + " (sichtbar bis x=" + s.VisibleNX + ")");
                        return;
                    }
                }
            }
            double secs = sw.Elapsed.TotalSeconds;
            int from = steps / 2;
            double mcd = 0, mcl = 0;
            for (int t = from; t < steps; t++) { mcd += cd[t]; mcl += cl[t]; }
            mcd /= steps - from; mcl /= steps - from;
            // Strouhal mit derselben Auswertung wie in der App
            var fs = new ForceStats(steps);
            for (int t = 0; t < steps; t++) fs.Add((float)cd[t], (float)cl[t]);
            double a, b, freq;
            fs.Analyze(steps - from, (int)(0.5 * d / s.U0), out a, out b, out freq);
            string st = freq > 0 ? (freq * d / s.U0).ToString("0.000") : "-";
            Console.WriteLine(string.Format("{0}: Cd={1:0.000} Cl={2:0.000} St={3} tau={4:0.0000} U0={5:0.000} rhoInf={7:0.0000} | {6:0} MLUPS",
                name, mcd, mcl, st, s.Tau, s.U0, (double)nx * ny * steps / secs / 1e6, s.RhoInf));
        }
    }
}
