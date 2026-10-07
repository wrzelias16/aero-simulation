using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Windkanal3D;

/// <summary>
/// Vorschau und Selbsttest für das 3D-Fenster ohne Bildschirmfoto:
/// prüft STL-Import (binär und Text, Rundreise) und die Umwandlung in Zellen,
/// öffnet das 3D-Fenster unsichtbar, lässt es einige Sekunden rechnen und speichert Bilder.
/// Aufruf: Vorschau3D.exe [Ausgabeordner]
/// </summary>
static class Vorschau3D
{
    [STAThread]
    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : ".";
        Directory.CreateDirectory(dir);
        int fails = 0;

        // 1. Import-Rundreise: eingebaute Kugel als STL (binär + Text) schreiben und wieder laden
        var sphere = Mesh.Sphere();
        string bin = Path.Combine(dir, "kugel_binaer.stl"), txt = Path.Combine(dir, "kugel_text.stl"), obj = Path.Combine(dir, "wuerfel.obj");
        WriteBinaryStl(sphere, bin);
        WriteTextStl(sphere, txt);
        File.WriteAllText(obj, "v -1 -1 -1\nv 1 -1 -1\nv 1 1 -1\nv -1 1 -1\nv -1 -1 1\nv 1 -1 1\nv 1 1 1\nv -1 1 1\n" +
                               "f 1 2 3 4\nf 5 8 7 6\nf 1 5 6 2\nf 2 6 7 3\nf 3 7 8 4\nf 4 8 5 1\n");
        foreach (var f in new[] { bin, txt, obj })
        {
            var m = Mesh.Load(f);
            int expect = f == obj ? 12 : sphere.Triangles;
            bool ok = m.Triangles == expect;
            Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + Path.GetFileName(f) + ": " + m.Triangles + " Dreiecke (erwartet " + expect + ")");
            if (!ok) fails++;
        }

        // 2. Zellen: Kugel muss etwa das Kugelvolumen füllen, Würfel genau seine Zellen, alles geschlossen
        foreach (var m in Mesh.BuiltIn())
        {
            var p = Placement.Build(m, 0, 0, 0.25, false, 256, 112, 112);
            string extra = "";
            bool ok = p.LeakyRays == 0 && p.SolidCells > 0;
            if (m.Name == "Kugel")
            {
                double r = p.LengthX / 2, vol = 4.0 / 3 * Math.PI * r * r * r;
                double err = Math.Abs(p.SolidCells - vol) / vol;
                ok &= err < 0.03;
                extra = ", Volumen " + p.SolidCells + " statt " + vol.ToString("0") + " (" + (err * 100).ToString("0.0") + " %)";
            }
            Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + m.Name + ": " + p.SolidCells + " Zellen, Stirnfläche " + p.FrontalCells
                              + ", offene Strahlen " + p.LeakyRays + extra);
            if (!ok) fails++;
        }

        // 2b. Ausrichtung: "Auto" mit einem Y-oben-Modell (Länge auf z, Höhe auf y), wie es viele OBJ-Dateien haben
        string car = Path.Combine(dir, "auto_y_oben.obj");
        File.WriteAllText(car, "v -0.9 0 -2\nv 0.9 0 -2\nv 0.9 1.4 -2\nv -0.9 1.4 -2\nv -0.9 0 2.5\nv 0.9 0 2.5\nv 0.9 1.4 2.5\nv -0.9 1.4 2.5\n" +
                               "f 1 2 3 4\nf 5 8 7 6\nf 1 5 6 2\nf 2 6 7 3\nf 3 7 8 4\nf 4 8 5 1\n");
        {
            var m = Mesh.Load(car);
            var p = Placement.Build(m, 0, 0, 0.5, true, 256, 112, 112);
            bool ok = p.LengthX > p.WidthY && p.WidthY > p.HeightZ && Math.Abs(p.LengthX / p.HeightZ - 4.5 / 1.4) < 0.05;
            Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "Ausrichtung Y-oben-Modell: Länge " + p.LengthX.ToString("0.0") + ", Breite "
                              + p.WidthY.ToString("0.0") + ", Höhe " + p.HeightZ.ToString("0.0") + " (Länge muss in Strömungsrichtung liegen, Höhe oben)");
            if (!ok) fails++;
            m.Rotate90(0);   // um die Strömungsachse kippen: jetzt liegt es auf der Seite
            p = Placement.Build(m, 0, 0, 0.5, true, 256, 112, 112);
            ok = Math.Abs(p.HeightZ / p.LengthX - 1.8 / 4.5) < 0.05;
            Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "Kippen um X: Höhe jetzt " + p.HeightZ.ToString("0.0") + " (= Breite vorher)");
            if (!ok) fails++;
        }

        // 3. Fenster unsichtbar öffnen, rechnen lassen, Bilder speichern
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        var form = new Form3D();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);
        form.ShowInTaskbar = false;
        form.ClientSize = new Size(1600, 960);
        int shot = 0;
        string[] names = { "kugel_stromlinien", "ahmed_stromlinien", "ahmed_rauch", "ahmed_schnittebene" };
        var t = new Timer { Interval = 5000 };
        t.Tick += delegate
        {
            Save(form, Path.Combine(dir, "fenster_" + names[shot] + ".png"));
            shot++;
            if (shot == 1) Find<Windkanal.DropDown>(form).SelectedIndex = 1;        // Ahmed-Körper
            else if (shot == 2) Find<Windkanal.Segmented>(form).SelectedIndex = 1;   // Rauch
            else if (shot == 3) Find<Windkanal.Segmented>(form).SelectedIndex = 2;   // Schnittebene
            else { t.Stop(); form.Close(); }
        };
        form.Shown += delegate { t.Start(); };
        Application.Run(form);

        // 4. 2D-Fenster mit dem neuen Umschalter im Kopf
        var f2 = new Windkanal.MainForm();
        f2.StartPosition = FormStartPosition.Manual;
        f2.Location = new Point(-4000, -4000);
        f2.ShowInTaskbar = false;
        f2.ClientSize = new Size(1600, 960);
        var t2 = new Timer { Interval = 2500 };
        t2.Tick += delegate { t2.Stop(); Save(f2, Path.Combine(dir, "fenster_2d.png")); f2.Close(); };
        f2.Shown += delegate { t2.Start(); };
        Application.Run(f2);

        Console.WriteLine(fails == 0 ? "ALLE TESTS BESTANDEN" : fails + " TEST(S) DURCHGEFALLEN");
        return fails == 0 ? 0 : 1;
    }

    static T Find<T>(Control c) where T : Control
    {
        foreach (Control k in c.Controls)
        {
            if (k is T) return (T)k;
            var r = Find<T>(k);
            if (r != null) return r;
        }
        return null;
    }

    static void Save(Form f, string path)
    {
        using (var bmp = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
        {
            // DrawToBitmap nimmt die Titelleiste mit; nur den Inhalt behalten
            var origin = f.PointToScreen(Point.Empty);
            int ox = origin.X - f.Left, oy = origin.Y - f.Top;
            using (var full = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(full, new Rectangle(0, 0, f.Width, f.Height));
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(full, -ox, -oy);
            }
            bmp.Save(path, ImageFormat.Png);
        }
        Console.WriteLine("Bild gespeichert: " + path);
    }

    static void WriteBinaryStl(Mesh m, string path)
    {
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(new byte[80]);
            w.Write((uint)m.Triangles);
            for (int t = 0; t < m.Triangles; t++)
            {
                for (int k = 0; k < 3; k++) w.Write(0f);
                for (int k = 0; k < 9; k++) w.Write(m.V[t * 9 + k]);
                w.Write((ushort)0);
            }
        }
    }

    static void WriteTextStl(Mesh m, string path)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        using (var w = new StreamWriter(path))
        {
            w.WriteLine("solid test");
            for (int t = 0; t < m.Triangles; t++)
            {
                w.WriteLine(" facet normal 0 0 0\n  outer loop");
                for (int k = 0; k < 3; k++)
                    w.WriteLine("   vertex " + m.V[t * 9 + 3 * k].ToString("R", inv) + " " + m.V[t * 9 + 3 * k + 1].ToString("R", inv) + " " + m.V[t * 9 + 3 * k + 2].ToString("R", inv));
                w.WriteLine("  endloop\n endfacet");
            }
            w.WriteLine("endsolid test");
        }
    }
}
