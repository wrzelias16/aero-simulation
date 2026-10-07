using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Windkanal3D
{
    /// <summary>
    /// Zeichnet den Windkanal in 3D (Körper, Kanalrahmen, Schnittebene) mit einem eigenen kleinen Software-Renderer:
    /// Tiefenpuffer, weiche Beleuchtung, Kantenglättung durch doppelte Auflösung. Läuft auf allen CPU-Kernen.
    /// Gezeichnet wird nur, wenn sich Kamera oder Körper ändern.
    /// </summary>
    public sealed class Scene
    {
        // Kamera (Kreisbahn um den Kanal)
        public double Yaw = -2.35, Pitch = 0.42, Distance = 1.0;
        public double PanX, PanY;

        public float[] Triangles;            // Zellkoordinaten
        public int NX = 256, NY = 112, NZ = 112;
        /// <summary>Schnittebene: -1 = keine, 0 = Seitenschnitt (y = SliceIndex), 1 = Draufsicht (z = SliceIndex).</summary>
        public int SliceAxis = -1, SliceIndex;

        /// <summary>Stromlinien: je Linie x, y, z, Geschwindigkeit/uIn je Punkt. Farbe aus Lut (0 … 1,5·U).</summary>
        public System.Collections.Generic.List<float[]> Lines;
        public int[] Lut;
        /// <summary>
        /// Zusätzliche Ebene über dem Bild (z. B. Rauch von der Grafikkarte): bekommt je Bildpunkt 1/Abstand zur nächsten
        /// Fläche (0 = keine) sowie die Kamera (Position, vorwärts, rechts, oben je xyz, Brennweite in Ausgabe-Bildpunkten)
        /// und liefert RGBA mit vormultiplizierter Deckkraft in der Ausgabegröße.
        /// </summary>
        public Func<float[], int, int, float[], byte[]> Overlay;
        /// <summary>Schnittebene als farbige Fläche im Raum (Werte = Geschwindigkeit/uIn, feste Zellen = -1).</summary>
        public float[] SliceData;
        public bool ShowSlicePlane;

        public Color Background = Color.FromArgb(23, 25, 28), Body = Color.FromArgb(206, 211, 219),
                     Frame = Color.FromArgb(102, 108, 117), Floor = Color.FromArgb(44, 47, 53), SliceColor = Color.FromArgb(98, 128, 255);

        int sw, sh, ss;
        int[] color;
        float[] depth;

        // Kamera-Basis (für die Projektion)
        double cx, cy, cz, rx, ry, rz, ux, uy, uz, fx, fy, fz, focal, tx, ty, tz;

        /// <summary>Blickziel (Mitte und Größe in Zellen); null = ganzer Kanal. Zeigt beim Start den Körper formatfüllend.</summary>
        public double[] Focus;

        public void ResetCamera() { Yaw = -2.35; Pitch = 0.42; Distance = 1.0; PanX = PanY = 0; }

        /// <summary>Bild in der Größe w x h zeichnen. quality = 2 glättet Kanten (4-fache Arbeit), 1 = schnell beim Drehen.</summary>
        public Bitmap Render(int w, int h, int quality)
        {
            w = Math.Max(1, w); h = Math.Max(1, h);
            ss = Math.Max(1, quality);
            sw = w * ss; sh = h * ss;
            if (color == null || color.Length != sw * sh) { color = new int[sw * sh]; depth = new float[sw * sh]; }
            int bg = Background.ToArgb();
            Parallel.For(0, sh, y => { int o = y * sw; for (int x = 0; x < sw; x++) { color[o + x] = bg; depth[o + x] = 0; } });

            SetupCamera();
            DrawFloor();
            if (Triangles != null) DrawMesh(Triangles);
            byte[] over = null;
            if (Overlay != null)
            {
                // Tiefe je Ausgabe-Bildpunkt: die nächste Fläche im Block
                var iz = new float[w * h];
                Parallel.For(0, h, y =>
                {
                    for (int x = 0; x < w; x++)
                    {
                        float m = 0;
                        for (int j = 0; j < ss; j++)
                            for (int i = 0; i < ss; i++) m = Math.Max(m, depth[(y * ss + j) * sw + x * ss + i]);
                        iz[y * w + x] = m;
                    }
                });
                var cam = new[] { (float)cx, (float)cy, (float)cz, (float)fx, (float)fy, (float)fz, (float)rx, (float)ry, (float)rz,
                                  (float)ux, (float)uy, (float)uz, (float)(focal / ss) };
                over = Overlay(iz, w, h, cam);
            }
            if (ShowSlicePlane && SliceData != null && Lut != null) DrawSlicePlane();
            if (Lines != null && Lut != null) foreach (var l in Lines) PolyLine(l);
            DrawFrame();
            if (SliceAxis >= 0) DrawSlice();

            var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            var row = new int[w];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (ss == 1) { row[x] = color[y * sw + x]; continue; }
                    int r = 0, g = 0, b = 0;
                    for (int j = 0; j < ss; j++)
                        for (int i = 0; i < ss; i++)
                        {
                            int c = color[(y * ss + j) * sw + x * ss + i];
                            r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255;
                        }
                    int n = ss * ss;
                    row[x] = (255 << 24) | ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
                if (over != null)
                    for (int x = 0; x < w; x++)
                    {
                        int o = 4 * (y * w + x), a = over[o + 3];
                        if (a == 0 && over[o] == 0) continue;
                        int c = row[x], k = 255 - a;
                        int r = Math.Min(255, over[o] + ((c >> 16) & 255) * k / 255);
                        int g = Math.Min(255, over[o + 1] + ((c >> 8) & 255) * k / 255);
                        int b = Math.Min(255, over[o + 2] + (c & 255) * k / 255);
                        row[x] = (255 << 24) | (r << 16) | (g << 8) | b;
                    }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
            }
            bmp.UnlockBits(data);
            return bmp;
        }

        void SetupCamera()
        {
            double size = Focus != null ? Focus[3] : Math.Sqrt((double)NX * NX + NY * NY + NZ * NZ);
            double cx0 = Focus != null ? Focus[0] : NX / 2.0, cy0 = Focus != null ? Focus[1] : NY / 2.0, cz0 = Focus != null ? Focus[2] : NZ / 2.0;
            double dist = size * 1.4 * Distance;
            // Blickziel: Kanalmitte, verschoben in der Bildebene
            double ox = Math.Cos(Pitch) * Math.Cos(Yaw), oy = Math.Cos(Pitch) * Math.Sin(Yaw), oz = Math.Sin(Pitch);
            fx = -ox; fy = -oy; fz = -oz;
            rx = fy; ry = -fx; rz = 0;   // rechts = vorwärts x oben(0,0,1)
            double rl = Math.Sqrt(rx * rx + ry * ry); if (rl < 1e-9) { rx = 1; ry = 0; rl = 1; }
            rx /= rl; ry /= rl;
            ux = ry * fz - rz * fy; uy = rz * fx - rx * fz; uz = rx * fy - ry * fx;
            tx = cx0 + (rx * PanX + ux * PanY) * size;
            ty = cy0 + (ry * PanX + uy * PanY) * size;
            tz = cz0 + (rz * PanX + uz * PanY) * size;
            cx = tx + ox * dist; cy = ty + oy * dist; cz = tz + oz * dist;
            focal = Math.Min(sw, sh * 1.6) * 0.95;
        }

        /// <summary>Weltpunkt -> Bildpunkt; Rückgabe false, wenn der Punkt hinter der Kamera liegt.</summary>
        bool Project(double x, double y, double z, out float px, out float py, out float iz)
        {
            double vx = x - cx, vy = y - cy, vz = z - cz;
            double zc = vx * fx + vy * fy + vz * fz;
            px = py = iz = 0;
            if (zc < 1) return false;
            double xc = vx * rx + vy * ry + vz * rz, yc = vx * ux + vy * uy + vz * uz;
            px = (float)(sw / 2.0 + focal * xc / zc);
            py = (float)(sh / 2.0 - focal * yc / zc);
            iz = (float)(1.0 / zc);
            return true;
        }

        // ------------------------------------------------------------ Dreiecke

        void DrawMesh(float[] t)
        {
            int n = t.Length / 9;
            var scr = new float[n * 9];      // je Ecke px, py, 1/z
            var col = new int[n];
            var ok = new bool[n];
            // Licht kommt von oben links vorne, relativ zur Kamera
            double lx = -fx * 0.55 - rx * 0.45 + ux * 0.7, ly = -fy * 0.55 - ry * 0.45 + uy * 0.7, lz = -fz * 0.55 - rz * 0.45 + uz * 0.7;
            double ll = Math.Sqrt(lx * lx + ly * ly + lz * lz); lx /= ll; ly /= ll; lz /= ll;
            Parallel.For(0, n, i =>
            {
                int o = i * 9;
                bool all = true;
                for (int k = 0; k < 3; k++)
                {
                    float px, py, iz;
                    all &= Project(t[o + 3 * k], t[o + 3 * k + 1], t[o + 3 * k + 2], out px, out py, out iz);
                    scr[o + 3 * k] = px; scr[o + 3 * k + 1] = py; scr[o + 3 * k + 2] = iz;
                }
                ok[i] = all;
                if (!all) return;
                double ax = t[o + 3] - t[o], ay = t[o + 4] - t[o + 1], az = t[o + 5] - t[o + 2];
                double bx = t[o + 6] - t[o], by = t[o + 7] - t[o + 1], bz = t[o + 8] - t[o + 2];
                double nx = ay * bz - az * by, ny = az * bx - ax * bz, nz = ax * by - ay * bx;
                double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (nl < 1e-20) { ok[i] = false; return; }
                nx /= nl; ny /= nl; nz /= nl;
                // zweiseitig: Normale zur Kamera drehen (STL-Normalen sind oft falsch herum)
                if (nx * fx + ny * fy + nz * fz > 0) { nx = -nx; ny = -ny; nz = -nz; }
                double diff = Math.Max(0, nx * lx + ny * ly + nz * lz);
                double fill = Math.Max(0, nx * -rx * 0.6 + nz * -0.3 + ny * 0.1) * 0.25;   // schwaches Gegenlicht
                double rim = Math.Pow(1 - Math.Abs(nx * fx + ny * fy + nz * fz), 3) * 0.18;
                double k2 = 0.30 + 0.62 * diff + fill + rim;
                col[i] = Shade(Body, k2);
            });

            int bands = Math.Max(1, Math.Min(64, Environment.ProcessorCount * 2));
            Parallel.For(0, bands, b =>
            {
                int y0 = sh * b / bands, y1 = sh * (b + 1) / bands;
                for (int i = 0; i < n; i++)
                    if (ok[i]) RasterTriangle(scr, i * 9, col[i], y0, y1);
            });
        }

        static int Shade(Color c, double k)
        {
            int r = Math.Min(255, (int)(c.R * k)), g = Math.Min(255, (int)(c.G * k)), b = Math.Min(255, (int)(c.B * k));
            return (255 << 24) | (r << 16) | (g << 8) | b;
        }

        void RasterTriangle(float[] s, int o, int c, int y0, int y1)
        {
            float ax = s[o], ay = s[o + 1], az = s[o + 2];
            float bx = s[o + 3], by = s[o + 4], bz = s[o + 5];
            float qx = s[o + 6], qy = s[o + 7], qz = s[o + 8];
            float minY = Math.Min(ay, Math.Min(by, qy)), maxY = Math.Max(ay, Math.Max(by, qy));
            int ya = Math.Max(y0, (int)Math.Ceiling(minY - 0.5f)), yb = Math.Min(y1 - 1, (int)Math.Floor(maxY - 0.5f));
            if (ya > yb) return;
            float minX = Math.Min(ax, Math.Min(bx, qx)), maxX = Math.Max(ax, Math.Max(bx, qx));
            int xa = Math.Max(0, (int)Math.Ceiling(minX - 0.5f)), xb = Math.Min(sw - 1, (int)Math.Floor(maxX - 0.5f));
            if (xa > xb) return;
            float area = (bx - ax) * (qy - ay) - (qx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-8f) return;
            float inv = 1f / area;
            for (int y = ya; y <= yb; y++)
            {
                float py = y + 0.5f;
                int row = y * sw;
                for (int x = xa; x <= xb; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((bx - px) * (qy - py) - (qx - px) * (by - py)) * inv;
                    float w1 = ((qx - px) * (ay - py) - (ax - px) * (qy - py)) * inv;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float iz = w0 * az + w1 * bz + w2 * qz;
                    int p = row + x;
                    if (iz > depth[p]) { depth[p] = iz; color[p] = c; }
                }
            }
        }

        // ------------------------------------------------------------ Linien

        void Line(double x0, double y0, double z0, double x1, double y1, double z1, Color c, float width, bool dashed = false)
        {
            // in Stücke teilen, damit Punkte hinter der Kamera sauber wegfallen
            const int seg = 24;
            int argb = c.ToArgb();
            for (int s = 0; s < seg; s++)
            {
                if (dashed && s % 2 == 1) continue;
                double a = (double)s / seg, b = (double)(s + 1) / seg;
                float ax, ay, az, bx, by, bz;
                if (!Project(x0 + (x1 - x0) * a, y0 + (y1 - y0) * a, z0 + (z1 - z0) * a, out ax, out ay, out az)) continue;
                if (!Project(x0 + (x1 - x0) * b, y0 + (y1 - y0) * b, z0 + (z1 - z0) * b, out bx, out by, out bz)) continue;
                Segment(ax, ay, az, bx, by, bz, argb, width * ss);
            }
        }

        void Segment(float ax, float ay, float az, float bx, float by, float bz, int c, float width)
        {
            float dx = bx - ax, dy = by - ay;
            int steps = (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy)));
            if (steps < 1) steps = 1;
            int r = Math.Max(0, (int)(width / 2));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                int x = (int)(ax + dx * t), y = (int)(ay + dy * t);
                float iz = (az + (bz - az) * t) * 1.002f;   // leicht nach vorn, damit Linien auf Flächen sichtbar bleiben
                for (int j = -r; j <= r; j++)
                    for (int k = -r; k <= r; k++)
                    {
                        int px = x + k, py = y + j;
                        if (px < 0 || py < 0 || px >= sw || py >= sh) continue;
                        int p = py * sw + px;
                        if (iz >= depth[p]) color[p] = c;
                    }
            }
        }

        int LutColor(float v) { return Lut[Math.Max(0, Math.Min(255, (int)(v / 1.5f * 255)))]; }

        void PolyLine(float[] l)
        {
            int n = l.Length / 4;
            float px = 0, py = 0, pz = 0;
            bool prev = false;
            for (int i = 0; i < n; i++)
            {
                float x, y, iz;
                bool ok = Project(l[4 * i], l[4 * i + 1], l[4 * i + 2], out x, out y, out iz);
                if (ok && prev) Segment(px, py, pz, x, y, iz, LutColor(l[4 * i + 3]), 1.3f * ss);
                px = x; py = y; pz = iz; prev = ok;
            }
        }



        /// <summary>Schnittebene als Fläche mit Farben im Raum, leicht durchsichtig; feste Zellen bleiben frei.</summary>
        void DrawSlicePlane()
        {
            double X = NX, Y = NY, Z = NZ;
            double[][] q;
            int tw = NX, th = SliceAxis == 0 ? NZ : NY;
            if (SliceData.Length != tw * th) return;
            if (SliceAxis == 0) { double y = SliceIndex + 0.5; q = new[] { new[] { 0, y, 0 }, new[] { X, y, 0 }, new[] { X, y, Z }, new[] { 0, y, Z } }; }
            else { double z = SliceIndex + 0.5; q = new[] { new[] { 0, 0, z }, new[] { X, 0, z }, new[] { X, Y, z }, new[] { 0, Y, z } }; }
            var uv = new[] { new[] { 0f, 0f }, new[] { 1f, 0f }, new[] { 1f, 1f }, new[] { 0f, 1f } };
            var s = new float[12];
            for (int k = 0; k < 4; k++)
                if (!Project(q[k][0], q[k][1], q[k][2], out s[3 * k], out s[3 * k + 1], out s[3 * k + 2])) return;
            int bands = Math.Max(1, Math.Min(64, Environment.ProcessorCount * 2));
            Parallel.For(0, bands, b =>
            {
                int y0 = sh * b / bands, y1 = sh * (b + 1) / bands;
                TexTri(s, uv, 0, 1, 2, tw, th, y0, y1);
                TexTri(s, uv, 0, 2, 3, tw, th, y0, y1);
            });
        }

        void TexTri(float[] s, float[][] uv, int ia, int ib, int ic, int tw, int th, int y0, int y1)
        {
            float ax = s[3 * ia], ay = s[3 * ia + 1], az = s[3 * ia + 2];
            float bx = s[3 * ib], by = s[3 * ib + 1], bz = s[3 * ib + 2];
            float qx = s[3 * ic], qy = s[3 * ic + 1], qz = s[3 * ic + 2];
            float area = (bx - ax) * (qy - ay) - (qx - ax) * (by - ay);
            if (Math.Abs(area) < 1e-6f) return;
            float inv = 1f / area;
            int ya = Math.Max(y0, (int)Math.Ceiling(Math.Min(ay, Math.Min(by, qy)) - 0.5f));
            int yb = Math.Min(y1 - 1, (int)Math.Floor(Math.Max(ay, Math.Max(by, qy)) - 0.5f));
            int xa = Math.Max(0, (int)Math.Ceiling(Math.Min(ax, Math.Min(bx, qx)) - 0.5f));
            int xb = Math.Min(sw - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, qx)) - 0.5f));
            for (int y = ya; y <= yb; y++)
            {
                float py = y + 0.5f;
                for (int x = xa; x <= xb; x++)
                {
                    float px = x + 0.5f;
                    float w0 = ((bx - px) * (qy - py) - (qx - px) * (by - py)) * inv;
                    float w1 = ((qx - px) * (ay - py) - (ax - px) * (qy - py)) * inv;
                    float w2 = 1 - w0 - w1;
                    if (w0 < 0 || w1 < 0 || w2 < 0) continue;
                    float iz = w0 * az + w1 * bz + w2 * qz;
                    int p = y * sw + x;
                    if (iz <= depth[p]) continue;
                    // perspektivisch richtig: Texturkoordinaten mit 1/z gewichten
                    float u = (w0 * uv[ia][0] * az + w1 * uv[ib][0] * bz + w2 * uv[ic][0] * qz) / iz;
                    float v = (w0 * uv[ia][1] * az + w1 * uv[ib][1] * bz + w2 * uv[ic][1] * qz) / iz;
                    int tx = Math.Min(tw - 1, Math.Max(0, (int)(u * tw))), ty = Math.Min(th - 1, Math.Max(0, (int)(v * th)));
                    float val = SliceData[ty * tw + tx];
                    if (val < 0) continue;   // im Körper
                    int c = LutColor(val), o = color[p];
                    // 85 % deckend
                    int r = (((c >> 16) & 255) * 85 + ((o >> 16) & 255) * 15) / 100;
                    int g = (((c >> 8) & 255) * 85 + ((o >> 8) & 255) * 15) / 100;
                    int bl = ((c & 255) * 85 + (o & 255) * 15) / 100;
                    color[p] = (255 << 24) | (r << 16) | (g << 8) | bl;
                    depth[p] = iz;
                }
            }
        }

        void DrawFrame()
        {
            double X = NX, Y = NY, Z = NZ;
            var e = new[,]
            {
                { 0, 0, 0, X, 0, 0 }, { 0, Y, 0, X, Y, 0 }, { 0, 0, Z, X, 0, Z }, { 0, Y, Z, X, Y, Z },
                { 0, 0, 0, 0, Y, 0 }, { 0, 0, Z, 0, Y, Z }, { 0, 0, 0, 0, 0, Z }, { 0, Y, 0, 0, Y, Z },
                { X, 0, 0, X, Y, 0 }, { X, 0, Z, X, Y, Z }, { X, 0, 0, X, 0, Z }, { X, Y, 0, X, Y, Z },
            };
            for (int i = 0; i < 12; i++) Line(e[i, 0], e[i, 1], e[i, 2], e[i, 3], e[i, 4], e[i, 5], Frame, 1.2f);
            // Strömungspfeil vor dem Einlass
            double y = NY / 2.0, z = NZ * 0.5, len = NX * 0.12;
            Line(-len * 1.3, y, z, -len * 0.25, y, z, SliceColor, 2.0f);
            Line(-len * 0.25, y, z, -len * 0.45, y - len * 0.14, z, SliceColor, 2.0f);
            Line(-len * 0.25, y, z, -len * 0.45, y + len * 0.14, z, SliceColor, 2.0f);
        }

        void DrawFloor()
        {
            int step = Math.Max(8, (int)Math.Round(NY / 8.0));
            for (int x = 0; x <= NX; x += step) Line(x, 0, 0, x, NY, 0, Floor, 1f);
            for (int y = 0; y <= NY; y += step) Line(0, y, 0, NX, y, 0, Floor, 1f);
        }

        void DrawSlice()
        {
            double X = NX, Y = NY, Z = NZ;
            if (SliceAxis == 0)
            {
                double y = SliceIndex + 0.5;
                Line(0, y, 0, X, y, 0, SliceColor, 1.4f, true); Line(0, y, Z, X, y, Z, SliceColor, 1.4f, true);
                Line(0, y, 0, 0, y, Z, SliceColor, 1.4f, true); Line(X, y, 0, X, y, Z, SliceColor, 1.4f, true);
            }
            else
            {
                double z = SliceIndex + 0.5;
                Line(0, 0, z, X, 0, z, SliceColor, 1.4f, true); Line(0, Y, z, X, Y, z, SliceColor, 1.4f, true);
                Line(0, 0, z, 0, Y, z, SliceColor, 1.4f, true); Line(X, 0, z, X, Y, z, SliceColor, 1.4f, true);
            }
        }
    }
}
