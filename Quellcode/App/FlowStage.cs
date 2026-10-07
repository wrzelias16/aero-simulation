using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Windkanal
{
    /// <summary>
    /// Kleine Strömungsanimation für Startfenster und Installer: Zylinder in Anströmung mit Kármánscher Wirbelstraße
    /// (Potentialströmung um den Zylinder plus mitschwimmende Wirbel mit weichem Kern), eingefärbt mit derselben
    /// Geschwindigkeits-Farbskala wie das Programm, dazu Rauchteilchen, die mit dieser Strömung mitfließen.
    /// Rein zur Schau, keine Simulation – rechnet pro Bild nur ein kleines Raster und ist darum sehr schnell.
    /// </summary>
    sealed class FlowStage
    {
        const int GW = 160, GH = 60;            // Raster der Farbfläche
        const float U = 1f, A = 0.30f;          // Anströmung und Zylinderradius (Einheiten der Bühne)
        const float CX = 1.45f;                 // Lage des Zylinders
        const float Spacing = 1.05f, Row = 0.21f, Gamma = 1.9f, Core = 0.22f, Drift = 0.82f;
        const int Vortices = 10;

        static readonly int[] lut = BuildLut();
        readonly Bitmap field = new Bitmap(GW, GH, PixelFormat.Format32bppRgb);
        readonly int[] px = new int[GW * GH];
        readonly float[] vx = new float[Vortices], vy = new float[Vortices], vg = new float[Vortices];
        readonly float[] PX, PY, OX, OY;
        readonly Random rnd = new Random(3);
        float lastT = -1, width, height, time;
        bool seeded;   // Teilchen beim ersten Bild über die ganze Breite verteilen (sonst wandern alle als eine Front)

        public FlowStage(int particles = 420)
        {
            PX = new float[particles]; PY = new float[particles]; OX = new float[particles]; OY = new float[particles];
            for (int i = 0; i < particles; i++) PX[i] = -1;   // werden beim ersten Bild verteilt
        }

        static int[] BuildLut()
        {
            float[,] s = {
                { 0.00f, 48, 18, 59 }, { 0.13f, 70, 107, 227 }, { 0.25f, 40, 170, 250 }, { 0.38f, 26, 228, 182 },
                { 0.50f, 106, 253, 98 }, { 0.63f, 196, 240, 52 }, { 0.75f, 251, 185, 56 }, { 0.88f, 237, 97, 23 },
                { 1.00f, 122, 4, 3 } };
            var l = new int[256];
            int n = s.GetLength(0);
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                int k = 0;
                while (k < n - 2 && t > s[k + 1, 0]) k++;
                float f = Math.Max(0, Math.Min(1, (t - s[k, 0]) / (s[k + 1, 0] - s[k, 0])));
                l[i] = (255 << 24) | ((int)(s[k, 1] + f * (s[k + 1, 1] - s[k, 1])) << 16)
                     | ((int)(s[k, 2] + f * (s[k + 1, 2] - s[k, 2])) << 8) | (int)(s[k, 3] + f * (s[k + 1, 3] - s[k, 3]));
            }
            return l;
        }

        /// <summary>Lage der Wirbel zur Zeit t: sie lösen sich abwechselnd oben und unten ab und schwimmen mit.</summary>
        void PlaceVortices(float t)
        {
            float start = CX + A + 0.25f, len = Spacing * Vortices / 2f;
            for (int k = 0; k < Vortices; k++)
            {
                float d = (t * Drift * U + k * Spacing / 2f) % len;
                vx[k] = start + d;
                vy[k] = (k % 2 == 0 ? Row : -Row) * (0.6f + 0.4f * Math.Min(1, d / 1.2f));
                // frisch abgelöste Wirbel wachsen an, weit hinten verblassen sie
                float grow = Math.Min(1, d / 0.6f), fade = Math.Max(0, 1 - d / len);
                vg[k] = (k % 2 == 0 ? -Gamma : Gamma) * grow * (0.35f + 0.65f * fade);
            }
        }

        void Velocity(float x, float y, out float u, out float v)
        {
            // Potentialströmung um den Zylinder: u - i v = U (1 - a² / z²)
            float zx = x - CX, zy = y, r2 = zx * zx + zy * zy;
            if (r2 < 1e-6f) { u = v = 0; return; }
            float a2 = A * A, inv = 1f / (r2 * r2);
            float re = (zx * zx - zy * zy) * inv, im = -2 * zx * zy * inv;   // 1 / z²
            u = U * (1 - a2 * re);
            v = U * (a2 * im);
            for (int k = 0; k < Vortices; k++)
            {
                float dx = x - vx[k], dy = y - vy[k], d2 = dx * dx + dy * dy + 1e-4f;
                float f = vg[k] / (2 * (float)Math.PI * d2) * (1 - (float)Math.Exp(-d2 / (Core * Core)));
                u += -f * dy; v += f * dx;
            }
            // pendelnder Nachlauf: langsame Zone hinter dem Zylinder, die mit der Wirbelablösung hin und her schwingt
            float behind = x - CX;
            if (behind > 0)
            {
                float on = Math.Min(1, behind / (1.2f * A));
                float w = 0.17f + 0.05f * behind, amp = 0.8f * (float)Math.Exp(-behind / 4.5f);
                float yc = 0.17f * Math.Min(1, behind / 0.9f) * (float)Math.Sin(2 * Math.PI * (behind / Spacing - time * Drift * U / Spacing));
                float g = (y - yc) / w;
                u -= U * amp * on * (float)Math.Exp(-g * g);
            }
        }

        /// <summary>Bühne in das Rechteck r zeichnen (mit runden Ecken), Zeit t in Sekunden.</summary>
        public void Draw(Graphics g, RectangleF r, float t, float radius, Color solid)
        {
            width = 5.6f; height = width * r.Height / r.Width;
            time = t;
            PlaceVortices(t);
            // Farbfläche: Geschwindigkeitsbetrag / U auf 0 … 2
            int solidArgb = solid.ToArgb();
            for (int j = 0; j < GH; j++)
            {
                float y = (0.5f - (j + 0.5f) / GH) * height;
                for (int i = 0; i < GW; i++)
                {
                    float x = (i + 0.5f) / GW * width;
                    float dx = x - CX;
                    if (dx * dx + y * y < A * A) { px[j * GW + i] = solidArgb; continue; }
                    float u, v;
                    Velocity(x, y, out u, out v);
                    float sp = (float)Math.Sqrt(u * u + v * v) / U / 2f;
                    px[j * GW + i] = lut[Math.Max(0, Math.Min(255, (int)(sp * 255)))];
                }
            }
            var d = field.LockBits(new Rectangle(0, 0, GW, GH), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            for (int j = 0; j < GH; j++) Marshal.Copy(px, j * GW, d.Scan0 + j * d.Stride, GW);
            field.UnlockBits(d);

            var state = g.Save();
            using (var path = Theme.Round(r, radius))
            {
                g.SetClip(path);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(field, r);

                // Rauchteilchen mit kurzer Spur
                float dt = lastT < 0 ? 0 : Math.Min(0.05f, t - lastT);
                lastT = t;
                float sx = r.Width / width, sy = r.Height / height;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var pen = new Pen(Color.FromArgb(210, 255, 255, 255), 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < PX.Length; i++)
                    {
                        if (PX[i] < 0 || PX[i] > width)
                        {
                            // neu am Einlass, in Reihen wie ein Rauchrechen
                            PX[i] = !seeded ? (float)rnd.NextDouble() * width : (float)rnd.NextDouble() * 0.15f;
                            PY[i] = ((float)rnd.Next(13) / 12f - 0.5f) * height * 0.92f + (float)(rnd.NextDouble() - 0.5) * 0.02f;
                            OX[i] = PX[i]; OY[i] = PY[i];
                        }
                        float u, v;
                        OX[i] = PX[i]; OY[i] = PY[i];
                        for (int s = 0; s < 2; s++)
                        {
                            Velocity(PX[i], PY[i], out u, out v);
                            PX[i] += u * dt / 2; PY[i] += v * dt / 2;
                        }
                        float ddx = PX[i] - CX;
                        if (ddx * ddx + PY[i] * PY[i] < A * A) { PX[i] = -1; continue; }
                        // Spur: Richtung und Länge aus der Geschwindigkeit (wie ein kurz belichteter Rauchfaden)
                        float x1 = r.X + PX[i] * sx, y1 = r.Y + (height / 2 - PY[i]) * sy;
                        float tu, tv;
                        Velocity(PX[i], PY[i], out tu, out tv);
                        const float trail = 0.09f;
                        g.DrawLine(pen, x1 - tu * trail * sx, y1 + tv * trail * sy, x1, y1);
                    }
                seeded = true;
                // Zylinder mit feinem Rand
                float cx = r.X + CX * sx, cy = r.Y + height / 2 * sy, rr = A * sx;
                using (var b = new SolidBrush(solid)) g.FillEllipse(b, cx - rr, cy - rr, 2 * rr, 2 * rr);
                using (var p = new Pen(Color.FromArgb(90, 0, 0, 0), 1)) g.DrawEllipse(p, cx - rr, cy - rr, 2 * rr, 2 * rr);
            }
            g.Restore(state);
        }
    }
}
