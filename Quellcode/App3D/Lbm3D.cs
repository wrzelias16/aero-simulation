using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Windkanal3D
{
    /// <summary>
    /// 3D-Strömungslöser nach der Lattice-Boltzmann-Methode (D3Q19, BGK) auf der Grafikkarte (OpenCL).
    /// Eigenständig: benutzt nichts aus dem 2D-Code (Namensraum Windkanal3D, eigener OpenCL-Zugriff),
    /// damit 2D und 3D sich nie gegenseitig beeinflussen.
    ///
    /// Randbedingungen: Einlass x=0 mit fester Geschwindigkeit, Auslass x=NX-1 mit festem Umgebungsdruck,
    /// Seitenwände (y, z) reibungsfrei (Spiegelung). Feste Körper: Bounce-Back an der halben Strecke,
    /// Kraft per Impulsaustausch. Alle Größen in Gittereinheiten (Dichte 1, Zellgröße 1, Zeitschritt 1).
    /// </summary>
    public sealed class Lbm3D : IDisposable
    {
        public const int Q = 19;

        // Richtungen D3Q19; Gegenrichtung von i (i > 0) ist i ungerade ? i + 1 : i - 1.
        static readonly int[] CX = { 0, 1, -1, 0, 0, 0, 0, 1, -1, 1, -1, 1, -1, 1, -1, 0, 0, 0, 0 };
        static readonly int[] CY = { 0, 0, 0, 1, -1, 0, 0, 1, -1, -1, 1, 0, 0, 0, 0, 1, -1, 1, -1 };
        static readonly int[] CZ = { 0, 0, 0, 0, 0, 1, -1, 0, 0, 0, 0, 1, -1, -1, 1, 1, -1, -1, 1 };

        // ------------------------------------------------------------ gemeinsamer Kontext

        static bool initTried;
        static IntPtr device, context, queue;
        static string deviceName, initError;
        static ulong globalMem;

        /// <summary>Name der Grafikkarte oder null, wenn keine nutzbar ist.</summary>
        public static string DeviceName { get { InitShared(); return deviceName; } }
        public static string InitError { get { InitShared(); return initError; } }
        /// <summary>Gesamter Grafikspeicher in Bytes (0, wenn keine GPU).</summary>
        public static ulong DeviceMemory { get { InitShared(); return globalMem; } }

        /// <summary>
        /// Wie viele Zellen in den Grafikspeicher passen (mit Reserve).
        /// 19 Richtungen x 2 Felder x 4 Byte + Flags + Rauch (3 Geschwindigkeiten + 4 Dichtefelder je 4 Byte).
        /// </summary>
        public static long MaxCells { get { return MaxCellsFor(true); } }

        public static long MaxCellsFor(bool fp16)
        {
            InitShared();
            long cells = (long)(globalMem * 0.6 / (Q * 2 * (fp16 ? 2 : 4) + 1 + 7 * 4));
            return Math.Min(cells, int.MaxValue / Q);   // Indizes bleiben 32 Bit
        }

        static void InitShared()
        {
            if (initTried) return;
            initTried = true;
            try
            {
                uint np;
                Check(CL.clGetPlatformIDs(0, null, out np), "clGetPlatformIDs");
                if (np == 0) throw new Exception("keine OpenCL-Plattform");
                var plats = new IntPtr[np];
                Check(CL.clGetPlatformIDs(np, plats, out np), "clGetPlatformIDs");
                IntPtr best = IntPtr.Zero; ulong bestMem = 0;
                foreach (var p in plats)
                {
                    uint nd;
                    if (CL.clGetDeviceIDs(p, CL.DEVICE_TYPE_GPU, 0, null, out nd) != 0 || nd == 0) continue;
                    var devs = new IntPtr[nd];
                    if (CL.clGetDeviceIDs(p, CL.DEVICE_TYPE_GPU, nd, devs, out nd) != 0) continue;
                    foreach (var d in devs)
                    {
                        ulong mem = BitConverter.ToUInt64(Info(d, CL.DEVICE_GLOBAL_MEM_SIZE), 0);
                        if (mem > bestMem) { bestMem = mem; best = d; }
                    }
                }
                if (best == IntPtr.Zero) throw new Exception("keine OpenCL-Grafikkarte gefunden");
                device = best;
                globalMem = bestMem;
                deviceName = Encoding.ASCII.GetString(Info(device, CL.DEVICE_NAME)).TrimEnd('\0').Trim();
                int err;
                context = CL.clCreateContext(IntPtr.Zero, 1, new[] { device }, IntPtr.Zero, IntPtr.Zero, out err);
                Check(err, "clCreateContext");
                queue = CL.clCreateCommandQueue(context, device, 0, out err);
                Check(err, "clCreateCommandQueue");
            }
            catch (Exception e)
            {
                deviceName = null;
                initError = e is DllNotFoundException ? "OpenCL.dll nicht gefunden" : e.Message;
            }
        }

        static byte[] Info(IntPtr d, uint param)
        {
            UIntPtr size;
            Check(CL.clGetDeviceInfo(d, param, UIntPtr.Zero, null, out size), "clGetDeviceInfo");
            var b = new byte[(int)size];
            Check(CL.clGetDeviceInfo(d, param, size, b, out size), "clGetDeviceInfo");
            return b;
        }

        static void Check(int err, string what)
        {
            if (err != 0) throw new Exception(what + " fehlgeschlagen (OpenCL-Fehler " + err + ")");
        }

        // ------------------------------------------------------------ pro Gitter

        public readonly int NX, NY, NZ, N;
        /// <summary>Verteilungen in halber Genauigkeit gespeichert (gerechnet wird immer in voller).</summary>
        public readonly bool Fp16;
        readonly int groups, local;
        IntPtr program, kStep, kBounds, kInit, kMacro, kSlice, kCoarse;
        IntPtr bufA, bufB, bFlag, bForce, bMacro, bSlice, bCoarse;
        long coarseBytes;

        // Rauch: Dichtefeld in voller Auflösung, mit der Strömung mitgetragen (semi-Lagrange + MacCormack, wie in 2D)
        IntPtr kSmTrace, kSmCorrect, kSmInject, kSmClear, kSmRender;
        IntPtr bUx, bUy, bUz, bPhi, bPhiNew, bHat, bBar, bSources, bDepth, bImage;
        int sourceCount, imageW, imageH;
        float sourceRadius;
        int smokeCounter;
        /// <summary>Rauch wird mitgerechnet (kostet etwa so viel wie die Strömung selbst).</summary>
        public bool SmokeOn;
        /// <summary>Rauch alle so viele Strömungsschritte weitertragen (mit entsprechend größerem Zeitschritt).</summary>
        public int SmokeEvery = 2;
        bool swapped;
        readonly float[] forceHost;

        IntPtr Src { get { return swapped ? bufB : bufA; } }
        IntPtr Dst { get { return swapped ? bufA : bufB; } }

        /// <summary>Legt den Löser an. Wirft eine Exception mit Klartext-Grund, wenn das nicht geht.</summary>
        public Lbm3D(int nx, int ny, int nz, bool fp16 = true, int localSize = 128)
        {
            if (DeviceName == null) throw new Exception(InitError ?? "keine Grafikkarte");
            long cells = (long)nx * ny * nz;
            if (cells > MaxCellsFor(fp16)) throw new Exception("Gitter zu groß für den Grafikspeicher (" + cells + " Zellen, höchstens " + MaxCellsFor(fp16) + ")");
            NX = nx; NY = ny; NZ = nz; N = nx * ny * nz;
            Fp16 = fp16;
            local = localSize;
            groups = (N + local - 1) / local;
            forceHost = new float[groups * 3];

            int err;
            program = CL.clCreateProgramWithSource(context, 1, new[] { BuildSource() }, IntPtr.Zero, out err);
            Check(err, "clCreateProgramWithSource");
            string opts = "-cl-fp32-correctly-rounded-divide-sqrt -D NX=" + nx + " -D NY=" + ny + " -D NZ=" + nz + " -D N=" + N
                          + " -D LOCAL=" + local + (fp16 ? " -D FP16" : "");
            err = CL.clBuildProgram(program, 1, new[] { device }, opts, IntPtr.Zero, IntPtr.Zero);
            if (err != 0)
            {
                UIntPtr size;
                CL.clGetProgramBuildInfo(program, device, CL.PROGRAM_BUILD_LOG, UIntPtr.Zero, null, out size);
                var log = new byte[(int)size];
                CL.clGetProgramBuildInfo(program, device, CL.PROGRAM_BUILD_LOG, size, log, out size);
                throw new Exception("OpenCL-Kernel konnte nicht übersetzt werden: " + Encoding.ASCII.GetString(log));
            }
            kStep = Kernel("lbm3_step");
            kBounds = Kernel("lbm3_bounds");
            kInit = Kernel("lbm3_init");
            kMacro = Kernel("lbm3_macro");
            kSlice = Kernel("lbm3_slice");
            kCoarse = Kernel("lbm3_coarse");
            kSmTrace = Kernel("smoke3_trace");
            kSmCorrect = Kernel("smoke3_correct");
            kSmInject = Kernel("smoke3_inject");
            kSmClear = Kernel("smoke3_clear");
            kSmRender = Kernel("smoke3_render");

            bufA = Buffer((long)Q * N * (fp16 ? 2 : 4));
            bufB = Buffer((long)Q * N * (fp16 ? 2 : 4));
            bFlag = Buffer(N);
            bForce = Buffer(groups * 3L * 4);
            bMacro = Buffer(N * 4L);
            bSlice = Buffer((long)nx * Math.Max(ny, nz) * 4);
        }

        IntPtr Kernel(string name)
        {
            int err;
            IntPtr k = CL.clCreateKernel(program, name, out err);
            Check(err, "clCreateKernel " + name);
            return k;
        }

        static IntPtr Buffer(long bytes)
        {
            int err;
            IntPtr b = CL.clCreateBuffer(context, CL.MEM_READ_WRITE, (UIntPtr)Math.Max(4L, bytes), IntPtr.Zero, out err);
            Check(err, "clCreateBuffer");
            return b;
        }

        static void Arg(IntPtr k, uint i, IntPtr buf) { Check(CL.clSetKernelArg(k, i, (UIntPtr)IntPtr.Size, ref buf), "clSetKernelArg"); }
        static void Arg(IntPtr k, uint i, int v) { Check(CL.clSetKernelArg(k, i, (UIntPtr)4, ref v), "clSetKernelArg"); }
        static void Arg(IntPtr k, uint i, float v) { Check(CL.clSetKernelArg(k, i, (UIntPtr)4, ref v), "clSetKernelArg"); }

        void Run(IntPtr k, long global)
        {
            long g = (global + local - 1) / local * local;
            Check(CL.clEnqueueNDRangeKernel(queue, k, 1, IntPtr.Zero, new[] { (UIntPtr)g }, new[] { (UIntPtr)local }, 0, IntPtr.Zero, IntPtr.Zero),
                  "clEnqueueNDRangeKernel");
        }

        /// <summary>Setzt das ganze Feld auf gleichmäßige Strömung (Dichte 1, Geschwindigkeit uIn in +x).</summary>
        public void Reset(float uIn)
        {
            Arg(kInit, 0, bufA); Arg(kInit, 1, bufB); Arg(kInit, 2, uIn);
            Run(kInit, N);
            swapped = false;
        }

        /// <summary>Setzt die festen Zellen (1 = fest, 0 = Fluid). Länge muss N sein, Index x + NX * (y + NY * z).</summary>
        public void SetSolid(byte[] solid)
        {
            if (solid.Length != N) throw new ArgumentException("Maske hat falsche Größe");
            Check(CL.clEnqueueWriteBuffer(queue, bFlag, 1, UIntPtr.Zero, (UIntPtr)N, solid, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
        }

        /// <summary>
        /// Rechnet 'steps' Zeitschritte. omega = 1 / tau (Relaxation), uIn = Einströmgeschwindigkeit.
        /// Gibt die Kraft des letzten Schritts auf alle festen Zellen zurück (Gittereinheiten).
        /// </summary>
        public void Step(int steps, float omega, float uIn, out double fx, out double fy, out double fz)
        {
            if (SmokeOn) EnsureSmoke();
            for (int s = 0; s < steps; s++)
            {
                bool smokeNow = SmokeOn && (smokeCounter + 1) % SmokeEvery == 0;
                IntPtr vx = smokeNow ? bUx : bForce, vy = smokeNow ? bUy : bForce, vz = smokeNow ? bUz : bForce;
                Arg(kStep, 0, Src); Arg(kStep, 1, Dst); Arg(kStep, 2, bFlag); Arg(kStep, 3, bForce); Arg(kStep, 4, omega);
                Arg(kStep, 5, s == steps - 1 ? 1 : 0);
                Arg(kStep, 6, vx); Arg(kStep, 7, vy); Arg(kStep, 8, vz); Arg(kStep, 9, smokeNow ? 1 : 0);
                Run(kStep, N);
                Arg(kBounds, 0, Dst); Arg(kBounds, 1, uIn);
                Run(kBounds, (long)NY * NZ);
                swapped = !swapped;
                if (SmokeOn && ++smokeCounter % SmokeEvery == 0) SmokeAdvance(SmokeEvery);
            }
            Check(CL.clEnqueueReadBuffer(queue, bForce, 1, UIntPtr.Zero, (UIntPtr)(groups * 3L * 4), forceHost, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            double a = 0, b = 0, c = 0;
            for (int g = 0; g < groups; g++) { a += forceHost[g * 3]; b += forceHost[g * 3 + 1]; c += forceHost[g * 3 + 2]; }
            fx = a; fy = b; fz = c;
        }

        /// <summary>Liest die Geschwindigkeitskomponente 0 = x, 1 = y, 2 = z (feste Zellen: 0), 3 = Dichte.</summary>
        public float[] ReadField(int component)
        {
            Arg(kMacro, 0, Src); Arg(kMacro, 1, bFlag); Arg(kMacro, 2, bMacro); Arg(kMacro, 3, component);
            Run(kMacro, N);
            var data = new float[N];
            Check(CL.clEnqueueReadBuffer(queue, bMacro, 1, UIntPtr.Zero, (UIntPtr)(N * 4L), data, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            return data;
        }

        /// <summary>
        /// Geschwindigkeitsbetrag geteilt durch uIn in einer Ebene: axis 0 = Seitenschnitt (x-z bei y = index),
        /// axis 1 = Draufsicht (x-y bei z = index). Feste Zellen = -1. Länge NX * NZ bzw. NX * NY, Zeile für Zeile in x.
        /// </summary>
        public float[] ReadSlice(int axis, int index, float uIn)
        {
            int count = NX * (axis == 0 ? NZ : NY);
            index = Math.Max(0, Math.Min((axis == 0 ? NY : NZ) - 1, index));
            Arg(kSlice, 0, Src); Arg(kSlice, 1, bFlag); Arg(kSlice, 2, bSlice); Arg(kSlice, 3, axis); Arg(kSlice, 4, index);
            Arg(kSlice, 5, 1.0f / uIn);
            Run(kSlice, count);
            var data = new float[count];
            Check(CL.clEnqueueReadBuffer(queue, bSlice, 1, UIntPtr.Zero, (UIntPtr)(count * 4L), data, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            return data;
        }

        /// <summary>
        /// Geschwindigkeitsfeld in jeder 'stride'-ten Zelle (x, y, z je Punkt, feste Zellen = 0), für Stromlinien und Rauch.
        /// Größe des groben Gitters: (NX + stride - 1) / stride usw.
        /// </summary>
        public float[] ReadVelocity(int stride, out int cnx, out int cny, out int cnz)
        {
            cnx = (NX + stride - 1) / stride; cny = (NY + stride - 1) / stride; cnz = (NZ + stride - 1) / stride;
            long count = (long)cnx * cny * cnz;
            if (bCoarse == IntPtr.Zero || coarseBytes < count * 12)
            {
                if (bCoarse != IntPtr.Zero) CL.clReleaseMemObject(bCoarse);
                coarseBytes = count * 12;
                bCoarse = Buffer(coarseBytes);
            }
            Arg(kCoarse, 0, Src); Arg(kCoarse, 1, bFlag); Arg(kCoarse, 2, bCoarse); Arg(kCoarse, 3, stride);
            Arg(kCoarse, 4, cnx); Arg(kCoarse, 5, cny); Arg(kCoarse, 6, cnz);
            Run(kCoarse, count);
            var data = new float[count * 3];
            Check(CL.clEnqueueReadBuffer(queue, bCoarse, 1, UIntPtr.Zero, (UIntPtr)(count * 12), data, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            return data;
        }

        // ------------------------------------------------------------ Rauch

        void EnsureSmoke()
        {
            if (bPhi != IntPtr.Zero) return;
            bUx = Buffer(N * 4L); bUy = Buffer(N * 4L); bUz = Buffer(N * 4L);
            bPhi = Buffer(N * 4L); bPhiNew = Buffer(N * 4L); bHat = Buffer(N * 4L); bBar = Buffer(N * 4L);
            SmokeClear();
        }

        /// <summary>Allen Rauch entfernen.</summary>
        public void SmokeClear()
        {
            if (bPhi == IntPtr.Zero) return;
            foreach (var b in new[] { bPhi, bPhiNew })
            {
                Arg(kSmClear, 0, b);
                Run(kSmClear, N);
            }
        }

        /// <summary>
        /// Rauchquellen (Düsen eines Rauchrechens): je Punkt x, y, z in Zellkoordinaten (Mitte von Zelle i = i + 0,5).
        /// Aus jeder Düse strömt laufend ein dünner Rauchfaden mit dem Radius 'radius' (Zellen).
        /// </summary>
        public void SetSmokeSources(float[] xyz, float radius)
        {
            EnsureSmoke();
            sourceCount = xyz.Length / 3;
            sourceRadius = radius;
            var pts = new float[Math.Max(1, sourceCount) * 4];
            for (int i = 0; i < sourceCount; i++)
            {
                pts[4 * i] = xyz[3 * i] - 0.5f; pts[4 * i + 1] = xyz[3 * i + 1] - 0.5f; pts[4 * i + 2] = xyz[3 * i + 2] - 0.5f;
            }
            if (bSources != IntPtr.Zero) CL.clReleaseMemObject(bSources);
            bSources = Buffer(pts.Length * 4L);
            Check(CL.clEnqueueWriteBuffer(queue, bSources, 1, UIntPtr.Zero, (UIntPtr)(pts.Length * 4L), pts, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
        }

        void SmokeAdvance(float dt)
        {
            // die Geschwindigkeit hat der letzte Strömungsschritt schon mitgeschrieben
            // vorwärts (hat), zurück (bar), dann Fehler korrigieren und auf die Nachbarwerte begrenzen
            SmokeTrace(bPhi, bHat, dt);
            SmokeTrace(bHat, bBar, -dt);
            Arg(kSmCorrect, 0, bPhi); Arg(kSmCorrect, 1, bHat); Arg(kSmCorrect, 2, bBar); Arg(kSmCorrect, 3, bPhiNew);
            Arg(kSmCorrect, 4, bUx); Arg(kSmCorrect, 5, bUy); Arg(kSmCorrect, 6, bUz); Arg(kSmCorrect, 7, bFlag); Arg(kSmCorrect, 8, dt);
            Run(kSmCorrect, N);
            var t = bPhi; bPhi = bPhiNew; bPhiNew = t;
            if (sourceCount > 0)
            {
                int r = (int)Math.Ceiling(sourceRadius) + 1, k = 2 * r + 1;
                Arg(kSmInject, 0, bPhi); Arg(kSmInject, 1, bFlag); Arg(kSmInject, 2, bSources); Arg(kSmInject, 3, sourceCount);
                Arg(kSmInject, 4, r); Arg(kSmInject, 5, sourceRadius);
                Run(kSmInject, (long)sourceCount * k * k * k);
            }
        }

        void SmokeTrace(IntPtr src, IntPtr dst, float dt)
        {
            Arg(kSmTrace, 0, src); Arg(kSmTrace, 1, dst); Arg(kSmTrace, 2, bUx); Arg(kSmTrace, 3, bUy); Arg(kSmTrace, 4, bUz);
            Arg(kSmTrace, 5, bFlag); Arg(kSmTrace, 6, dt);
            Run(kSmTrace, N);
        }

        /// <summary>
        /// Zeichnet den Rauch als Volumen (Strahlen durch das Dichtefeld, Licht wird verschluckt).
        /// cam = Kamera { Position xyz, vorwärts xyz, rechts xyz, oben xyz, Brennweite } für ein Bild w x h.
        /// invDepth = 1 / Abstand zur nächsten Fläche je Bildpunkt (0 = keine), damit der Körper den Rauch verdeckt.
        /// Ergebnis: RGBA je Bildpunkt, Farbe bereits mit der Deckkraft multipliziert.
        /// </summary>
        public byte[] RenderSmoke(int w, int h, float[] cam, float[] invDepth, float r, float g, float b, float density)
        {
            EnsureSmoke();
            if (bImage == IntPtr.Zero || imageW != w || imageH != h)
            {
                if (bImage != IntPtr.Zero) CL.clReleaseMemObject(bImage);
                if (bDepth != IntPtr.Zero) CL.clReleaseMemObject(bDepth);
                bImage = Buffer((long)w * h * 4);
                bDepth = Buffer((long)w * h * 4);
                imageW = w; imageH = h;
            }
            Check(CL.clEnqueueWriteBuffer(queue, bDepth, 1, UIntPtr.Zero, (UIntPtr)((long)w * h * 4), invDepth, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
            uint a = 0;
            Arg(kSmRender, a++, bPhi); Arg(kSmRender, a++, bDepth); Arg(kSmRender, a++, bImage); Arg(kSmRender, a++, w); Arg(kSmRender, a++, h);
            for (int i = 0; i < 13; i++) Arg(kSmRender, a++, cam[i]);
            Arg(kSmRender, a++, r); Arg(kSmRender, a++, g); Arg(kSmRender, a++, b); Arg(kSmRender, a++, density);
            Run(kSmRender, (long)w * h);
            var img = new byte[(long)w * h * 4];
            Check(CL.clEnqueueReadBuffer(queue, bImage, 1, UIntPtr.Zero, (UIntPtr)img.LongLength, img, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            return img;
        }

        public void Dispose()
        {
            foreach (var b in new[] { bufA, bufB, bFlag, bForce, bMacro, bSlice, bCoarse, bUx, bUy, bUz, bPhi, bPhiNew, bHat, bBar, bSources, bDepth, bImage })
                if (b != IntPtr.Zero) CL.clReleaseMemObject(b);
            foreach (var k in new[] { kStep, kBounds, kInit, kMacro, kSlice, kCoarse, kSmTrace, kSmCorrect, kSmInject, kSmClear, kSmRender })
                if (k != IntPtr.Zero) CL.clReleaseKernel(k);
            if (program != IntPtr.Zero) CL.clReleaseProgram(program);
            bufA = bufB = bFlag = bForce = bMacro = bSlice = bCoarse = IntPtr.Zero;
            bUx = bUy = bUz = bPhi = bPhiNew = bHat = bBar = bSources = bDepth = bImage = IntPtr.Zero;
            kStep = kBounds = kInit = kMacro = kSlice = kCoarse = IntPtr.Zero;
            kSmTrace = kSmCorrect = kSmInject = kSmClear = kSmRender = IntPtr.Zero;
            program = IntPtr.Zero;
        }

        // ------------------------------------------------------------ Kernel

        static string IntTable(string name, int[] v)
        {
            return "__constant int " + name + "[" + v.Length + "] = {" + string.Join(",", Array.ConvertAll(v, x => x.ToString())) + "};\n";
        }

        static int Find(int cx, int cy, int cz)
        {
            for (int i = 0; i < Q; i++) if (CX[i] == cx && CY[i] == cy && CZ[i] == cz) return i;
            throw new Exception("Richtung fehlt");
        }

        static string BuildSource()
        {
            var opp = new int[Q]; var ry = new int[Q]; var rz = new int[Q];
            var w = new string[Q];
            for (int i = 0; i < Q; i++)
            {
                opp[i] = i == 0 ? 0 : (i % 2 == 1 ? i + 1 : i - 1);
                ry[i] = Find(CX[i], -CY[i], CZ[i]);
                rz[i] = Find(CX[i], CY[i], -CZ[i]);
                int n2 = CX[i] * CX[i] + CY[i] * CY[i] + CZ[i] * CZ[i];
                w[i] = (n2 == 0 ? "0.33333333f" : n2 == 1 ? "0.055555556f" : "0.027777778f");
            }
            var sb = new StringBuilder();
            sb.Append(IntTable("CX", CX)).Append(IntTable("CY", CY)).Append(IntTable("CZ", CZ));
            sb.Append(IntTable("OPP", opp)).Append(IntTable("RY", ry)).Append(IntTable("RZ", rz));
            sb.Append("__constant float W[" + Q + "] = {" + string.Join(",", w) + "};\n");
            sb.Append(KernelBody);
            return sb.ToString();
        }

        const string KernelBody = @"
// Speicherform der Verteilungen: FP16 (halb so viele Bytes, doppelt so schnell) oder FP32.
// Gespeichert wird f - W[i] (die Abweichung vom Ruhezustand): kleine Zahlen, dadurch reicht FP16 aus.
#ifdef FP16
#define FT half
#define LD(p, k, i) (vload_half((k), (p)) + W[i])
#define ST(p, k, i, v) vstore_half((v) - W[i], (k), (p))
#else
#define FT float
#define LD(p, k, i) ((p)[k] + W[i])
#define ST(p, k, i, v) ((p)[k] = (v) - W[i])
#endif

// Ein Schritt: Strömen (Pull) + Bounce-Back + BGK-Stoß, dazu Kraft per Impulsaustausch (Summe pro Arbeitsgruppe).
// doForce = 0: Kraft nicht summieren (nur im letzten Schritt eines Pakets nötig).
// writeVel = 1: Geschwindigkeit für den Rauch mitschreiben (spart einen eigenen Lesedurchgang).
__kernel void lbm3_step(__global const FT* src, __global FT* dst, __global const uchar* flag,
                        __global float* gforce, float omega, int doForce,
                        __global float* vx, __global float* vy, __global float* vz, int writeVel)
{
    int c = get_global_id(0);
    int lid = get_local_id(0);
    float fx = 0.0f, fy = 0.0f, fz = 0.0f;
    if (c < N)
    {
        int x = c % NX;
        int y = (c / NX) % NY;
        int z = c / (NX * NY);
        if (flag[c] != 0)
        {
            for (int i = 0; i < 19; i++) ST(dst, i * N + c, i, W[i]);
            if (writeVel) { vx[c] = 0.0f; vy[c] = 0.0f; vz[c] = 0.0f; }
        }
        else
        {
            float f[19];
            for (int i = 0; i < 19; i++)
            {
                int sx = clamp(x - CX[i], 0, NX - 1);
                int sy = y - CY[i];
                int sz = z - CZ[i];
                int d = i;
                if (sy < 0 || sy >= NY) { sy = y; d = RY[d]; }
                if (sz < 0 || sz >= NZ) { sz = z; d = RZ[d]; }
                int sc = sx + NX * (sy + NY * sz);
                if (flag[sc] != 0)
                {
                    float v = LD(src, OPP[i] * N + c, OPP[i]);
                    f[i] = v;
                    // Kraft relativ zum Umgebungsdruck (Dichte 1): bei Körpern, die auf dem Boden stehen,
                    // drückt sonst der absolute Druck nur von oben. Für frei umströmte Körper ändert das nichts.
                    float m = 2.0f * (v - W[i]);
                    fx += m * (float)CX[OPP[i]];
                    fy += m * (float)CY[OPP[i]];
                    fz += m * (float)CZ[OPP[i]];
                }
                else f[i] = LD(src, d * N + sc, d);
            }
            float rho = 0.0f, ux = 0.0f, uy = 0.0f, uz = 0.0f;
            for (int i = 0; i < 19; i++)
            {
                rho += f[i];
                ux += f[i] * (float)CX[i]; uy += f[i] * (float)CY[i]; uz += f[i] * (float)CZ[i];
            }
            float inv = 1.0f / rho;
            ux *= inv; uy *= inv; uz *= inv;
            if (writeVel) { vx[c] = ux; vy[c] = uy; vz[c] = uz; }
            float usq = 1.5f * (ux * ux + uy * uy + uz * uz);
            for (int i = 0; i < 19; i++)
            {
                float cu = 3.0f * ((float)CX[i] * ux + (float)CY[i] * uy + (float)CZ[i] * uz);
                float feq = W[i] * rho * (1.0f + cu + 0.5f * cu * cu - usq);
                ST(dst, i * N + c, i, f[i] - omega * (f[i] - feq));
            }
        }
    }
    if (!doForce) return;   // für alle Arbeitsgruppen gleich, darum ohne Gefahr vor den Barrieren
    __local float lf[3 * LOCAL];
    lf[lid] = fx; lf[LOCAL + lid] = fy; lf[2 * LOCAL + lid] = fz;
    barrier(CLK_LOCAL_MEM_FENCE);
    for (int s = LOCAL / 2; s > 0; s >>= 1)
    {
        if (lid < s)
        {
            lf[lid] += lf[lid + s]; lf[LOCAL + lid] += lf[LOCAL + lid + s]; lf[2 * LOCAL + lid] += lf[2 * LOCAL + lid + s];
        }
        barrier(CLK_LOCAL_MEM_FENCE);
    }
    if (lid == 0)
    {
        int g = get_group_id(0);
        gforce[g * 3] = lf[0]; gforce[g * 3 + 1] = lf[LOCAL]; gforce[g * 3 + 2] = lf[2 * LOCAL];
    }
}

// Einlass (x = 0): Gleichgewicht mit uIn.
// Auslass (x = NX-1): Umgebungsdruck fest (Dichte 1), Geschwindigkeit und Nicht-Gleichgewichtsanteil von der Nachbarzelle.
// Ohne festen Druck wächst die Masse im Kanal langsam an (bei FP16 schneller) und verfälscht den Auftrieb.
__kernel void lbm3_bounds(__global FT* dst, float uIn)
{
    int j = get_global_id(0);
    if (j >= NY * NZ) return;
    int y = j % NY, z = j / NY;
    int cIn = NX * (y + NY * z);
    int cOut = cIn + NX - 1, cN = cOut - 1;
    float usq = 1.5f * uIn * uIn;
    float fn[19];
    float rho = 0.0f, ux = 0.0f, uy = 0.0f, uz = 0.0f;
    for (int i = 0; i < 19; i++)
    {
        float cu = 3.0f * (float)CX[i] * uIn;
        ST(dst, i * N + cIn, i, W[i] * (1.0f + cu + 0.5f * cu * cu - usq));
        fn[i] = LD(dst, i * N + cN, i);
        rho += fn[i]; ux += fn[i] * (float)CX[i]; uy += fn[i] * (float)CY[i]; uz += fn[i] * (float)CZ[i];
    }
    ux /= rho; uy /= rho; uz /= rho;
    float us = 1.5f * (ux * ux + uy * uy + uz * uz);
    for (int i = 0; i < 19; i++)
    {
        float cu = 3.0f * ((float)CX[i] * ux + (float)CY[i] * uy + (float)CZ[i] * uz);
        float e = W[i] * (1.0f + cu + 0.5f * cu * cu - us);   // Gleichgewicht für Dichte 1
        ST(dst, i * N + cOut, i, e + (fn[i] - rho * e));
    }
}

__kernel void lbm3_init(__global FT* fa, __global FT* fb, float uIn)
{
    int c = get_global_id(0);
    if (c >= N) return;
    float usq = 1.5f * uIn * uIn;
    for (int i = 0; i < 19; i++)
    {
        float cu = 3.0f * (float)CX[i] * uIn;
        float v = W[i] * (1.0f + cu + 0.5f * cu * cu - usq);
        ST(fa, i * N + c, i, v); ST(fb, i * N + c, i, v);
    }
}

__kernel void lbm3_macro(__global const FT* src, __global const uchar* flag, __global float* out, int comp)
{
    int c = get_global_id(0);
    if (c >= N) return;
    if (flag[c] != 0) { out[c] = comp == 3 ? 1.0f : 0.0f; return; }
    float rho = 0.0f, m = 0.0f;
    for (int i = 0; i < 19; i++)
    {
        float f = LD(src, i * N + c, i);
        rho += f;
        if (comp == 0) m += f * (float)CX[i];
        else if (comp == 1) m += f * (float)CY[i];
        else if (comp == 2) m += f * (float)CZ[i];
    }
    out[c] = comp == 3 ? rho : m / rho;
}

__kernel void lbm3_slice(__global const FT* src, __global const uchar* flag, __global float* out, int axis, int index, float invU)
{
    int j = get_global_id(0);
    int count = NX * (axis == 0 ? NZ : NY);
    if (j >= count) return;
    int x = j % NX, r = j / NX;
    int c = axis == 0 ? x + NX * (index + NY * r) : x + NX * (r + NY * index);
    if (flag[c] != 0) { out[j] = -1.0f; return; }
    float rho = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int i = 0; i < 19; i++)
    {
        float f = LD(src, i * N + c, i);
        rho += f; mx += f * (float)CX[i]; my += f * (float)CY[i]; mz += f * (float)CZ[i];
    }
    out[j] = sqrt(mx * mx + my * my + mz * mz) / rho * invU;
}

__kernel void lbm3_coarse(__global const FT* src, __global const uchar* flag, __global float* out, int s, int cnx, int cny, int cnz)
{
    int j = get_global_id(0);
    if (j >= cnx * cny * cnz) return;
    int i = j % cnx, k = (j / cnx) % cny, l = j / (cnx * cny);
    int x = min(i * s, NX - 1), y = min(k * s, NY - 1), z = min(l * s, NZ - 1);
    int c = x + NX * (y + NY * z);
    if (flag[c] != 0) { out[3 * j] = 0.0f; out[3 * j + 1] = 0.0f; out[3 * j + 2] = 0.0f; return; }
    float rho = 0.0f, mx = 0.0f, my = 0.0f, mz = 0.0f;
    for (int q = 0; q < 19; q++)
    {
        float f = LD(src, q * N + c, q);
        rho += f; mx += f * (float)CX[q]; my += f * (float)CY[q]; mz += f * (float)CZ[q];
    }
    out[3 * j] = mx / rho; out[3 * j + 1] = my / rho; out[3 * j + 2] = mz / rho;
}

// ---- Rauch ----

// trilinear in Gitterkoordinaten (Zelle i liegt bei i)
float trilin(__global const float* a, float x, float y, float z)
{
    x = clamp(x, 0.0f, (float)NX - 1.001f);
    y = clamp(y, 0.0f, (float)NY - 1.001f);
    z = clamp(z, 0.0f, (float)NZ - 1.001f);
    int x0 = (int)x, y0 = (int)y, z0 = (int)z;
    float fx = x - (float)x0, fy = y - (float)y0, fz = z - (float)z0;
    int c = x0 + NX * (y0 + NY * z0);
    const int dy = NX, dz = NX * NY;
    float c00 = a[c] * (1.0f - fx) + a[c + 1] * fx;
    float c10 = a[c + dy] * (1.0f - fx) + a[c + dy + 1] * fx;
    float c01 = a[c + dz] * (1.0f - fx) + a[c + dz + 1] * fx;
    float c11 = a[c + dy + dz] * (1.0f - fx) + a[c + dy + dz + 1] * fx;
    return (c00 * (1.0f - fy) + c10 * fy) * (1.0f - fz) + (c01 * (1.0f - fy) + c11 * fy) * fz;
}

// Rückverfolgung mit der Mittelpunktsregel
float3 back3(int c, float dt, __global const float* ux, __global const float* uy, __global const float* uz)
{
    float x = (float)(c % NX), y = (float)((c / NX) % NY), z = (float)(c / (NX * NY));
    float mx = x - 0.5f * dt * ux[c], my = y - 0.5f * dt * uy[c], mz = z - 0.5f * dt * uz[c];
    return (float3)(x - dt * trilin(ux, mx, my, mz), y - dt * trilin(uy, mx, my, mz), z - dt * trilin(uz, mx, my, mz));
}

__kernel void smoke3_trace(__global const float* src, __global float* dst, __global const float* ux, __global const float* uy,
                           __global const float* uz, __global const uchar* flag, float dt)
{
    int c = get_global_id(0);
    if (c >= N) return;
    if (flag[c] != 0) { dst[c] = 0.0f; return; }
    float3 b = back3(c, dt, ux, uy, uz);
    dst[c] = trilin(src, b.x, b.y, b.z);
}

__kernel void smoke3_correct(__global const float* phi, __global const float* hat, __global const float* bar, __global float* dst,
                             __global const float* ux, __global const float* uy, __global const float* uz,
                             __global const uchar* flag, float dt)
{
    int c = get_global_id(0);
    if (c >= N) return;
    if (flag[c] != 0) { dst[c] = 0.0f; return; }
    float3 b = back3(c, dt, ux, uy, uz);
    float bx = clamp(b.x, 0.0f, (float)NX - 1.001f), by = clamp(b.y, 0.0f, (float)NY - 1.001f), bz = clamp(b.z, 0.0f, (float)NZ - 1.001f);
    int k = (int)bx + NX * ((int)by + NY * (int)bz);
    const int dy = NX, dz = NX * NY;
    float lo = phi[k], hi = phi[k];
    int nb[7] = { 1, dy, dy + 1, dz, dz + 1, dz + dy, dz + dy + 1 };
    for (int i = 0; i < 7; i++) { float v = phi[k + nb[i]]; lo = fmin(lo, v); hi = fmax(hi, v); }
    float v = hat[c] + 0.5f * (phi[c] - bar[c]);
    dst[c] = clamp(v, lo, hi);
}

// Düsen: an jedem Quellpunkt eine kleine, weich auslaufende Kugel Rauch nachfüllen
__kernel void smoke3_inject(__global float* phi, __global const uchar* flag, __global const float4* pts, int count, int r, float radius)
{
    int j = get_global_id(0);
    int k = 2 * r + 1, kk = k * k * k;
    if (j >= count * kk) return;
    float4 p = pts[j / kk];
    int o = j % kk;
    int x = (int)round(p.x) + o % k - r, y = (int)round(p.y) + (o / k) % k - r, z = (int)round(p.z) + o / (k * k) - r;
    if (x < 1 || y < 0 || z < 0 || x >= NX || y >= NY || z >= NZ) return;
    int c = x + NX * (y + NY * z);
    if (flag[c] != 0) return;
    float d = length((float3)((float)x - p.x, (float)y - p.y, (float)z - p.z));
    float v = clamp((radius - d) / 0.6f + 0.5f, 0.0f, 1.0f);
    if (v > phi[c]) phi[c] = v;
}

__kernel void smoke3_clear(__global float* a)
{
    int c = get_global_id(0);
    if (c < N) a[c] = 0.0f;
}

// Volumen-Darstellung: Strahl je Bildpunkt durch den Kanal, Rauch verschluckt und streut Licht (von vorn nach hinten).
__kernel void smoke3_render(__global const float* phi, __global const float* invDepth, __global uchar4* img, int W, int H,
                            float cx, float cy, float cz, float fx, float fy, float fz, float rx, float ry, float rz,
                            float ux, float uy, float uz, float focal, float cr, float cg, float cb, float sigma)
{
    int p = get_global_id(0);
    if (p >= W * H) return;
    int i = p % W, j = p / W;
    float sx = ((float)i + 0.5f - 0.5f * (float)W) / focal, sy = ((float)j + 0.5f - 0.5f * (float)H) / focal;
    float3 f = (float3)(fx, fy, fz);
    float3 d = normalize(f + sx * (float3)(rx, ry, rz) - sy * (float3)(ux, uy, uz));
    float3 o = (float3)(cx, cy, cz);
    // Schnitt mit dem Kanal [0,NX] x [0,NY] x [0,NZ]
    float3 inv = 1.0f / d;
    float3 t0 = (0.0f - o) * inv, t1 = ((float3)((float)NX, (float)NY, (float)NZ) - o) * inv;
    float3 tmin = fmin(t0, t1), tmax = fmax(t0, t1);
    float tn = fmax(fmax(tmin.x, tmin.y), fmax(tmin.z, 0.0f)), tf = fmin(fmin(tmax.x, tmax.y), tmax.z);
    float iz = invDepth[p];
    if (iz > 0.0f) tf = fmin(tf, 1.0f / (iz * dot(d, f)));
    float3 acc = (float3)(0.0f, 0.0f, 0.0f);
    float alpha = 0.0f;
    if (tf > tn)
    {
        const float step = 0.5f;
        // kleiner, je Bildpunkt verschiedener Versatz gegen Streifenmuster
        float hs = sin((float)p * 12.9898f) * 43758.5453f;
        float jitter = hs - floor(hs);
        for (float t = tn + jitter * step; t < tf && alpha < 0.995f; t += step)
        {
            float3 q = o + t * d - 0.5f;
            float rho = trilin(phi, q.x, q.y, q.z);
            if (rho < 0.002f) continue;
            float a = 1.0f - exp(-sigma * rho * step);
            // etwas Licht von oben, damit der Rauch Form bekommt
            float light = 0.78f + 0.22f * clamp(q.z / (float)NZ, 0.0f, 1.0f);
            acc += (1.0f - alpha) * a * light * (float3)(cr, cg, cb);
            alpha += (1.0f - alpha) * a;
        }
    }
    img[p] = (uchar4)((uchar)(clamp(acc.x, 0.0f, 1.0f) * 255.0f), (uchar)(clamp(acc.y, 0.0f, 1.0f) * 255.0f),
                      (uchar)(clamp(acc.z, 0.0f, 1.0f) * 255.0f), (uchar)(clamp(alpha, 0.0f, 1.0f) * 255.0f));
}
";
    }

    /// <summary>Die benötigten Funktionen aus OpenCL.dll (Teil des Grafiktreibers).</summary>
    static class CL
    {
        const string Lib = "OpenCL.dll";
        public const ulong DEVICE_TYPE_GPU = 1 << 2;
        public const uint DEVICE_GLOBAL_MEM_SIZE = 0x101F;
        public const uint DEVICE_NAME = 0x102B;
        public const uint PROGRAM_BUILD_LOG = 0x1183;
        public const ulong MEM_READ_WRITE = 1;

        [DllImport(Lib)] public static extern int clGetPlatformIDs(uint num, [Out] IntPtr[] platforms, out uint count);
        [DllImport(Lib)] public static extern int clGetDeviceIDs(IntPtr platform, ulong type, uint num, [Out] IntPtr[] devices, out uint count);
        [DllImport(Lib)] public static extern int clGetDeviceInfo(IntPtr device, uint param, UIntPtr size, [Out] byte[] value, out UIntPtr sizeRet);
        [DllImport(Lib)] public static extern IntPtr clCreateContext(IntPtr props, uint num, IntPtr[] devices, IntPtr notify, IntPtr user, out int err);
        [DllImport(Lib)] public static extern IntPtr clCreateCommandQueue(IntPtr context, IntPtr device, ulong props, out int err);
        [DllImport(Lib)] public static extern IntPtr clCreateBuffer(IntPtr context, ulong flags, UIntPtr size, IntPtr host, out int err);
        [DllImport(Lib)] public static extern IntPtr clCreateProgramWithSource(IntPtr context, uint count, string[] sources, IntPtr lengths, out int err);
        [DllImport(Lib)] public static extern int clBuildProgram(IntPtr program, uint num, IntPtr[] devices, string options, IntPtr notify, IntPtr user);
        [DllImport(Lib)] public static extern int clGetProgramBuildInfo(IntPtr program, IntPtr device, uint param, UIntPtr size, [Out] byte[] value, out UIntPtr sizeRet);
        [DllImport(Lib)] public static extern IntPtr clCreateKernel(IntPtr program, string name, out int err);
        [DllImport(Lib)] public static extern int clSetKernelArg(IntPtr kernel, uint index, UIntPtr size, ref IntPtr value);
        [DllImport(Lib)] public static extern int clSetKernelArg(IntPtr kernel, uint index, UIntPtr size, ref int value);
        [DllImport(Lib)] public static extern int clSetKernelArg(IntPtr kernel, uint index, UIntPtr size, ref float value);
        [DllImport(Lib)] public static extern int clEnqueueNDRangeKernel(IntPtr queue, IntPtr kernel, uint dim, IntPtr offset, UIntPtr[] global, UIntPtr[] local, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [In] byte[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [In] float[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [Out] float[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [Out] byte[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clReleaseMemObject(IntPtr mem);
        [DllImport(Lib)] public static extern int clReleaseKernel(IntPtr kernel);
        [DllImport(Lib)] public static extern int clReleaseProgram(IntPtr program);
    }
}
