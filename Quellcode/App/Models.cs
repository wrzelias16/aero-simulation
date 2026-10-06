using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;

namespace Windkanal
{
    /// <summary>Ein Körper für den Windkanal, geladen aus einer .modell-Datei (Format siehe Quellcode/Modelle/LIESMICH.md).</summary>
    public sealed class Model
    {
        public string Id = "", Name = "", Category = "Weitere", Description = "", Source = "", License = "";
        public string RefName = "Bezugslänge";
        /// <summary>True: Bezugslänge ist die Formgröße (Sehne, Durchmesser). False: gemessene Stirnhöhe.</summary>
        public bool RefIsSize = true;
        /// <summary>Fahrzeuge und Bauwerke stehen knapp über dem Boden statt mittig im Kanal.</summary>
        public bool OnGround;
        public int SizePercent = 25, Angle, Reynolds = 1000;
        /// <summary>Feinstes Detail (dünnstes Teil oder engster Spalt) als Anteil der Formgröße, 0 = nicht angegeben.</summary>
        public float Detail;
        /// <summary>Abstand des tiefsten Punkts vom Boden in Einheiten der Formgröße; 0 = steht auf dem Boden, negativ = 3 Zellen.</summary>
        public float GroundGap = -1;
        /// <summary>„Eigene Zeichnung“: keine Teile, die Maske kommt vom Zeichnen mit der Maus.</summary>
        public bool IsCustom;
        public readonly List<ModelPart> Parts = new List<ModelPart>();
    }

    public sealed class ModelPart
    {
        public string Name = "";
        public List<PointF> Points = new List<PointF>();
        /// <summary>Feine Teile werden mit <see cref="Shapes.FillFine"/> gerastert (Umriss zählt mit).</summary>
        public bool Fine = true;
    }

    /// <summary>
    /// Lädt alle Modelle: eingebettet in die .exe (Quellcode/Modelle) und zusätzlich eigene .modell-Dateien
    /// aus dem Ordner „Modelle“ neben der Programmdatei. Die Reihenfolge der Kategorien und Modelle steht in katalog.txt.
    /// </summary>
    public static class ModelLibrary
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static List<Model> all;
        static readonly List<string> errors = new List<string>();
        static readonly Dictionary<string, List<PointF>> profileCache = new Dictionary<string, List<PointF>>();

        /// <summary>Fehler beim Laden (Datei und Zeile), leer wenn alles geklappt hat.</summary>
        public static IList<string> Errors { get { Load(); return errors; } }

        public static List<Model> All { get { Load(); return all; } }

        /// <summary>Kategorien in Katalog-Reihenfolge mit ihren Modellen.</summary>
        public static List<KeyValuePair<string, List<Model>>> ByCategory()
        {
            var r = new List<KeyValuePair<string, List<Model>>>();
            foreach (var m in All)
            {
                int i = r.FindIndex(kv => kv.Key == m.Category);
                if (i < 0) { r.Add(new KeyValuePair<string, List<Model>>(m.Category, new List<Model>())); i = r.Count - 1; }
                r[i].Value.Add(m);
            }
            return r;
        }

        public static Model Find(string id)
        {
            foreach (var m in All) if (m.Id == id) return m;
            return null;
        }

        public static Model Custom()
        {
            foreach (var m in All) if (m.IsCustom) return m;
            return null;
        }

        static void Load()
        {
            if (all != null) return;
            all = new List<Model>();
            var files = new Dictionary<string, string>();   // id -> Text
            var asm = typeof(ModelLibrary).Assembly;
            string catalog = null;
            foreach (var res in asm.GetManifestResourceNames())
            {
                if (res == "Modelle.katalog.txt") catalog = ReadResource(res);
                else if (res.StartsWith("Modelle.") && res.EndsWith(".modell"))
                    files[res.Substring(8, res.Length - 8 - 7)] = ReadResource(res);
            }
            // eigene Modelle neben der .exe (überschreiben eingebaute mit gleichem Namen)
            string extra = null;
            try
            {
                extra = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modelle");
                if (Directory.Exists(extra))
                {
                    foreach (var f in Directory.GetFiles(extra, "*.modell"))
                        files[Path.GetFileNameWithoutExtension(f)] = File.ReadAllText(f, Encoding.UTF8);
                    string cat = Path.Combine(extra, "katalog.txt");
                    if (File.Exists(cat)) catalog = File.ReadAllText(cat, Encoding.UTF8);
                }
            }
            catch (Exception e) { errors.Add("Ordner „Modelle“: " + e.Message); }

            var order = new List<KeyValuePair<string, string>>();   // (Kategorie, id)
            if (catalog != null)
            {
                string cat = "Weitere";
                foreach (var raw in catalog.Split('\n'))
                {
                    string line = StripComment(raw);
                    if (line.Length == 0) continue;
                    if (line.StartsWith("[") && line.EndsWith("]")) cat = line.Substring(1, line.Length - 2).Trim();
                    else order.Add(new KeyValuePair<string, string>(cat, line));
                }
            }
            var done = new HashSet<string>();
            foreach (var kv in order)
            {
                if (kv.Value == "eigene") { all.Add(new Model { Id = "eigene", Name = "Eigene Zeichnung", Category = kv.Key, IsCustom = true, RefIsSize = false, RefName = "Stirnhöhe" }); done.Add(kv.Value); continue; }
                string text;
                if (!files.TryGetValue(kv.Value, out text)) { errors.Add("katalog.txt: Modell „" + kv.Value + "“ nicht gefunden"); continue; }
                var m = Parse(kv.Value, text, kv.Key);
                if (m != null) all.Add(m);
                done.Add(kv.Value);
            }
            // Modelle, die nicht im Katalog stehen (z. B. eigene Dateien), unter ihrer eigenen Kategorie anhängen
            var rest = new List<string>(files.Keys);
            rest.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var id in rest)
            {
                if (done.Contains(id)) continue;
                var m = Parse(id, files[id], null);
                if (m != null) all.Add(m);
            }
            if (Custom() == null) all.Add(new Model { Id = "eigene", Name = "Eigene Zeichnung", Category = "Eigene", IsCustom = true, RefIsSize = false, RefName = "Stirnhöhe" });
        }

        static string ReadResource(string name)
        {
            using (var s = typeof(ModelLibrary).Assembly.GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        static string StripComment(string line)
        {
            int i = line.IndexOf('#');
            return (i >= 0 ? line.Substring(0, i) : line).Trim();
        }

        // ------------------------------------------------------------------ Modelldatei

        /// <summary>Liest eine .modell-Datei. Bei Fehlern wird das Modell übersprungen und der Fehler gemerkt.</summary>
        public static Model Parse(string id, string text, string category)
        {
            var m = new Model { Id = id };
            if (category != null) m.Category = category;
            bool refSet = false;
            float pivotX = 0, pivotY = 0;
            PartBuilder part = null;
            var lines = text.Replace("\r", "").Split('\n');
            int ln = 0;
            try
            {
                for (ln = 0; ln < lines.Length; ln++)
                {
                    string line = StripComment(lines[ln]);
                    if (line.Length == 0) continue;
                    // Koordinatenzeile innerhalb eines Teils
                    if (part != null && (char.IsDigit(line[0]) || line[0] == '-' || line[0] == '.' || line.StartsWith("bogen ")))
                    {
                        part.AddPointLine(line);
                        continue;
                    }
                    int c = line.IndexOf(':');
                    if (c < 0) throw new FormatException("„Schlüssel: Wert“ erwartet");
                    string key = line.Substring(0, c).Trim().ToLowerInvariant(), val = line.Substring(c + 1).Trim();
                    if (key == "teil")
                    {
                        if (part != null) m.Parts.Add(part.Build());
                        part = new PartBuilder { Name = val };
                        continue;
                    }
                    if (part != null) { part.Set(key, val); continue; }
                    switch (key)
                    {
                        case "name": m.Name = val; break;
                        case "kategorie": if (category == null) m.Category = val; break;
                        case "beschreibung": m.Description = val; break;
                        case "quelle": m.Source = val; break;
                        case "lizenz": m.License = val; break;
                        case "bezug": m.RefName = val; break;
                        case "bezug-ist-groesse": m.RefIsSize = Bool(val); refSet = true; break;
                        case "boden": m.OnGround = Bool(val); break;
                        case "groesse": m.SizePercent = (int)Num(val); break;
                        case "winkel": m.Angle = (int)Num(val); break;
                        case "reynolds": m.Reynolds = (int)Num(val); break;
                        case "detail": m.Detail = Num(val); break;
                        case "bodenabstand": m.GroundGap = Num(val); break;
                        case "drehpunkt": { var v = Nums(val, 2); pivotX = v[0]; pivotY = v[1]; } break;
                        default: throw new FormatException("unbekannter Schlüssel „" + key + "“");
                    }
                }
                if (part != null) m.Parts.Add(part.Build());
            }
            catch (Exception e)
            {
                errors.Add(id + ".modell, Zeile " + (ln + 1) + ": " + e.Message);
                return null;
            }
            if (!refSet) m.RefIsSize = !m.OnGround;
            if (m.Name.Length == 0) m.Name = id;
            if (m.Parts.Count == 0) { errors.Add(id + ".modell: keine Teile"); return null; }
            foreach (var p in m.Parts)
                for (int i = 0; i < p.Points.Count; i++) p.Points[i] = new PointF(p.Points[i].X - pivotX, p.Points[i].Y - pivotY);
            return m;
        }

        /// <summary>Alle Teile als Punktlisten (Einheiten der Formgröße, Drehpunkt im Ursprung, y nach oben).</summary>
        public static List<List<PointF>> Polygons(Model m)
        {
            var r = new List<List<PointF>>();
            foreach (var p in m.Parts) r.Add(p.Points);
            return r;
        }

        /// <summary>
        /// Rastert das Modell aufs Gitter: Drehpunkt bei (pivotX, Kanalmitte), Formgröße in Zellen, Anstellwinkel in Grad.
        /// Bodenmodelle werden senkrecht so verschoben, dass ihr tiefster Punkt den gewünschten Bodenabstand hat.
        /// </summary>
        public static bool[] Rasterize(Model m, int nx, int ny, float pivotX, float sizeCells, float angleDeg)
        {
            var mask = new bool[nx * ny];
            var parts = new List<PointF[]>(m.Parts.Count);
            foreach (var p in m.Parts) parts.Add(Shapes.Transform(p.Points, sizeCells, angleDeg, pivotX, (ny - 1) / 2f));
            if (m.OnGround)
            {
                float minY = float.MaxValue;
                foreach (var pts in parts) foreach (var q in pts) minY = Math.Min(minY, q.Y);
                // 0 = aufgesetzt (unterste Zellreihe gehört zum Körper), sonst Abstand, mindestens 1 Zelle, ohne Angabe 3 Zellen
                float target = m.GroundGap < 0 ? 3f : m.GroundGap == 0 ? -0.5f : Math.Max(1f, m.GroundGap * sizeCells);
                float shift = target - minY;
                foreach (var pts in parts)
                    for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(pts[i].X, pts[i].Y + shift);
            }
            for (int i = 0; i < parts.Count; i++)
            {
                if (m.Parts[i].Fine) Shapes.FillFine(parts[i], mask, nx, ny);
                else Shapes.Fill(parts[i], mask, nx, ny);
            }
            return mask;
        }

        static bool Bool(string v)
        {
            v = v.ToLowerInvariant();
            return v == "ja" || v == "true" || v == "1";
        }

        static float Num(string v) { return float.Parse(v.Trim(), Inv); }

        static float[] Nums(string v, int min)
        {
            var parts = v.Split(new[] { ' ', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < min) throw new FormatException(min + " Zahlen erwartet: „" + v + "“");
            var r = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++) r[i] = float.Parse(parts[i], Inv);
            return r;
        }

        sealed class PartBuilder
        {
            public string Name;
            readonly List<PointF> pts = new List<PointF>();
            readonly List<bool> corners = new List<bool>();
            List<PointF> shape;
            bool primitive;
            int smooth;
            float chord = 1, thick = 1, angle, x, y;
            bool mirror, reverse;

            public void AddPointLine(string line)
            {
                if (line.StartsWith("bogen "))
                {
                    // bogen cx cy r start ende [n]: Kreisbogen in Grad, gegen den Uhrzeigersinn bei start < ende
                    var v = Nums(line.Substring(6), 5);
                    int n = v.Length > 5 ? (int)v[5] : Math.Max(4, (int)(Math.Abs(v[4] - v[3]) / 6));
                    for (int i = 0; i <= n; i++)
                    {
                        double a = (v[3] + (v[4] - v[3]) * i / n) * Math.PI / 180;
                        pts.Add(new PointF(v[0] + v[2] * (float)Math.Cos(a), v[1] + v[2] * (float)Math.Sin(a)));
                        corners.Add(false);
                    }
                    return;
                }
                bool corner = line.EndsWith("!");
                var xy = Nums(corner ? line.TrimEnd('!') : line, 2);
                pts.Add(new PointF(xy[0], xy[1]));
                corners.Add(corner);
            }

            public void Set(string key, string val)
            {
                switch (key)
                {
                    case "profil": shape = ProfileFile(val); break;
                    case "naca": shape = NacaCode(val); break;
                    case "kreis": { var v = Nums(val, 3); shape = Shapes.Circle(v[0], v[1], v[2], v.Length > 3 ? (int)v[3] : 96); primitive = true; } break;
                    case "ellipse": { var v = Nums(val, 4); shape = Shapes.Ellipse(v[0], v[1], v[2], v[3], v.Length > 4 ? (int)v[4] : 128); primitive = true; } break;
                    case "rechteck": { var v = Nums(val, 4); shape = Shapes.Rect(v[0], v[1], v[2], v[3]); primitive = true; } break;
                    case "stab": { var v = Nums(val, 5); shape = Capsule(v[0], v[1], v[2], v[3], v[4]); } break;
                    case "punkte": break;   // die folgenden Zeilen sind Koordinaten
                    case "glatt": smooth = (int)Num(val); break;
                    case "sehne": chord = Num(val); break;
                    case "dicke": thick = Num(val); break;
                    case "winkel": angle = Num(val); break;
                    case "lage": { var v = Nums(val, 2); x = v[0]; y = v[1]; } break;
                    case "spiegeln": mirror = Bool(val); break;
                    case "umkehren": reverse = Bool(val); break;
                    default: throw new FormatException("unbekannter Schlüssel „" + key + "“ in Teil „" + Name + "“");
                }
            }

            public ModelPart Build()
            {
                List<PointF> src = shape ?? (smooth > 1 ? Shapes.Smooth(pts, corners, smooth) : new List<PointF>(pts));
                if (src.Count < 3) throw new FormatException("Teil „" + Name + "“ hat weniger als 3 Punkte");
                // Profilteil: Sehne skalieren, Dicke strecken, spiegeln (umgedrehter Flügel), um die Vorderkante drehen, verschieben
                double a = angle * Math.PI / 180.0;
                float ca = (float)Math.Cos(a), sa = (float)Math.Sin(a);
                var r = new List<PointF>(src.Count);
                foreach (var q in src)
                {
                    float px = q.X * chord, py = (mirror ? -q.Y : q.Y) * chord * thick;
                    if (reverse) px = chord - px;
                    r.Add(new PointF(x + px * ca + py * sa, y - px * sa + py * ca));
                }
                return new ModelPart { Name = Name, Points = r, Fine = !primitive };
            }
        }

        /// <summary>Stab mit runden Enden von (x0, y0) nach (x1, y1) mit Dicke d (Streben, Rohre, Halo, Brückenträger).</summary>
        static List<PointF> Capsule(float x0, float y0, float x1, float y1, float d)
        {
            double a = Math.Atan2(y1 - y0, x1 - x0), r = d / 2;
            var p = new List<PointF>();
            for (int i = 0; i <= 12; i++)
            {
                double t = a - Math.PI / 2 + Math.PI * i / 12;
                p.Add(new PointF(x1 + (float)(r * Math.Cos(t)), y1 + (float)(r * Math.Sin(t))));
            }
            for (int i = 0; i <= 12; i++)
            {
                double t = a + Math.PI / 2 + Math.PI * i / 12;
                p.Add(new PointF(x0 + (float)(r * Math.Cos(t)), y0 + (float)(r * Math.Sin(t))));
            }
            return p;
        }

        /// <summary>NACA-4-Ziffern-Code, z. B. „2412“, wahlweise mit Punktzahl: „2412 160“.</summary>
        static List<PointF> NacaCode(string val)
        {
            var parts = val.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            string code = parts[0];
            if (code.Length != 4) throw new FormatException("NACA-Code mit 4 Ziffern erwartet: „" + code + "“");
            float m = (code[0] - '0') / 100f, p = (code[1] - '0') / 10f, t = int.Parse(code.Substring(2)) / 100f;
            return Shapes.Naca(m, p, t, parts.Length > 1 ? int.Parse(parts[1]) : 120);
        }

        /// <summary>
        /// Profil aus Quellcode/Modelle/Profile/&lt;name&gt;.dat (Original-Datei der UIUC-Datenbank).
        /// Versteht das Selig-Format (eine Punktliste Hinterkante–Oberseite–Vorderkante–Unterseite–Hinterkante)
        /// und das Lednicer-Format (Anzahl der Punkte, dann Ober- und Unterseite je von der Vorderkante aus).
        /// Ergebnis: Sehne 1, Vorderkante im Ursprung.
        /// </summary>
        static List<PointF> ProfileFile(string name)
        {
            name = name.Trim().ToLowerInvariant();
            List<PointF> cached;
            if (profileCache.TryGetValue(name, out cached)) return new List<PointF>(cached);
            string text = null;
            try
            {
                string f = Path.Combine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modelle"), name + ".dat");
                if (File.Exists(f)) text = File.ReadAllText(f);
            }
            catch { }
            if (text == null)
            {
                using (var s = typeof(ModelLibrary).Assembly.GetManifestResourceStream("Profile." + name + ".dat"))
                {
                    if (s == null) throw new FormatException("Profil „" + name + "“ nicht gefunden");
                    using (var r = new StreamReader(s, Encoding.GetEncoding(28591))) text = r.ReadToEnd();
                }
            }
            var rows = new List<float[]>();
            bool first = true;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string line = StripComment(raw);
                if (first) { first = false; continue; }   // erste Zeile: Name des Profils
                if (line.Length == 0) continue;
                rows.Add(Nums(line, 2));
            }
            List<PointF> poly;
            if (rows.Count > 0 && rows[0][0] > 1.5f && rows[0][1] > 1.5f)
            {
                // Lednicer: erste Zeile = Punktzahlen, dann Oberseite und Unterseite (Vorderkante -> Hinterkante)
                int nu = (int)rows[0][0], nl = (int)rows[0][1];
                if (rows.Count < 1 + nu + nl) throw new FormatException("Profil „" + name + "“: " + (nu + nl) + " Punkte erwartet");
                poly = new List<PointF>();
                for (int i = nu; i >= 1; i--) poly.Add(new PointF(rows[i][0], rows[i][1]));
                for (int i = 2; i <= nl; i++) poly.Add(new PointF(rows[nu + i][0], rows[nu + i][1]));
            }
            else
            {
                poly = new List<PointF>();
                foreach (var v in rows) poly.Add(new PointF(v[0], v[1]));
            }
            // doppelte Endpunkte (geschlossene Hinterkante) entfernen
            while (poly.Count > 3 && Dist(poly[0], poly[poly.Count - 1]) < 1e-6f) poly.RemoveAt(poly.Count - 1);
            // auf Sehne 1 normieren: Vorderkante = Punkt mit kleinstem x, Hinterkante = Mitte der Endpunkte
            int le = 0;
            for (int i = 1; i < poly.Count; i++) if (poly[i].X < poly[le].X) le = i;
            PointF L = poly[le], T = new PointF((poly[0].X + poly[poly.Count - 1].X) / 2, (poly[0].Y + poly[poly.Count - 1].Y) / 2);
            float len = Dist(L, T);
            for (int i = 0; i < poly.Count; i++) poly[i] = new PointF((poly[i].X - L.X) / len, (poly[i].Y - L.Y) / len);
            profileCache[name] = new List<PointF>(poly);
            return poly;
        }

        static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }
    }
}
