using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Windkanal3D
{
    /// <summary>
    /// Dreiecksnetz als "Dreieckssuppe": je Dreieck 9 Zahlen (x0 y0 z0 x1 y1 z1 x2 y2 z2).
    /// Achsen im Windkanal: x = Strömungsrichtung, y = seitlich, z = oben.
    /// Lädt STL (binär und Text) und OBJ und enthält ein paar eingebaute Körper.
    /// </summary>
    public sealed class Mesh
    {
        public readonly float[] V;
        public readonly string Name;
        /// <summary>Datei, aus der das Netz geladen wurde (null bei eingebauten Körpern).</summary>
        public string SourcePath;
        /// <summary>Grundausrichtung (Zeilen = Windkanal-Achsen x, y, z in Modell-Achsen), nur Vielfache von 90°.</summary>
        public int[] Rot = { 1, 0, 0, 0, 1, 0, 0, 0, 1 };

        public Mesh(float[] v, string name) { V = v; Name = name; }

        public int Triangles { get { return V.Length / 9; } }

        public void Bounds(out float[] min, out float[] max)
        {
            min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
            max = new[] { float.MinValue, float.MinValue, float.MinValue };
            for (int i = 0; i < V.Length; i++)
            {
                int a = i % 3;
                if (V[i] < min[a]) min[a] = V[i];
                if (V[i] > max[a]) max[a] = V[i];
            }
        }

        /// <summary>
        /// Automatisch ausrichten: längste Seite in Strömungsrichtung (x), flachste nach oben (z).
        /// Die Richtung "oben" der Datei bleibt oben; vorne/hinten lässt sich danach mit der Drehung um die Hochachse tauschen.
        /// </summary>
        public void AutoOrient()
        {
            float[] min, max;
            Bounds(out min, out max);
            var ext = new[] { max[0] - min[0], max[1] - min[1], max[2] - min[2] };
            var idx = new[] { 0, 1, 2 };
            Array.Sort(idx, (a, b) => ext[b].CompareTo(ext[a]));   // absteigend
            var r = new int[9];
            for (int row = 0; row < 3; row++) r[row * 3 + idx[row]] = 1;
            // ungerade Vertauschung wäre ein Spiegelbild: dann die Querachse umdrehen
            int det = r[0] * (r[4] * r[8] - r[5] * r[7]) - r[1] * (r[3] * r[8] - r[5] * r[6]) + r[2] * (r[3] * r[7] - r[4] * r[6]);
            if (det < 0) for (int k = 3; k < 6; k++) r[k] = -r[k];
            Rot = r;
            if (FrontIsHigher()) Rotate180();
        }

        /// <summary>
        /// Vorne = gegen die Strömung (-x). Bei Fahrzeugen ist das Heck meist das höhere Ende (Heckflügel, Dach, Kofferraum),
        /// die Nase das flache. Vergleicht die größte Höhe im vorderen und im hinteren Sechstel.
        /// </summary>
        bool FrontIsHigher()
        {
            var R = Rot;
            float xmin = float.MaxValue, xmax = float.MinValue, zmin = float.MaxValue, zmax = float.MinValue;
            for (int i = 0; i < V.Length; i += 3)
            {
                float x = R[0] * V[i] + R[1] * V[i + 1] + R[2] * V[i + 2], z = R[6] * V[i] + R[7] * V[i + 1] + R[8] * V[i + 2];
                xmin = Math.Min(xmin, x); xmax = Math.Max(xmax, x); zmin = Math.Min(zmin, z); zmax = Math.Max(zmax, z);
            }
            float band = (xmax - xmin) / 6, front = float.MinValue, rear = float.MinValue;
            for (int i = 0; i < V.Length; i += 3)
            {
                float x = R[0] * V[i] + R[1] * V[i + 1] + R[2] * V[i + 2], z = R[6] * V[i] + R[7] * V[i + 1] + R[8] * V[i + 2];
                if (x < xmin + band) front = Math.Max(front, z);
                if (x > xmax - band) rear = Math.Max(rear, z);
            }
            return front > rear + 0.03f * (zmax - zmin);
        }

        /// <summary>Vorne und hinten tauschen (180° um die Hochachse).</summary>
        public void Rotate180() { Rotate90(2); Rotate90(2); }

        /// <summary>Um 90° um eine Windkanal-Achse kippen (0 = x, 1 = y, 2 = z).</summary>
        public void Rotate90(int axis)
        {
            int[] q = axis == 0 ? new[] { 1, 0, 0, 0, 0, -1, 0, 1, 0 }
                    : axis == 1 ? new[] { 0, 0, 1, 0, 1, 0, -1, 0, 0 }
                    : new[] { 0, -1, 0, 1, 0, 0, 0, 0, 1 };
            var r = new int[9];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    for (int k = 0; k < 3; k++) r[i * 3 + j] += q[i * 3 + k] * Rot[k * 3 + j];
            Rot = r;
        }

        // ------------------------------------------------------------ Dateien

        /// <summary>Lädt eine .stl- oder .obj-Datei. Wirft eine Exception mit verständlichem Grund.</summary>
        public static Mesh Load(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            string name = Path.GetFileNameWithoutExtension(path);
            float[] v;
            if (ext == ".stl") v = LoadStl(File.ReadAllBytes(path));
            else if (ext == ".obj") v = LoadObj(File.ReadAllLines(path));
            else throw new Exception("Nur .stl- und .obj-Dateien werden unterstützt.");
            if (v.Length < 9) throw new Exception("Die Datei enthält keine Dreiecke.");
            for (int i = 0; i < v.Length; i++)
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) throw new Exception("Die Datei enthält ungültige Koordinaten.");
            var m = new Mesh(v, name) { SourcePath = Path.GetFullPath(path) };
            m.AutoOrient();
            return m;
        }

        static float[] LoadStl(byte[] data)
        {
            // Binär: 80 Byte Kopf, 4 Byte Anzahl, je Dreieck 50 Byte. Manche Binärdateien beginnen trotzdem mit "solid",
            // darum entscheidet die Dateigröße.
            if (data.Length >= 84)
            {
                uint count = BitConverter.ToUInt32(data, 80);
                if (count > 0 && 84L + 50L * count == data.Length)
                {
                    var v = new float[count * 9];
                    for (int t = 0; t < count; t++)
                    {
                        int o = 84 + 50 * t + 12;   // Normale überspringen
                        for (int k = 0; k < 9; k++) v[t * 9 + k] = BitConverter.ToSingle(data, o + 4 * k);
                    }
                    return v;
                }
            }
            var list = new List<float>();
            string text = Encoding.ASCII.GetString(data);
            var inv = CultureInfo.InvariantCulture;
            foreach (var raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (p.Length < 4) continue;
                list.Add(float.Parse(p[1], inv)); list.Add(float.Parse(p[2], inv)); list.Add(float.Parse(p[3], inv));
            }
            if (list.Count == 0 && data.Length >= 84) throw new Exception("Die STL-Datei ist beschädigt oder unvollständig.");
            list.RemoveRange(list.Count - list.Count % 9, list.Count % 9);
            return list.ToArray();
        }

        static float[] LoadObj(string[] lines)
        {
            var inv = CultureInfo.InvariantCulture;
            var pos = new List<float>();
            var tris = new List<float>();
            var face = new List<int>();
            foreach (var raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("v "))
                {
                    var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    pos.Add(float.Parse(p[1], inv)); pos.Add(float.Parse(p[2], inv)); pos.Add(float.Parse(p[3], inv));
                }
                else if (line.StartsWith("f "))
                {
                    var p = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    face.Clear();
                    for (int i = 1; i < p.Length; i++)
                    {
                        int slash = p[i].IndexOf('/');
                        int idx = int.Parse(slash < 0 ? p[i] : p[i].Substring(0, slash), inv);
                        idx = idx < 0 ? pos.Count / 3 + idx : idx - 1;   // negative Indizes zählen vom Ende
                        if (idx < 0 || idx >= pos.Count / 3) throw new Exception("Die OBJ-Datei verweist auf einen Punkt, den es nicht gibt.");
                        face.Add(idx);
                    }
                    for (int i = 1; i + 1 < face.Count; i++)   // Vielecke als Fächer zerlegen
                        foreach (int k in new[] { face[0], face[i], face[i + 1] })
                        {
                            tris.Add(pos[3 * k]); tris.Add(pos[3 * k + 1]); tris.Add(pos[3 * k + 2]);
                        }
                }
            }
            return tris.ToArray();
        }

        // ------------------------------------------------------------ eingebaute Körper

        sealed class Builder
        {
            public readonly List<float> V = new List<float>();
            public void Tri(double[] a, double[] b, double[] c)
            {
                foreach (var p in new[] { a, b, c }) { V.Add((float)p[0]); V.Add((float)p[1]); V.Add((float)p[2]); }
            }
            public void Quad(double[] a, double[] b, double[] c, double[] d) { Tri(a, b, c); Tri(a, c, d); }
        }

        static double[] P(double x, double y, double z) { return new[] { x, y, z }; }

        public static Mesh Sphere()
        {
            var b = new Builder();
            const int nu = 64, nv = 32;
            Func<int, int, double[]> pt = (i, j) =>
            {
                double th = Math.PI * j / nv, ph = 2 * Math.PI * i / nu;
                return P(Math.Sin(th) * Math.Cos(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(th));
            };
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    var a = pt(i, j); var c = pt(i + 1, j + 1);
                    if (j > 0) b.Tri(a, pt(i + 1, j), c);
                    if (j < nv - 1) b.Tri(a, c, pt(i, j + 1));
                }
            return new Mesh(b.V.ToArray(), "Kugel");
        }

        public static Mesh Cube()
        {
            return Loft("Würfel", new[] { new[] { -1.0, 1, -1, 1 }, new[] { 1.0, 1, -1, 1 } });
        }

        /// <summary>Zylinder quer zur Strömung (Achse = y), Länge 3 x Durchmesser.</summary>
        public static Mesh Cylinder()
        {
            var b = new Builder();
            const int n = 64;
            double h = 1.5;
            for (int i = 0; i < n; i++)
            {
                double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
                var p0 = P(0.5 * Math.Cos(a0), -h, 0.5 * Math.Sin(a0)); var p1 = P(0.5 * Math.Cos(a1), -h, 0.5 * Math.Sin(a1));
                var q0 = P(p0[0], h, p0[2]); var q1 = P(p1[0], h, p1[2]);
                b.Quad(p0, p1, q1, q0);
                b.Tri(P(0, -h, 0), p1, p0);
                b.Tri(P(0, h, 0), q0, q1);
            }
            return new Mesh(b.V.ToArray(), "Zylinder (quer)");
        }

        /// <summary>
        /// Ahmed-Körper: Standard-Referenzkörper der Fahrzeugaerodynamik (1044 x 389 x 288 mm,
        /// vordere Kanten mit 100 mm gerundet, Heckschräge 25° auf 222 mm). Ohne Stelzen, Bodenabstand 50 mm.
        /// </summary>
        public static Mesh Ahmed()
        {
            const double L = 1.044, W = 0.389, H = 0.288, R = 0.100, Ls = 0.222, gap = 0.050;
            double slant = Math.Tan(25 * Math.PI / 180);
            var st = new List<double[]>();
            for (int i = 0; i <= 12; i++)
            {
                double x = R * (1 - Math.Cos(0.5 * Math.PI * i / 12));   // dichter an der Nase
                double d = R - Math.Sqrt(Math.Max(0, R * R - (R - x) * (R - x)));
                st.Add(new[] { x, W / 2 - d, gap + d, gap + H - d });
            }
            st.Add(new[] { L - Ls, W / 2, gap, gap + H });
            st.Add(new[] { L, W / 2, gap, gap + H - Ls * slant });
            // Nase zeigt gegen die Strömung (-x)
            return Loft("Ahmed-Körper (Auto-Referenz)", st.ToArray());
        }

        /// <summary>Körper aus Rechteck-Querschnitten entlang x: je Station { x, halbe Breite, unten, oben }.</summary>
        static Mesh Loft(string name, double[][] st)
        {
            var b = new Builder();
            Func<double[], double[][]> ring = s => new[]
            {
                P(s[0], -s[1], s[2]), P(s[0], s[1], s[2]), P(s[0], s[1], s[3]), P(s[0], -s[1], s[3])
            };
            for (int i = 0; i + 1 < st.Length; i++)
            {
                var a = ring(st[i]); var c = ring(st[i + 1]);
                for (int k = 0; k < 4; k++) b.Quad(a[k], a[(k + 1) % 4], c[(k + 1) % 4], c[k]);
            }
            var f = ring(st[0]); var e = ring(st[st.Length - 1]);
            b.Quad(f[0], f[3], f[2], f[1]);
            b.Quad(e[0], e[1], e[2], e[3]);
            return new Mesh(b.V.ToArray(), name);
        }

        public static Mesh[] BuiltIn()
        {
            return new[] { Sphere(), Ahmed(), Cube(), Cylinder() };
        }
    }

    /// <summary>
    /// Setzt ein Netz in den Windkanal (drehen, skalieren, verschieben) und wandelt es in feste Zellen um.
    /// </summary>
    public sealed class Placement
    {
        /// <summary>Dreiecke in Zellkoordinaten (Zelle i hat ihren Mittelpunkt bei i + 0,5).</summary>
        public float[] World;
        /// <summary>Feste Zellen, Index x + NX * (y + NY * z).</summary>
        public byte[] Solid;
        public int SolidCells, FrontalCells;
        /// <summary>Länge in Strömungsrichtung, Breite und Höhe in Zellen.</summary>
        public float LengthX, WidthY, HeightZ;
        /// <summary>Netz war nicht geschlossen (ungerade Anzahl Schnittpunkte auf manchen Strahlen).</summary>
        public int LeakyRays;

        /// <param name="yaw">Drehung um die Hochachse in Grad</param>
        /// <param name="pitch">Anstellwinkel in Grad (positiv = Nase hoch)</param>
        /// <param name="size">größte Abmessung als Anteil der Tunnelbreite</param>
        public static Placement Build(Mesh mesh, double yaw, double pitch, double size, bool onGround,
                                      int nx, int ny, int nz)
        {
            var src = mesh.V;
            var w = new float[src.Length];
            double cp = Math.Cos(pitch * Math.PI / 180), sp = Math.Sin(pitch * Math.PI / 180);
            double cy = Math.Cos(yaw * Math.PI / 180), sy = Math.Sin(yaw * Math.PI / 180);

            float[] min, max;
            mesh.Bounds(out min, out max);
            double mx = (min[0] + max[0]) / 2, my = (min[1] + max[1]) / 2, mz = (min[2] + max[2]) / 2;
            var R = mesh.Rot;
            for (int i = 0; i < src.Length; i += 3)
            {
                double a = src[i] - mx, b = src[i + 1] - my, c = src[i + 2] - mz;
                double x = R[0] * a + R[1] * b + R[2] * c, y = R[3] * a + R[4] * b + R[5] * c, z = R[6] * a + R[7] * b + R[8] * c;
                double x1 = x * cp + z * sp, z1 = -x * sp + z * cp;          // Anstellwinkel (um y)
                double x2 = x1 * cy - y * sy, y2 = x1 * sy + y * cy;          // Gieren (um z)
                w[i] = (float)x2; w[i + 1] = (float)y2; w[i + 2] = (float)z1;
            }

            var p = new Placement { World = w };
            var bmin = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
            var bmax = new[] { double.MinValue, double.MinValue, double.MinValue };
            for (int i = 0; i < w.Length; i++) { int a = i % 3; bmin[a] = Math.Min(bmin[a], w[i]); bmax[a] = Math.Max(bmax[a], w[i]); }
            double ex = bmax[0] - bmin[0], ey = bmax[1] - bmin[1], ez = bmax[2] - bmin[2];
            double big = Math.Max(ex, Math.Max(ey, ez));
            if (big <= 0) big = 1;
            double s = size * ny / big;
            // muss in den Kanal passen (mit Rand)
            s = Math.Min(s, Math.Min(0.6 * nx / Math.Max(ex, 1e-9), Math.Min(0.9 * ny / Math.Max(ey, 1e-9), 0.9 * nz / Math.Max(ez, 1e-9))));

            double front = Math.Max(0.15 * nx, 0.3 * nx - s * ex / 2);
            double ox = front - s * bmin[0];
            double oy = ny / 2.0 - s * (bmin[1] + bmax[1]) / 2;
            double oz = onGround ? -s * bmin[2] : nz / 2.0 - s * (bmin[2] + bmax[2]) / 2;
            for (int i = 0; i < w.Length; i += 3)
            {
                w[i] = (float)(w[i] * s + ox);
                w[i + 1] = (float)(w[i + 1] * s + oy);
                w[i + 2] = (float)(w[i + 2] * s + oz);
            }
            p.LengthX = (float)(s * ex); p.WidthY = (float)(s * ey); p.HeightZ = (float)(s * ez);
            p.Voxelize(nx, ny, nz);
            return p;
        }

        /// <summary>
        /// Je Strahl entlang x (durch die Zellmitten in y und z) werden alle Schnittpunkte mit den Dreiecken gesammelt;
        /// zwischen Eintritt und Austritt ist der Körper. Funktioniert für geschlossene Netze.
        /// </summary>
        void Voxelize(int nx, int ny, int nz)
        {
            var hits = new List<float>[ny * nz];
            var w = World;
            // winziger, "krummer" Versatz, damit Strahlen nie genau auf einer Dreieckskante liegen
            const double ey = 1.37e-4, ez = 2.71e-4;
            for (int t = 0; t < w.Length; t += 9)
            {
                double y0 = w[t + 1], z0 = w[t + 2], y1 = w[t + 4], z1 = w[t + 5], y2 = w[t + 7], z2 = w[t + 8];
                double area = (y1 - y0) * (z2 - z0) - (y2 - y0) * (z1 - z0);
                if (Math.Abs(area) < 1e-12) continue;   // steht parallel zur Strömung
                int ja = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2)) - 0.5));
                int jb = Math.Min(ny - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2)) - 0.5));
                int ka = Math.Max(0, (int)Math.Floor(Math.Min(z0, Math.Min(z1, z2)) - 0.5));
                int kb = Math.Min(nz - 1, (int)Math.Ceiling(Math.Max(z0, Math.Max(z1, z2)) - 0.5));
                for (int k = ka; k <= kb; k++)
                    for (int j = ja; j <= jb; j++)
                    {
                        double py = j + 0.5 + ey, pz = k + 0.5 + ez;
                        double b0 = ((y1 - py) * (z2 - pz) - (y2 - py) * (z1 - pz)) / area;
                        double b1 = ((y2 - py) * (z0 - pz) - (y0 - py) * (z2 - pz)) / area;
                        double b2 = 1 - b0 - b1;
                        if (b0 < 0 || b1 < 0 || b2 < 0) continue;
                        float x = (float)(b0 * w[t] + b1 * w[t + 3] + b2 * w[t + 6]);
                        int r = j + ny * k;
                        if (hits[r] == null) hits[r] = new List<float>();
                        hits[r].Add(x);
                    }
            }

            var solid = new byte[nx * ny * nz];
            int leaky = 0, frontal = 0, cells = 0;
            object sync = new object();
            Parallel.For(0, ny * nz, r =>
            {
                var h = hits[r];
                if (h == null) return;
                h.Sort();
                int pairs = h.Count / 2, local = 0;
                int j = r % ny, k = r / ny;
                for (int q = 0; q < pairs; q++)
                {
                    int xa = Math.Max(0, (int)Math.Ceiling(h[2 * q] - 0.5));
                    int xb = Math.Min(nx - 1, (int)Math.Floor(h[2 * q + 1] - 0.5));
                    for (int x = xa; x <= xb; x++) { solid[x + nx * (j + ny * k)] = 1; local++; }
                }
                lock (sync)
                {
                    if (h.Count % 2 == 1) leaky++;
                    if (local > 0) frontal++;
                    cells += local;
                }
            });
            // Einlass- und Auslass-Ebene müssen frei bleiben
            for (int k = 0; k < nz; k++)
                for (int j = 0; j < ny; j++) { solid[nx * (j + ny * k)] = 0; solid[nx - 1 + nx * (j + ny * k)] = 0; }
            Solid = solid; LeakyRays = leaky; FrontalCells = frontal; SolidCells = cells;
        }
    }
}
