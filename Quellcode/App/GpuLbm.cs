using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Windkanal
{
    /// <summary>
    /// Rechnet denselben LBM-Löser wie <see cref="Solver"/> auf der Grafikkarte (OpenCL).
    /// OpenCL steckt in jedem aktuellen Grafiktreiber (NVIDIA, AMD, Intel), deshalb braucht der Build
    /// weder SDK noch Zusatzpakete. Die Kernel sind eine 1:1-Übersetzung von Solver.StepRow,
    /// Einlass/Auslass, Reset, ApplyMask und Kick.
    /// </summary>
    sealed class GpuLbm : IDisposable
    {
        /// <summary>Höchstzahl Schritte, deren Kräfte in einem Rutsch zurückgelesen werden.</summary>
        public const int MaxBatch = 512;

        // ------------------------------------------------------------ gemeinsamer Kontext

        static bool initTried;
        static IntPtr device, context, queue;
        static bool fp64;
        static string deviceName, initError;

        /// <summary>Name der Grafikkarte oder null, wenn keine nutzbar ist.</summary>
        public static string DeviceName { get { InitShared(); return deviceName; } }
        public static string InitError { get { InitShared(); return initError; } }

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
                deviceName = Encoding.ASCII.GetString(Info(device, CL.DEVICE_NAME)).TrimEnd('\0').Trim();
                fp64 = Encoding.ASCII.GetString(Info(device, CL.DEVICE_EXTENSIONS)).Contains("cl_khr_fp64");
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

        readonly int nx, ny, n, realSize;
        IntPtr program;
        IntPtr kStep, kBound, kReduce, kReset, kSetEq, kKick;
        IntPtr bufA, bufB, bFlag, bSponge, bRho, bUx, bUy, bSlot, bCell, bForce, bList;
        int cellCap, listCap, slots;
        bool swapped;
        readonly double[] forceD;
        readonly float[] forceF;

        IntPtr Src { get { return swapped ? bufB : bufA; } }
        IntPtr Dst { get { return swapped ? bufA : bufB; } }

        /// <summary>Legt den GPU-Löser an oder gibt null zurück (Grund in 'reason').</summary>
        public static GpuLbm TryCreate(int nx, int ny, float[] sponge, out string reason)
        {
            reason = null;
            if (DeviceName == null) { reason = InitError; return null; }
            GpuLbm g = null;
            try
            {
                g = new GpuLbm(nx, ny, sponge);
                return g;
            }
            catch (Exception e)
            {
                if (g != null) g.Dispose();
                reason = e.Message;
                return null;
            }
        }

        GpuLbm(int nx, int ny, float[] sponge)
        {
            this.nx = nx; this.ny = ny; n = nx * ny;
            realSize = fp64 ? 8 : 4;
            forceD = new double[2 * MaxBatch];
            forceF = new float[2 * MaxBatch];

            int err;
            program = CL.clCreateProgramWithSource(context, 1, new[] { KernelSource }, IntPtr.Zero, out err);
            Check(err, "clCreateProgramWithSource");
            string opts = "-cl-fp32-correctly-rounded-divide-sqrt -D NX=" + nx + " -D NY=" + ny + " -D N=" + n + (fp64 ? " -D USE_FP64" : "");
            err = CL.clBuildProgram(program, 1, new[] { device }, opts, IntPtr.Zero, IntPtr.Zero);
            if (err != 0)
            {
                UIntPtr size;
                CL.clGetProgramBuildInfo(program, device, CL.PROGRAM_BUILD_LOG, UIntPtr.Zero, null, out size);
                var log = new byte[(int)size];
                CL.clGetProgramBuildInfo(program, device, CL.PROGRAM_BUILD_LOG, size, log, out size);
                throw new Exception("OpenCL-Kernel konnte nicht übersetzt werden: " + Encoding.ASCII.GetString(log));
            }
            kStep = Kernel("lbm_step");
            kBound = Kernel("lbm_inlet_outlet");
            kReduce = Kernel("lbm_reduce_forces");
            kReset = Kernel("lbm_reset");
            kSetEq = Kernel("lbm_set_eq");
            kKick = Kernel("lbm_kick");

            bufA = Buffer(9L * n * 4);
            bufB = Buffer(9L * n * 4);
            bFlag = Buffer(n);
            bSponge = Buffer(nx * 4L);
            bRho = Buffer(n * 4L);
            bUx = Buffer(n * 4L);
            bUy = Buffer(n * 4L);
            bSlot = Buffer(n * 4L);
            bForce = Buffer(2L * MaxBatch * realSize);
            Check(CL.clEnqueueWriteBuffer(queue, bSponge, 1, UIntPtr.Zero, (UIntPtr)(nx * 4L), sponge, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
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

        static void Release(ref IntPtr b)
        {
            if (b != IntPtr.Zero) CL.clReleaseMemObject(b);
            b = IntPtr.Zero;
        }

        void Arg(IntPtr k, uint i, IntPtr buf) { Check(CL.clSetKernelArg(k, i, (UIntPtr)IntPtr.Size, ref buf), "clSetKernelArg"); }
        void Arg(IntPtr k, uint i, int v) { Check(CL.clSetKernelArg(k, i, (UIntPtr)4, ref v), "clSetKernelArg"); }
        void Arg(IntPtr k, uint i, float v) { Check(CL.clSetKernelArg(k, i, (UIntPtr)4, ref v), "clSetKernelArg"); }

        void Run(IntPtr k, long global, int local)
        {
            long g = (global + local - 1) / local * local;
            Check(CL.clEnqueueNDRangeKernel(queue, k, 1, IntPtr.Zero, new[] { (UIntPtr)g }, new[] { (UIntPtr)local }, 0, IntPtr.Zero, IntPtr.Zero),
                  "clEnqueueNDRangeKernel");
        }

        /// <summary>Alle Zellen auf Ruhe-Gleichgewicht (rho = 1, u = 0).</summary>
        public void Reset()
        {
            Arg(kReset, 0, bufA); Arg(kReset, 1, bufB); Arg(kReset, 2, bRho); Arg(kReset, 3, bUx); Arg(kReset, 4, bUy);
            Run(kReset, n, 128);
        }

        /// <summary>Neue Zellmarkierungen und Kraft-Slots; 'changed' Zellen werden auf Ruhe-Gleichgewicht gesetzt.</summary>
        public void ApplyMask(byte[] flags, int[] slotOf, int slotCount, int[] changed, int changedCount)
        {
            Check(CL.clEnqueueWriteBuffer(queue, bFlag, 1, UIntPtr.Zero, (UIntPtr)n, flags, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
            Check(CL.clEnqueueWriteBuffer(queue, bSlot, 1, UIntPtr.Zero, (UIntPtr)(n * 4L), slotOf, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
            slots = slotCount;
            long need = (long)MaxBatch * Math.Max(1, slotCount);
            if (need > cellCap)
            {
                Release(ref bCell);
                cellCap = (int)Math.Max(need, (long)MaxBatch * 256);
                bCell = Buffer(cellCap * 2L * realSize);
            }
            if (changedCount > 0)
            {
                if (changedCount > listCap)
                {
                    Release(ref bList);
                    listCap = Math.Max(changedCount, 4096);
                    bList = Buffer(listCap * 4L);
                }
                Check(CL.clEnqueueWriteBuffer(queue, bList, 1, UIntPtr.Zero, (UIntPtr)(changedCount * 4L), changed, 0, IntPtr.Zero, IntPtr.Zero), "Schreiben");
                Arg(kSetEq, 0, bufA); Arg(kSetEq, 1, bufB); Arg(kSetEq, 2, bRho); Arg(kSetEq, 3, bUx); Arg(kSetEq, 4, bUy);
                Arg(kSetEq, 5, bList); Arg(kSetEq, 6, changedCount);
                Run(kSetEq, changedCount, 128);
            }
        }

        /// <summary>Gaußförmige Querstörung (wie Solver.Kick) auf die aktuellen Verteilungen.</summary>
        public void Kick(float px, float py, float inv2s2, float u0)
        {
            Arg(kKick, 0, Src); Arg(kKick, 1, bFlag); Arg(kKick, 2, bRho); Arg(kKick, 3, bUx); Arg(kKick, 4, bUy);
            Arg(kKick, 5, px); Arg(kKick, 6, py); Arg(kKick, 7, inv2s2); Arg(kKick, 8, u0);
            Run(kKick, n, 128);
        }

        /// <summary>Reiht einen Zeitschritt ein; 'k' ist die Nummer innerhalb des aktuellen Rutsches.</summary>
        public void EnqueueStep(int k, float tau0, float spongeAmp, float smagK, float uIn, bool noSlip)
        {
            IntPtr src = Src, dst = Dst;
            Arg(kStep, 0, src); Arg(kStep, 1, dst); Arg(kStep, 2, bFlag); Arg(kStep, 3, bSponge);
            Arg(kStep, 4, bRho); Arg(kStep, 5, bUx); Arg(kStep, 6, bUy); Arg(kStep, 7, bSlot); Arg(kStep, 8, bCell);
            Arg(kStep, 9, k * slots); Arg(kStep, 10, tau0); Arg(kStep, 11, spongeAmp); Arg(kStep, 12, smagK);
            Arg(kStep, 13, uIn); Arg(kStep, 14, noSlip ? 1 : 0);
            Run(kStep, n, 128);

            Arg(kBound, 0, dst); Arg(kBound, 1, bRho); Arg(kBound, 2, bUx); Arg(kBound, 3, bUy); Arg(kBound, 4, uIn);
            Run(kBound, ny, 64);
            swapped = !swapped;
        }

        /// <summary>Summiert die Kräfte der letzten 'steps' Schritte und liest sie zurück.</summary>
        public void ReadForces(int steps, double[] fx, double[] fy, int offset)
        {
            if (slots == 0)
            {
                for (int k = 0; k < steps; k++) { fx[offset + k] = 0; fy[offset + k] = 0; }
                return;
            }
            Arg(kReduce, 0, bCell); Arg(kReduce, 1, slots); Arg(kReduce, 2, bForce);
            Run(kReduce, steps * 256L, 256);
            UIntPtr bytes = (UIntPtr)(2L * steps * realSize);
            if (fp64)
            {
                Check(CL.clEnqueueReadBuffer(queue, bForce, 1, UIntPtr.Zero, bytes, forceD, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
                for (int k = 0; k < steps; k++) { fx[offset + k] = forceD[2 * k]; fy[offset + k] = forceD[2 * k + 1]; }
            }
            else
            {
                Check(CL.clEnqueueReadBuffer(queue, bForce, 1, UIntPtr.Zero, bytes, forceF, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
                for (int k = 0; k < steps; k++) { fx[offset + k] = forceF[2 * k]; fy[offset + k] = forceF[2 * k + 1]; }
            }
        }

        /// <summary>Kopiert Dichte und Geschwindigkeit für Anzeige und Auswertung in den Hauptspeicher.</summary>
        public void ReadMacros(float[] rho, float[] ux, float[] uy)
        {
            UIntPtr bytes = (UIntPtr)(n * 4L);
            Check(CL.clEnqueueReadBuffer(queue, bRho, 0, UIntPtr.Zero, bytes, rho, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            Check(CL.clEnqueueReadBuffer(queue, bUx, 0, UIntPtr.Zero, bytes, ux, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
            Check(CL.clEnqueueReadBuffer(queue, bUy, 1, UIntPtr.Zero, bytes, uy, 0, IntPtr.Zero, IntPtr.Zero), "Lesen");
        }

        public void Dispose()
        {
            if (queue != IntPtr.Zero) CL.clFinish(queue);
            Release(ref bufA); Release(ref bufB); Release(ref bFlag); Release(ref bSponge); Release(ref bRho);
            Release(ref bUx); Release(ref bUy); Release(ref bSlot); Release(ref bCell); Release(ref bForce); Release(ref bList);
            foreach (var k in new[] { kStep, kBound, kReduce, kReset, kSetEq, kKick })
                if (k != IntPtr.Zero) CL.clReleaseKernel(k);
            kStep = kBound = kReduce = kReset = kSetEq = kKick = IntPtr.Zero;
            if (program != IntPtr.Zero) CL.clReleaseProgram(program);
            program = IntPtr.Zero;
        }

        // ------------------------------------------------------------ Kernel (OpenCL C)

        const string KernelSource = @"
#pragma OPENCL FP_CONTRACT OFF
#ifdef USE_FP64
#pragma OPENCL EXTENSION cl_khr_fp64 : enable
typedef double real;
typedef double2 real2;
#else
typedef float real;
typedef float2 real2;
#endif

#define FLUID 0
#define BOUNDARY 1
#define SOLID 2

__constant int CX[9] = { 0, 1, 0, -1, 0, 1, -1, -1, 1 };
__constant int CY[9] = { 0, 0, 1, 0, -1, 1, 1, -1, -1 };
__constant int OPP[9] = { 0, 3, 4, 1, 2, 7, 8, 5, 6 };
__constant int MIRY[9] = { 0, 1, 4, 3, 2, 8, 7, 6, 5 };
__constant float W[9] = { 4.0f / 9, 1.0f / 9, 1.0f / 9, 1.0f / 9, 1.0f / 9, 1.0f / 36, 1.0f / 36, 1.0f / 36, 1.0f / 36 };

__kernel void lbm_step(__global const float* src, __global float* dst, __global const uchar* flag,
                       __global const float* sp, __global float* rhoA, __global float* uxA, __global float* uyA,
                       __global const int* fslot, __global real2* fcell, int slotBase,
                       float tau0, float spongeAmp, float smagK, float uIn, int noSlip)
{
    int c = get_global_id(0);
    if (c >= N) return;
    int x = c % NX, y = c / NX;
    if (x < 1 || x > NX - 2) return;
    uchar fl = flag[c];
    if (fl == SOLID) return;

    float f[9];
    real fxs = 0, fys = 0;
    if (fl == FLUID)
    {
        f[0] = src[c];
        f[1] = src[N + c - 1];
        f[2] = src[2 * N + c - NX];
        f[3] = src[3 * N + c + 1];
        f[4] = src[4 * N + c + NX];
        f[5] = src[5 * N + c - NX - 1];
        f[6] = src[6 * N + c - NX + 1];
        f[7] = src[7 * N + c + NX + 1];
        f[8] = src[8 * N + c + NX - 1];
    }
    else
    {
        int rowBase = y * NX;
        f[0] = src[c];
        #pragma unroll
        for (int i = 1; i < 9; i++)
        {
            int sx = x - CX[i], sy = y - CY[i];
            if (sy < 0 || sy >= NY)
            {
                int mc = rowBase + sx;
                if (noSlip || flag[mc] == SOLID) f[i] = src[OPP[i] * N + c];
                else f[i] = src[MIRY[i] * N + mc];
            }
            else
            {
                int sc = sy * NX + sx;
                if (flag[sc] == SOLID)
                {
                    float v = src[OPP[i] * N + c];
                    f[i] = v;
                    fxs -= (real)2.0 * (real)v * (real)CX[i];
                    fys -= (real)2.0 * (real)v * (real)CY[i];
                }
                else f[i] = src[i * N + sc];
            }
        }
    }

    float f0 = f[0], f1 = f[1], f2 = f[2], f3 = f[3], f4 = f[4], f5 = f[5], f6 = f[6], f7 = f[7], f8 = f[8];
    float rho = f0 + f1 + f2 + f3 + f4 + f5 + f6 + f7 + f8;
    float inv = 1.0f / rho;
    float ux = (f1 - f3 + f5 - f6 - f7 + f8) * inv;
    float uy = (f2 - f4 + f5 + f6 - f7 - f8) * inv;
    float u2 = ux * ux + uy * uy;
    if (u2 > 0.0625f)
    {
        float scl = 0.25f / sqrt(u2);
        ux *= scl; uy *= scl;
    }
    float spx = sp[x];
    if (spx > 0.0f)
    {
        float damp = 0.06f * spx;
        ux += damp * (uIn - ux);
        uy -= damp * uy;
    }
    rhoA[c] = rho; uxA[c] = ux; uyA[c] = uy;

    float usq = 1.5f * (ux * ux + uy * uy);
    float r1 = rho * (1.0f / 9.0f), r2 = rho * (1.0f / 36.0f);
    float e0 = rho * (4.0f / 9.0f) * (1.0f - usq);
    float e1 = r1 * (1.0f + 3.0f * ux + 4.5f * ux * ux - usq);
    float e3 = r1 * (1.0f - 3.0f * ux + 4.5f * ux * ux - usq);
    float e2 = r1 * (1.0f + 3.0f * uy + 4.5f * uy * uy - usq);
    float e4 = r1 * (1.0f - 3.0f * uy + 4.5f * uy * uy - usq);
    float a = ux + uy, b = uy - ux;
    float e5 = r2 * (1.0f + 3.0f * a + 4.5f * a * a - usq);
    float e7 = r2 * (1.0f - 3.0f * a + 4.5f * a * a - usq);
    float e6 = r2 * (1.0f + 3.0f * b + 4.5f * b * b - usq);
    float e8 = r2 * (1.0f - 3.0f * b + 4.5f * b * b - usq);

    float pxx = (f1 - e1) + (f3 - e3) + (f5 - e5) + (f6 - e6) + (f7 - e7) + (f8 - e8);
    float pyy = (f2 - e2) + (f4 - e4) + (f5 - e5) + (f6 - e6) + (f7 - e7) + (f8 - e8);
    float pxy = (f5 - e5) - (f6 - e6) + (f7 - e7) - (f8 - e8);

    float q = sqrt(pxx * pxx + pyy * pyy + 2.0f * pxy * pxy);
    float tau = tau0 + spongeAmp * spx;
    float tauE = 0.5f * (tau + sqrt(tau * tau + smagK * q * inv));
    float k = 1.0f - 1.0f / tauE;

    float tr = (pxx + pyy) * (1.0f / 3.0f);
    float n0 = -2.0f * tr;
    float nx1 = 0.5f * (pxx - tr);
    float ny1 = 0.5f * (pyy - tr);
    float nd = 0.125f * (2.0f * tr);
    float nxy = 0.125f * 2.0f * pxy;

    dst[c] = e0 + k * n0;
    dst[N + c] = e1 + k * nx1;
    dst[2 * N + c] = e2 + k * ny1;
    dst[3 * N + c] = e3 + k * nx1;
    dst[4 * N + c] = e4 + k * ny1;
    dst[5 * N + c] = e5 + k * (nd + nxy);
    dst[6 * N + c] = e6 + k * (nd - nxy);
    dst[7 * N + c] = e7 + k * (nd + nxy);
    dst[8 * N + c] = e8 + k * (nd - nxy);

    if (fl == BOUNDARY)
    {
        int s = fslot[c];
        if (s >= 0) fcell[slotBase + s] = (real2)(fxs, fys);
    }
}

__kernel void lbm_inlet_outlet(__global float* dst, __global float* rhoA, __global float* uxA, __global float* uyA, float uIn)
{
    int y = get_global_id(0);
    if (y >= NY) return;
    {
        int c = y * NX;
        float rho = rhoA[c + 1];
        if (!(rho > 0.2f && rho < 5.0f)) rho = 1.0f;
        float ux = uIn, usq = 1.5f * ux * ux;
        for (int i = 0; i < 9; i++)
        {
            float cu = 3.0f * ((float)CX[i] * ux);
            dst[i * N + c] = W[i] * rho * (1.0f + cu + 0.5f * cu * cu - usq);
        }
        rhoA[c] = rho; uxA[c] = ux; uyA[c] = 0.0f;
    }
    {
        int c = y * NX + NX - 1, cn = c - 1;
        float rn = rhoA[cn], ux = uxA[cn], uy = uyA[cn];
        if (!(rn > 0.2f && rn < 5.0f)) { rn = 1.0f; ux = uIn; uy = 0.0f; }
        float usq = 1.5f * (ux * ux + uy * uy);
        for (int i = 0; i < 9; i++)
        {
            float cu = 3.0f * ((float)CX[i] * ux + (float)CY[i] * uy);
            float eq = W[i] * (1.0f + cu + 0.5f * cu * cu - usq);
            dst[i * N + c] = eq + (dst[i * N + cn] - rn * eq);
        }
        rhoA[c] = 1.0f; uxA[c] = ux; uyA[c] = uy;
    }
}

__kernel void lbm_reduce_forces(__global const real2* fcell, int slots, __global real* out)
{
    __local real sx[256];
    __local real sy[256];
    int k = get_group_id(0), lid = get_local_id(0);
    __global const real2* p = fcell + (long)k * slots;
    real ax = 0, ay = 0;
    for (int j = lid; j < slots; j += 256) { real2 v = p[j]; ax += v.x; ay += v.y; }
    sx[lid] = ax; sy[lid] = ay;
    barrier(CLK_LOCAL_MEM_FENCE);
    for (int s = 128; s > 0; s >>= 1)
    {
        if (lid < s) { sx[lid] += sx[lid + s]; sy[lid] += sy[lid + s]; }
        barrier(CLK_LOCAL_MEM_FENCE);
    }
    if (lid == 0) { out[2 * k] = sx[0]; out[2 * k + 1] = sy[0]; }
}

__kernel void lbm_reset(__global float* fa, __global float* fb, __global float* rhoA, __global float* uxA, __global float* uyA)
{
    int c = get_global_id(0);
    if (c >= N) return;
    for (int i = 0; i < 9; i++) { fa[i * N + c] = W[i]; fb[i * N + c] = W[i]; }
    rhoA[c] = 1.0f; uxA[c] = 0.0f; uyA[c] = 0.0f;
}

__kernel void lbm_set_eq(__global float* fa, __global float* fb, __global float* rhoA, __global float* uxA, __global float* uyA,
                         __global const int* list, int count)
{
    int j = get_global_id(0);
    if (j >= count) return;
    int c = list[j];
    for (int i = 0; i < 9; i++) { fa[i * N + c] = W[i]; fb[i * N + c] = W[i]; }
    rhoA[c] = 1.0f; uxA[c] = 0.0f; uyA[c] = 0.0f;
}

__kernel void lbm_kick(__global float* src, __global const uchar* flag, __global const float* rhoA, __global const float* uxA,
                       __global float* uyA, float px, float py, float inv2s2, float u0)
{
    int c = get_global_id(0);
    if (c >= N) return;
    if (flag[c] == SOLID) return;
    float dx = (float)(c % NX) - px, dy = (float)(c / NX) - py;
    float arg = -(dx * dx + dy * dy) * inv2s2;
    float d = 0.1f * u0 * (float)exp((real)arg);
    if (d < 1e-6f) return;
    float rho = rhoA[c], ux = uxA[c], uy0 = uyA[c], uy1 = uy0 + d;
    float us0 = 1.5f * (ux * ux + uy0 * uy0), us1 = 1.5f * (ux * ux + uy1 * uy1);
    for (int i = 0; i < 9; i++)
    {
        float cu0 = 3.0f * ((float)CX[i] * ux + (float)CY[i] * uy0), cu1 = 3.0f * ((float)CX[i] * ux + (float)CY[i] * uy1);
        src[i * N + c] += W[i] * rho * ((cu1 + 0.5f * cu1 * cu1 - us1) - (cu0 + 0.5f * cu0 * cu0 - us0));
    }
    uyA[c] = uy1;
}
";
    }

    /// <summary>Die benötigten Funktionen aus OpenCL.dll (Teil des Grafiktreibers).</summary>
    static class CL
    {
        const string Lib = "OpenCL.dll";
        public const ulong DEVICE_TYPE_GPU = 1 << 2;
        public const uint DEVICE_MAX_COMPUTE_UNITS = 0x1002;
        public const uint DEVICE_GLOBAL_MEM_SIZE = 0x101F;
        public const uint DEVICE_NAME = 0x102B;
        public const uint DEVICE_EXTENSIONS = 0x1030;
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
        [DllImport(Lib)] public static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [In] float[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [In] byte[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueWriteBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [In] int[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [Out] float[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clEnqueueReadBuffer(IntPtr queue, IntPtr buffer, uint blocking, UIntPtr offset, UIntPtr size, [Out] double[] data, uint numEvents, IntPtr events, IntPtr evt);
        [DllImport(Lib)] public static extern int clFinish(IntPtr queue);
        [DllImport(Lib)] public static extern int clReleaseMemObject(IntPtr mem);
        [DllImport(Lib)] public static extern int clReleaseKernel(IntPtr kernel);
        [DllImport(Lib)] public static extern int clReleaseProgram(IntPtr program);
    }
}
