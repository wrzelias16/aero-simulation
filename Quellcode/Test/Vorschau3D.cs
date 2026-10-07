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
        int recFails = 0;

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

        // 2c. Vorne/hinten: Auto mit Heckflügel, der in der Datei bei -z liegt (Y oben). Nach dem Laden muss der Flügel hinten (+x) sein.
        {
            string wing = Path.Combine(dir, "auto_mit_fluegel.obj");
            var sb = new System.Text.StringBuilder();
            Action<double, double, double, double, double, double> box = (x0, x1, y0, y1, z0, z1) =>
            {
                int b = 0;
                foreach (var l in sb.ToString().Split('\n')) if (l.StartsWith("v ")) b++;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var z in new[] { z0, z1 }) foreach (var y in new[] { y0, y1 }) foreach (var x in new[] { x0, x1 })
                    sb.Append(string.Format(inv, "v {0} {1} {2}\n", x, y, z));
                int[][] f = { new[] { 1, 3, 4, 2 }, new[] { 5, 6, 8, 7 }, new[] { 1, 2, 6, 5 }, new[] { 3, 7, 8, 4 }, new[] { 1, 5, 7, 3 }, new[] { 2, 4, 8, 6 } };
                foreach (var q in f) sb.Append("f " + (q[0] + b) + " " + (q[1] + b) + " " + (q[2] + b) + " " + (q[3] + b) + "\n");
            };
            box(-0.9, 0.9, 0.0, 1.0, -2.2, 2.2);    // Wagen: Länge auf z, Höhe auf y
            box(-0.9, 0.9, 1.0, 1.5, -2.2, -1.7);   // Flügel bei -z, deutlich höher als der Wagen
            File.WriteAllText(wing, sb.ToString());
            var m = Mesh.Load(wing);
            var p = Placement.Build(m, 0, 0, 0.5, true, 256, 112, 112);
            float wx = 0, wn = 0, xmin = float.MaxValue, xmax = float.MinValue, ztop = float.MinValue;
            for (int i = 0; i < p.World.Length; i += 3) { xmin = Math.Min(xmin, p.World[i]); xmax = Math.Max(xmax, p.World[i]); ztop = Math.Max(ztop, p.World[i + 2]); }
            for (int i = 0; i < p.World.Length; i += 3) if (p.World[i + 2] > ztop - 0.01f) { wx += p.World[i]; wn++; }
            bool ok = wx / wn > (xmin + xmax) / 2;
            Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "Vorne/hinten: Flügel liegt " + (ok ? "hinten" : "vorne") + " (höchster Punkt bei x = "
                              + (wx / wn).ToString("0") + ", Körper " + xmin.ToString("0") + " … " + xmax.ToString("0") + ")");
            if (!ok) fails++;
        }

        // 2d. Startfenster: unsichtbar zeigen und nach 1,5 s als Bild speichern
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        {
            var sp = (Form)Activator.CreateInstance(typeof(Windkanal.Splash), true);
            sp.StartPosition = FormStartPosition.Manual;
            sp.Location = new Point(-4000, -4000);
            sp.ShowInTaskbar = false;
            var ts = new Timer { Interval = 1500 };
            ts.Tick += delegate
            {
                ts.Stop();
                ((Windkanal.Splash)sp).Report("Rechengitter wird vorbereitet …", 0.62f);
                Application.DoEvents();
                System.Threading.Thread.Sleep(300);
                Application.DoEvents();
                using (var bmp = new Bitmap(sp.Width, sp.Height))
                {
                    sp.DrawToBitmap(bmp, new Rectangle(0, 0, sp.Width, sp.Height));
                    bmp.Save(Path.Combine(dir, "startfenster.png"), ImageFormat.Png);
                }
                Console.WriteLine("Bild gespeichert: startfenster.png");
                sp.Close();
            };
            sp.Shown += delegate { ts.Start(); };
            Application.Run(sp);
        }

        // 3. Fenster unsichtbar öffnen, rechnen lassen, Bilder speichern
        var form = new Form3D();
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-4000, -4000);
        form.ShowInTaskbar = false;
        form.ClientSize = new Size(1600, 960);
        int shot = 0;
        string[] names = { "kugel_stromlinien", "ahmed_stromlinien", "ahmed_rauch", "ahmed_schnittebene", "ahmed_druck", "3d_ohne_bild", "3d_klein" };
        var t = new Timer { Interval = 5000 };
        t.Tick += delegate
        {
            if (shot != 5) Save(form, Path.Combine(dir, "fenster_" + names[shot] + ".png"));
            shot++;
            if (shot == 1) Find<Windkanal.DropDown>(form).SelectedIndex = 1;        // Ahmed-Körper
            else if (shot == 2)
            {
                Find<Windkanal.Segmented>(form).SelectedIndex = 1;   // Rauch
                ((Windkanal.FileMenu)Field(form, "fileMenu")).StartRecording(false);
            }
            else if (shot == 3)
            {
                FinishRecording(form, Path.Combine(dir, "aufnahme_rauch.gif"), ref recFails);
                // Sitzung: speichern, verstellen, wieder laden
                var sess = (System.Collections.Generic.Dictionary<string, string>)Call(form, "SaveSession");
                Windkanal.Session.Write(Path.Combine(dir, "test.windkanal"), sess);
                var back = Windkanal.Session.Read(Path.Combine(dir, "test.windkanal"));
                Find<Windkanal.Segmented>(form).SelectedIndex = 2;   // Schnittebene (ändert die Ansicht)
                Call(form, "LoadSession", back);
                bool ok = (int)Field(form, "vizMode") == 1 && (int)Field(form, "sizePercent") == int.Parse(sess["groesse"]);
                Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "3D-Sitzung: gespeichert und wieder geladen (Ansicht " + Field(form, "vizMode") + ", Größe " + Field(form, "sizePercent") + " %)");
                if (!ok) recFails++;
                Find<Windkanal.Segmented>(form).SelectedIndex = 2;   // Schnittebene
            }
            else if (shot == 4) Find<Windkanal.Segmented>(form).SelectedIndex = 3;   // Oberflächendruck
            else if (shot == 5) { form.ClientSize = new Size(1280, 800); t.Interval = 2000; }   // kleines Fenster
            else if (shot == 6) { }   // nächster Takt speichert das kleine Fenster
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
        // danach: Heckflügel mit DRS, vor dem Öffnen, während und nach dem Öffnen
        int step2 = 0;
        var t2 = new Timer { Interval = 2500 };
        t2.Tick += delegate
        {
            switch (step2++)
            {
                case 0:
                    Save(f2, Path.Combine(dir, "fenster_2d.png"));
                    Find<Windkanal.ModelPicker>(f2).Selected = Windkanal.ModelLibrary.Find("f1-heckfluegel-drs");
                    t2.Interval = 6000;
                    break;
                case 1:
                    Save(f2, Path.Combine(dir, "fenster_2d_drs_zu.png"));
                    ((Windkanal.FileMenu)Field(f2, "fileMenu")).StartRecording(true);
                    foreach (var b in All<Windkanal.FlatButton>(f2))
                        if (b.Text.StartsWith("DRS"))
                            typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                                           .Invoke(b, new object[] { EventArgs.Empty });
                    t2.Interval = 1500;
                    break;
                case 2:
                    {
                        // Echtzeit 1x: das DRS muss in etwa 0,4 s offen sein (Uhr-Auflösung ein Bild, ~30 ms)
                        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        double secs = (double)typeof(Windkanal.MainForm).GetField("lastMotionSeconds", bf).GetValue(f2);
                        float mot = (float)typeof(Windkanal.MainForm).GetField("motion", bf).GetValue(f2);
                        bool ok = mot >= 1 && secs >= 0.39 && secs <= 0.47;
                        Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "DRS in Echtzeit: offen nach " + secs.ToString("0.000") + " s (Soll 0,400 s), Stand " + (mot * 100).ToString("0") + " %");
                        if (!ok) fails++;
                        Save(f2, Path.Combine(dir, "fenster_2d_drs_offen.png"));
                        FinishRecording(f2, Path.Combine(dir, "aufnahme_drs.mp4"), ref recFails);
                        // 2D-Sitzung: speichern, Größe verstellen, wieder laden
                        var sess = (System.Collections.Generic.Dictionary<string, string>)Call(f2, "SaveSession");
                        var tb = (Windkanal.FlatSlider)Field(f2, "tbSize");
                        tb.Value = tb.Value + 7;
                        Call(f2, "LoadSession", sess);
                        bool sok = (int)Field(f2, "sizePercent") == int.Parse(sess["groesse"]) && (float)Field(f2, "motion") >= 1;
                        Console.WriteLine((sok ? "[OK]     " : "[FEHLER] ") + "2D-Sitzung: Größe " + Field(f2, "sizePercent") + " % (gespeichert " + sess["groesse"] + " %), DRS offen: " + ((float)Field(f2, "motion") >= 1));
                        if (!sok) recFails++;
                        t2.Interval = 5000;
                    }
                    break;
                case 3:
                    Save(f2, Path.Combine(dir, "fenster_2d_drs_nachlauf.png"));
                    // Vergleich: A mit geschlossenem DRS, B mit offenem
                    {
                        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                        typeof(Windkanal.MainForm).GetField("motion", bf).SetValue(f2, 0f);
                        Call(f2, "ApplyMotionMask");
                        Call(f2, "UpdateMotionButton");
                        ((Windkanal.Toggle)Field(f2, "chkCompare")).Checked = true;
                    }
                    t2.Interval = 9000;
                    break;
                case 4:
                    {
                        Save(f2, Path.Combine(dir, "fenster_2d_vergleich.png"));
                        double a = (double)Field(f2, "meanCd"), b = (double)Field(f2, "meanCdB");
                        bool ok = (bool)Field(f2, "compare") && b > 0 && b < a;
                        Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "Vergleich DRS zu/offen: cw A " + a.ToString("0.000") + ", cw B " + b.ToString("0.000") + " (offen muss weniger Widerstand haben)");
                        if (!ok) recFails++;
                        f2.ClientSize = new Size(1280, 800);
                        t2.Interval = 1500;
                    }
                    break;
                case 5:
                    Save(f2, Path.Combine(dir, "fenster_2d_klein.png"));
                    // Einstellungen mit dem Mausrad ganz nach unten scrollen (Rahmen muss sauber bleiben)
                    {
                        var set = (Control)Field(f2, "setScroll");
                        for (int k = 0; k < 10; k++) SendMessage(set.Handle, 0x20A, (IntPtr)((-120) << 16), IntPtr.Zero);
                    }
                    t2.Interval = 800;
                    break;
                case 6:
                    Save(f2, Path.Combine(dir, "fenster_2d_klein_gescrollt.png"));
                    // hell/dunkel mit Überblenden umschalten (darf nicht abstürzen), danach zurück
                    try
                    {
                        Call(f2, "SwitchTheme");
                        Console.WriteLine("[OK]     Design umgeschaltet mit Überblenden");
                    }
                    catch (Exception ex) { Console.WriteLine("[FEHLER] Design umschalten: " + ex.Message); recFails++; }
                    t2.Interval = 600;
                    break;
                case 7:
                    Save(f2, Path.Combine(dir, "fenster_2d_hell.png"));
                    Call(f2, "SwitchTheme");
                    t2.Interval = 600;
                    break;
                case 8:
                    // Ansicht groß (Vergleich läuft noch), etwas größeres Fenster
                    f2.ClientSize = new Size(1600, 960);
                    Call(f2, "SetViewMax", true);
                    t2.Interval = 1500;
                    break;
                case 9:
                    Save(f2, Path.Combine(dir, "fenster_2d_gross.png"));
                    t2.Interval = 100;
                    break;
                default:
                    t2.Stop();
                    f2.Close();
                    break;
            }
        };
        f2.Shown += delegate { t2.Start(); };
        Application.Run(f2);

        fails += recFails;
        Console.WriteLine(fails == 0 ? "ALLE TESTS BESTANDEN" : fails + " TEST(S) DURCHGEFALLEN");
        return fails == 0 ? 0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);

    static object Field(object o, string name)
    {
        return o.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(o);
    }

    static object Call(object o, string name, params object[] args)
    {
        return o.GetType().GetMethod(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(o, args);
    }

    /// <summary>Aufnahme beenden ohne Speichern-Dialog und Datei in den Testordner kopieren.</summary>
    static int FinishRecording(object form, string target, ref int fails)
    {
        var fm = (Windkanal.FileMenu)Field(form, "fileMenu");
        var rec = fm.Rec;
        fm.Rec = null;
        rec.Finish();
        long size = File.Exists(rec.TempFile) ? new FileInfo(rec.TempFile).Length : 0;
        bool ok = rec.Error == null && rec.Frames > 5 && size > 10000;
        if (ok) File.Copy(rec.TempFile, target, true);
        Console.WriteLine((ok ? "[OK]     " : "[FEHLER] ") + "Aufnahme " + Path.GetFileName(target) + ": " + rec.Frames + " Bilder, "
                          + (size / 1024) + " KB" + (rec.Error != null ? ", Fehler: " + rec.Error : ""));
        if (!ok) fails++;
        rec.Dispose();
        return rec.Frames;
    }

    static System.Collections.Generic.List<T> All<T>(Control c) where T : Control
    {
        var r = new System.Collections.Generic.List<T>();
        foreach (Control k in c.Controls)
        {
            if (k is T) r.Add((T)k);
            r.AddRange(All<T>(k));
        }
        return r;
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
