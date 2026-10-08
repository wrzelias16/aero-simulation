using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Windkanal
{
    public enum ViewMode { Geschwindigkeit, Druck, Wirbel, Rauch }

    /// <summary>Rauchpartikel, die wie im echten Windkanal von einem Rechen am Einlass ausgestoßen werden.</summary>
    public sealed class Particles
    {
        public readonly float[] X, Y, Age;
        public int Count;
        public readonly int Emitters;
        readonly int cap;
        float emitAcc;

        public Particles(int nx, int ny)
        {
            Emitters = Math.Max(16, ny / 9);
            cap = Emitters * nx * 3;
            X = new float[cap]; Y = new float[cap]; Age = new float[cap];
        }

        public void Clear() { Count = 0; emitAcc = 0; }

        static void Sample(Solver s, float x, float y, out float u, out float v)
        {
            int nx = s.NX, ny = s.NY;
            if (x < 0) x = 0; if (x > nx - 1.001f) x = nx - 1.001f;
            if (y < 0) y = 0; if (y > ny - 1.001f) y = ny - 1.001f;
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            int c = y0 * nx + x0;
            float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
            u = s.Ux[c] * w00 + s.Ux[c + 1] * w10 + s.Ux[c + nx] * w01 + s.Ux[c + nx + 1] * w11;
            v = s.Uy[c] * w00 + s.Uy[c + 1] * w10 + s.Uy[c + nx] * w01 + s.Uy[c + nx + 1] * w11;
        }

        public void Update(Solver s, float dt, float maxAge)
        {
            // Bei vielen Rechenschritten pro Bild (GPU) in Teilschritten bewegen, damit ein Partikel
            // pro Teilschritt höchstens etwa eine Zelle weit fliegt und der Strömung genau folgt.
            float maxSub = 1.5f / Math.Max(1e-3f, s.U0);
            int subs = Math.Max(1, (int)Math.Ceiling(dt / maxSub));
            for (int k = 0; k < subs; k++) Advance(s, dt / subs, maxAge);
        }

        void Advance(Solver s, float dt, float maxAge)
        {
            int nx = s.NX, ny = s.NY;
            float[] X = this.X, Y = this.Y, Age = this.Age;
            Parallel.For(0, (Count + 2047) / 2048, chunk =>
            {
                int end = Math.Min(Count, (chunk + 1) * 2048);
                for (int i = chunk * 2048; i < end; i++)
                {
                    float x = X[i], y = Y[i], u1, v1, u2, v2;
                    Sample(s, x, y, out u1, out v1);
                    Sample(s, x + 0.5f * dt * u1, y + 0.5f * dt * v1, out u2, out v2);
                    x += dt * u2; y += dt * v2;
                    float age = Age[i] + dt;
                    int cx = (int)(x + 0.5f), cy = (int)(y + 0.5f);
                    bool dead = x < 0 || x >= s.VisibleNX - 1 || y < 0 || y >= ny - 1 || age > maxAge
                                || s.Solid[Math.Min(ny - 1, cy) * nx + Math.Min(nx - 1, cx)];
                    X[i] = x; Y[i] = y; Age[i] = dead ? float.NaN : age;
                }
            });
            int alive = 0;
            for (int i = 0; i < Count; i++)
            {
                if (float.IsNaN(Age[i])) continue;
                X[alive] = X[i]; Y[alive] = Y[i]; Age[alive] = Age[i];
                alive++;
            }
            Count = alive;

            emitAcc += s.U0 * dt;
            const float spacing = 0.6f;
            while (emitAcc >= spacing)
            {
                emitAcc -= spacing;
                for (int e = 0; e < Emitters && Count < cap; e++)
                {
                    X[Count] = 1f + emitAcc;
                    Y[Count] = (e + 0.5f) * ny / Emitters;
                    Age[Count] = 0;
                    Count++;
                }
            }
        }
    }

    /// <summary>Speichert den zeitlichen Verlauf von cw und ca und wertet ihn aus.</summary>
    public sealed class ForceStats
    {
        readonly float[] cd, cl;
        int head, count;

        public ForceStats(int capacity) { cd = new float[capacity]; cl = new float[capacity]; }
        public int Count { get { return count; } }
        public void Clear() { head = 0; count = 0; }

        public void Add(float cdv, float clv)
        {
            cd[head] = cdv; cl[head] = clv;
            head = (head + 1) % cd.Length;
            if (count < cd.Length) count++;
        }

        /// <summary>back = 0 ist der neueste Wert.</summary>
        public float Cd(int back) { return cd[(head - 1 - back + 2 * cd.Length) % cd.Length]; }
        public float Cl(int back) { return cl[(head - 1 - back + 2 * cd.Length) % cd.Length]; }

        /// <summary>
        /// Mittelwerte über die letzten 'window' Schritte und Ablösefrequenz (pro Schritt, 0 = keine).
        /// Für die Frequenz wird ca vorher über 'smooth' Schritte geglättet (filtert Druckwellen-Rauschen).
        /// </summary>
        public void Analyze(int window, int smooth, out double meanCd, out double meanCl, out double freq)
        {
            window = Math.Min(window, count);
            meanCd = 0; meanCl = 0; freq = 0;
            if (window < 2) return;
            for (int k = 0; k < window; k++) { meanCd += Cd(k); meanCl += Cl(k); }
            meanCd /= window; meanCl /= window;

            // geglättete Zeitreihe (alt -> neu), gleitender Mittelwert
            smooth = Math.Max(1, Math.Min(smooth, window / 4));
            var sm = new double[window];
            double run = 0;
            for (int t = 0; t < window; t++)
            {
                run += Cl(window - 1 - t) - meanCl;
                if (t >= smooth) run -= Cl(window - 1 - (t - smooth)) - meanCl;
                sm[t] = run / Math.Min(t + 1, smooth);
            }
            double sq = 0;
            for (int t = smooth; t < window; t++) sq += sm[t] * sm[t];
            double std = Math.Sqrt(sq / Math.Max(1, window - smooth));
            if (std < 2e-3) return;
            double hyst = 0.3 * std;
            int crossings = 0, first = -1, last = -1;
            bool below = false;
            for (int t = smooth; t < window; t++)
            {
                if (sm[t] < -hyst) below = true;
                else if (sm[t] > hyst && below)
                {
                    below = false;
                    crossings++;
                    if (first < 0) first = t;
                    last = t;
                }
            }
            if (crossings >= 3) freq = (crossings - 1) / (double)(last - first);
        }
    }

    /// <summary>Zeichnet das Strömungsfeld mit Farbskalen und Rauch in ein Bitmap.</summary>
    public sealed class Renderer
    {
        public Bitmap Bitmap;
        int w, h;
        float[] field;
        float[] solidF;
        readonly int[] lutSpeed, lutPressure, lutVort;
        public float Scale, OffX, OffY;

        public static int BgColor = Rgb(18, 21, 26);   // wird vom Design (hell/dunkel) gesetzt
        const int SolidR = 205, SolidG = 209, SolidB = 216;
        // Rauchansicht: weißer Rauch auf fast schwarzem Grund, Körper dunkelgrau, damit er sich vom Rauch abhebt
        const int SmokeBgR = 10, SmokeBgG = 12, SmokeBgB = 16;
        const int SmokeR = 238, SmokeG = 241, SmokeB = 245;
        const int SmokeSolidR = 58, SmokeSolidG = 63, SmokeSolidB = 72;
        readonly int[] lutSmoke;

        public Renderer()
        {
            lutSpeed = BuildLut(new float[,] {
                { 0.00f, 48, 18, 59 }, { 0.13f, 70, 107, 227 }, { 0.25f, 40, 170, 250 }, { 0.38f, 26, 228, 182 },
                { 0.50f, 106, 253, 98 }, { 0.63f, 196, 240, 52 }, { 0.75f, 251, 185, 56 }, { 0.88f, 237, 97, 23 },
                { 1.00f, 122, 4, 3 } });
            lutPressure = BuildLut(new float[,] {
                { 0.00f, 33, 76, 160 }, { 0.25f, 103, 169, 207 }, { 0.50f, 247, 247, 247 },
                { 0.75f, 239, 138, 98 }, { 1.00f, 178, 24, 43 } });
            lutVort = BuildLut(new float[,] {
                { 0.00f, 120, 200, 255 }, { 0.30f, 30, 90, 190 }, { 0.50f, 14, 16, 22 },
                { 0.70f, 190, 45, 45 }, { 1.00f, 255, 190, 110 } });
            // weiche Deckkraft: dünner Rauch bleibt als Schleier sichtbar, dichter Rauch sättigt sanft
            lutSmoke = new int[256];
            for (int i = 0; i < 256; i++)
            {
                double a = (1 - Math.Exp(-2.4 * i / 255.0)) / (1 - Math.Exp(-2.4));
                lutSmoke[i] = Rgb((int)(SmokeBgR + (SmokeR - SmokeBgR) * a), (int)(SmokeBgG + (SmokeG - SmokeBgG) * a),
                                  (int)(SmokeBgB + (SmokeB - SmokeBgB) * a));
            }
        }

        static int Rgb(int r, int g, int b) { return (255 << 24) | (r << 16) | (g << 8) | b; }

        static int[] BuildLut(float[,] stops)
        {
            var lut = new int[256];
            int n = stops.GetLength(0);
            for (int i = 0; i < 256; i++)
            {
                float t = i / 255f;
                int k = 0;
                while (k < n - 2 && t > stops[k + 1, 0]) k++;
                float t0 = stops[k, 0], t1 = stops[k + 1, 0];
                float f = Math.Max(0, Math.Min(1, (t - t0) / (t1 - t0)));
                int r = (int)(stops[k, 1] + f * (stops[k + 1, 1] - stops[k, 1]));
                int g = (int)(stops[k, 2] + f * (stops[k + 1, 2] - stops[k, 2]));
                int b = (int)(stops[k, 3] + f * (stops[k + 1, 3] - stops[k, 3]));
                lut[i] = Rgb(r, g, b);
            }
            return lut;
        }

        public static Color LutColor(ViewMode mode, float t, Renderer r)
        {
            int[] lut = mode == ViewMode.Druck ? r.lutPressure : mode == ViewMode.Wirbel ? r.lutVort
                      : mode == ViewMode.Rauch ? r.lutSmoke : r.lutSpeed;
            int v = lut[Math.Max(0, Math.Min(255, (int)(t * 255)))];
            return Color.FromArgb(v);
        }

        public void EnsureSize(int width, int height)
        {
            width = Math.Max(1, width); height = Math.Max(1, height);
            if (Bitmap != null && w == width && h == height) return;
            if (Bitmap != null) Bitmap.Dispose();
            w = width; h = height;
            Bitmap = new Bitmap(w, h, PixelFormat.Format32bppRgb);
        }

        /// <summary>Bildschirm -> Gitterkoordinaten (y nach oben).</summary>
        public PointF ToGrid(Solver s, int px, int py)
        {
            float gx = (px - OffX) / Scale - 0.5f;
            float gy = s.NY - 1 - ((py - OffY) / Scale - 0.5f);
            return new PointF(gx, gy);
        }

        // Körpermaske als Zahlen nur neu, wenn sich die Maske geändert hat
        Solver solidOf;
        int solidVersion;
        // je Bildspalte: Gitterspalte (-1 = Hintergrund) und Gewicht, gilt für Breite/Maßstab/Versatz unten
        int[] colX0;
        float[] colFx;
        int colW, colVnx;
        float colScale = float.NaN, colOffX;

        public unsafe void Render(Solver s, Particles particles, ViewMode mode, bool smoke, float refLen)
        {
            int nx = s.NX, ny = s.NY, n = s.N, vnx = s.VisibleNX;
            if (field == null || field.Length != n) { field = new float[n]; solidF = new float[n]; solidOf = null; }
            Scale = Math.Min(w / (float)vnx, h / (float)ny);
            OffX = (w - vnx * Scale) / 2f;
            OffY = (h - ny * Scale) / 2f;

            float u0 = s.U0;
            float rhoInf = s.RhoInf;
            float q = 0.5f * u0 * u0;
            float vortScale = refLen / u0;
            float[] fld = field, sol = solidF;
            if (solidOf != s || solidVersion != s.MaskVersion)
            {
                bool[] solid = s.Solid;
                for (int c = 0; c < n; c++) sol[c] = solid[c] ? 1f : 0f;
                solidOf = s; solidVersion = s.MaskVersion;
            }
            // Farbwerte nur für den sichtbaren Teil (die Bildpunkte lesen höchstens Spalte vnx - 1)
            Parallel.For(0, ny, y =>
            {
                for (int x = 0; x < vnx; x++)
                {
                    int c = y * nx + x;
                    float t;
                    switch (mode)
                    {
                        case ViewMode.Druck:
                            {
                                float cp = (s.Rho[c] - rhoInf) / 3f / q;
                                t = cp >= 0 ? 0.5f + 0.5f * cp : 0.5f + 0.25f * cp;
                                break;
                            }
                        case ViewMode.Rauch:
                            t = s.Smoke[c];
                            break;
                        case ViewMode.Wirbel:
                            {
                                int xm = Math.Max(0, x - 1), xp = Math.Min(nx - 1, x + 1);
                                int ym = Math.Max(0, y - 1), yp = Math.Min(ny - 1, y + 1);
                                float om = (s.Uy[y * nx + xp] - s.Uy[y * nx + xm]) / (xp - xm)
                                         - (s.Ux[yp * nx + x] - s.Ux[ym * nx + x]) / (yp - ym);
                                t = 0.5f + 0.5f * om * vortScale / 6f;
                                break;
                            }
                        default:
                            {
                                float sp = (float)Math.Sqrt(s.Ux[c] * s.Ux[c] + s.Uy[c] * s.Uy[c]);
                                t = 0.5f * sp / u0;
                                break;
                            }
                    }
                    fld[c] = t < 0 ? 0 : t > 1 ? 1 : t;
                }
            });

            int[] lut = mode == ViewMode.Druck ? lutPressure : mode == ViewMode.Wirbel ? lutVort
                      : mode == ViewMode.Rauch ? lutSmoke : lutSpeed;
            bool fieldOn = mode != ViewMode.Rauch;
            int solR = fieldOn ? SolidR : SmokeSolidR, solG = fieldOn ? SolidG : SmokeSolidG, solB = fieldOn ? SolidB : SmokeSolidB;
            float scale = Scale, offX = OffX, offY = OffY;
            int width = w;
            if (colX0 == null || colW != w || colVnx != vnx || !colScale.Equals(scale) || !colOffX.Equals(offX))
            {
                // Spaltenlage hängt nur von Breite und Maßstab ab: einmal rechnen statt für jeden Bildpunkt
                colX0 = new int[w]; colFx = new float[w];
                for (int pxi = 0; pxi < w; pxi++)
                {
                    float gx = (pxi - offX + 0.5f) / scale - 0.5f;
                    if (gx < -0.5f || gx > vnx - 0.5f) { colX0[pxi] = -1; continue; }
                    if (gx < 0) gx = 0; if (gx > vnx - 1.001f) gx = vnx - 1.001f;
                    int x0 = (int)gx;
                    colX0[pxi] = x0; colFx[pxi] = gx - x0;
                }
                colW = w; colVnx = vnx; colScale = scale; colOffX = offX;
            }
            int[] cx0 = colX0;
            float[] cfx = colFx;
            // direkt in das Bild schreiben (32 bit je Punkt, Zeilen ohne Lücke)
            var data = Bitmap.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
            IntPtr scan0 = data.Scan0;
            int stride = data.Stride / 4;
            Parallel.For(0, h, py =>
            {
                int* px = (int*)scan0;
                int row = py * stride;
                float gy = ny - 1 - ((py - offY + 0.5f) / scale - 0.5f);
                if (gy < -0.5f || gy > ny - 0.5f)
                {
                    for (int x = 0; x < width; x++) px[row + x] = BgColor;
                    return;
                }
                if (gy < 0) gy = 0; if (gy > ny - 1.001f) gy = ny - 1.001f;
                int y0 = (int)gy; float fy = gy - y0;
                for (int pxi = 0; pxi < width; pxi++)
                {
                    int x0 = cx0[pxi];
                    if (x0 < 0) { px[row + pxi] = BgColor; continue; }
                    float fx = cfx[pxi];
                    int c = y0 * nx + x0;
                    float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
                    float sf = sol[c] * w00 + sol[c + 1] * w10 + sol[c + nx] * w01 + sol[c + nx + 1] * w11;
                    float t = fld[c] * w00 + fld[c + 1] * w10 + fld[c + nx] * w01 + fld[c + nx + 1] * w11;
                    int col = lut[(int)(t * 255f)];
                    if (sf > 0.01f)
                    {
                        float a = sf < 0.35f ? 0f : sf > 0.65f ? 1f : (sf - 0.35f) / 0.3f;
                        int r = (col >> 16) & 255, g = (col >> 8) & 255, b = col & 255;
                        r = (int)(r + (solR - r) * a); g = (int)(g + (solG - g) * a); b = (int)(b + (solB - b) * a);
                        col = Rgb(r, g, b);
                    }
                    px[row + pxi] = col;
                }
            });

            if (smoke && fieldOn && particles != null)
            {
                int* px = (int*)scan0;
                const float alpha = 0.45f;
                int dot = scale >= 2.5f ? 2 : 1;
                for (int i = 0; i < particles.Count; i++)
                {
                    int sx = (int)(offX + (particles.X[i] + 0.5f) * scale);
                    int sy = (int)(offY + (ny - 1 - particles.Y[i] + 0.5f) * scale);
                    for (int dy = 0; dy < dot; dy++)
                        for (int dx = 0; dx < dot; dx++)
                        {
                            int xx = sx + dx, yy = sy + dy;
                            if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                            int idx = yy * stride + xx;
                            int col = px[idx];
                            int r = (col >> 16) & 255, g = (col >> 8) & 255, b = col & 255;
                            r = (int)(r + (255 - r) * alpha); g = (int)(g + (255 - g) * alpha); b = (int)(b + (250 - b) * alpha);
                            px[idx] = Rgb(r, g, b);
                        }
                }
            }

            Bitmap.UnlockBits(data);
        }
    }
}
