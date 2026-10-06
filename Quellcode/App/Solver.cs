using System;
using System.Threading.Tasks;

namespace Windkanal
{
    /// <summary>
    /// 2D-Strömungslöser nach der Lattice-Boltzmann-Methode (D2Q9).
    /// Kollision: BGK mit Smagorinsky-Turbulenzmodell (LES).
    /// Ränder: Geschwindigkeitseinlass links, Null-Gradient-Auslass rechts mit Dämpfungszone,
    /// oben/unten wahlweise reibungsfrei (Symmetrie) oder mit Haftung,
    /// Körper per Halfway-Bounce-Back, Kräfte per Impulsaustausch-Methode.
    /// Rechnet auf der Grafikkarte (OpenCL, <see cref="GpuLbm"/>), wenn eine da ist, sonst auf allen CPU-Kernen.
    /// Mit der Umgebungsvariable WK_CPU=1 wird die CPU erzwungen.
    /// </summary>
    public sealed class Solver : IDisposable
    {
        public const byte FLUID = 0, BOUNDARY = 1, SOLID = 2;

        static readonly int[] CX = { 0, 1, 0, -1, 0, 1, -1, -1, 1 };
        static readonly int[] CY = { 0, 0, 1, 0, -1, 1, 1, -1, -1 };
        static readonly float[] W = { 4f / 9, 1f / 9, 1f / 9, 1f / 9, 1f / 9, 1f / 36, 1f / 36, 1f / 36, 1f / 36 };
        static readonly int[] OPP = { 0, 3, 4, 1, 2, 7, 8, 5, 6 };
        static readonly int[] MIRY = { 0, 1, 4, 3, 2, 8, 7, 6, 5 };

        public readonly int NX, NY, N;
        /// <summary>Sichtbare Messstrecke; dahinter liegt eine unsichtbare Beruhigungsstrecke.</summary>
        public readonly int VisibleNX;
        float[] fSrc, fDst;
        public readonly byte[] Flags;
        public readonly bool[] Solid;
        public readonly float[] Rho, Ux, Uy;
        readonly float[] sponge;
        readonly double[] rowFx, rowFy;

        /// <summary>Anströmgeschwindigkeit in Gittereinheiten.</summary>
        public float U0 = 0.1f;
        /// <summary>Kinematische Viskosität in Gittereinheiten.</summary>
        public float Nu = 0.02f;
        public float SmagorinskyCs = 0.1f;
        public bool NoSlipWalls;
        public long Steps;
        /// <summary>Kraft auf den Körper im letzten Zeitschritt (Gittereinheiten).</summary>
        public double Fx, Fy;

        /// <summary>
        /// Rauchdichte 0..1 je Zelle für die Rauchansicht. Ein Rechen am Einlass gibt dünne Rauchfäden ab,
        /// die mit der Strömung mitgeführt werden (siehe <see cref="AdvectSmoke"/>).
        /// Auf der GPU erst nach <see cref="ReadSmoke"/> aktuell.
        /// </summary>
        public readonly float[] Smoke;
        float[] smokeHat, smokeBar, smokeNew;
        bool smokeStale;

        GpuLbm gpu;
        readonly double[] batchFx = new double[GpuLbm.MaxBatch], batchFy = new double[GpuLbm.MaxBatch];
        /// <summary>Womit gerechnet wird, z. B. "GPU (NVIDIA GeForce RTX 5070 Ti)" oder "CPU (20 Kerne)".</summary>
        public readonly string Backend;
        /// <summary>Warum die GPU nicht genutzt wird (null, wenn sie genutzt wird).</summary>
        public readonly string GpuUnavailableReason;
        public bool OnGpu { get { return gpu != null; } }

        public Solver(int visibleNx, int ny)
        {
            int nx = visibleNx + visibleNx * 3 / 20;
            VisibleNX = visibleNx;
            NX = nx; NY = ny; N = nx * ny;
            Flags = new byte[N];
            Solid = new bool[N];
            Rho = new float[N];
            Ux = new float[N];
            Uy = new float[N];
            rowFx = new double[ny];
            rowFy = new double[ny];
            sponge = new float[nx];
            int start = visibleNx;
            for (int x = start; x < nx; x++)
            {
                float s = (x - start) / (float)(nx - 1 - start);
                sponge[x] = s * s;
            }
            string reason;
            if (Environment.GetEnvironmentVariable("WK_CPU") == "1") reason = "per WK_CPU=1 abgeschaltet";
            else gpu = GpuLbm.TryCreate(nx, ny, sponge, out reason);
            Smoke = new float[N];
            if (gpu != null) Backend = "GPU (" + GpuLbm.DeviceName + ")";
            else
            {
                GpuUnavailableReason = reason;
                Backend = "CPU (" + Environment.ProcessorCount + " Kerne)";
                fSrc = new float[9 * N];
                fDst = new float[9 * N];
                smokeHat = new float[N];
                smokeBar = new float[N];
                smokeNew = new float[N];
            }
            RebuildFlags();
            Reset();
        }

        public void Dispose()
        {
            if (gpu != null) gpu.Dispose();
            gpu = null;
        }

        public float Tau { get { return 3f * Nu + 0.5f; } }

        /// <summary>
        /// Wählt die Gitter-Anströmgeschwindigkeit: klein genug für geringe Kompressibilitätsfehler
        /// (auch im verengten Spalt neben großen Körpern) und so, dass tau &lt;= 1.5 bleibt.
        /// </summary>
        public static float ChooseU0(double reynolds, double refLen, double blockage)
        {
            // Spitzengeschwindigkeit am Körper ~ 2·U0/(1-Versperrung) soll unter ~0.15 (Ma < 0.26) bleiben
            double u = Math.Min(0.08, 0.075 * (1.0 - Math.Min(0.8, blockage)));
            u = Math.Min(u, reynolds * 1.0 / (3.0 * Math.Max(2.0, refLen)));
            return (float)u;
        }

        /// <summary>Dauer des sanften Hochfahrens der Anströmung (Schritte).</summary>
        public int RampSteps { get { return 2 * NX; } }

        float InletVelocity
        {
            get
            {
                if (Steps >= RampSteps) return U0;
                double t = Steps / (double)RampSteps;
                return (float)(U0 * t * t * (3 - 2 * t));
            }
        }

        public void Reset()
        {
            Steps = 0; Fx = 0; Fy = 0;
            // Start aus der Ruhe – die Anströmung fährt wie beim echten Gebläse sanft hoch.
            for (int c = 0; c < N; c++)
            {
                if (gpu == null) SetEquilibrium(c, 1f, 0f, 0f);
                Rho[c] = 1f; Ux[c] = 0f; Uy[c] = 0f;
                Smoke[c] = 0f;
            }
            if (gpu != null) gpu.Reset();
            smokeStale = false;
        }

        /// <summary>
        /// Kleine lokale Querstörung hinter dem Körper: die symmetrische Nachlaufströmung ist
        /// physikalisch instabil, in der Natur stoßen winzige Störungen die Wirbelablösung an.
        /// </summary>
        void Kick()
        {
            int x0 = NX, x1 = -1, y0 = NY, y1 = -1;
            for (int c = 0; c < N; c++)
            {
                if (!Solid[c]) continue;
                int x = c % NX, y = c / NX;
                if (x < x0) x0 = x; if (x > x1) x1 = x;
                if (y < y0) y0 = y; if (y > y1) y1 = y;
            }
            if (x1 < 0) return;
            float h = Math.Max(3f, y1 - y0 + 1);
            float px = x1 + h, py = (y0 + y1) / 2f, inv2s2 = 1f / (2f * h * h);
            if (gpu != null) { gpu.Kick(px, py, inv2s2, U0); return; }
            for (int c = 0; c < N; c++)
            {
                if (Solid[c]) continue;
                float dx = c % NX - px, dy = c / NX - py;
                float d = 0.1f * U0 * (float)Math.Exp(-(dx * dx + dy * dy) * inv2s2);
                if (d < 1e-6f) continue;
                float rho = Rho[c], ux = Ux[c], uy0 = Uy[c], uy1 = uy0 + d;
                float us0 = 1.5f * (ux * ux + uy0 * uy0), us1 = 1.5f * (ux * ux + uy1 * uy1);
                for (int i = 0; i < 9; i++)
                {
                    float cu0 = 3f * (CX[i] * ux + CY[i] * uy0), cu1 = 3f * (CX[i] * ux + CY[i] * uy1);
                    fSrc[i * N + c] += W[i] * rho * ((cu1 + 0.5f * cu1 * cu1 - us1) - (cu0 + 0.5f * cu0 * cu0 - us0));
                }
                Uy[c] = uy1;
            }
        }

        void SetEquilibrium(int c, float rho, float ux, float uy)
        {
            float usq = 1.5f * (ux * ux + uy * uy);
            for (int i = 0; i < 9; i++)
            {
                float cu = 3f * (CX[i] * ux + CY[i] * uy);
                float feq = W[i] * rho * (1f + cu + 0.5f * cu * cu - usq);
                fSrc[i * N + c] = feq;
                fDst[i * N + c] = feq;
            }
        }

        /// <summary>Übernimmt eine neue Hindernismaske, ohne die Strömung neu zu starten.</summary>
        public void ApplyMask(bool[] mask)
        {
            int[] changed = gpu != null ? new int[N] : null;
            int nChanged = 0;
            for (int c = 0; c < N; c++)
            {
                int x = c % NX;
                bool s = mask[c] && x >= 2 && x <= VisibleNX - 3;
                if (s == Solid[c]) continue;
                Solid[c] = s;
                if (gpu != null) changed[nChanged++] = c;
                else SetEquilibrium(c, 1f, 0f, 0f);
                Rho[c] = 1f; Ux[c] = 0f; Uy[c] = 0f;
            }
            RebuildFlags(changed, nChanged);
        }

        void RebuildFlags(int[] changed = null, int nChanged = 0)
        {
            RebuildFlagsCpu();
            if (gpu == null) return;
            // Kraft-Slots: jede Randzelle mit Verbindung zum Körper bekommt einen Platz,
            // die GPU summiert die Impulsaustausch-Beiträge dieser Zellen je Schritt.
            var slotOf = new int[N];
            int slots = 0;
            for (int y = 0; y < NY; y++)
                for (int x = 0; x < NX; x++)
                {
                    int c = y * NX + x;
                    slotOf[c] = -1;
                    if (Flags[c] != BOUNDARY || x < 1 || x > NX - 2) continue;
                    for (int i = 1; i < 9; i++)
                    {
                        int sx = x - CX[i], sy = y - CY[i];
                        if (sy < 0 || sy >= NY) continue;
                        if (Solid[sy * NX + sx]) { slotOf[c] = slots++; break; }
                    }
                }
            gpu.ApplyMask(Flags, slotOf, slots, changed, nChanged);
        }

        void RebuildFlagsCpu()
        {
            for (int y = 0; y < NY; y++)
            {
                for (int x = 0; x < NX; x++)
                {
                    int c = y * NX + x;
                    if (Solid[c]) { Flags[c] = SOLID; continue; }
                    bool b = y == 0 || y == NY - 1;
                    for (int i = 1; i < 9 && !b; i++)
                    {
                        int sx = x - CX[i], sy = y - CY[i];
                        if (sx < 0 || sx >= NX || sy < 0 || sy >= NY) continue;
                        if (Solid[sy * NX + sx]) b = true;
                    }
                    Flags[c] = b ? BOUNDARY : FLUID;
                }
            }
        }

        /// <summary>Ein Zeitschritt. Fx/Fy, Rho, Ux, Uy sind danach aktuell.</summary>
        public void Step()
        {
            if (gpu != null) StepMany(1, null, null);
            else StepCpu();
        }

        /// <summary>
        /// 'count' Zeitschritte am Stück; die Kraft jedes einzelnen Schritts landet in fx/fy (dürfen null sein).
        /// Auf der GPU laufen die Schritte ohne Warten hintereinander, Rho/Ux/Uy werden nur am Ende zurückgeholt.
        /// </summary>
        public void StepMany(int count, double[] fx, double[] fy)
        {
            if (gpu == null)
            {
                for (int k = 0; k < count; k++)
                {
                    StepCpu();
                    if (fx != null) { fx[k] = Fx; fy[k] = Fy; }
                }
                return;
            }
            float smagK = 18f * 1.41421356f * SmagorinskyCs * SmagorinskyCs;
            for (int done = 0; done < count; )
            {
                int m = Math.Min(count - done, GpuLbm.MaxBatch);
                for (int k = 0; k < m; k++)
                {
                    if (Steps == RampSteps) Kick();
                    float uIn = InletVelocity;
                    float tau0 = Tau;
                    float spongeAmp = Math.Max(0f, 1.0f - tau0);
                    gpu.EnqueueStep(k, tau0, spongeAmp, smagK, uIn, NoSlipWalls);
                    Steps++;
                }
                gpu.ReadForces(m, batchFx, batchFy, 0);
                if (fx != null)
                {
                    Array.Copy(batchFx, 0, fx, done, m);
                    Array.Copy(batchFy, 0, fy, done, m);
                }
                Fx = batchFx[m - 1]; Fy = batchFy[m - 1];
                done += m;
            }
            gpu.ReadMacros(Rho, Ux, Uy);
        }

        void StepCpu()
        {
            if (Steps == RampSteps) Kick();
            float uIn = InletVelocity;
            float tau0 = Tau;
            float spongeAmp = Math.Max(0f, 1.0f - tau0);
            float smagK = 18f * 1.41421356f * SmagorinskyCs * SmagorinskyCs;
            float[] src = fSrc, dst = fDst;

            Parallel.For(0, NY, y => StepRow(y, src, dst, tau0, spongeAmp, smagK, uIn));

            // Einlass (x = 0): Gleichgewicht mit vorgegebener Geschwindigkeit,
            // Dichte aus dem Nachbarn extrapoliert (lässt Druckwellen hinaus).
            for (int y = 0; y < NY; y++)
            {
                int c = y * NX;
                float rho = Rho[c + 1];
                if (!(rho > 0.2f && rho < 5f)) rho = 1f;
                float ux = uIn, usq = 1.5f * ux * ux;
                for (int i = 0; i < 9; i++)
                {
                    float cu = 3f * (CX[i] * ux);
                    dst[i * N + c] = W[i] * rho * (1f + cu + 0.5f * cu * cu - usq);
                }
                Rho[c] = rho; Ux[c] = ux; Uy[c] = 0f;
            }
            // Auslass (x = NX-1): fester Umgebungsdruck (rho = 1), Geschwindigkeit und
            // Nicht-Gleichgewichtsanteil aus dem Nachbarn extrapoliert
            for (int y = 0; y < NY; y++)
            {
                int c = y * NX + NX - 1, cn = c - 1;
                float rn = Rho[cn], ux = Ux[cn], uy = Uy[cn];
                if (!(rn > 0.2f && rn < 5f)) { rn = 1f; ux = uIn; uy = 0f; }
                float usq = 1.5f * (ux * ux + uy * uy);
                for (int i = 0; i < 9; i++)
                {
                    float cu = 3f * (CX[i] * ux + CY[i] * uy);
                    float eq = W[i] * (1f + cu + 0.5f * cu * cu - usq);
                    dst[i * N + c] = eq + (dst[i * N + cn] - rn * eq);
                }
                Rho[c] = 1f; Ux[c] = ux; Uy[c] = uy;
            }

            double fx = 0, fy = 0;
            for (int y = 0; y < NY; y++) { fx += rowFx[y]; fy += rowFy[y]; }
            Fx = fx; Fy = fy;

            fSrc = dst; fDst = src;
            Steps++;
        }

        unsafe void StepRow(int y, float[] srcArr, float[] dstArr, float tau0, float spongeAmp, float smagK, float uIn)
        {
            int n = N, nx = NX, ny = NY;
            bool noSlip = NoSlipWalls;
            double fxs = 0, fys = 0;
            float* f = stackalloc float[9];
            fixed (float* src = srcArr, dst = dstArr, rhoA = Rho, uxA = Ux, uyA = Uy, sp = sponge)
            fixed (byte* flag = Flags)
            {
                int rowBase = y * nx;
                for (int x = 1; x < nx - 1; x++)
                {
                    int c = rowBase + x;
                    byte fl = flag[c];
                    if (fl == SOLID) continue;

                    // --- Streaming (Pull) ---
                    if (fl == FLUID)
                    {
                        f[0] = src[c];
                        f[1] = src[n + c - 1];
                        f[2] = src[2 * n + c - nx];
                        f[3] = src[3 * n + c + 1];
                        f[4] = src[4 * n + c + nx];
                        f[5] = src[5 * n + c - nx - 1];
                        f[6] = src[6 * n + c - nx + 1];
                        f[7] = src[7 * n + c + nx + 1];
                        f[8] = src[8 * n + c + nx - 1];
                    }
                    else
                    {
                        f[0] = src[c];
                        for (int i = 1; i < 9; i++)
                        {
                            int sx = x - CX[i], sy = y - CY[i];
                            if (sy < 0 || sy >= ny)
                            {
                                int mc = rowBase + sx;
                                if (noSlip || flag[mc] == SOLID) f[i] = src[OPP[i] * n + c];
                                else f[i] = src[MIRY[i] * n + mc];
                            }
                            else
                            {
                                int sc = sy * nx + sx;
                                if (flag[sc] == SOLID)
                                {
                                    // Halfway-Bounce-Back + Impulsaustausch
                                    float v = src[OPP[i] * n + c];
                                    f[i] = v;
                                    fxs -= 2.0 * v * CX[i];
                                    fys -= 2.0 * v * CY[i];
                                }
                                else f[i] = src[i * n + sc];
                            }
                        }
                    }

                    // --- Makroskopische Größen ---
                    float f0 = f[0], f1 = f[1], f2 = f[2], f3 = f[3], f4 = f[4], f5 = f[5], f6 = f[6], f7 = f[7], f8 = f[8];
                    float rho = f0 + f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8;
                    float inv = 1f / rho;
                    float ux = (f1 - f3 + f5 - f6 - f7 + f8) * inv;
                    float uy = (f2 - f4 + f5 + f6 - f7 - f8) * inv;
                    float u2 = ux * ux + uy * uy;
                    if (u2 > 0.0625f)
                    {
                        // Sicherheitsnetz: Gittergeschwindigkeit < 0.25 halten (sonst bricht LBM zusammen)
                        float sc = 0.25f / (float)Math.Sqrt(u2);
                        ux *= sc; uy *= sc;
                    }
                    float spx = sp[x];
                    if (spx > 0f)
                    {
                        // Beruhigungsstrecke: Wirbel sanft in Richtung Parallelströmung ausdämpfen
                        float damp = 0.06f * spx;
                        ux += damp * (uIn - ux);
                        uy -= damp * uy;
                    }
                    rhoA[c] = rho; uxA[c] = ux; uyA[c] = uy;

                    // --- Gleichgewicht ---
                    float usq = 1.5f * (ux * ux + uy * uy);
                    float r1 = rho * (1f / 9f), r2 = rho * (1f / 36f);
                    float e0 = rho * (4f / 9f) * (1f - usq);
                    float e1 = r1 * (1f + 3f * ux + 4.5f * ux * ux - usq);
                    float e3 = r1 * (1f - 3f * ux + 4.5f * ux * ux - usq);
                    float e2 = r1 * (1f + 3f * uy + 4.5f * uy * uy - usq);
                    float e4 = r1 * (1f - 3f * uy + 4.5f * uy * uy - usq);
                    float a = ux + uy, b = uy - ux;
                    float e5 = r2 * (1f + 3f * a + 4.5f * a * a - usq);
                    float e7 = r2 * (1f - 3f * a + 4.5f * a * a - usq);
                    float e6 = r2 * (1f + 3f * b + 4.5f * b * b - usq);
                    float e8 = r2 * (1f - 3f * b + 4.5f * b * b - usq);

                    // --- Nicht-Gleichgewichts-Spannungstensor ---
                    float pxx = (f1 - e1) + (f3 - e3) + (f5 - e5) + (f6 - e6) + (f7 - e7) + (f8 - e8);
                    float pyy = (f2 - e2) + (f4 - e4) + (f5 - e5) + (f6 - e6) + (f7 - e7) + (f8 - e8);
                    float pxy = (f5 - e5) - (f6 - e6) + (f7 - e7) - (f8 - e8);

                    // --- Smagorinsky: lokale Relaxationszeit ---
                    float q = (float)Math.Sqrt(pxx * pxx + pyy * pyy + 2f * pxy * pxy);
                    float tau = tau0 + spongeAmp * spx;
                    float tauE = 0.5f * (tau + (float)Math.Sqrt(tau * tau + smagK * q * inv));
                    float k = 1f - 1f / tauE;

                    // --- Regularisierung: Nicht-Gleichgewicht nur aus dem physikalischen Spannungstensor
                    //     rekonstruieren (filtert unphysikalische "Geister"-Moden, stabil bei hohem Re) ---
                    float tr = (pxx + pyy) * (1f / 3f);
                    float n0 = -2f * tr;
                    float nx1 = 0.5f * (pxx - tr);
                    float ny1 = 0.5f * (pyy - tr);
                    float nd = 0.125f * (2f * tr);
                    float nxy = 0.125f * 2f * pxy;

                    dst[c] = e0 + k * n0;
                    dst[n + c] = e1 + k * nx1;
                    dst[2 * n + c] = e2 + k * ny1;
                    dst[3 * n + c] = e3 + k * nx1;
                    dst[4 * n + c] = e4 + k * ny1;
                    dst[5 * n + c] = e5 + k * (nd + nxy);
                    dst[6 * n + c] = e6 + k * (nd - nxy);
                    dst[7 * n + c] = e7 + k * (nd + nxy);
                    dst[8 * n + c] = e8 + k * (nd - nxy);
                }
            }
            rowFx[y] = fxs; rowFy[y] = fys;
        }

        // ------------------------------------------------------------ Rauch

        /// <summary>Anzahl der Rauchfäden am Einlass.</summary>
        public int SmokeStreaks { get { return Math.Max(16, NY / 9); } }

        /// <summary>
        /// Führt die Rauchdichte mit der aktuellen Strömung um 'steps' Zeitschritte weiter.
        /// Verfahren: semi-Lagrange (Rückverfolgung entlang der Geschwindigkeit) mit MacCormack-Korrektur,
        /// damit die Rauchfäden scharf bleiben statt zu verschmieren. Pro Teilschritt wandert der Rauch
        /// höchstens gut eine Zelle weit.
        /// </summary>
        public void AdvectSmoke(int steps)
        {
            if (steps <= 0) return;
            float speed = 2f * Math.Max(U0, 1e-3f);
            int subs = Math.Max(1, (int)Math.Ceiling(steps * speed / 1.2f));
            float dt = steps / (float)subs;
            int streaks = SmokeStreaks;
            if (gpu != null)
            {
                for (int k = 0; k < subs; k++) gpu.EnqueueSmoke(dt, streaks);
                smokeStale = true;
                return;
            }
            for (int k = 0; k < subs; k++)
            {
                float[] phi = Smoke, hat = smokeHat, bar = smokeBar, nw = smokeNew;
                Parallel.For(0, NY, y => SmokeTrace(y, phi, hat, dt));
                Parallel.For(0, NY, y => SmokeTrace(y, hat, bar, -dt));
                Parallel.For(0, NY, y => SmokeCorrect(y, phi, hat, bar, nw, dt, streaks));
                Array.Copy(nw, Smoke, N);
            }
        }

        /// <summary>Holt die Rauchdichte von der Grafikkarte (auf der CPU ohne Wirkung).</summary>
        public void ReadSmoke()
        {
            if (gpu == null || !smokeStale) return;
            gpu.ReadSmoke(Smoke);
            smokeStale = false;
        }

        /// <summary>Dichte eines Rauchfadens am Einlass: weiches Profil, gut eine Zelle breit.</summary>
        public static float SmokeInlet(int y, int ny, int streaks)
        {
            float s = ny / (float)streaks;
            float f = y / s - 0.5f;
            float d = Math.Abs(f - (float)Math.Round(f)) * s;
            return Math.Max(0f, Math.Min(1f, 1.6f - d / 1.1f));
        }

        static float Bilinear(float[] a, float x, float y, int nx, int ny)
        {
            if (x < 0) x = 0; if (x > nx - 1.001f) x = nx - 1.001f;
            if (y < 0) y = 0; if (y > ny - 1.001f) y = ny - 1.001f;
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            int c = y0 * nx + x0;
            return (a[c] * (1 - fx) + a[c + 1] * fx) * (1 - fy) + (a[c + nx] * (1 - fx) + a[c + nx + 1] * fx) * fy;
        }

        /// <summary>Rückverfolgung (Mittelpunktregel) um dt; negatives dt verfolgt vorwärts.</summary>
        void BackTrace(int x, int y, float dt, out float bx, out float by)
        {
            int c = y * NX + x;
            float mx = x - 0.5f * dt * Ux[c], my = y - 0.5f * dt * Uy[c];
            bx = x - dt * Bilinear(Ux, mx, my, NX, NY);
            by = y - dt * Bilinear(Uy, mx, my, NX, NY);
        }

        void SmokeTrace(int y, float[] src, float[] dst, float dt)
        {
            for (int x = 0; x < NX; x++)
            {
                int c = y * NX + x;
                if (Solid[c]) { dst[c] = 0f; continue; }
                float bx, by;
                BackTrace(x, y, dt, out bx, out by);
                dst[c] = Bilinear(src, bx, by, NX, NY);
            }
        }

        void SmokeCorrect(int y, float[] phi, float[] hat, float[] bar, float[] dst, float dt, int streaks)
        {
            float inlet = SmokeInlet(y, NY, streaks);
            for (int x = 0; x < NX; x++)
            {
                int c = y * NX + x;
                if (Solid[c]) { dst[c] = 0f; continue; }
                if (x <= 1) { dst[c] = inlet; continue; }
                float bx, by;
                BackTrace(x, y, dt, out bx, out by);
                if (bx < 0) bx = 0; if (bx > NX - 1.001f) bx = NX - 1.001f;
                if (by < 0) by = 0; if (by > NY - 1.001f) by = NY - 1.001f;
                int b = (int)by * NX + (int)bx;
                float lo = Math.Min(Math.Min(phi[b], phi[b + 1]), Math.Min(phi[b + NX], phi[b + NX + 1]));
                float hi = Math.Max(Math.Max(phi[b], phi[b + 1]), Math.Max(phi[b + NX], phi[b + NX + 1]));
                float v = hat[c] + 0.5f * (phi[c] - bar[c]);
                dst[c] = v < lo ? lo : v > hi ? hi : v;
            }
        }

        /// <summary>Freistromdichte (am Einlass gemessen) als Druckreferenz.</summary>
        public float RhoInf
        {
            get
            {
                float s = 0; int cnt = 0;
                for (int y = NY / 4; y < 3 * NY / 4; y++) { s += Rho[y * NX + 2]; cnt++; }
                return cnt > 0 ? s / cnt : 1f;
            }
        }

        public bool IsStable()
        {
            if (double.IsNaN(Fx) || double.IsInfinity(Fx)) return false;
            for (int c = 0; c < N; c += 37)
            {
                float r = Rho[c];
                if (!(r > 0.2f && r < 4f)) return false;
            }
            return true;
        }
    }
}
