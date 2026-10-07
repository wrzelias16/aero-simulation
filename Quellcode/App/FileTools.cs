using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Windkanal
{
    /// <summary>Sitzungsdatei (*.windkanal): einfache Textzeilen „schlüssel: wert“, lesbar und von Hand änderbar.</summary>
    public static class Session
    {
        public const string Filter = "Windkanal-Sitzung (*.windkanal)|*.windkanal";
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static void Write(string path, Dictionary<string, string> values)
        {
            var sb = new StringBuilder("# Windkanal-Sitzung, Version " + AppInfo.Version + "\n");
            foreach (var kv in values) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static Dictionary<string, string> Read(string path)
        {
            var d = new Dictionary<string, string>();
            foreach (var raw in File.ReadAllLines(path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int c = line.IndexOf(':');
                if (c > 0) d[line.Substring(0, c).Trim()] = line.Substring(c + 1).Trim();
            }
            if (!d.ContainsKey("modus")) throw new FormatException("Das ist keine Windkanal-Sitzung.");
            return d;
        }

        public static string F(double v) { return v.ToString("R", Inv); }
        public static double D(Dictionary<string, string> d, string k, double fallback)
        {
            string s; double v;
            return d.TryGetValue(k, out s) && double.TryParse(s, NumberStyles.Float, Inv, out v) ? v : fallback;
        }
        public static int I(Dictionary<string, string> d, string k, int fallback) { return (int)Math.Round(D(d, k, fallback)); }
        public static bool B(Dictionary<string, string> d, string k, bool fallback)
        {
            string s;
            return d.TryGetValue(k, out s) ? s == "ja" || s == "1" || s == "true" : fallback;
        }
        public static string Yes(bool b) { return b ? "ja" : "nein"; }
    }

    /// <summary>
    /// Datei-Menü für 2D und 3D: Sitzung speichern/öffnen, Bild speichern, Aufnahme als MP4 oder GIF.
    /// Die Aufnahme zeigt die Ansicht-Karte (Bild, Kopfzeile mit Zustand und Farbskala) in Echtzeit.
    /// </summary>
    public sealed class FileMenu
    {
        readonly Form owner;
        readonly Control target;
        readonly Func<Dictionary<string, string>> save;
        readonly Action<Dictionary<string, string>> load;
        readonly Action<string> message;
        Bitmap frame;
        public Recorder Rec;
        /// <summary>Zuletzt geöffnete Sitzungsdatei.</summary>
        public string LastPath;

        /// <param name="target">Was als Bild bzw. Video aufgenommen wird (die Ansicht-Karte)</param>
        /// <param name="save">liefert die Einstellungen der Sitzung</param>
        /// <param name="load">übernimmt eine gelesene Sitzung</param>
        /// <param name="message">kurze Meldung im Fenster</param>
        public FileMenu(Form owner, Control target, Func<Dictionary<string, string>> save, Action<Dictionary<string, string>> load, Action<string> message)
        {
            this.owner = owner; this.target = target; this.save = save; this.load = load; this.message = message;
        }

        public bool Recording { get { return Rec != null; } }

        /// <summary>z. B. „Aufnahme 0:07“, solange aufgenommen wird.</summary>
        public string RecordingText
        {
            get
            {
                if (Rec == null) return null;
                int s = (int)Rec.Seconds;
                return "Aufnahme " + s / 60 + ":" + (s % 60).ToString("00") + (Rec.Mp4 ? " · MP4" : " · GIF");
            }
        }

        public void Show(Control anchor)
        {
            var menu = new ContextMenuStrip
            {
                Renderer = new MenuRenderer(), ShowImageMargin = false, ShowCheckMargin = false,
                BackColor = Theme.Surface, ForeColor = Theme.Text, Font = Theme.Base, Padding = new Padding(4), DropShadowEnabled = true
            };
            Action<string, string, Action> item = (text, keys, act) =>
            {
                var mi = new ToolStripMenuItem(text) { AutoSize = false, Size = new Size(300, 34), ForeColor = Theme.Text, Padding = new Padding(6, 0, 0, 0) };
                if (keys != null) { mi.ShortcutKeyDisplayString = keys; mi.ShowShortcutKeys = true; }
                mi.Click += delegate { act(); };
                menu.Items.Add(mi);
            };
            item("Sitzung speichern …", "Strg+S", SaveSession);
            item("Sitzung öffnen …", "Strg+O", OpenSession);
            menu.Items.Add(new ToolStripSeparator());
            item("Bild speichern (PNG) …", null, SaveImage);
            if (Rec == null)
            {
                item("Video aufnehmen (MP4)", "Strg+R", () => StartRecording(true));
                item("GIF aufnehmen", null, () => StartRecording(false));
            }
            else item("Aufnahme beenden und speichern …", "Strg+R", StopRecording);
            menu.Show(anchor, new Point(anchor.Width - 300, anchor.Height + 6));
        }

        /// <summary>Tastenkürzel: Strg+S, Strg+O, Strg+R. Gibt true zurück, wenn die Taste verarbeitet wurde.</summary>
        public bool HandleKey(KeyEventArgs e)
        {
            if (!e.Control) return false;
            if (e.KeyCode == Keys.S) SaveSession();
            else if (e.KeyCode == Keys.O) OpenSession();
            else if (e.KeyCode == Keys.R) { if (Rec == null) StartRecording(true); else StopRecording(); }
            else return false;
            return true;
        }

        public void SaveSession()
        {
            using (var dlg = new SaveFileDialog { Filter = Session.Filter, FileName = "sitzung-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".windkanal" })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                try { Session.Write(dlg.FileName, save()); message("Sitzung gespeichert: " + Path.GetFileName(dlg.FileName)); }
                catch (Exception ex) { message("Speichern fehlgeschlagen: " + ex.Message); }
            }
        }

        public void OpenSession()
        {
            using (var dlg = new OpenFileDialog { Filter = Session.Filter })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                OpenSession(dlg.FileName);
            }
        }

        public void OpenSession(string path)
        {
            LastPath = path;
            try { load(Session.Read(path)); }
            catch (Exception ex) { message("Sitzung konnte nicht geöffnet werden: " + ex.Message); }
        }

        Bitmap Grab()
        {
            int w = target.Width, h = target.Height;
            if (w < 2 || h < 2) return null;
            if (frame == null || frame.Width != w || frame.Height != h)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            }
            target.DrawToBitmap(frame, new Rectangle(0, 0, w, h));
            return frame;
        }

        void SaveImage()
        {
            var bmp = Grab();
            if (bmp == null) return;
            using (var copy = new Bitmap(bmp))
            using (var dlg = new SaveFileDialog { Filter = "PNG-Bild (*.png)|*.png", FileName = "windkanal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png",
                                                   InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) })
            {
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                try { copy.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png); message("Bild gespeichert: " + Path.GetFileName(dlg.FileName)); }
                catch (Exception ex) { message("Bild konnte nicht gespeichert werden: " + ex.Message); }
            }
        }

        public void StartRecording(bool mp4)
        {
            if (Rec != null) return;
            Rec = new Recorder(mp4);
            message(mp4 ? "Video-Aufnahme läuft (Strg+R beendet)" : "GIF-Aufnahme läuft (Strg+R beendet)");
        }

        /// <summary>Nach jedem neuen Bild aufrufen.</summary>
        public void Capture()
        {
            if (Rec == null) return;
            var bmp = Grab();
            if (bmp != null) Rec.AddFrame(bmp);
            if (Rec.Error != null) { message("Aufnahme abgebrochen: " + Rec.Error); Rec.Dispose(); Rec = null; }
        }

        public void StopRecording()
        {
            if (Rec == null) return;
            var rec = Rec;
            Rec = null;
            owner.Cursor = Cursors.WaitCursor;
            rec.Finish();
            owner.Cursor = Cursors.Default;
            if (rec.Error != null || rec.Frames == 0)
            {
                message("Aufnahme fehlgeschlagen: " + (rec.Error ?? "keine Bilder"));
                rec.Dispose();
                return;
            }
            string ext = rec.Mp4 ? "mp4" : "gif";
            using (var dlg = new SaveFileDialog
            {
                Filter = rec.Mp4 ? "MP4-Video (*.mp4)|*.mp4" : "GIF-Animation (*.gif)|*.gif",
                FileName = "windkanal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "." + ext,
                InitialDirectory = Environment.GetFolderPath(rec.Mp4 ? Environment.SpecialFolder.MyVideos : Environment.SpecialFolder.MyPictures)
            })
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    try
                    {
                        if (File.Exists(dlg.FileName)) File.Delete(dlg.FileName);
                        File.Move(rec.TempFile, dlg.FileName);
                        message((rec.Mp4 ? "Video" : "GIF") + " gespeichert: " + Path.GetFileName(dlg.FileName) + " (" + rec.Frames + " Bilder)");
                    }
                    catch (Exception ex) { message("Speichern fehlgeschlagen: " + ex.Message); }
                }
            }
            rec.Dispose();
        }
    }
}
