using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Windkanal
{
    /// <summary>
    /// Prüft alle Modelle und zeichnet eine Übersicht: jedes Modell so gerastert, wie es im Windkanal liegt
    /// (Zellen dunkel, Umriss farbig). Aufruf: ModellVorschau.exe [Ausgabe.png] [Auflösung 0..3] [Modell-Id]
    /// </summary>
    static class ModellVorschau
    {
        static readonly int[] ResNX = { 400, 600, 900, 1200 };
        static readonly int[] ResNY = { 160, 240, 360, 480 };

        static int Main(string[] args)
        {
            string outFile = args.Length > 0 ? args[0] : "modelle.png";
            int res = args.Length > 1 ? int.Parse(args[1]) : 2;
            string only = args.Length > 2 ? args[2] : null;
            var models = ModelLibrary.All;
            foreach (var e in ModelLibrary.Errors) Console.WriteLine("FEHLER: " + e);
            Console.WriteLine(models.Count + " Modelle geladen");

            var list = new List<Model>();
            foreach (var m in models) if (!m.IsCustom && (only == null || m.Id == only)) list.Add(m);
            int nx = ResNX[res], ny = ResNY[res];
            int cols = only != null ? 1 : 6, scale = only != null ? 2 : 1;
            int tw = (only != null ? nx : nx / 2) * scale, th = (only != null ? ny : ny / 2 + 30) * scale;
            int rows = (list.Count + cols - 1) / cols;
            using (var bmp = new Bitmap(cols * tw, rows * th + (only != null ? 30 : 0)))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 9f))
            {
                g.Clear(Color.FromArgb(246, 247, 249));
                for (int i = 0; i < list.Count; i++)
                {
                    var m = list[i];
                    float size = m.SizePercent / 100f * ny;
                    float px = (m.OnGround ? 0.3f : 0.25f) * nx;
                    var mask = ModelLibrary.Rasterize(m, nx, ny, px, size, m.Angle);
                    int ox = (i % cols) * tw, oy = (i / cols) * th;
                    // Ausschnitt: bei der Übersicht nur die linke Hälfte des Kanals (dort liegt der Körper)
                    int x0 = only != null ? 0 : (int)(px - nx / 4f);
                    int y0 = only != null ? 0 : (m.OnGround ? 0 : ny / 4);
                    using (var tile = new Bitmap(only != null ? nx : nx / 2, only != null ? ny : ny / 2))
                    {
                        for (int y = 0; y < tile.Height; y++)
                            for (int x = 0; x < tile.Width; x++)
                            {
                                int gx = x + x0, gy = y + y0;
                                bool s = gx >= 0 && gx < nx && gy >= 0 && gy < ny && mask[gy * nx + gx];
                                tile.SetPixel(x, tile.Height - 1 - y, s ? Color.FromArgb(21, 23, 26) : (gy == 0 ? Color.FromArgb(200, 120, 60) : Color.White));
                            }
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(tile, ox, oy + 20 * scale, tile.Width * scale, tile.Height * scale);
                    }
                    int solid = 0;
                    foreach (bool b in mask) if (b) solid++;
                    string det = m.Detail > 0 ? (m.Detail * size).ToString("0.0") + " Zellen" : "–";
                    g.DrawString(m.Name + "  ·  Detail " + det + "  ·  " + solid + " Zellen", font, Brushes.Black, ox + 4, oy + 2);
                    Console.WriteLine(string.Format("{0,-24} {1,-46} Detail {2,-12} Teile {3}", m.Id, m.Name, det, m.Parts.Count));
                }
                bmp.Save(outFile, ImageFormat.Png);
            }
            Console.WriteLine("Bild: " + Path.GetFullPath(outFile));
            return ModelLibrary.Errors.Count == 0 ? 0 : 1;
        }
    }
}
