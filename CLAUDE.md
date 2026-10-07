# Windkanal – Hinweise für Claude

Virtueller Windkanal für Windows in C# (.NET Framework 4.8), Windows-.exe.
Repo: github.com/wrzelias16/aero-simulation (privat). Ausführliche Technik steht in `README.md`.

## Stand
- **v1.0.0** (Release auf GitHub, von Elias so gewünscht): erste stabile Version mit 2D **und** 3D, alles unten beschrieben.
- **v0.0.1** = frühere reine 2D-Version ("Windkanal 2D").
- GPU-Löser (OpenCL, CPU-Fallback), neues UI (Schrift Outfit, Hell/Dunkel, Startfenster), Rauchmodus,
  79 Modelle als Textdateien (`Quellcode/Modelle/LIESMICH.md`, keine echten CAD-Daten), Anzeige "Feinstes Detail".
- Rendering und Rauch-Partikel laufen noch auf der CPU (nächster Perf-Schritt: Bild auf GPU zeichnen, zurückgestellt).
- 3D ist seit v1.0.0 drin. Elias will, dass 2D "genau da bleibt, wo es ist" (2D-Löser und ValidationTest unverändert).

## 3D-Version (Ordner `Quellcode/App3D`, Namensraum `Windkanal3D`)
- 2D und 3D sind **getrennt**: 3D nutzt Design/Bedienelemente aus `App` (Theme, Card, FlatButton …) nur lesend und
  ändert keinen 2D-Code. Gemeinsames lieber nach `App3D` kopieren als das 2D-Original umbauen.
  Einzige Berührungspunkte: Umschalter "2D | 3D" im Kopf von `MainForm` und die Fensterwechsel in `Program.cs`.
- `Lbm3D.cs`: D3Q19-BGK-Löser auf der GPU (eigener OpenCL-Zugriff), nur GPU, kein CPU-Fallback (so gewollt).
  Kräfte per Impulsaustausch relativ zum Umgebungsdruck. Turbulenzmodell LES (Smagorinsky, Konstante wie FluidX3D):
  an der Kugel bis Re 1 Mio. stabil, Cd plausibel (Re 400: 0,65; Re 1e5: 0,44). uIn bleibt 0,05 (0,08 verfälscht Cd).
  Leistung: Verteilungen als FP16 gespeichert (f - W, gerechnet in FP32) = ~1,9x schneller, halber Speicher;
  FP16 weicht < 0,5 % von FP32 ab (Test3D prüft das). Auslass mit festem Druck (Dichte 1), sonst driftet die Masse.
  Kraftsumme nur im letzten Schritt eines Pakets, Rauch-Geschwindigkeit wird im Strömungsschritt mitgeschrieben.
  RTX 5070 Ti: ~6900 MLUPS (FP16) gegen ~3650 (FP32), ~530-590 GB/s. `Test3D.exe bench` misst das.
- CUDA bewusst nicht: der Löser ist speichergebunden, OpenCL läuft auf NVIDIA über denselben Treiber gleich schnell,
  und CUDA bräuchte das Toolkit (NVRTC) auf jedem Rechner bzw. liefe nicht auf AMD/Intel.
- `Mesh.cs`: STL (binär/Text) und OBJ laden, eingebaute Körper (Kugel, Ahmed-Körper, Würfel, Zylinder),
  `Placement` dreht/skaliert/setzt den Körper und wandelt ihn in Zellen um (Strahl-Parität entlang x).
- `Scene.cs`: eigener Software-Renderer für die 3D-Ansicht (Tiefenpuffer, Kantenglättung, alle CPU-Kerne).
- `Flow3D.cs`: grobes Geschwindigkeitsfeld von der GPU (`Lbm3D.ReadVelocity`), Stromlinien (RK2), Rauchrechen (Kreuzform).
- Rauch: echtes Dichtefeld auf eigenem Gitter (Kasten um Körper + Nachlauf, `SetSmokeBox`, meist 2x feiner) auf der GPU (`Lbm3D.SmokeOn`, semi-Lagrange + MacCormack wie in 2D,
  alle 2 Strömungsschritte), als Volumen gezeichnet (`RenderSmoke`, Strahlen mit Tiefe aus `Scene.Overlay`).
- `Form3D.cs`: 3D-Fenster. Achsen: x = Strömung, y = seitlich, z = oben. Darstellung: Stromlinien, Rauch,
  Schnittebene im Raum, Oberflächendruck (cp je fein unterteiltem Dreieck, Dichte der Zelle davor), nur Körper.
  Kamera-Knopf speichert die 3D-Ansicht als PNG. Geladene Modelle werden automatisch ausgerichtet (`Mesh.AutoOrient`:
  längste Seite = x, flachste = oben, höheres Ende = hinten), Knöpfe X/Y/Z kippen um 90°, 180° tauscht vorne/hinten. Schrittzahl pro Bild nur nach GPU-Zeit
  bemessen, sonst bremst das Zeichnen die Rechnung aus. Gerechnet wird in einem eigenen Thread (`SimLoop`), alle
  GPU-Zugriffe unter `lock (gpu)` bzw. `EnterGpu/LeaveGpu`. Auflösung beim Start per Probe-Rechnung gewählt
  (`PickResolution`: größtes Gitter mit >= ~300 Schritten/s, das in den Speicher passt). Kantenglättung und
  Rauch-Auflösung passen sich automatisch an, wenn Zeichnen zu lange dauert.
- Tests: `Test3D.exe` (Rechenkern: leerer Kanal, Kugel Re 20/40, Würfel auf dem Boden),
  `Vorschau3D.exe <Ordner>` (Import-Rundreise, Zellen, speichert Bilder vom 3D- und 2D-Fenster ohne Bildschirmfoto).
- Rechner: PC RTX 5070 Ti (16 GB) + Core Ultra 7 265KF, 32 GB RAM; Laptop GTX 1660 Ti (6 GB, kleinere Gitter).
  CUDA ist auf dem PC installiert; Rechenkern-Optimierung ist bewusst zurückgestellt.

## 2D: bewegliche Teile
- Modellformat kann bewegliche Teile (`bewegung: x y grad` am Teil, `bewegung-name/-dauer/-tempo`, `bezug-meter`).
  Dauer in Rechenschritten = gleich viele Überströmungen der Bezugslänge wie am echten Auto.
- Eigene Kategorie „Animiert · Klappen & aktive Aero“ (DRS-Flügel, Macarena, F1 mit DRS, F1 2026 mit aktiver Aero
  [Front- und Heckflügel, Winkel der Front-Flaps angenommen], Flügel mit ausfahrender Landeklappe [18 s, 25°]).
- Gitter 2D bis „Extrem“ 2400 × 960 (RTX 5070 Ti: ~2800 Schritte/s, Anzeige ~30 FPS); 3D bis „Extrem“ 704 × 320 × 320.
- Knopf „Neu“ löscht eigene Zeichnungen (zurück zum Modell, auf das gezeichnet wurde, sonst leere Fläche).
- Modelle: Heckflügel mit DRS (2025: < 400 ms, 85 mm, Art. 3.10.10), Heckflügel 2026 „Macarena“ (Ferrari SF-26,
  Flap dreht 180°; Drehpunkt Sehnenmitte = Annahme), F1-Wagen mit animiertem DRS. Knopf im Strömungsbild, Taste D.
- Tempo-Auswahl neben dem Knopf: Echtzeit-Faktoren 0,25x … 4x (Bewegung nach der Uhr, DRS bei 1x in 0,40 s, gemessen
  0,403 s in Vorschau3D) oder „Physikalisch“ (nach Rechenschritten = Zeitlupe). Während der Bewegung keine
  500-Schritte-Grenze und kurze Bilder (12 ms Rechenzeit). Die Anzeige nennt, wie viel % Echtzeit die Luft dabei schafft.
- Modelle werden mit `Quellcode/Modelle/Werkzeug/modelle_erzeugen.py` erzeugt. Achtung: das Skript schreibt alle
  Dateien mit LF neu; danach unveränderte Dateien mit `git ls-files -m Quellcode/Modelle | xargs git checkout --` zurücksetzen.

## 2D: Vergleichsmodus
- Einstellungen → „Vergleich“: zweite Strömung B (eigener Solver, gleiche Re/Größe/Winkel/Auflösung), anderes Modell
  oder Klappe offen. A oben, B unten im Strömungsbild; Karten zeigen B und den Unterschied zu A in %, Verlauf B blass.
  Gerechnet im Gleichschritt (gleiche Schrittzahl je Paket). Getestet: DRS zu 0,77 gegen offen 0,35 (−55 %).
- Einstellungen-Karten (2D und 3D) scrollen bei niedrigen Fenstern; Titel/Legenden kürzen sich bei schmalen Karten.
  Kleine Fenster (1280 × 800) prüft Vorschau3D mit (`fenster_2d_klein.png`, `fenster_3d_klein.png`).

## Datei-Menü (2D und 3D, `App/FileTools.cs`, `App/Recorder.cs`)
- Sitzung speichern/öffnen (`*.windkanal`, Textzeilen „schlüssel: wert“, Strg+S / Strg+O); eine 3D-Sitzung im
  2D-Fenster geöffnet wechselt nach 3D und umgekehrt (Events OpenIn3D/OpenIn2D in Program.cs).
- Bild (PNG) und Aufnahme in Echtzeit (Strg+R): MP4 über Media Foundation (H.264, in Windows enthalten, kein ffmpeg)
  oder GIF (eigener Encoder, Median-Cut-Palette, LZW). Aufgenommen wird die Ansicht-Karte per DrawToBitmap.
- Länge (m) und Tempo (km/h) ergeben die echte Reynoldszahl; die Simulation übernimmt sie bis zum Reglerende
  (2D 20 000, 3D 1 Mio.), Kräfte gelten fürs echte Tempo (3D: N und kg Abtrieb in „Kennzahlen“).

## Bauen und Testen
- Bauen (ohne SDK, nutzt csc aus .NET Framework 4.8):
  `powershell -ExecutionPolicy Bypass -File Quellcode\build.ps1` -> `Quellcode\bin\Windkanal2D.exe`
- Installer: `Quellcode/Setup` (erzeugt `Windkanal2D-Setup.exe`), im Programm-Design: wird mit `App/Ui.cs`, `App/FlowStage.cs`,
  `App/AppInfo.cs` (+ Abhängigkeiten) und den Outfit-Schriften gebaut. Rückfragen im Fenster statt Meldungsfenster.
  Anzeige „Windkanal“, Ordner/Registry/exe behalten die alten Namen (Windkanal2D), damit Updates die Installation finden.
- Versionsnummer an einer Stelle: `App/AppInfo.cs` (Startfenster, Installer, Windows-Apps-Eintrag).
- Startfenster und Installer zeigen `FlowStage` (angedeutete Wirbelstraße + Rauch, keine echte Simulation). Test: `Quellcode/Test/ValidationTest.cs`
  (muss nach Löser-Änderungen weiter dieselben Ergebnisse liefern).
- `*.exe` und `Quellcode/bin/` stehen in `.gitignore` und kommen nicht über GitHub; auf jedem Rechner neu bauen.

## Versionsnummern (Git-Tags)
- v0.0.1 = jetziger 2D-Stand (gesetzt). Danach v0.0.2 bis v0.0.9; v0.0.9 ist die letzte Version vor der stabilen 3D-Version
  (auch v0.0.10 und höher sind erlaubt, falls mehr Zwischenstände nötig sind).
- **v1.0.0** = erste saubere, stabile 3D-Version (gesetzt; Elias hat direkt von v0.0.1 auf v1.0.0 gesprungen).
  Weitere Versionen nach diesem Muster: kleine Verbesserungen v1.0.x, neue Funktionen v1.x.0.
- Vor jedem neuen Tag und vor jedem Push Elias kurz fragen.

## Arbeitsablauf (Git)
- Jede neue Aufgabe auf einem **neuen Branch**, nie direkt auf `main`.
- Auf dem PC des Nutzers bauen und testen, damit Elias die .exe selbst starten kann.
- Fertig = Branch pushen + Pull Request (PR). **Claude merged nicht selbst**, Elias merged auf GitHub.
  Bei gestapelten PRs auf die Merge-Reihenfolge hinweisen.
- Elias arbeitet an zwei Rechnern (Desktop mit RTX 5070 Ti und Laptop), jeweils Ordner `Documents\aero-simulation`.
  Regel: **zu Beginn `git pull`/`git fetch` und `git status` prüfen, am Ende pushen.**
  Unfertige Arbeit darf als Sicherung auf den Branch gepusht werden; sie landet erst mit dem Merge in `main`.
- Mehrere parallele Chats teilen sich einen Ordner: bei paralleler Arbeit eigenen `git worktree` benutzen.
- Alte, gemergte Branches (detail-modelle, detail-rauch, gpu-opencl, neues-design) können liegen bleiben.

## Elias und Kommunikation
- Schreibt Deutsch, kennt sich mit Git/Entwicklung wenig aus. Erklärungen **einfach, ruhig, auf Deutsch, in kleinen Schritten,
  ohne Fachchinesisch**. Begriffe wie Branch, PR, Merge, pull/push wurden schon erklärt, trotzdem kurz in Klartext halten.
- Qualität soll gleich beim ersten Mal hoch sein; Usage/Kosten sparen, nicht unnötig viele Runden.
- Bei Unsicherheit einfach fragen.
- Bildschirmfotos per Screen-Zugriff hat Elias abgelehnt: Vorschaubilder aus dem Zeichencode erzeugen
  (siehe `Quellcode/Test/ModellVorschau.cs`).

## Design
- Kein "AI-Slop": keine generischen Schriften (Segoe UI, Arial, Inter vermeiden), keine violetten Verläufe.
- Eng an Eliasʼ Referenzbilder anlehnen. Aktuelle Schrift: **Outfit** (`Quellcode/Fonts`). Hell- und Dunkelmodus.
- Aufteilung (2D und 3D gleich, von Elias so gewünscht): Kopf (Logo, Ansichten, 2D/3D, Knöpfe), **Ansicht groß in der
  Mitte**, **Einstellungen rechts** (Abschnitte OBJEKT · STRÖMUNG · GITTER · DARSTELLUNG · VERGLEICH bzw. SCHNITTEBENE,
  scrollt bei kleinen Fenstern), **Ergebnisse unten** als technische Tabelle (Kennwert | Aktuell | Mittel | ± | B | Δ)
  mit Verlauf bzw. Schnittbild daneben, **Statuszeile** ganz unten (Zustand, GPU, Gitter, Tempo, Meldungen).
- Look „technischer, gleiche Farben“: Karten-Ecken 12, keine Schatten, Knöpfe/Felder mit Ecken 8–10 statt Pillen,
  Abschnittstitel in Großbuchstaben.
- „Ansicht groß“ (Knopf oben rechts in der Ansicht, Taste F, zurück mit F/Esc): blendet Einstellungen und Ergebnisse aus
  (2D und 3D); im Vergleich werden A und B so ~1,4x größer.
- Übergänge: 2D ↔ 3D und hell ↔ dunkel blenden weich über (`Transition.CrossFade` in Ui.cs: Fensterfoto als
  Deckblatt, 240 ms Ease-out). Ohne Windows-Animationen (UIEffectsEnabled) sofort.

## Neuer Rechner (z. B. Laptop)
```powershell
git clone https://github.com/wrzelias16/aero-simulation.git   # vorher: gh auth login
powershell -ExecutionPolicy Bypass -File aero-simulation\Quellcode\build.ps1
```
