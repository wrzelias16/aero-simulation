using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Windkanal3D
{
    /// <summary>
    /// Grobes Geschwindigkeitsfeld von der Grafikkarte (jede s-te Zelle) mit trilinearer Abfrage
    /// an beliebigen Punkten in Zellkoordinaten.
    /// </summary>
    public sealed class FlowField
    {
        public readonly float[] U;
        public readonly int S, CNX, CNY, CNZ, NX, NY, NZ;
        readonly byte[] solid;

        public FlowField(float[] u, int s, int cnx, int cny, int cnz, int nx, int ny, int nz, byte[] solidFine)
        {
            U = u; S = s; CNX = cnx; CNY = cny; CNZ = cnz; NX = nx; NY = ny; NZ = nz; solid = solidFine;
        }

        public bool Inside(float x, float y, float z)
        {
            return x >= 0 && y >= 0 && z >= 0 && x < NX && y < NY && z < NZ;
        }

        public bool IsSolid(float x, float y, float z)
        {
            if (!Inside(x, y, z)) return false;
            return solid != null && solid[(int)x + NX * ((int)y + NY * (int)z)] != 0;
        }

        /// <summary>Geschwindigkeit am Punkt (x, y, z); Zelle i hat ihren Mittelpunkt bei i + 0,5.</summary>
        public void Sample(float x, float y, float z, out float ux, out float uy, out float uz)
        {
            float gx = Math.Max(0, Math.Min(CNX - 1.001f, (x - 0.5f) / S));
            float gy = Math.Max(0, Math.Min(CNY - 1.001f, (y - 0.5f) / S));
            float gz = Math.Max(0, Math.Min(CNZ - 1.001f, (z - 0.5f) / S));
            int i = (int)gx, j = (int)gy, k = (int)gz;
            float fx = gx - i, fy = gy - j, fz = gz - k;
            int i1 = Math.Min(i + 1, CNX - 1), j1 = Math.Min(j + 1, CNY - 1), k1 = Math.Min(k + 1, CNZ - 1);
            ux = uy = uz = 0;
            for (int c = 0; c < 8; c++)
            {
                int ii = (c & 1) == 0 ? i : i1, jj = (c & 2) == 0 ? j : j1, kk = (c & 4) == 0 ? k : k1;
                float w = ((c & 1) == 0 ? 1 - fx : fx) * ((c & 2) == 0 ? 1 - fy : fy) * ((c & 4) == 0 ? 1 - fz : fz);
                int o = 3 * (ii + CNX * (jj + CNY * kk));
                ux += w * U[o]; uy += w * U[o + 1]; uz += w * U[o + 2];
            }
        }
    }

    /// <summary>Startpunkte vor dem Körper: ein Rechen aus Punkten quer zur Strömung.</summary>
    public static class Rake
    {
        /// <param name="bmin">Körper-Box min (Zellen)</param>
        /// <param name="bmax">Körper-Box max (Zellen)</param>
        public static List<float[]> Build(float[] bmin, float[] bmax, int nx, int ny, int nz, int cols, int rows, bool onGround)
        {
            float w = bmax[1] - bmin[1], h = bmax[2] - bmin[2], len = bmax[0] - bmin[0];
            float x = Math.Max(2, bmin[0] - Math.Max(4, 0.35f * Math.Max(len, Math.Max(w, h))));
            float y0 = Math.Max(1, bmin[1] - 0.6f * w), y1 = Math.Min(ny - 1, bmax[1] + 0.6f * w);
            float z0 = onGround ? 1.0f : Math.Max(1, bmin[2] - 0.5f * h), z1 = Math.Min(nz - 1, bmax[2] + 0.6f * h);
            var pts = new List<float[]>();
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    pts.Add(new[] { x, y0 + (y1 - y0) * (c + 0.5f) / cols, z0 + (z1 - z0) * (r + 0.5f) / rows });
            return pts;
        }
    }

    /// <summary>Rauchrechen wie im echten Windkanal: wenige, klar getrennte Düsen in Kreuzform vor dem Körper.</summary>
    public static class SmokeRake
    {
        /// <summary>Senkrechte Reihe in der Mitte (Strömung über den Körper) und waagrechte Reihe auf halber Höhe (seitlich).</summary>
        public static List<float[]> Build(float[] bmin, float[] bmax, int nx, int ny, int nz, bool onGround)
        {
            float w = bmax[1] - bmin[1], h = bmax[2] - bmin[2], len = bmax[0] - bmin[0];
            float x = Math.Max(2, bmin[0] - Math.Max(4, 0.35f * Math.Max(len, Math.Max(w, h))));
            float yc = (bmin[1] + bmax[1]) / 2, zc = (bmin[2] + bmax[2]) / 2;
            var pts = new List<float[]>();
            float z0 = onGround ? Math.Max(1.5f, bmin[2] + 0.15f * h) : Math.Max(1, bmin[2] - 0.4f * h), z1 = Math.Min(nz - 1, bmax[2] + 0.5f * h);
            const int rows = 7, cols = 9;
            for (int r = 0; r < rows; r++) pts.Add(new[] { x, yc, z0 + (z1 - z0) * r / (rows - 1) });
            float y0 = Math.Max(1, bmin[1] - 0.5f * w), y1 = Math.Min(ny - 1, bmax[1] + 0.5f * w);
            for (int c = 0; c < cols; c++)
            {
                if (c == cols / 2) continue;   // Mitte hat schon die senkrechte Reihe
                pts.Add(new[] { x, y0 + (y1 - y0) * c / (cols - 1), zc });
            }
            return pts;
        }
    }

    public static class Streamlines
    {
        /// <summary>
        /// Stromlinien von den Startpunkten aus (Runge-Kutta 2. Ordnung, feste Schrittweite in Zellen).
        /// Ergebnis je Linie: x, y, z, Geschwindigkeit/uIn je Punkt.
        /// </summary>
        public static List<float[]> Trace(FlowField f, List<float[]> seeds, float uIn)
        {
            var lines = new float[seeds.Count][];
            const float h = 0.7f;
            int maxSteps = (int)(2.5f * f.NX / h);
            Parallel.For(0, seeds.Count, n =>
            {
                var pts = new List<float>(4 * 256);
                float x = seeds[n][0], y = seeds[n][1], z = seeds[n][2];
                for (int s = 0; s < maxSteps; s++)
                {
                    float ux, uy, uz;
                    f.Sample(x, y, z, out ux, out uy, out uz);
                    float sp = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
                    pts.Add(x); pts.Add(y); pts.Add(z); pts.Add(sp / uIn);
                    if (sp < 1e-3f * uIn) break;
                    float mx = x + 0.5f * h * ux / sp, my = y + 0.5f * h * uy / sp, mz = z + 0.5f * h * uz / sp;
                    f.Sample(mx, my, mz, out ux, out uy, out uz);
                    sp = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz);
                    if (sp < 1e-3f * uIn) break;
                    x += h * ux / sp; y += h * uy / sp; z += h * uz / sp;
                    if (!f.Inside(x, y, z) || f.IsSolid(x, y, z)) break;
                }
                lines[n] = pts.ToArray();
            });
            return new List<float[]>(lines);
        }
    }

}
