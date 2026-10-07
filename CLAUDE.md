# Windkanal – Hinweise für Claude

Virtueller Windkanal für Windows in C# (.NET Framework 4.8), Windows-.exe.
Repo: github.com/wrzelias16/aero-simulation (privat). Ausführliche Technik steht in `README.md`.

## Stand
- **v0.0.1** = fertige 2D-Version ("Windkanal 2D"), alles in `main`.
- GPU-Löser (OpenCL, CPU-Fallback), neues UI (Schrift Outfit, Hell/Dunkel, Startfenster), Rauchmodus,
  79 Modelle als Textdateien (`Quellcode/Modelle/LIESMICH.md`, keine echten CAD-Daten), Anzeige "Feinstes Detail".
- Rendering und Rauch-Partikel laufen noch auf der CPU (nächster Perf-Schritt: Bild auf GPU zeichnen, zurückgestellt).
- **Nächstes Ziel: 3D-Version.** Elias will, dass 2D "genau da bleibt, wo es ist".

## 3D-Version (Ordner `Quellcode/App3D`, Namensraum `Windkanal3D`)
- 2D und 3D sind **getrennt**: 3D nutzt Design/Bedienelemente aus `App` (Theme, Card, FlatButton …) nur lesend und
  ändert keinen 2D-Code. Gemeinsames lieber nach `App3D` kopieren als das 2D-Original umbauen.
  Einzige Berührungspunkte: Umschalter "2D | 3D" im Kopf von `MainForm` und die Fensterwechsel in `Program.cs`.
- `Lbm3D.cs`: D3Q19-BGK-Löser auf der GPU (eigener OpenCL-Zugriff), nur GPU, kein CPU-Fallback (so gewollt).
  Kräfte per Impulsaustausch relativ zum Umgebungsdruck. Stabil bis etwa tau 0,52, darum Re im Fenster begrenzt.
- `Mesh.cs`: STL (binär/Text) und OBJ laden, eingebaute Körper (Kugel, Ahmed-Körper, Würfel, Zylinder),
  `Placement` dreht/skaliert/setzt den Körper und wandelt ihn in Zellen um (Strahl-Parität entlang x).
- `Scene.cs`: eigener Software-Renderer für die 3D-Ansicht (Tiefenpuffer, Kantenglättung, alle CPU-Kerne).
- `Flow3D.cs`: grobes Geschwindigkeitsfeld von der GPU (`Lbm3D.ReadVelocity`), Stromlinien (RK2), Rauchrechen (Kreuzform).
- Rauch: echtes Dichtefeld in voller Auflösung auf der GPU (`Lbm3D.SmokeOn`, semi-Lagrange + MacCormack wie in 2D,
  alle 2 Strömungsschritte), als Volumen gezeichnet (`RenderSmoke`, Strahlen mit Tiefe aus `Scene.Overlay`).
- `Form3D.cs`: 3D-Fenster. Achsen: x = Strömung, y = seitlich, z = oben. Darstellung: Stromlinien, Rauch,
  Schnittebene im Raum, nur Körper. Geladene Modelle werden automatisch ausgerichtet (`Mesh.AutoOrient`:
  längste Seite = x, flachste = oben, höheres Ende = hinten), Knöpfe X/Y/Z kippen um 90°, 180° tauscht vorne/hinten. Schrittzahl pro Bild nur nach GPU-Zeit
  bemessen, sonst bremst das Zeichnen die Rechnung aus.
- Tests: `Test3D.exe` (Rechenkern: leerer Kanal, Kugel Re 20/40, Würfel auf dem Boden),
  `Vorschau3D.exe <Ordner>` (Import-Rundreise, Zellen, speichert Bilder vom 3D- und 2D-Fenster ohne Bildschirmfoto).
- Rechner: PC RTX 5070 Ti (16 GB) + Core Ultra 7 265KF, 32 GB RAM; Laptop GTX 1660 Ti (6 GB, kleinere Gitter).
  CUDA ist auf dem PC installiert; Rechenkern-Optimierung ist bewusst zurückgestellt.

## Bauen und Testen
- Bauen (ohne SDK, nutzt csc aus .NET Framework 4.8):
  `powershell -ExecutionPolicy Bypass -File Quellcode\build.ps1` -> `Quellcode\bin\Windkanal2D.exe`
- Installer: `Quellcode/Setup` (erzeugt `Windkanal2D-Setup.exe`). Test: `Quellcode/Test/ValidationTest.cs`
  (muss nach Löser-Änderungen weiter dieselben Ergebnisse liefern).
- `*.exe` und `Quellcode/bin/` stehen in `.gitignore` und kommen nicht über GitHub; auf jedem Rechner neu bauen.

## Versionsnummern (Git-Tags)
- v0.0.1 = jetziger 2D-Stand (gesetzt). Danach v0.0.2 bis v0.0.9; v0.0.9 ist die letzte Version vor der stabilen 3D-Version
  (auch v0.0.10 und höher sind erlaubt, falls mehr Zwischenstände nötig sind).
- **v1.0.0** = erste saubere, stabile 3D-Version.
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

## Neuer Rechner (z. B. Laptop)
```powershell
git clone https://github.com/wrzelias16/aero-simulation.git   # vorher: gh auth login
powershell -ExecutionPolicy Bypass -File aero-simulation\Quellcode\build.ps1
```
