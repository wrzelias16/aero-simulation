using System;
using System.Diagnostics;
using Windkanal3D;

/// <summary>
/// Prüfstand für den 3D-Rechenkern (Konsole).
/// 1. Leerer Kanal: die Strömung muss gleichmäßig bleiben, keine Kraft, nichts explodiert.
/// 2. Kugel bei Re = 20 und Re = 40: Widerstandsbeiwert gegen die bekannte Formel von Schiller-Naumann
///    (Messwerte für die freie Kugel; der Kanal hat etwa 3 % Versperrung, darum Toleranz 15 %).
///    Querkräfte müssen fast null sein (die Kugel ist symmetrisch).
///    Kontrollrechnung (RTX 5070 Ti): 256x128x128 -> +12,1 % / +10,6 %; 448x224x224 -> +7,4 % / +6,5 %.
///    Die Abweichung schrumpft mit größerem Kanal, kommt also von den Wänden und nicht vom Löser.
/// Rückgabe: 0 = bestanden, 1 = durchgefallen, 2 = keine Grafikkarte.
/// </summary>
static class Test3D
{
    static int NX = 256, NY = 128, NZ = 128;
    static double SphereX = 80, ConvTol = 1e-3;
    const float UIn = 0.05f;

    static int failed;

    static int Main(string[] args)
    {
        // Für Kontrollrechnungen: Test3D.exe NX NY NZ KugelX Toleranz
        if (args.Length >= 5)
        {
            NX = int.Parse(args[0]); NY = int.Parse(args[1]); NZ = int.Parse(args[2]);
            SphereX = double.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture);
            ConvTol = double.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture);
        }
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("Windkanal 3D – Prüfstand Rechenkern (D3Q19)");
        if (Lbm3D.DeviceName == null)
        {
            Console.WriteLine("Keine nutzbare Grafikkarte: " + Lbm3D.InitError);
            return 2;
        }
        Console.WriteLine("Grafikkarte: " + Lbm3D.DeviceName + ", " + (Lbm3D.DeviceMemory >> 20) + " MB, Platz für "
                          + (Lbm3D.MaxCells / 1000000) + " Mio. Zellen");
        Console.WriteLine("Gitter: " + NX + " x " + NY + " x " + NZ + " = " + (NX * NY * NZ / 1000000.0).ToString("0.0") + " Mio. Zellen");
        Console.WriteLine();

        using (var lbm = new Lbm3D(NX, NY, NZ))
        {
            EmptyTunnel(lbm);
            Sphere(lbm, 20);
            Sphere(lbm, 40);
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "ALLE TESTS BESTANDEN" : failed + " TEST(S) DURCHGEFALLEN");
        return failed == 0 ? 0 : 1;
    }

    static void Report(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "  [OK]     " : "  [FEHLER] ") + name + ": " + detail);
        if (!ok) failed++;
    }

    static void EmptyTunnel(Lbm3D lbm)
    {
        Console.WriteLine("Test 1: leerer Kanal");
        lbm.SetSolid(new byte[lbm.N]);
        lbm.Reset(UIn);
        float omega = 1.0f / (3.0f * 0.02f + 0.5f);
        double fx, fy, fz;
        var sw = Stopwatch.StartNew();
        const int steps = 2000;
        lbm.Step(steps, omega, UIn, out fx, out fy, out fz);
        sw.Stop();
        double mlups = (double)lbm.N * steps / sw.Elapsed.TotalSeconds / 1e6;

        float[] ux = lbm.ReadField(0), uy = lbm.ReadField(1), rho = lbm.ReadField(3);
        double maxDev = 0, maxCross = 0, maxRho = 0;
        bool finite = true;
        for (int c = 0; c < lbm.N; c++)
        {
            if (float.IsNaN(ux[c]) || float.IsInfinity(ux[c]) || float.IsNaN(rho[c])) { finite = false; break; }
            maxDev = Math.Max(maxDev, Math.Abs(ux[c] - UIn));
            maxCross = Math.Max(maxCross, Math.Abs(uy[c]));
            maxRho = Math.Max(maxRho, Math.Abs(rho[c] - 1.0));
        }
        Report("Zahlen gültig", finite, finite ? "keine NaN/Unendlich" : "NaN gefunden");
        Report("Strömung gleichmäßig", maxDev < 1e-3 * UIn, "größte Abweichung " + (maxDev / UIn * 100).ToString("0.0000") + " %");
        Report("keine Querströmung", maxCross < 1e-3 * UIn, "größte Quergeschwindigkeit " + (maxCross / UIn * 100).ToString("0.0000") + " %");
        Report("Dichte konstant", maxRho < 1e-3, "größte Abweichung " + maxRho.ToString("0.000000"));
        Report("keine Kraft ohne Körper", fx == 0 && fy == 0 && fz == 0, "F = (" + fx + ", " + fy + ", " + fz + ")");
        Console.WriteLine("  Tempo: " + mlups.ToString("0") + " Mio. Zellen-Schritte pro Sekunde ("
                          + (steps / sw.Elapsed.TotalSeconds).ToString("0") + " Schritte/s)");
        Console.WriteLine();
    }

    static void Sphere(Lbm3D lbm, double re)
    {
        const double D = 24, R = D / 2;
        double cx = SphereX, cy = NY / 2.0 - 0.5, cz = NZ / 2.0 - 0.5;
        Console.WriteLine("Test Kugel, Re = " + re + " (Durchmesser " + D + " Zellen)");

        var solid = new byte[lbm.N];
        int vox = 0;
        for (int z = 0; z < NZ; z++)
            for (int y = 0; y < NY; y++)
                for (int x = 0; x < NX; x++)
                {
                    double dx = x - cx, dy = y - cy, dz = z - cz;
                    if (dx * dx + dy * dy + dz * dz < R * R) { solid[x + NX * (y + NY * z)] = 1; vox++; }
                }
        lbm.SetSolid(solid);
        lbm.Reset(UIn);

        double nu = UIn * D / re;
        float omega = (float)(1.0 / (3.0 * nu + 0.5));
        double area = Math.PI * R * R;
        double q = 0.5 * UIn * UIn * area;

        double fx = 0, fy = 0, fz = 0, last = double.NaN;
        int steps = 0, calm = 0;
        var sw = Stopwatch.StartNew();
        while (steps < 40000)
        {
            lbm.Step(500, omega, UIn, out fx, out fy, out fz);
            steps += 500;
            if (double.IsNaN(fx) || double.IsInfinity(fx)) break;
            double change = Math.Abs(fx - last) / Math.Abs(fx);
            calm = change < ConvTol ? calm + 1 : 0;
            if (calm >= 3) break;
            last = fx;
        }
        sw.Stop();

        double cd = fx / q;
        double cdRef = 24.0 / re * (1 + 0.15 * Math.Pow(re, 0.687));
        double err = (cd - cdRef) / cdRef;
        double cross = Math.Sqrt(fy * fy + fz * fz) / Math.Abs(fx);
        double rEff = Math.Pow(3.0 * vox / (4 * Math.PI), 1.0 / 3.0);

        Console.WriteLine("  " + steps + " Schritte in " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s, tau = "
                          + (1 / omega).ToString("0.000") + ", Kugel aus " + vox + " Zellen (wirksamer Radius " + rEff.ToString("0.00") + ")");
        Report("eingeschwungen", calm >= 3, calm >= 3 ? "Kraft ändert sich < 0,1 % pro 500 Schritte" : "nicht ruhig geworden");
        Report("Widerstandsbeiwert", !double.IsNaN(cd) && Math.Abs(err) < 0.15,
               "Cw = " + cd.ToString("0.000") + ", Referenz " + cdRef.ToString("0.000") + ", Abweichung " + (err * 100).ToString("+0.0;-0.0") + " %");
        Report("Querkraft fast null", cross < 0.01, "Querkraft / Widerstand = " + (cross * 100).ToString("0.000") + " %");
        Console.WriteLine();
    }
}
