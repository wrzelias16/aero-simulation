using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Windkanal
{
    /// <summary>
    /// Nimmt Bilder in Echtzeit auf und schreibt sie im Hintergrund als MP4 (H.264 über Media Foundation, Teil von Windows)
    /// oder als GIF. Die Zeitstempel sind die echten Aufnahmezeiten: was in 0,4 s passiert, dauert im Video 0,4 s.
    /// </summary>
    public sealed class Recorder : IDisposable
    {
        public readonly bool Mp4;
        public readonly string TempFile;
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Queue<KeyValuePair<long, int[]>> queue = new Queue<KeyValuePair<long, int[]>>();
        readonly Thread worker;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        volatile bool stopping;
        int w, h;
        double lastFrameMs = -1e9;
        const double MinFrameMs = 1000.0 / 30;   // höchstens 30 Bilder pro Sekunde
        public string Error;
        public int Frames { get; private set; }
        public double Seconds { get { return clock.Elapsed.TotalSeconds; } }

        public Recorder(bool mp4)
        {
            Mp4 = mp4;
            TempFile = Path.Combine(Path.GetTempPath(), "windkanal-aufnahme-" + Guid.NewGuid().ToString("N") + (mp4 ? ".mp4" : ".gif"));
            worker = new Thread(Work) { IsBackground = true, Name = "Windkanal-Aufnahme" };
            worker.Start();
        }

        /// <summary>Bild übernehmen (wird auf 30 Bilder/s ausgedünnt). Größe richtet sich nach dem ersten Bild.</summary>
        public void AddFrame(Bitmap bmp)
        {
            if (stopping || Error != null) return;
            double now = clock.Elapsed.TotalMilliseconds;
            if (now - lastFrameMs < MinFrameMs) return;
            lastFrameMs = now;
            if (w == 0)
            {
                // MP4 braucht gerade Maße; GIF wird auf höchstens 800 Bildpunkte Breite verkleinert (sonst riesig)
                double s = Mp4 ? 1 : Math.Min(1, 800.0 / bmp.Width);
                w = Math.Max(2, (int)(bmp.Width * s) & ~1);
                h = Math.Max(2, (int)(bmp.Height * s) & ~1);
            }
            var px = new int[w * h];
            using (var scaled = bmp.Width == w && bmp.Height == h ? null : new Bitmap(bmp, w, h))
            {
                var src = scaled ?? bmp;
                var d = src.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                for (int y = 0; y < h; y++) Marshal.Copy(d.Scan0 + y * d.Stride, px, y * w, w);
                src.UnlockBits(d);
            }
            lock (queue)
            {
                if (queue.Count > 90) return;   // Hintergrund kommt nicht hinterher: Bild auslassen statt Speicher zu füllen
                queue.Enqueue(new KeyValuePair<long, int[]>((long)(now * 10000), px));
            }
            wake.Set();
        }

        /// <summary>Aufnahme beenden und auf das Schreiben warten. Danach liegt die Datei in TempFile.</summary>
        public void Finish()
        {
            stopping = true;
            wake.Set();
            worker.Join();
        }

        public void Dispose()
        {
            if (!stopping) Finish();
            try { if (File.Exists(TempFile)) File.Delete(TempFile); } catch { }
        }

        void Work()
        {
            IFrameWriter writer = null;
            try
            {
                while (true)
                {
                    KeyValuePair<long, int[]> f;
                    bool got = false;
                    lock (queue) { if (queue.Count > 0) { f = queue.Dequeue(); got = true; } else f = default(KeyValuePair<long, int[]>); }
                    if (!got)
                    {
                        if (stopping) break;
                        wake.WaitOne(50);
                        continue;
                    }
                    if (writer == null) writer = Mp4 ? (IFrameWriter)new Mp4Writer(TempFile, w, h) : new GifWriter(TempFile, w, h, f.Value);
                    writer.Write(f.Value, f.Key);
                    Frames++;
                }
                if (writer != null) writer.Close((long)(clock.Elapsed.TotalMilliseconds * 10000));
            }
            catch (Exception e)
            {
                Error = e.Message;
                try { if (writer != null) writer.Close(0); } catch { }
            }
        }
    }

    interface IFrameWriter
    {
        /// <param name="time">Zeitpunkt in 100 ns</param>
        void Write(int[] px, long time);
        void Close(long endTime);
    }

    // ====================================================================== GIF

    /// <summary>
    /// GIF89a mit einer gemeinsamen Farbtafel (256 Farben, Median-Cut aus dem ersten Bild) und LZW-Kompression.
    /// Wird Bild für Bild geschrieben, damit lange Aufnahmen keinen Speicher füllen.
    /// </summary>
    sealed class GifWriter : IFrameWriter
    {
        readonly Stream fs;
        readonly int w, h;
        readonly int[] palette;
        readonly byte[] lookup = new byte[32768];   // 15-Bit-Farbe -> Index in der Farbtafel
        long lastTime = -1;
        byte[] pending;

        public GifWriter(string path, int width, int height, int[] first)
        {
            w = width; h = height;
            palette = MedianCut(first, 256);
            for (int c = 0; c < 32768; c++)
            {
                int r = ((c >> 10) & 31) * 255 / 31, g = ((c >> 5) & 31) * 255 / 31, b = (c & 31) * 255 / 31;
                int best = 0, bd = int.MaxValue;
                for (int i = 0; i < palette.Length; i++)
                {
                    int p = palette[i], dr = ((p >> 16) & 255) - r, dg = ((p >> 8) & 255) - g, db = (p & 255) - b;
                    int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
                    if (d < bd) { bd = d; best = i; }
                }
                lookup[c] = (byte)best;
            }
            fs = new BufferedStream(File.Create(path), 1 << 16);
            var hd = new List<byte>();
            hd.AddRange(System.Text.Encoding.ASCII.GetBytes("GIF89a"));
            hd.Add((byte)w); hd.Add((byte)(w >> 8)); hd.Add((byte)h); hd.Add((byte)(h >> 8));
            hd.Add(0xF7); hd.Add(0); hd.Add(0);   // globale Farbtafel mit 256 Einträgen
            for (int i = 0; i < 256; i++)
            {
                int p = i < palette.Length ? palette[i] : 0;
                hd.Add((byte)(p >> 16)); hd.Add((byte)(p >> 8)); hd.Add((byte)p);
            }
            // endlos wiederholen
            hd.AddRange(new byte[] { 0x21, 0xFF, 0x0B });
            hd.AddRange(System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            hd.AddRange(new byte[] { 3, 1, 0, 0, 0 });
            fs.Write(hd.ToArray(), 0, hd.Count);
        }

        public void Write(int[] px, long time)
        {
            if (pending != null) Flush(time);
            // leichtes geordnetes Rastern gegen Stufen in Farbverläufen
            var idx = new byte[w * h];
            int[] bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int p = px[y * w + x], t = bayer[(y & 3) * 4 + (x & 3)] - 8;
                    int r = Clamp(((p >> 16) & 255) + t), g = Clamp(((p >> 8) & 255) + t), b = Clamp((p & 255) + t);
                    idx[y * w + x] = lookup[((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3)];
                }
            pending = idx;
            lastTime = time;
        }

        static int Clamp(int v) { return v < 0 ? 0 : v > 255 ? 255 : v; }

        void Flush(long nextTime)
        {
            int delay = Math.Max(2, (int)Math.Round((nextTime - lastTime) / 100000.0));   // Hundertstelsekunden
            fs.Write(new byte[] { 0x21, 0xF9, 4, 0, (byte)delay, (byte)(delay >> 8), 0, 0 }, 0, 8);
            fs.Write(new byte[] { 0x2C, 0, 0, 0, 0, (byte)w, (byte)(w >> 8), (byte)h, (byte)(h >> 8), 0 }, 0, 10);
            Lzw(pending);
            pending = null;
        }

        public void Close(long endTime)
        {
            if (pending != null) Flush(Math.Max(endTime, lastTime + 400000));
            fs.WriteByte(0x3B);
            fs.Dispose();
        }

        /// <summary>LZW nach GIF-Norm (8-Bit-Indizes, variable Codelänge 9 … 12 Bit).</summary>
        void Lzw(byte[] data)
        {
            const int minCode = 8, clear = 256, end = 257;
            fs.WriteByte(minCode);
            var block = new byte[256];
            int blockLen = 0, bitBuf = 0, bitCount = 0, codeSize = 9, next = 258;
            var dict = new Dictionary<int, int>(5003);
            Action<int> emit = code =>
            {
                bitBuf |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8)
                {
                    block[++blockLen] = (byte)bitBuf;
                    bitBuf >>= 8; bitCount -= 8;
                    if (blockLen == 255) { block[0] = 255; fs.Write(block, 0, 256); blockLen = 0; }
                }
            };
            emit(clear);
            int prefix = data[0];
            for (int i = 1; i < data.Length; i++)
            {
                int k = data[i], key = (prefix << 8) | k, code;
                if (dict.TryGetValue(key, out code)) { prefix = code; continue; }
                emit(prefix);
                if (next < 4096)
                {
                    dict[key] = next++;
                    if (next > (1 << codeSize) && codeSize < 12) codeSize++;
                }
                else
                {
                    emit(clear);
                    dict.Clear();
                    next = 258; codeSize = 9;
                }
                prefix = k;
            }
            emit(prefix);
            emit(end);
            if (bitCount > 0) { block[++blockLen] = (byte)bitBuf; if (blockLen == 255) { block[0] = 255; fs.Write(block, 0, 256); blockLen = 0; } }
            if (blockLen > 0) { block[0] = (byte)blockLen; fs.Write(block, 0, blockLen + 1); }
            fs.WriteByte(0);
        }

        /// <summary>Farbtafel per Median-Cut aus einer Stichprobe der Bildpunkte.</summary>
        static int[] MedianCut(int[] px, int count)
        {
            var sample = new List<int>();
            int step = Math.Max(1, px.Length / 60000);
            for (int i = 0; i < px.Length; i += step) sample.Add(px[i] & 0xFFFFFF);
            // ein paar feste Farben, damit auch später auftauchende Farben (Rauch, Warnungen) gut getroffen werden
            foreach (int c in new[] { 0x000000, 0xFFFFFF, 0x808080, 0xFF0000, 0x00FF00, 0x0000FF, 0xFF8000, 0x6280FF })
                for (int k = 0; k < 40; k++) sample.Add(c);
            var boxes = new List<List<int>> { sample };
            while (boxes.Count < count)
            {
                int bi = -1, bestRange = 0, ch = 0;
                for (int i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2) continue;
                    for (int c = 0; c < 3; c++)
                    {
                        int sh = 16 - 8 * c, lo = 255, hi = 0;
                        foreach (int p in boxes[i]) { int v = (p >> sh) & 255; if (v < lo) lo = v; if (v > hi) hi = v; }
                        if (hi - lo > bestRange) { bestRange = hi - lo; bi = i; ch = c; }
                    }
                }
                if (bi < 0) break;
                int shift = 16 - 8 * ch;
                var box = boxes[bi];
                box.Sort((a, b) => ((a >> shift) & 255).CompareTo((b >> shift) & 255));
                int mid = box.Count / 2;
                boxes[bi] = box.GetRange(0, mid);
                boxes.Add(box.GetRange(mid, box.Count - mid));
            }
            var pal = new int[boxes.Count];
            for (int i = 0; i < boxes.Count; i++)
            {
                long r = 0, g = 0, b = 0;
                foreach (int p in boxes[i]) { r += (p >> 16) & 255; g += (p >> 8) & 255; b += p & 255; }
                int n = Math.Max(1, boxes[i].Count);
                pal[i] = (int)((r / n) << 16 | (g / n) << 8 | (b / n));
            }
            return pal;
        }
    }

    // ====================================================================== MP4 (Media Foundation)

    /// <summary>H.264 in MP4 über den Sink Writer von Media Foundation (in Windows enthalten, kein Zusatzprogramm).</summary>
    sealed class Mp4Writer : IFrameWriter
    {
        readonly IMFSinkWriter sink;
        readonly int stream, w, h;
        long lastTime = -1;
        int[] pending;

        public Mp4Writer(string path, int width, int height)
        {
            w = width; h = height;
            MF.Check(MF.MFStartup(0x20070, 0), "MFStartup");
            MF.Check(MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, IntPtr.Zero, out sink), "Sink Writer");
            IMFMediaType outType, inType;
            MF.Check(MF.MFCreateMediaType(out outType), "Medientyp");
            outType.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
            outType.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_H264);
            outType.SetUINT32(MF.MT_AVG_BITRATE, Math.Max(2000000, Math.Min(20000000, w * h * 10)));
            outType.SetUINT32(MF.MT_INTERLACE_MODE, 2);   // progressiv
            outType.SetUINT64(MF.MT_FRAME_SIZE, ((long)w << 32) | (uint)h);
            outType.SetUINT64(MF.MT_FRAME_RATE, (30L << 32) | 1);
            outType.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
            MF.Check(sink.AddStream(outType, out stream), "Videospur");
            MF.Check(MF.MFCreateMediaType(out inType), "Medientyp");
            inType.SetGUID(MF.MT_MAJOR_TYPE, MF.MediaType_Video);
            inType.SetGUID(MF.MT_SUBTYPE, MF.VideoFormat_RGB32);
            inType.SetUINT32(MF.MT_INTERLACE_MODE, 2);
            inType.SetUINT32(MF.MT_DEFAULT_STRIDE, w * 4);   // Zeilen von oben nach unten
            inType.SetUINT64(MF.MT_FRAME_SIZE, ((long)w << 32) | (uint)h);
            inType.SetUINT64(MF.MT_FRAME_RATE, (30L << 32) | 1);
            inType.SetUINT64(MF.MT_PIXEL_ASPECT_RATIO, (1L << 32) | 1);
            MF.Check(sink.SetInputMediaType(stream, inType, IntPtr.Zero), "Eingangsformat");
            MF.Check(sink.BeginWriting(), "BeginWriting");
            Marshal.ReleaseComObject(outType);
            Marshal.ReleaseComObject(inType);
        }

        public void Write(int[] px, long time)
        {
            if (pending != null) Send(pending, lastTime, time - lastTime);
            pending = px;
            lastTime = time;
        }

        void Send(int[] px, long time, long duration)
        {
            IMFMediaBuffer buf;
            int len = w * h * 4;
            MF.Check(MF.MFCreateMemoryBuffer(len, out buf), "Puffer");
            IntPtr p; int max, cur;
            MF.Check(buf.Lock(out p, out max, out cur), "Lock");
            Marshal.Copy(px, 0, p, w * h);
            buf.Unlock();
            buf.SetCurrentLength(len);
            IMFSample sample;
            MF.Check(MF.MFCreateSample(out sample), "Sample");
            sample.AddBuffer(buf);
            sample.SetSampleTime(time);
            sample.SetSampleDuration(Math.Max(1, duration));
            MF.Check(sink.WriteSample(stream, sample), "WriteSample");
            Marshal.ReleaseComObject(sample);
            Marshal.ReleaseComObject(buf);
        }

        public void Close(long endTime)
        {
            if (pending != null) Send(pending, lastTime, Math.Max(333333, endTime - lastTime));
            pending = null;
            sink.DoFinalize();
            Marshal.ReleaseComObject(sink);
            MF.MFShutdown();
        }
    }

    static class MF
    {
        public static readonly Guid MT_MAJOR_TYPE = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        public static readonly Guid MT_SUBTYPE = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        public static readonly Guid MT_AVG_BITRATE = new Guid("20332624-fb0d-4d9e-bd0d-cbf6786c102e");
        public static readonly Guid MT_INTERLACE_MODE = new Guid("e2724bb8-e676-4806-b4b2-a8d6efb44ccd");
        public static readonly Guid MT_FRAME_SIZE = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        public static readonly Guid MT_FRAME_RATE = new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0");
        public static readonly Guid MT_PIXEL_ASPECT_RATIO = new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6");
        public static readonly Guid MT_DEFAULT_STRIDE = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        public static readonly Guid MediaType_Video = new Guid("73646976-0000-0010-8000-00AA00389B71");
        public static readonly Guid VideoFormat_H264 = new Guid("34363248-0000-0010-8000-00AA00389B71");
        public static readonly Guid VideoFormat_RGB32 = new Guid("00000016-0000-0010-8000-00AA00389B71");

        [DllImport("mfplat.dll")] public static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll")] public static extern int MFShutdown();
        [DllImport("mfplat.dll")] public static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll")] public static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll")] public static extern int MFCreateMemoryBuffer(int maxLength, out IMFMediaBuffer buffer);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode)]
        public static extern int MFCreateSinkWriterFromURL(string url, IntPtr byteStream, IntPtr attributes, out IMFSinkWriter writer);

        public static void Check(int hr, string what)
        {
            if (hr < 0) throw new Exception("Video: " + what + " fehlgeschlagen (0x" + hr.ToString("X8") + ")");
        }
    }

    // Nur die benutzten Methoden haben Parameter; die übrigen stehen als Platzhalter für die richtige Reihenfolge in der vtable.
    [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaType
    {
        void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare(); void _GetUINT32(); void _GetUINT64();
        void _GetDouble(); void _GetGUID(); void _GetStringLength(); void _GetString(); void _GetAllocatedString();
        void _GetBlobSize(); void _GetBlob(); void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem();
        void _DeleteAllItems();
        [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
        [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
        void _SetDouble();
        [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
    }

    [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSample
    {
        void _GetItem(); void _GetItemType(); void _CompareItem(); void _Compare(); void _GetUINT32(); void _GetUINT64();
        void _GetDouble(); void _GetGUID(); void _GetStringLength(); void _GetString(); void _GetAllocatedString();
        void _GetBlobSize(); void _GetBlob(); void _GetAllocatedBlob(); void _GetUnknown(); void _SetItem(); void _DeleteItem();
        void _DeleteAllItems(); void _SetUINT32(); void _SetUINT64(); void _SetDouble(); void _SetGUID(); void _SetString();
        void _SetBlob(); void _SetUnknown(); void _LockStore(); void _UnlockStore(); void _GetCount(); void _GetItemByIndex();
        void _CopyAllItems();
        void _GetSampleFlags(); void _SetSampleFlags(); void _GetSampleTime();
        [PreserveSig] int SetSampleTime(long time);
        void _GetSampleDuration();
        [PreserveSig] int SetSampleDuration(long duration);
        void _GetBufferCount(); void _GetBufferByIndex(); void _ConvertToContiguousBuffer();
        [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
    }

    [ComImport, Guid("045FA593-8799-42b8-BC8D-8968C6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFMediaBuffer
    {
        [PreserveSig] int Lock(out IntPtr buffer, out int maxLength, out int currentLength);
        [PreserveSig] int Unlock();
        void _GetCurrentLength();
        [PreserveSig] int SetCurrentLength(int length);
    }

    [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMFSinkWriter
    {
        [PreserveSig] int AddStream(IMFMediaType targetType, out int streamIndex);
        [PreserveSig] int SetInputMediaType(int streamIndex, IMFMediaType inputType, IntPtr encodingParameters);
        [PreserveSig] int BeginWriting();
        [PreserveSig] int WriteSample(int streamIndex, IMFSample sample);
        void _SendStreamTick(); void _PlaceMarker(); void _NotifyEndOfSegment(); void _Flush();
        [PreserveSig] int DoFinalize();
    }
}
