using System;
using System.Diagnostics;

namespace Windkanal
{
    // Misst, wo im 2D-Programm die Zeit bleibt: Rechenschritte, Zurücklesen, Rauch, Partikel, Zeichnen.
    // Aufruf: Bench2D.exe [gpu|cpu|beide]  (Standard: beide)
    static class Bench2D
    {
        static readonly int[] NXs = { 400, 600, 900, 1200, 1600, 2400 };
        static readonly int[] NYs = { 160, 240, 360, 480, 640, 960 };

        static void Main(string[] args)
        {
            string which = args.Length > 0 ? args[0] : "beide";
            if (which == "pruef")
            {
                // Fingerabdruck der Ergebnisse: muss vor und nach Leistungsänderungen Bit für Bit gleich sein
                string mode = args.Length > 1 ? args[1] : "beide";
                if (mode != "cpu") Fingerprint(false);
                if (mode != "gpu") Fingerprint(true);
                if (args.Length > 2 && args[2] == "paket")
                {
                    // Pakete ohne Warten (BeginSteps/EndSteps) müssen dasselbe ergeben, auch über mehrere Rutsche
                    if (mode != "cpu") { Fingerprint(false, true); BigBatch(false); }
                    if (mode != "gpu") { Fingerprint(true, true); BigBatch(true); }
                }
                return;
            }
            if (which != "cpu") Pass(false);
            if (which != "gpu") Pass(true);
        }

        static ulong Mix(ulong h, ulong v) { h ^= v; h *= 1099511628211UL; return h; }
        static ulong Hash(float[] a) { ulong h = 14695981039346656037UL; foreach (var v in a) h = Mix(h, (uint)BitConverter.ToInt32(BitConverter.GetBytes(v), 0)); return h; }

        static void BigBatch(bool cpu)
        {
            Environment.SetEnvironmentVariable("WK_CPU", cpu ? "1" : null);
            var res = new string[2];
            for (int pass = 0; pass < 2; pass++)
                using (var s = new Solver(300, 120))
                {
                    var mask = new bool[s.N];
                    Shapes.Fill(Shapes.Transform(Shapes.Polygon(ShapeKind.Zylinder), 24, 0, 75, 59.5f), mask, s.NX, 120);
                    s.U0 = Solver.ChooseU0(300, 24, 0.2); s.Nu = (float)(s.U0 * 24 / 300);
                    s.ApplyMask(mask); s.Reset();
                    var fx = new double[Solver.MaxPending]; var fy = new double[Solver.MaxPending];
                    ulong hf = 14695981039346656037UL;
                    foreach (int n in new[] { 1300, 7, 2100 })
                    {
                        if (pass == 0) s.StepMany(n, fx, fy);
                        else { s.BeginSteps(n); s.EndSteps(fx, fy); }
                        for (int k = 0; k < n; k++) { hf = Mix(hf, (ulong)BitConverter.DoubleToInt64Bits(fx[k])); hf = Mix(hf, (ulong)BitConverter.DoubleToInt64Bits(fy[k])); }
                    }
                    res[pass] = string.Format("Kräfte {0:X16} Rho {1:X16} Ux {2:X16} Uy {3:X16} Schritte {4}", hf, Hash(s.Rho), Hash(s.Ux), Hash(s.Uy), s.Steps);
                }
            Console.WriteLine((cpu ? "CPU" : "GPU") + " große Pakete: " + (res[0] == res[1] ? "GLEICH" : "ANDERS\n  " + res[0] + "\n  " + res[1]));
        }

        static void Fingerprint(bool cpu, bool pipelined = false)
        {
            Environment.SetEnvironmentVariable("WK_CPU", cpu ? "1" : null);
            var cases = new[] {
                new { Kind = ShapeKind.Zylinder, Angle = 0f, NoSlip = false, Batch = 37, Re = 200.0 },
                new { Kind = ShapeKind.Naca4412, Angle = 12f, NoSlip = true, Batch = 50, Re = 5000.0 },
            };
            foreach (var cs in cases)
            {
                using (var s = new Solver(300, 120))
                {
                    if (!cpu && !s.OnGpu) { Console.WriteLine("keine GPU"); return; }
                    int ny = 120; float d = 24;
                    var mask = new bool[s.N];
                    Shapes.Fill(Shapes.Transform(Shapes.Polygon(cs.Kind), d, cs.Angle, 300 * 0.25f, (ny - 1) / 2f), mask, s.NX, ny);
                    s.U0 = Solver.ChooseU0(cs.Re, d, 0.2);
                    s.Nu = (float)(s.U0 * d / cs.Re);
                    s.NoSlipWalls = cs.NoSlip;
                    s.ApplyMask(mask);
                    s.Reset();
                    var fx = new double[GpuLbm.MaxBatch];
                    var fy = new double[GpuLbm.MaxBatch];
                    ulong hf = 14695981039346656037UL;
                    var p = new Particles(s.NX, s.NY);
                    var rd = new Renderer();
                    rd.EnsureSize(700, 300);
                    ulong hr = 14695981039346656037UL;
                    for (int t = 0; t < 1500; t += cs.Batch)
                    {
                        if (pipelined) { s.BeginSteps(cs.Batch); s.EndSteps(fx, fy); }
                        else s.StepMany(cs.Batch, fx, fy);
                        for (int k = 0; k < cs.Batch; k++) { hf = Mix(hf, (ulong)BitConverter.DoubleToInt64Bits(fx[k])); hf = Mix(hf, (ulong)BitConverter.DoubleToInt64Bits(fy[k])); }
                        s.AdvectSmoke(cs.Batch);
                        p.Update(s, cs.Batch, 2.5f * s.NX / s.U0);
                        if (t % (cs.Batch * 8) == 0)
                        {
                            s.ReadSmoke();
                            foreach (ViewMode vm in Enum.GetValues(typeof(ViewMode)))
                            {
                                rd.Render(s, vm == ViewMode.Rauch ? null : p, vm, vm != ViewMode.Rauch, d);
                                hr = Mix(hr, HashBitmap(rd.Bitmap));
                            }
                        }
                    }
                    // gleiche Strömung nach einer Maskenänderung (wie bei beweglichen Teilen)
                    var mask2 = new bool[s.N];
                    Shapes.Fill(Shapes.Transform(Shapes.Polygon(cs.Kind), d, cs.Angle + 5, 300 * 0.25f, (ny - 1) / 2f), mask2, s.NX, ny);
                    s.ApplyMask(mask2);
                    for (int t = 0; t < 600; t += cs.Batch)
                    {
                        if (pipelined) { s.BeginSteps(cs.Batch); s.EndSteps(fx, fy); }
                        else s.StepMany(cs.Batch, fx, fy);
                        for (int k = 0; k < cs.Batch; k++) { hf = Mix(hf, (ulong)BitConverter.DoubleToInt64Bits(fx[k])); hf = Mix(hf, (ulong)BitConverter.DoubleToInt64Bits(fy[k])); }
                        s.AdvectSmoke(cs.Batch);
                    }
                    s.ReadSmoke();
                    Console.WriteLine(string.Format("{0} {1,-9} Kräfte {2:X16}  Rho {3:X16}  Ux {4:X16}  Uy {5:X16}  Rauch {6:X16}  Bilder {7:X16}  Partikel {8}",
                        cpu ? "CPU" : "GPU", cs.Kind, hf, Hash(s.Rho), Hash(s.Ux), Hash(s.Uy), Hash(s.Smoke), hr, p.Count));
                }
            }
        }

        static ulong HashBitmap(System.Drawing.Bitmap bmp)
        {
            var r = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(r, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            var px = new int[bmp.Width * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, px, 0, px.Length);
            bmp.UnlockBits(data);
            ulong h = 14695981039346656037UL;
            foreach (var v in px) h = Mix(h, (uint)v);
            return h;
        }

        static void Pass(bool cpu)
        {
            Environment.SetEnvironmentVariable("WK_CPU", cpu ? "1" : null);
            using (var probe = new Solver(40, 16))
            {
                if (!cpu && !probe.OnGpu) { Console.WriteLine("Keine GPU: " + probe.GpuUnavailableReason); return; }
                Console.WriteLine("== " + probe.Backend);
            }
            Console.WriteLine("Gitter        Zellen   MLUPS(Rechnen)  Lesen ms  Rauch ms  Partikel ms  Bild ms(Tempo)  Bild ms(Rauch)");
            for (int r = 0; r < NXs.Length; r++)
            {
                if (cpu && r > 4) break;   // Extrem auf der CPU dauert zu lange
                using (var s = new Solver(NXs[r], NYs[r])) Case(s, NXs[r], NYs[r], cpu);
            }
        }

        static void Case(Solver s, int vnx, int ny, bool cpu)
        {
            float d = ny * 0.1f;
            var mask = new bool[s.N];
            Shapes.Fill(Shapes.Transform(Shapes.Polygon(ShapeKind.Zylinder), d, 0, vnx * 0.25f, (ny - 1) / 2f), mask, s.NX, ny);
            s.U0 = Solver.ChooseU0(1000, d, 0.1);
            s.Nu = (float)(s.U0 * d / 1000);
            s.ApplyMask(mask);
            s.Reset();
            var fx = new double[GpuLbm.MaxBatch];
            var fy = new double[GpuLbm.MaxBatch];
            int batch = cpu ? 4 : 200;
            s.StepMany(batch, fx, fy);   // aufwärmen (Kernel/JIT)

            // Rechnen: viele Schritte, einmal zurücklesen, dann reines Lesen (1 Schritt) abziehen
            int reps = cpu ? Math.Max(2, 40000000 / s.N / batch) : Math.Max(3, 400000000 / s.N / batch);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < reps; i++) s.StepMany(batch, fx, fy);
            double msBatch = sw.Elapsed.TotalMilliseconds / reps;
            sw.Restart();
            for (int i = 0; i < 10; i++) s.StepMany(1, fx, fy);
            double msOne = sw.Elapsed.TotalMilliseconds / 10;
            double msStep = (msBatch - msOne) / (batch - 1);
            double msRead = Math.Max(0, msOne - msStep);
            double mlups = s.N / (msStep * 1000.0);

            // Rauch und Partikel für eine feste Schrittzahl je Bild (vergleichbar zwischen alt und neu)
            int stepsFrame = cpu ? 10 : 100;
            sw.Restart();
            for (int i = 0; i < 5; i++) { s.AdvectSmoke(stepsFrame); s.ReadSmoke(); }
            double msSmoke = sw.Elapsed.TotalMilliseconds / 5;

            var p = new Particles(s.NX, s.NY);
            for (int i = 0; i < 30; i++) p.Update(s, stepsFrame, 2.5f * s.NX / s.U0);
            sw.Restart();
            for (int i = 0; i < 5; i++) p.Update(s, stepsFrame, 2.5f * s.NX / s.U0);
            double msPart = sw.Elapsed.TotalMilliseconds / 5;

            var rd = new Renderer();
            rd.EnsureSize(1500, 600);
            rd.Render(s, p, ViewMode.Geschwindigkeit, true, d);
            sw.Restart();
            for (int i = 0; i < 10; i++) rd.Render(s, p, ViewMode.Geschwindigkeit, true, d);
            double msDraw = sw.Elapsed.TotalMilliseconds / 10;
            sw.Restart();
            for (int i = 0; i < 10; i++) rd.Render(s, null, ViewMode.Rauch, false, d);
            double msDrawSmoke = sw.Elapsed.TotalMilliseconds / 10;

            Console.WriteLine(string.Format("{0,4} x {1,-4} {2,9:N0} {3,10:0} {4,14:0.00} {5,9:0.00} {6,11:0.00} {7,13:0.00} {8,15:0.00}   ({9} Schritte/Bild, {10} Partikel)",
                vnx, ny, s.N, mlups, msRead, msSmoke, msPart, msDraw, msDrawSmoke, stepsFrame, p.Count));
        }
    }
}
