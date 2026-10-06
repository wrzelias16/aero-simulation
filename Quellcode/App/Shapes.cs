using System;
using System.Collections.Generic;
using System.Drawing;

namespace Windkanal
{
    /// <summary>Grundformen, die der Validierungstest direkt erzeugt (die App lädt alle Modelle aus Datendateien, siehe <see cref="ModelLibrary"/>).</summary>
    public enum ShapeKind { Zylinder, Quadrat, Platte, Naca0012, Naca2412, Naca4412 }

    /// <summary>Geometrie-Bausteine: Kreis, Rechteck, NACA-Profile, Glättung, Transformation und Rasterung aufs Gitter.</summary>
    public static class Shapes
    {
        /// <summary>Umriss einer Grundform in Einheiten der Formgröße, Drehpunkt im Ursprung, y nach oben.</summary>
        public static List<PointF> Polygon(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Zylinder: return Circle(0, 0, 0.5f, 160);
                case ShapeKind.Quadrat: return Rect(-0.5f, -0.5f, 0.5f, 0.5f);
                case ShapeKind.Platte: return Rect(-0.5f, -0.025f, 0.5f, 0.025f);
                case ShapeKind.Naca0012: return Shift(Naca(0f, 0f, 0.12f, 120), -0.25f, 0);
                case ShapeKind.Naca2412: return Shift(Naca(0.02f, 0.4f, 0.12f, 120), -0.25f, 0);
                default: return Shift(Naca(0.04f, 0.4f, 0.12f, 120), -0.25f, 0);
            }
        }

        public static List<PointF> Circle(float cx, float cy, float rad, int n)
        {
            return Ellipse(cx, cy, rad, rad, n);
        }

        public static List<PointF> Ellipse(float cx, float cy, float rx, float ry, int n)
        {
            var p = new List<PointF>(n);
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                p.Add(new PointF(cx + (float)(rx * Math.Cos(a)), cy + (float)(ry * Math.Sin(a))));
            }
            return p;
        }

        public static List<PointF> Rect(float x0, float y0, float x1, float y1)
        {
            return new List<PointF> { new PointF(x0, y0), new PointF(x1, y0), new PointF(x1, y1), new PointF(x0, y1) };
        }

        static List<PointF> Shift(List<PointF> p, float dx, float dy)
        {
            for (int i = 0; i < p.Count; i++) p[i] = new PointF(p[i].X + dx, p[i].Y + dy);
            return p;
        }

        /// <summary>
        /// NACA-4-Ziffern-Profil (m = Wölbung, p = Wölbungsrücklage, t = Dicke, alles Anteile der Sehne).
        /// Sehne 1, Vorderkante im Ursprung, Punkte von der Hinterkante über die Oberseite zur Unterseite.
        /// Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ (NACA Report 824).
        /// </summary>
        public static List<PointF> Naca(float m, float p, float t, int n)
        {
            var upper = new List<PointF>();
            var lower = new List<PointF>();
            for (int i = 0; i <= n; i++)
            {
                double beta = Math.PI * i / n;
                double x = 0.5 * (1 - Math.Cos(beta));
                double yt = 5 * t * (0.2969 * Math.Sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x * x * x - 0.1036 * x * x * x * x);
                double yc, dyc;
                Camber4(m, p, x, out yc, out dyc);
                double th = Math.Atan(dyc);
                upper.Add(new PointF((float)(x - yt * Math.Sin(th)), (float)(yc + yt * Math.Cos(th))));
                lower.Add(new PointF((float)(x + yt * Math.Sin(th)), (float)(yc - yt * Math.Cos(th))));
            }
            var poly = new List<PointF>();
            for (int i = n; i >= 0; i--) poly.Add(upper[i]);
            for (int i = 1; i < n; i++) poly.Add(lower[i]);
            return poly;
        }

        static void Camber4(double m, double p, double x, out double yc, out double dyc)
        {
            yc = 0; dyc = 0;
            if (m <= 0 || p <= 0) return;
            if (x < p) { yc = m / (p * p) * (2 * p * x - x * x); dyc = 2 * m / (p * p) * (p - x); }
            else { yc = m / ((1 - p) * (1 - p)) * ((1 - 2 * p) + 2 * p * x - x * x); dyc = 2 * m / ((1 - p) * (1 - p)) * (p - x); }
        }

        /// <summary>
        /// Glatte geschlossene Kurve durch die Stützpunkte (Catmull-Rom). Punkte mit corner[i] = true bleiben Ecken:
        /// dort beginnt bzw. endet ein eigener Kurvenabschnitt, sodass Kanten (Abrisskanten, Kastenformen) scharf bleiben.
        /// </summary>
        public static List<PointF> Smooth(List<PointF> pts, List<bool> corner, int sub)
        {
            int n = pts.Count;
            var r = new List<PointF>(n * sub);
            if (n < 3 || sub <= 1) { r.AddRange(pts); return r; }
            int first = corner.IndexOf(true);
            if (first < 0)
            {
                for (int i = 0; i < n; i++)
                    Segment(r, pts[(i - 1 + n) % n], pts[i], pts[(i + 1) % n], pts[(i + 2) % n], sub);
                return r;
            }
            // Abschnitte von Ecke zu Ecke; an den Ecken wird der Nachbarpunkt gespiegelt (Tangente entlang der Sehne)
            for (int k = 0; k < n; k++)
            {
                int i = (first + k) % n, j = (i + 1) % n;
                PointF a = pts[i], b = pts[j];
                PointF pa = corner[i] ? Reflect(b, a) : pts[(i - 1 + n) % n];
                PointF pb = corner[j] ? Reflect(a, b) : pts[(j + 1) % n];
                Segment(r, pa, a, b, pb, sub);
            }
            return r;
        }

        static PointF Reflect(PointF p, PointF about) { return new PointF(2 * about.X - p.X, 2 * about.Y - p.Y); }

        static void Segment(List<PointF> r, PointF p0, PointF p1, PointF p2, PointF p3, int sub)
        {
            for (int k = 0; k < sub; k++)
            {
                float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                float b0 = -0.5f * t3 + t2 - 0.5f * t, b1 = 1.5f * t3 - 2.5f * t2 + 1;
                float b2 = -1.5f * t3 + 2f * t2 + 0.5f * t, b3 = 0.5f * t3 - 0.5f * t2;
                r.Add(new PointF(b0 * p0.X + b1 * p1.X + b2 * p2.X + b3 * p3.X, b0 * p0.Y + b1 * p1.Y + b2 * p2.Y + b3 * p3.Y));
            }
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

        /// <summary>
        /// Wie <see cref="Fill"/>, setzt zusätzlich jede Zelle, deren Mittelpunkt dem Umriss am nächsten liegt.
        /// So bleiben dünne Teile (Hinterkanten, Klappen, Flügelelemente, Spoilerlippen) als lückenlose,
        /// mindestens eine Zelle starke Wand erhalten, statt in einzelne lose Zellen zu zerfallen.
        /// Spalte zwischen zwei Teilen bleiben offen, solange sie gut eine Zelle breit sind.
        /// </summary>
        public static void FillFine(PointF[] pts, bool[] mask, int nx, int ny)
        {
            Fill(pts, mask, nx, ny);
            for (int i = 0; i < pts.Length; i++)
            {
                PointF a = pts[i], b = pts[(i + 1) % pts.Length];
                float dx = b.X - a.X, dy = b.Y - a.Y;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)) * 3));
                for (int s = 0; s <= steps; s++)
                {
                    float t = s / (float)steps;
                    int x = (int)Math.Round(a.X + t * dx), y = (int)Math.Round(a.Y + t * dy);
                    if (x >= 0 && x < nx && y >= 0 && y < ny) mask[y * nx + x] = true;
                }
            }
        }
    }
}
