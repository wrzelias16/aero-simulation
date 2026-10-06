using System;
using System.Collections.Generic;
using System.Drawing;

namespace Windkanal
{
    public enum ShapeKind { Zylinder, Quadrat, Platte, Naca0012, Naca2412, Naca4412, Auto, AutoDetail, Rennwagen, Klappenfluegel, Eigene }

    public static class Shapes
    {
        public static readonly ShapeKind[] All =
        {
            ShapeKind.Zylinder, ShapeKind.Quadrat, ShapeKind.Platte, ShapeKind.Naca0012,
            ShapeKind.Naca2412, ShapeKind.Naca4412, ShapeKind.Klappenfluegel, ShapeKind.Auto, ShapeKind.AutoDetail,
            ShapeKind.Rennwagen, ShapeKind.Eigene
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
                case ShapeKind.Auto: return "Auto (einfaches Seitenprofil)";
                case ShapeKind.AutoDetail: return "Auto detailliert (mit Rädern)";
                case ShapeKind.Rennwagen: return "Rennwagen mit Front- und Heckflügel";
                case ShapeKind.Klappenfluegel: return "Flugzeugflügel mit Landeklappe";
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
                case ShapeKind.AutoDetail: return 50;
                case ShapeKind.Rennwagen: return 55;
                case ShapeKind.Klappenfluegel: return 40;
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
                // Detailmodelle etwas ruhiger starten: bei Re = 5000 wird die 2D-Strömung im ganzen Kanal
                // unruhig und die Rauchfäden zerfasern schon vor dem Fahrzeug
                case ShapeKind.AutoDetail:
                case ShapeKind.Rennwagen: return 1000;
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
                case ShapeKind.Naca4412:
                case ShapeKind.Klappenfluegel: return "Sehnenlänge";
                default: return "Stirnhöhe";
            }
        }

        /// <summary>True, wenn die Bezugslänge die Formgröße selbst ist (sonst: gemessene Stirnhöhe).</summary>
        public static bool RefIsSize(ShapeKind k)
        {
            return k != ShapeKind.Eigene && !IsVehicle(k);
        }

        /// <summary>Fahrzeuge stehen knapp über dem Boden statt mittig im Kanal.</summary>
        public static bool IsVehicle(ShapeKind k)
        {
            return k == ShapeKind.Auto || k == ShapeKind.AutoDetail || k == ShapeKind.Rennwagen;
        }

        /// <summary>Anstellwinkel, mit dem die Form ausgewählt wird.</summary>
        public static int DefaultAngle(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.Naca0012:
                case ShapeKind.Naca2412:
                case ShapeKind.Naca4412:
                case ShapeKind.Klappenfluegel: return 5;
                default: return 0;
            }
        }

        /// <summary>Umriss (bei mehrteiligen Formen das erste Teil) in Einheiten der Formgröße.</summary>
        public static List<PointF> Polygon(ShapeKind k)
        {
            return Parts(k)[0];
        }

        /// <summary>
        /// Alle Teile der Form (z. B. Karosserie und Räder), in Einheiten der Formgröße,
        /// Drehpunkt im Ursprung, y nach oben. Überlappende Teile werden beim Füllen vereinigt.
        /// </summary>
        public static List<List<PointF>> Parts(ShapeKind k)
        {
            switch (k)
            {
                case ShapeKind.AutoDetail: return DetailedCar();
                case ShapeKind.Rennwagen: return RaceCar();
                case ShapeKind.Klappenfluegel: return FlapWing();
                default: return new List<List<PointF>> { SinglePolygon(k) };
            }
        }

        static List<PointF> SinglePolygon(ShapeKind k)
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

        // ------------------------------------------------------------ Detailmodelle
        // Alle Maße als Anteil der Gesamtlänge. Die kleinsten Details (Flügeldicke, Spalt der Klappe)
        // sind etwa 2,5 % der Länge groß, damit sie bei der Standardgröße mindestens 3 Gitterzellen breit sind.

        /// <summary>Limousine mit Motorhaube, Frontscheibe, Dach, Heckscheibe, Abrisskante, Diffusor und zwei Rädern.</summary>
        static List<List<PointF>> DetailedCar()
        {
            var body = Smooth(new float[,]
            {
                { 0.030f, 0.068f }, { 0.006f, 0.095f }, { 0.000f, 0.125f }, { 0.008f, 0.158f },  // Stoßfänger vorn
                { 0.040f, 0.180f }, { 0.130f, 0.196f }, { 0.290f, 0.214f },                       // Motorhaube
                { 0.320f, 0.222f }, { 0.420f, 0.298f },                                           // Frontscheibe
                { 0.470f, 0.314f }, { 0.560f, 0.318f }, { 0.630f, 0.308f },                       // Dach
                { 0.760f, 0.252f }, { 0.800f, 0.240f },                                           // Heckscheibe
                { 0.900f, 0.236f }, { 0.945f, 0.240f }, { 0.958f, 0.246f }, { 0.962f, 0.236f },  // Kofferraum mit Abrisskante
                { 0.985f, 0.205f }, { 1.000f, 0.160f }, { 0.998f, 0.115f }, { 0.985f, 0.090f },  // Heck
                { 0.950f, 0.084f }, { 0.880f, 0.062f },                                           // Diffusor
                { 0.700f, 0.058f }, { 0.300f, 0.058f }, { 0.100f, 0.060f }                        // Unterboden
            }, 6);
            var parts = new List<List<PointF>>
            {
                body,
                Circle(0.19f, 0.075f, 0.075f, 64),   // Vorderrad
                Circle(0.79f, 0.075f, 0.075f, 64)    // Hinterrad
            };
            return Offset(parts, -0.5f, -0.16f);
        }

        /// <summary>Formelwagen: flache Nase, Cockpit mit Fahrerhelm, Airbox, Diffusor, Front- und Heckflügel, große Räder.</summary>
        static List<List<PointF>> RaceCar()
        {
            var body = Smooth(new float[,]
            {
                { 0.030f, 0.060f }, { 0.050f, 0.074f }, { 0.150f, 0.090f }, { 0.300f, 0.110f },  // Nase
                { 0.360f, 0.120f }, { 0.395f, 0.128f },                                           // Cockpit-Rand
                { 0.410f, 0.152f }, { 0.432f, 0.166f }, { 0.456f, 0.160f }, { 0.466f, 0.140f },  // Helm
                { 0.490f, 0.150f }, { 0.505f, 0.188f }, { 0.540f, 0.196f }, { 0.590f, 0.176f },  // Airbox
                { 0.700f, 0.130f }, { 0.820f, 0.098f }, { 0.885f, 0.088f },                       // Motorabdeckung
                { 0.905f, 0.070f }, { 0.905f, 0.052f },                                           // Heck
                { 0.840f, 0.036f }, { 0.780f, 0.024f },                                           // Diffusor
                { 0.600f, 0.022f }, { 0.300f, 0.022f },                                           // Unterboden
                { 0.180f, 0.040f }, { 0.080f, 0.048f }                                            // Nase unten
            }, 6);
            var parts = new List<List<PointF>>
            {
                body,
                Circle(0.200f, 0.068f, 0.068f, 64),  // Vorderrad
                Circle(0.790f, 0.074f, 0.074f, 64),  // Hinterrad
                // Flügel umgekehrt eingebaut: Wölbung nach unten, Hinterkante oben -> Abtrieb
                Airfoil(0.05f, 0.4f, 0.18f, 0.140f, -6f, -0.030f, 0.030f, true),   // Frontflügel
                Airfoil(0.06f, 0.4f, 0.18f, 0.160f, -14f, 0.835f, 0.150f, true)    // Heckflügel
            };
            return Offset(parts, -0.5f, -0.09f);
        }

        /// <summary>Tragflügel (NACA 2412) mit Spaltklappe (NACA 4412, 25° ausgefahren), Gesamtsehne etwa 1.</summary>
        static List<List<PointF>> FlapWing()
        {
            var parts = new List<List<PointF>>
            {
                Airfoil(0.02f, 0.4f, 0.13f, 0.78f, 0f, 0f, 0f, false),
                Airfoil(0.04f, 0.4f, 0.15f, 0.30f, 25f, 0.69f, -0.052f, false)
            };
            return Offset(parts, -0.25f, 0f);
        }

        /// <summary>NACA-Profil mit Sehne 'chord', um die Vorderkante gedreht (positiv = Nase hoch), Vorderkante bei (x, y).</summary>
        static List<PointF> Airfoil(float m, float p, float t, float chord, float angleDeg, float x, float y, bool inverted)
        {
            var src = Naca(m, p, t);
            double a = angleDeg * Math.PI / 180.0;
            float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
            var r = new List<PointF>(src.Count);
            foreach (var q in src)
            {
                float px = (q.X + 0.25f) * chord, py = (inverted ? -q.Y : q.Y) * chord;
                r.Add(new PointF(x + px * ca + py * sa, y - px * sa + py * ca));
            }
            return r;
        }

        static List<PointF> Circle(float cx, float cy, float rad, int n)
        {
            var p = new List<PointF>();
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n;
                p.Add(new PointF(cx + (float)(rad * Math.Cos(a)), cy + (float)(rad * Math.Sin(a))));
            }
            return p;
        }

        /// <summary>Geschlossene Catmull-Rom-Kurve durch die Stützpunkte (weiche Karosserieformen).</summary>
        static List<PointF> Smooth(float[,] pts, int sub)
        {
            int n = pts.GetLength(0);
            var r = new List<PointF>(n * sub);
            for (int i = 0; i < n; i++)
            {
                int i0 = (i - 1 + n) % n, i2 = (i + 1) % n, i3 = (i + 2) % n;
                for (int k = 0; k < sub; k++)
                {
                    float t = k / (float)sub, t2 = t * t, t3 = t2 * t;
                    float b0 = -0.5f * t3 + t2 - 0.5f * t, b1 = 1.5f * t3 - 2.5f * t2 + 1;
                    float b2 = -1.5f * t3 + 2f * t2 + 0.5f * t, b3 = 0.5f * t3 - 0.5f * t2;
                    r.Add(new PointF(b0 * pts[i0, 0] + b1 * pts[i, 0] + b2 * pts[i2, 0] + b3 * pts[i3, 0],
                                     b0 * pts[i0, 1] + b1 * pts[i, 1] + b2 * pts[i2, 1] + b3 * pts[i3, 1]));
                }
            }
            return r;
        }

        static List<List<PointF>> Offset(List<List<PointF>> parts, float dx, float dy)
        {
            foreach (var part in parts)
                for (int i = 0; i < part.Count; i++) part[i] = new PointF(part[i].X + dx, part[i].Y + dy);
            return parts;
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

        /// <summary>Transformiert alle Teile einer Form (siehe <see cref="Transform"/>).</summary>
        public static List<PointF[]> TransformParts(List<List<PointF>> parts, float scale, float angleDeg, float cx, float cy)
        {
            var r = new List<PointF[]>(parts.Count);
            foreach (var p in parts) r.Add(Transform(p, scale, angleDeg, cx, cy));
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
