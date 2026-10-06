using System;
using System.Collections.Generic;
using System.Drawing;

namespace Windkanal
{
    public enum ShapeKind { Zylinder, Quadrat, Platte, Naca0012, Naca2412, Naca4412, Auto, Eigene }

    public static class Shapes
    {
        public static readonly ShapeKind[] All =
        {
            ShapeKind.Zylinder, ShapeKind.Quadrat, ShapeKind.Platte, ShapeKind.Naca0012,
            ShapeKind.Naca2412, ShapeKind.Naca4412, ShapeKind.Auto, ShapeKind.Eigene
        };

        public static string Name(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Zylinder: return "Zylinder";
                case ShapeKind.Quadrat: return "Quadrat";
                case ShapeKind.Platte: return "Flache Platte";
                case ShapeKind.Naca0012: return "Tragfläche NACA 0012 (symmetrisch)";
                case ShapeKind.Naca2412: return "Tragfläche NACA 2412 (leicht gewölbt)";
                case ShapeKind.Naca4412: return "Tragfläche NACA 4412 (stark gewölbt)";
                case ShapeKind.Auto: return "Auto (Seitenprofil)";
                default: return "Eigene Zeichnung";
            }
        }

        /// <summary>Standardgröße der Form in Prozent der Tunnelhöhe.</summary>
        public static int DefaultSizePercent(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Zylinder: return 10;
                case ShapeKind.Quadrat: return 10;
                case ShapeKind.Platte: return 20;
                case ShapeKind.Auto: return 45;
                default: return 25;
            }
        }

        public static int DefaultReynolds(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Zylinder: return 100;
                case ShapeKind.Quadrat: return 100;
                case ShapeKind.Auto: return 5000;
                default: return 1000;
            }
        }

        /// <summary>Bezeichnung der Bezugslänge für Re, cw und ca.</summary>
        public static string RefName(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Zylinder: return "Durchmesser";
                case ShapeKind.Quadrat: return "Kantenlänge";
                case ShapeKind.Platte:
                case ShapeKind.Naca0012:
                case ShapeKind.Naca2412:
                case ShapeKind.Naca4412: return "Sehnenlänge";
                default: return "Stirnhöhe";
            }
        }

        /// <summary>True, wenn die Bezugslänge die Formgröße selbst ist (sonst: gemessene Stirnhöhe).</summary>
        public static bool RefIsSize(ShapeKind k)
        {
            return k != ShapeKind.Auto && k != ShapeKind.Eigene;
        }

        /// <summary>Umriss in Einheiten der Formgröße, Drehpunkt im Ursprung, y nach oben.</summary>
        public static List<PointF> Polygon(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Zylinder: return Circle(160);
                case ShapeKind.Quadrat: return Rect(1f, 1f);
                case ShapeKind.Platte: return Rect(1f, 0.05f);
                case ShapeKind.Naca0012: return Naca(0f, 0f, 0.12f);
                case ShapeKind.Naca2412: return Naca(0.02f, 0.4f, 0.12f);
                case ShapeKind.Naca4412: return Naca(0.04f, 0.4f, 0.12f);
                case ShapeKind.Auto: return Car();
                default: return new List<PointF>();
            }
        }

        static List<PointF> Circle(int n)
        {
            var p = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                p.Add(new PointF((float)(0.5 * Math.Cos(a)), (float)(0.5 * Math.Sin(a))));
            }
            return p;
        }

        static List<PointF> Rect(float w, float h)
        {
            return new List<PointF>
            {
                new PointF(-w / 2, -h / 2), new PointF(w / 2, -h / 2),
                new PointF(w / 2, h / 2), new PointF(-w / 2, h / 2)
            };
        }

        // NACA-4-Ziffern-Profil, Sehne 1, Drehpunkt bei 25 % der Sehne
        static List<PointF> Naca(float m, float p, float t)
        {
            const int n = 120;
            var upper = new List<PointF>();
            var lower = new List<PointF>();
            for (int i = 0; i <= n; i++)
            {
                double beta = Math.PI * i / n;
                double x = 0.5 * (1 - Math.Cos(beta));
                double yt = 5 * t * (0.2969 * Math.Sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x);
                double yc = 0, dyc = 0;
                if (m > 0)
                {
                    if (x < p) { yc = m / (p * p) * (2 * p * x - x * x); dyc = 2 * m / (p * p) * (p - x); }
                    else { yc = m / ((1 - p) * (1 - p)) * ((1 - 2 * p) + 2 * p * x - x * x); dyc = 2 * m / ((1 - p) * (1 - p)) * (p - x); }
                }
                double th = Math.Atan(dyc);
                upper.Add(new PointF((float)(x - yt * Math.Sin(th) - 0.25), (float)(yc + yt * Math.Cos(th))));
                lower.Add(new PointF((float)(x + yt * Math.Sin(th) - 0.25), (float)(yc - yt * Math.Cos(th))));
            }
            var poly = new List<PointF>();
            for (int i = n; i >= 0; i--) poly.Add(upper[i]);
            for (int i = 1; i < n; i++) poly.Add(lower[i]);
            return poly;
        }

        // Vereinfachtes Limousinen-Seitenprofil, Länge 1, Front links
        static List<PointF> Car()
        {
            float[,] pts =
            {
                { 0.00f, 0.08f }, { 0.00f, 0.14f }, { 0.02f, 0.17f }, { 0.08f, 0.19f },
                { 0.30f, 0.21f }, { 0.45f, 0.31f }, { 0.70f, 0.31f }, { 0.88f, 0.24f },
                { 0.98f, 0.22f }, { 1.00f, 0.18f }, { 1.00f, 0.08f }, { 0.95f, 0.04f },
                { 0.05f, 0.04f }
            };
            var p = new List<PointF>();
            for (int i = 0; i < pts.GetLength(0); i++) p.Add(new PointF(pts[i, 0] - 0.5f, pts[i, 1] - 0.175f));
            return p;
        }

        /// <summary>Skaliert, dreht (positiver Winkel = Nase hoch) und verschiebt den Umriss in Gitterkoordinaten.</summary>
        public static PointF[] Transform(List<PointF> poly, float scale, float angleDeg, float cx, float cy)
        {
            double a = angleDeg * Math.PI / 180.0;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            var r = new PointF[poly.Count];
            for (int i = 0; i < poly.Count; i++)
            {
                float x = poly[i].X, y = poly[i].Y;
                float xr = x * ca + y * sa;
                float yr = -x * sa + y * ca;
                r[i] = new PointF(cx + xr * scale, cy + yr * scale);
            }
            return r;
        }

        /// <summary>Füllt das Polygon (Zellmittelpunkte innerhalb) in die Maske.</summary>
        public static void Fill(PointF[] pts, bool[] mask, int nx, int ny)
        {
            if (pts.Length < 3) return;
            var xs = new List<float>();
            for (int y = 0; y < ny; y++)
            {
                xs.Clear();
                float yc = y;
                for (int i = 0; i < pts.Length; i++)
                {
                    PointF a = pts[i], b = pts[(i + 1) % pts.Length];
                    if ((a.Y <= yc && b.Y > yc) || (b.Y <= yc && a.Y > yc))
                        xs.Add(a.X + (yc - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
                if (xs.Count < 2) continue;
                xs.Sort();
                for (int j = 0; j + 1 < xs.Count; j += 2)
                {
                    int x0 = Math.Max(0, (int)Math.Ceiling(xs[j]));
                    int x1 = Math.Min(nx - 1, (int)Math.Floor(xs[j + 1]));
                    for (int x = x0; x <= x1; x++) mask[y * nx + x] = true;
                }
            }
        }
    }
}
