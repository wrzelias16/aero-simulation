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
    /// Randbedingungen: Einlass x=0 mit fester Geschwindigkeit, Auslass x=NX-1 mit Nullgradient,
    /// Seitenwände (y, z) reibungsfrei (Spiegelung). Feste Körper: Bounce-Back an der halben Strecke,
    /// Kraft per Impulsaustausch. Alle Größen in Gittereinheiten (Dichte 1, Zellgröße 1, Zeitschritt 1).
    /// </summary>
    public sealed class Lbm3D : IDisposable
    {
        public const int Q = 19;
        const int Local = 128;

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

        /// <summary>Wie viele Zellen in den Grafikspeicher passen (mit Reserve). 19 Richtungen x 2 Felder x 4 Byte + Flags.</summary>
        public static long MaxCells { get { InitShared(); return (long)(globalMem * 0.6 / (Q * 2 * 4 + 1)); } }

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
        readonly int groups;
        IntPtr program, kStep, kBounds, kInit, kMacro, kSlice, kCoarse;
        IntPtr bufA, bufB, bFlag, bForce, bMacro, bSlice, bCoarse;
        long coarseBytes;
        bool swapped;
        readonly float[] forceHost;

        IntPtr Src { get { return swapped ? bufB : bufA; } }
        IntPtr Dst { get { return swapped ? bufA : bufB; } }

        /// <summary>Legt den Löser an. Wirft eine Exception mit Klartext-Grund, wenn das nicht geht.</summary>
        public Lbm3D(int nx, int ny, int nz)
        {
            if (DeviceName == null) throw new Exception(InitError ?? "keine Grafikkarte");
            long cells = (long)nx * ny * nz;
            if (cells > MaxCells) throw new Exception("Gitter zu groß für den Grafikspeicher (" + cells + " Zellen, höchstens " + MaxCells + ")");
            NX = nx; NY = ny; NZ = nz; N = nx * ny * nz;
            groups = (N + Local - 1) / Local;
            forceHost = new float[groups * 3];

            int err;
            program = CL.clCreateProgramWithSource(context, 1, new[] { BuildSource() }, IntPtr.Zero, out err);
            Check(err, "clCreateProgramWithSource");
            string opts = "-cl-fp32-correctly-rounded-divide-sqrt -D NX=" + nx + " -D NY=" + ny + " -D NZ=" + nz + " -D N=" + N;
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

            bufA = Buffer((long)Q * N * 4);
            bufB = Buffer((long)Q * N * 4);
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

        static void Run(IntPtr k, long global)
        {
            long g = (global + Local - 1) / Local * Local;
            Check(CL.clEnqueueNDRangeKernel(queue, k, 1, IntPtr.Zero, new[] { (UIntPtr)g }, new[] { (UIntPtr)Local }, 0, IntPtr.Zero, IntPtr.Zero),
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
            for (int s = 0; s < steps; s++)
            {
                Arg(kStep, 0, Src); Arg(kStep, 1, Dst); Arg(kStep, 2, bFlag); Arg(kStep, 3, bForce); Arg(kStep, 4, omega);
                Run(kStep, N);
                Arg(kBounds, 0, Dst); Arg(kBounds, 1, uIn);
                Run(kBounds, (long)NY * NZ);
                swapped = !swapped;
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

        public void Dispose()
        {
            foreach (var b in new[] { bufA, bufB, bFlag, bForce, bMacro, bSlice, bCoarse }) if (b != IntPtr.Zero) CL.clReleaseMemObject(b);
            foreach (var k in new[] { kStep, kBounds, kInit, kMacro, kSlice, kCoarse }) if (k != IntPtr.Zero) CL.clReleaseKernel(k);
            if (program != IntPtr.Zero) CL.clReleaseProgram(program);
            bufA = bufB = bFlag = bForce = bMacro = bSlice = bCoarse = IntPtr.Zero;
            kStep = kBounds = kInit = kMacro = kSlice = kCoarse = IntPtr.Zero;
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
#define LOCAL 128

// Ein Schritt: Strömen (Pull) + Bounce-Back + BGK-Stoß, dazu Kraft per Impulsaustausch (Summe pro Arbeitsgruppe).
__kernel void lbm3_step(__global const float* src, __global float* dst, __global const uchar* flag,
                        __global float* gforce, float omega)
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
            for (int i = 0; i < 19; i++) dst[i * N + c] = W[i];
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
                    float v = src[OPP[i] * N + c];
                    f[i] = v;
                    // Kraft relativ zum Umgebungsdruck (Dichte 1): bei Körpern, die auf dem Boden stehen,
                    // drückt sonst der absolute Druck nur von oben. Für frei umströmte Körper ändert das nichts.
                    float m = 2.0f * (v - W[i]);
                    fx += m * (float)CX[OPP[i]];
                    fy += m * (float)CY[OPP[i]];
                    fz += m * (float)CZ[OPP[i]];
                }
                else f[i] = src[d * N + sc];
            }
            float rho = 0.0f, ux = 0.0f, uy = 0.0f, uz = 0.0f;
            for (int i = 0; i < 19; i++)
            {
                rho += f[i];
                ux += f[i] * (float)CX[i]; uy += f[i] * (float)CY[i]; uz += f[i] * (float)CZ[i];
            }
            float inv = 1.0f / rho;
            ux *= inv; uy *= inv; uz *= inv;
            float usq = 1.5f * (ux * ux + uy * uy + uz * uz);
            for (int i = 0; i < 19; i++)
            {
                float cu = 3.0f * ((float)CX[i] * ux + (float)CY[i] * uy + (float)CZ[i] * uz);
                float feq = W[i] * rho * (1.0f + cu + 0.5f * cu * cu - usq);
                dst[i * N + c] = f[i] - omega * (f[i] - feq);
            }
        }
    }
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

// Einlass (x = 0): Gleichgewicht mit uIn. Auslass (x = NX-1): Werte der Nachbarzelle übernehmen.
__kernel void lbm3_bounds(__global float* dst, float uIn)
{
    int j = get_global_id(0);
    if (j >= NY * NZ) return;
    int y = j % NY, z = j / NY;
    int cIn = NX * (y + NY * z);
    int cOut = cIn + NX - 1;
    float usq = 1.5f * uIn * uIn;
    for (int i = 0; i < 19; i++)
    {
        float cu = 3.0f * (float)CX[i] * uIn;
        dst[i * N + cIn] = W[i] * (1.0f + cu + 0.5f * cu * cu - usq);
        dst[i * N + cOut] = dst[i * N + cOut - 1];
    }
}

__kernel void lbm3_init(__global float* fa, __global float* fb, float uIn)
{
    int c = get_global_id(0);
    if (c >= N) return;
    float usq = 1.5f * uIn * uIn;
    for (int i = 0; i < 19; i++)
    {
        float cu = 3.0f * (float)CX[i] * uIn;
        float v = W[i] * (1.0f + cu + 0.5f * cu * cu - usq);
        fa[i * N + c] = v; fb[i * N + c] = v;
    }
}

__kernel void lbm3_macro(__global const float* src, __global const uchar* flag, __global float* out, int comp)
{
    int c = get_global_id(0);
    if (c >= N) return;
    if (flag[c] != 0) { out[c] = comp == 3 ? 1.0f : 0.0f; return; }
    float rho = 0.0f, m = 0.0f;
    for (int i = 0; i < 19; i++)
    {
        float f = src[i * N + c];
        rho += f;
        if (comp == 0) m += f * (float)CX[i];
        else if (comp == 1) m += f * (float)CY[i];
        else if (comp == 2) m += f * (float)CZ[i];
    }
    out[c] = comp == 3 ? rho : m / rho;
}

__kernel void lbm3_slice(__global const float* src, __global const uchar* flag, __global float* out, int axis, int index, float invU)
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
        float f = src[i * N + c];
        rho += f; mx += f * (float)CX[i]; my += f * (float)CY[i]; mz += f * (float)CZ[i];
    }
    out[j] = sqrt(mx * mx + my * my + mz * mz) / rho * invU;
}

__kernel void lbm3_coarse(__global const float* src, __global const uchar* flag, __global float* out, int s, int cnx, int cny, int cnz)
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
        float f = src[q * N + c];
        rho += f; mx += f * (float)CX[q]; my += f * (float)CY[q]; mz += f * (float)CZ[q];
    }
    out[3 * j] = mx / rho; out[3 * j + 1] = my / rho; out[3 * j + 2] = mz / rho;
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
        [DllImport(Lib)] public static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [Out] float[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clReleaseMemObject(IntPtr mem);
        [DllImport(Lib)] public static extern int clReleaseKernel(IntPtr kernel);
        [DllImport(Lib)] public static extern int clReleaseProgram(IntPtr program);
    }
}
