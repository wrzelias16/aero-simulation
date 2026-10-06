# Windkanal 2D

Virtueller 2D-Windkanal für Windows: Strömung um Zylinder, Tragflächen, ein Auto-Profil oder eigene
Formen, mit Rauchlinien, Druck- und Wirbelansicht sowie Messwerten für Widerstand und Auftrieb.

**Stand:** Version 1 ist fertig und läuft. Die Strömungsrechnung läuft jetzt auf der **Grafikkarte (OpenCL)**, wenn eine da ist,
sonst wie bisher auf allen CPU-Kernen. Bedienung und Ergebnisse sind gleich geblieben (siehe Abschnitt 4).
Der Rest von Version 2 (echter Rauch, genauere Randbedingung) ist unten Schritt für Schritt beschrieben.

## Inhalt

1. [Schnellstart auf einem neuen PC](#1-schnellstart-auf-einem-neuen-pc)
2. [Was Version 1 kann](#2-was-version-1-kann-fertig)
3. [Wie Version 1 funktioniert (Technik)](#3-wie-version-1-funktioniert-technik)
4. [Validierung: was gemessen wurde](#4-validierung-was-gemessen-wurde)
5. [Bekannte Schwächen und nicht getestete Stellen](#5-bekannte-schwächen-und-nicht-getestete-stellen)
6. [Roadmap Version 2 (GPU, Rauch, Realismus)](#6-roadmap-version-2)
7. [Projektstruktur und Build](#7-projektstruktur-und-build)
8. [Installer und Deinstallierer](#8-installer-und-deinstallierer)
9. [Starter-Prompt für Claude auf dem neuen PC](#9-starter-prompt-für-claude-auf-dem-neuen-pc)

---

## 1. Schnellstart auf einem neuen PC

```powershell
# Repo holen (privates Repo, vorher bei GitHub anmelden: winget install GitHub.cli ; gh auth login)
git clone https://github.com/wrzelias16/aero-simulation.git
cd aero-simulation

# Version 1 bauen (braucht nur Windows, kein SDK: nutzt den mitgelieferten C#-Compiler von .NET Framework 4.8)
powershell -ExecutionPolicy Bypass -File Quellcode\build.ps1

# App direkt starten
Quellcode\bin\Windkanal2D.exe
```

Danach liegen `Windkanal2D-Setup.exe` und `Windkanal2D-Deinstallieren.exe` im Projektordner.

**GPU:** Es muss nichts zusätzlich installiert werden. Die GPU-Rechnung nutzt OpenCL, das in jedem aktuellen
Grafiktreiber (NVIDIA, AMD, Intel) steckt (`C:\Windows\System32\OpenCL.dll`). Findet die App keine nutzbare GPU,
rechnet sie automatisch auf der CPU. Unten in der Statuszeile steht, womit gerade gerechnet wird.
Zum Vergleichen kann man die CPU erzwingen: vor dem Start `$env:WK_CPU = "1"` setzen.

**Reihenfolge am neuen PC (empfohlen):**

1. Version 1 bauen und ausprobieren, damit man das Referenzverhalten kennt.
2. Validierungstest laufen lassen (siehe Abschnitt 4), die Zahlen notieren. Sie sind der Maßstab für die GPU-Version.
3. Mit Roadmap Phase 1 anfangen (GPU-Port).

Hardware des Entwicklungs-PCs (zum Vergleich): NVIDIA GTX 1660 Ti (6 GB), 12 logische CPU-Kerne, Windows 10.

---

## 2. Was Version 1 kann (fertig)

### Objekte
Zylinder, Quadrat, flache Platte, NACA-Tragflächen 0012 / 2412 / 4412, Auto-Seitenprofil (stark vereinfacht),
und **eigene Formen mit der Maus zeichnen** (linke Taste = Wand, rechte Taste = radieren, Bürstenradius skaliert mit der Auflösung).
Anstellwinkel (-90° bis +90°) und Größe (4 bis 60 % der Tunnelhöhe) sind per Regler einstellbar.

### Strömung
- Reynoldszahl von 10 bis 20.000 (logarithmischer Regler)
- Vier Auflösungen: 400×160, 600×240, 900×360, 1200×480 (sichtbare Messstrecke, dazu kommt hinten eine unsichtbare Beruhigungsstrecke)
- Tunnelwände wahlweise reibungsfrei (Symmetrie, Standard) oder mit Haftbedingung
- Sanftes Hochfahren der Anströmung beim Start, danach kleiner Anstoß hinter dem Körper, damit die Wirbelablösung einsetzt
- Automatischer Neustart mit Hinweis, falls die Simulation doch instabil wird

### Darstellung
- Geschwindigkeit, Druck (cp), Wirbelstärke, "nur Rauch"
- Rauchlinien aus Partikeln: Rechen am Einlass, Partikel werden mit RK2 durch das Geschwindigkeitsfeld getragen
- Farbskala-Legende, Cursor-Anzeige (|u|/U∞, cp, ungefähre Geschwindigkeit in m/s)
- Diagramm von cw und ca über der Zeit, Leertaste = Pause/Start

### Messwerte
cw (Widerstand) und ca (Auftrieb) als Momentanwert und Mittelwert, Gleitzahl ca/cw, Strouhal-Zahl, Versperrung,
Bezugslänge, Zeit in Umströmungszeiten (t·U/L), Leistung (MLUPS und FPS).

### Umrechnung auf echte Luft (20 °C)
Echte Bezugslänge in Metern eingeben, daraus ergeben sich Anströmgeschwindigkeit (m/s und km/h) und die Kräfte in N je Meter Spannweite.
Dazu: Re = U·L/ν mit ν = 1,516·10⁻⁵ m²/s und ρ = 1,204 kg/m³. Bezugslänge L ist je nach Objekt Durchmesser, Kantenlänge, Sehnenlänge
oder (bei Auto und eigener Form) die gemessene Stirnhöhe in Zellen.

### Installer
`Windkanal2D-Setup.exe` mit Fenster (Installieren / Deinstallieren), Verknüpfungen auf Wunsch, ohne Admin-Rechte. Details in Abschnitt 8.

### Status der Arbeit (Chronik, was nacheinander gemacht und entschieden wurde)

1. LBM-Löser D2Q9 mit Smagorinsky-Turbulenzmodell geschrieben, Formen und Rasterung, Oberfläche, Installer.
2. Erster Test gegen Literatur: Zylinder bei Re = 100 lieferte cw = 1,52 und St = 0,177. Ergebnis: Frequenz etwas zu hoch, cw deutlich zu hoch.
3. Bei Re = 10.000 und 20.000 wurde die Simulation instabil. Gegenmaßnahmen, jeweils einzeln getestet:
   - **Regularisierte Kollision** (Nicht-Gleichgewichtsanteil nur aus dem Spannungstensor rekonstruiert): Re = 20.000 stabil.
   - **Sanftes Hochfahren** der Anströmung (smoothstep über 2·NX Schritte) statt Sprung aus dem Stand.
   - **Gitter-Machzahl begrenzen** (`Solver.ChooseU0`): U0 abhängig von der Versperrung, bei 40 bis 60 % Versperrung bricht sonst die Strömung im engen Spalt zusammen. Diagnose-Ausgabe zeigte: Instabilität entstand direkt am Körper (x≈90, y≈45), nicht am Rand.
   - **Sicherheitsnetz:** Gittergeschwindigkeit wird auf 0,25 gekappt.
   - **Beruhigungsstrecke** hinter der Messstrecke (15 % der Länge, unsichtbar): erhöhte Viskosität und Dämpfung der Wirbel.
   - **Auslass mit festem Druck** (ρ = 1) statt Null-Gradient: cw sank von 1,58 auf 1,54 (Zylinder, Re = 100, 8 % Versperrung), St stieg dabei leicht von 0,168 auf 0,172.
4. Strouhal-Auswertung robuster gemacht (geglättetes ca, Hysterese, mindestens 3 Nulldurchgänge).
5. Installer und Deinstallierer vollständig getestet (still und mit Fenster, danach Prüfung: Ordner, Verknüpfungen, Registry sind weg).
6. Auf GitHub hochgeladen.

---

## 3. Wie Version 1 funktioniert (Technik)

Datei: `Quellcode/App/Solver.cs`. Alles in **Gittereinheiten** (Δx = Δt = 1, Dichte ρ = 1, Schallgeschwindigkeit² = 1/3).

### Methode
- **Lattice-Boltzmann, D2Q9** (9 Geschwindigkeiten je Zelle), Verteilungsfunktionen `f[9*N]` als Struct-of-Arrays (`f[i*N + zelle]`), zwei Puffer (Ping-Pong), Streaming als "Pull".
- **Kollision:** BGK mit lokaler Relaxationszeit, Nicht-Gleichgewichtsanteil **regularisiert**: nur der physikalische Spannungstensor (pxx, pyy, pxy) wird zurückgeführt.
- **Turbulenz:** Smagorinsky-LES, Cs = 0,1. Lokale Relaxationszeit `tauE = 0.5·(tau + sqrt(tau² + 18·√2·Cs²·|Π|/ρ))` mit |Π| aus dem Nicht-Gleichgewichts-Spannungstensor.
- **Viskosität:** ν = U0·L/Re, tau = 3ν + 0,5. U0 wird so gewählt, dass tau höchstens ca. 1,5 bleibt und die Mach-Zahl klein ist.
- **Parallelisierung (CPU):** `Parallel.For` über die Gitterzeilen, Kernel mit `unsafe`-Zeigern. Rund 55 bis 70 MLUPS (Millionen Zellen-Updates pro Sekunde) auf 12 Kernen, 330 bis 650 MLUPS auf 20 Kernen (Core Ultra 7 265KF).
- **GPU (`GpuLbm.cs`):** OpenCL über P/Invoke direkt auf `OpenCL.dll`, kein SDK und keine Zusatzpakete. Die Kernel sind eine 1:1-Übersetzung
  von `StepRow`, Einlass/Auslass, `Reset`, `ApplyMask` und `Kick` (gleiche Formeln, gleiche Reihenfolge, `FP_CONTRACT OFF`, korrekt gerundete
  Division/Wurzel, Kräfte in `double`). Je Schritt laufen zwei Kernel ohne Warten hintereinander; die Kräfte jedes Schritts werden pro Paket
  (bis 512 Schritte) auf der GPU summiert und zusammen zurückgelesen, Dichte und Geschwindigkeit einmal pro Paket für Anzeige und Rauch.
  Die App wählt die Paketgröße so, dass ein Bild im Zeitbudget von 22 ms bleibt. `Solver.StepMany` ist die Schnittstelle dafür.

### Ränder
| Rand | Behandlung |
|---|---|
| Einlass (links) | Gleichgewicht mit Soll-Geschwindigkeit (mit Hochfahren), Dichte aus dem Nachbarn extrapoliert |
| Auslass (rechts) | fester Druck ρ = 1, Geschwindigkeit und Nicht-Gleichgewicht aus dem Nachbarn extrapoliert |
| Oben/unten | Standard: Spiegelung (reibungsfrei). Option: Bounce-Back (Haftung) |
| Körper | Halfway-Bounce-Back (treppenförmig!), Kräfte über **Impulsaustausch** an den Rand-Verbindungen |
| Beruhigungsstrecke | letzte 15 % des Gitters: Viskosität steigt auf tau = 1 (quadratischer Verlauf), Geschwindigkeit wird zur Anströmung relaxiert |

### Kräfte und Beiwerte
`cw = Fx / (½·U0²·L)`, `ca = Fy / (½·U0²·L)`, mit L = Bezugslänge in Zellen und ρ = 1. Der Druckbeiwert ist
`cp = (ρ − ρ∞) / 3 / (½·U0²)`, ρ∞ wird am Einlass gemittelt (`Solver.RhoInf`).

### Start und Anstoß
`Reset()` startet aus der Ruhe. Die Anströmung fährt über `RampSteps = 2·NX` Schritte hoch. Genau danach (`Kick()`) wird einmalig eine
kleine Querstörung (10 % von U0, gaußförmig) hinter dem Körper aufgebracht, weil die symmetrische Nachlaufströmung sonst sehr lange braucht, bis sie ablöst.

### Auswertung (`Visuals.cs`, Klasse `ForceStats`)
Ringpuffer für cw und ca. "Eingeschwungen" gilt nach 15 Umströmungszeiten (T = L/U0 Schritte). Mittelwerte über bis zu 40 T.
Strouhal-Zahl: ca wird über 0,5 T gleitend geglättet, Nulldurchgänge mit Hysterese (0,3 × Standardabweichung) gezählt, mindestens 3 nötig.

### Darstellung (`Visuals.cs`, Klasse `Renderer`)
Farbtabellen (LUT) für Geschwindigkeit, Druck, Wirbelstärke. Bilineare Abtastung des Gitters pro Bildschirmpixel, Körper mit weicher Kante.
Rauchpartikel werden als Punkte gezeichnet. **Alles läuft auf der CPU**, das ist der Hauptgrund für die ca. 27 FPS bei 600×240.

---

## 4. Validierung: was gemessen wurde

Testprogramm: `Quellcode/Test/ValidationTest.cs`. Bauen und starten:

`build.ps1` baut den Test mit (`Quellcode\bin\ValidationTest.exe`). Die erste Zeile der Ausgabe sagt, ob auf GPU oder CPU gerechnet wird.

```powershell
Quellcode\bin\ValidationTest.exe                          # komplette Reihe (GPU: ca. 1 Minute, CPU: einige Minuten)
Quellcode\bin\ValidationTest.exe 900 360 30 100 30000     # Einzelfall: sichtbares nx ny D Re Schritte
$env:WK_CPU = "1"; Quellcode\bin\ValidationTest.exe       # dieselbe Reihe auf der CPU (zum Vergleich)
```

### Zylinder (Literatur: Re = 100, Kármánsche Wirbelstraße, kaum Versperrung: St ≈ 0,164 bis 0,167, cw ≈ 1,33 bis 1,40)

| Fall | Versperrung | cw | St | Hinweis |
|---|---|---|---|---|
| Re = 100, 900×360, D = 30 | 8 % | 1,535 | 0,172 | Endstand |
| Re = 100, 1200×480, D = 20 | 4 % | 1,502 | 0,166 | Endstand |
| Re = 100, 900×360, D = 30 | 8 % | 1,519 | 0,177 | erster Stand (Auslass mit Null-Gradient) |
| Re = 20, 900×360, D = 30 | 8 % | 2,340 | – | **nur erster Stand**, nicht mit dem Endstand wiederholt (Literatur ca. 2,0 bis 2,1) |

**Fazit:** Wirbelfrequenz stimmt gut (St innerhalb 1 bis 3 %). Der Widerstand liegt ca. 8 bis 12 % zu hoch.
Mögliche Ursachen: Versperrung durch die Tunnelwände (wie im echten Windkanal, keine Blockagekorrektur eingebaut) und die treppenförmige Kontur.
Bei 4 % Versperrung ist cw näher an der Literatur als bei 8 %.

### Stabilitätstests (Endstand, nur Zahlen, kein Literaturvergleich)

| Fall | Ergebnis |
|---|---|
| Zylinder, Re = 20.000, 600×240, D = 24 | stabil, cw = 1,82, St = 0,149 |
| NACA 4412, Anstellwinkel 15°, Re = 20.000 | stabil, cw = 0,32, ca = 1,16 |
| Zylinder 40 % Versperrung, Re = 20.000 | stabil, cw = 2,75 (stark vom Kanal beeinflusst) |
| Quadrat 60 % Versperrung, Re = 20.000 | stabil, cw = 11,8 (Kanal fast zu, physikalisch nur grob) |

Wichtig: Bei Re = 20.000 sind 2D-Werte nur Richtwerte (echte Turbulenz ist 3D).

### Geschwindigkeit
Ca. 50 bis 70 MLUPS bei den Testgittern. Die Werte von 16 bis 18 MLUPS bei den 40- und 60-%-Versperrungsfällen wurden gemessen, während
andere Testprozesse parallel liefen, sie sind daher kein Maß für die reine Geschwindigkeit.

### GPU gegen CPU (gemessen am 06.10.2026, RTX 5070 Ti und Core Ultra 7 265KF mit 20 Kernen)

Komplette Reihe einmal mit dem alten Stand (nur CPU) und einmal mit dem neuen Stand auf der GPU. **Alle cw-, ca- und St-Werte
stimmen auf alle angezeigten Stellen überein** (z. B. Re = 100, 8 %: cw = 1,496, St = 0,174 in beiden). Die CPU-Werte schwanken
auf diesem PC von Lauf zu Lauf um etwa ±20 %.

| Fall | Gitter (mit Beruhigung) | CPU vorher | GPU nachher | Faktor |
|---|---|---|---|---|
| Zylinder 40 %, Re = 20.000 | 460×160 | 489 MLUPS | 3467 MLUPS | ca. 7× |
| Quadrat 60 %, Re = 20.000 | 460×160 | 503 MLUPS | 3572 MLUPS | ca. 7× |
| Zylinder, Re = 20.000 | 690×240 | 568 MLUPS | 6154 MLUPS | ca. 11× |
| NACA 4412, 15°, Re = 20.000 | 690×240 | 542 MLUPS | 6219 MLUPS | ca. 11× |
| Zylinder Re = 100, 8 % | 1035×360 | 649 MLUPS | 11394 MLUPS | ca. 17× |
| Zylinder Re = 100, 4 % | 1380×480 | 627 MLUPS | 8101 MLUPS | ca. 13× |
| Zylinder Re = 20, 8 % | 1035×360 | 652 MLUPS | 11332 MLUPS | ca. 17× |

Kleine Gitter nutzen die GPU nicht voll aus (die Zeit pro Schritt wird dort vom Starten der Kernel bestimmt), große umso besser.
In der App (600×240, Zylinder) zeigt die Anzeige ca. 6500 MLUPS und 46 FPS.

---

## 5. Bekannte Schwächen und nicht getestete Stellen

**Bekannte Schwächen**
- cw ca. 10 % zu hoch (treppenförmige Körperkontur, Versperrung). Siehe Roadmap Phase 3.
- Keine Blockagekorrektur: bei großen Objekten im schmalen Kanal stimmt cw nicht mit dem freien Feld überein.
- Re = 20 (stationäres Wirbelpaar) wurde mit dem Endstand des Lösers **nicht** erneut gemessen.
- Alles auf der CPU: Auflösung und Geschwindigkeit begrenzt, Darstellung belegt zusätzlich Rechenzeit.
- Rauch ist punktbasiert, wirkt wie Linien aus Punkten. Kein echtes Dichtefeld.
- Der Einschwingvorgang (erste ca. 15 Umströmungszeiten) verfälscht die Anzeige "jetzt", der Mittelwert wird erst danach gezeigt.
- Auto-Profil ist eine grobe Polygon-Näherung ohne Räder und Unterboden.
- Installer ist nicht signiert, Windows SmartScreen kann beim ersten Start warnen.
- Bei kleinen Fenstern (unter ca. 940 Pixel Höhe) muss die Seitenleiste gescrollt werden.

**Nicht getestet (nur gebaut, nie bewusst geprüft)**
- Ansichten "Druck" und "Wirbelstärke" wurden nicht systematisch angeschaut (nur Geschwindigkeit per Screenshot geprüft).
- Tragflächen und Auto wurden nur in Zahlen getestet (Stabilität), nicht visuell kontrolliert.
- Zeichnen mit der Maus wurde nicht interaktiv ausprobiert.
- Auflösungswechsel während des Betriebs und Übernahme einer gezeichneten Form auf ein neues Gitter.
- Verhalten auf anderen Bildschirmgrößen und mit Windows-Skalierung über 100 % (Entwicklungs-PC: 96 DPI).

---

## 6. Roadmap Version 2

Ziele: **(a)** Simulation auf der GPU, **(b)** echter Rauch wie in einem Rauchkanal, **(c)** realistischere Zahlen.
Reihenfolge ist Absicht: GPU zuerst, weil feiner Rauch und hohe Auflösung auf der CPU nicht flüssig laufen würden.

### Phase 0: Vorbereitung am neuen PC
- [x] Repo klonen, Version 1 bauen, Validierungsreihe laufen lassen, Zahlen notieren (Referenzwerte für Phase 1)
- [x] ~~.NET 8 SDK installieren, neues Projekt anlegen~~ nicht nötig: die GPU-Rechnung steckt im bestehenden Projekt, die CPU-Rechnung bleibt als Referenz und Ersatz erhalten
- [x] Entscheidung zur GPU-Technik: **OpenCL direkt über `OpenCL.dll`** (im Grafiktreiber enthalten, funktioniert mit dem vorhandenen `csc.exe` ohne SDK und ohne NuGet-Pakete)

### Phase 1: GPU-Port der Strömungsrechnung  (größter Gewinn)

**Technikwahl (Empfehlung, noch nicht ausprobiert):**

| Option | Vorteil | Nachteil |
|---|---|---|
| **Direct3D 11 Compute-Shader (HLSL) über Vortice.Windows** (Empfehlung) | läuft auf jeder Windows-GPU, Shader als eigene `.hlsl`-Dateien, Rechnung und Darstellung bleiben auf der GPU (kein Rücklesen) | mehr Handarbeit bei Puffern und Synchronisation |
| ComputeSharp (Shader in C# geschrieben, DX12) | wenig Code, sehr bequem | Abhängigkeit von der Bibliothek, Debuggen schwieriger |
| OpenCL (z. B. Silk.NET) | gut dokumentierte LBM-Beispiele | Treiberabhängig, Darstellung braucht Umweg |
| Vulkan / wgpu | modern, portabel | deutlich mehr Aufwand |

Oberfläche: Windows Forms bleibt möglich, die GPU-Darstellung kommt in ein Steuerelement mit eigener Swapchain.

**Umsetzung:**
- [x] Datenlayout wie in V1 (Struct-of-Arrays, `f[i*N + zelle]`, zwei Puffer). Je Zelle 9 Floats in 2 Puffern = 72 Byte.
- [x] Ein Kernel für **Streaming + Kollision** (1:1 aus `Solver.StepRow` übersetzt: regularisiert, Smagorinsky, Bounce-Back, Impulsaustausch)
- [x] Kleine Kernel für Einlass, Auslass, Beruhigungsstrecke (wie in `Solver.Step`)
- [x] **Kräfte:** Impulsaustausch je Randzelle in einen Puffer (ein Platz je Randzelle und Schritt), dann Reduktion in `double` je Schritt.
- [ ] **Darstellung direkt aus den GPU-Puffern**: Pixel-Shader oder Compute-Kernel färbt Geschwindigkeit/Druck/Wirbel mit denselben Farbtabellen wie V1.
- [x] Mehrere Rechenschritte pro Bild (automatisch nach Zeitbudget, wie bisher höchstens 500), damit die GPU ausgelastet ist. Rauchpartikel werden dafür in Teilschritten bewegt.
- [x] Körper-Maske und Anstellwinkel weiterhin auf der CPU erzeugen (Rasterung aus `Shapes.cs`), nur die Maske zur GPU kopieren.
- [x] **Regressionstest:** gleiche Fälle wie in Abschnitt 4 auf GPU und CPU rechnen, cw und St müssen innerhalb von 1 % übereinstimmen. Erst dann weiterbauen.
- [ ] Neue Auflösungsstufen: bis ca. 2400×960 (sichtbar), Auswahl wie bisher.

**Leistungs-Abschätzung (Rechnung, nicht gemessen):**
- GTX 1660 Ti: 288 GB/s Speicherbandbreite. Je Zellen-Update werden 9 Werte gelesen und 9 geschrieben = 72 Byte. Theoretisch ca. 4000 MLUPS, realistisch 50 bis 70 % davon, also ca. 2000 bis 2800 MLUPS. Version 1 schafft ca. 60 MLUPS. Das ist ca. 30- bis 40-mal schneller.
- Speicher bei 2400×960 mit Beruhigungsstrecke (ca. 2,65 Mio. Zellen): ca. 190 MB. Passt locker in 6 GB.
- **Wichtig zu verstehen:** Verdoppelt man die Auflösung je Achse, steigt der Rechenaufwand pro Umströmungszeit um das 8-Fache (4× Zellen, 2× Schritte). Deshalb bringt die GPU nicht nur "flüssiger", sondern vor allem "feiner":

| Gitter (sichtbar) | Zellen mit Beruhigungsstrecke | geschätzte Umströmungszeiten pro Sekunde auf GPU | V1 (CPU) |
|---|---|---|---|
| 600×240 | ca. 0,17 Mio. | ca. 30 (läuft im Zeitraffer) | ca. 1 |
| 1200×480 | ca. 0,66 Mio. | ca. 4 | nicht flüssig |
| 2400×960 | ca. 2,65 Mio. | ca. 0,5 (etwa wie V1 heute, aber 4× feiner je Achse) | – |

### Phase 2: Echter Rauch (Rauchkanal-Gefühl)

Idee: Ein echter Rauchkanal ist selbst ein fast zweidimensionales Experiment (Lichtschnitt, Rauchfäden). 2D passt dafür besonders gut.

- [ ] **Rauch als Dichtefeld** (eigene Textur auf der GPU, 2× bis 4× feiner als das Strömungsgitter), nicht mehr als Punkte
- [ ] **Advektion** mit dem Geschwindigkeitsfeld: Semi-Lagrange mit **MacCormack oder BFECC**-Korrektur (sonst verschmiert der Rauch zu schnell, das ist das Hauptproblem). Alternative: Hybrid mit Partikeln für die scharfen Fäden.
- [ ] Sehr kleine Diffusion einstellbar (Schieberegler "Rauchschärfe")
- [ ] **Rauchrechen** am Einlass: einstellbarer Düsenabstand, einstellbare Anzahl der Fäden
- [ ] **Rauchsonde mit der Maus:** Rauch strömt dort aus, wo man die Maus hält (wie das Rauchröhrchen im echten Windkanal)
- [ ] **Darstellung wie im Lichtschnitt:** schwarzer Hintergrund, halbtransparenter weißer Rauch, weiche Übergänge, leichtes Leuchten (Bloom), optional Rauchfarbe
- [ ] Rauch am Körper absorbieren, damit nichts durch die Wand "leckt"
- [ ] Ansicht "Nur Rauch" wird zur Hauptansicht, Geschwindigkeit/Druck/Wirbel bleiben als Wahl

### Phase 3: Mehr Realismus bei den Messwerten
- [ ] **Interpolierte Randbedingung nach Bouzidi** (statt treppenförmigem Bounce-Back). Für jede Rand-Verbindung wird der Abstand q (0 bis 1) zur echten Kontur gespeichert. Für Polygone analytisch aus Strahl-Kante-Schnitt, für gezeichnete Formen aus einem Distanzfeld. Erwartung: cw bei Re = 100 rückt näher an 1,35. Die Kraftberechnung über Impulsaustausch bleibt gültig.
- [ ] **Besseres Kollisionsmodell** (Kumulanten oder Central-Moment-MRT) für höhere Reynoldszahlen und weniger numerische Dämpfung. Smagorinsky bleibt als Vergleich wählbar.
- [ ] **Blockagekorrektur** für cw und ca (z. B. nach Maskell) oder Warnung bei Versperrung über 10 %
- [ ] **Offene Seitenränder** als Option (statt Spiegelwand), damit auch große Objekte realistischer sind
- [ ] Verfeinerte Gitter nahe am Körper (nur falls Phase 1 und 3 nicht reichen, aufwendig)
- [ ] **Erweiterte Validierung**, jeweils mit Literaturwert vorher nachschlagen und im README festhalten:
  - Zylinder Re = 20 und 40: cw und Länge der Rückströmzone
  - Zylinder Re = 100 mit 1 bis 2 % Versperrung: cw und St
  - Quadrat Re = 100: cw und St
  - NACA 0012 im Anstellwinkelbereich: Auftriebsanstieg (ca. 0,1 pro Grad bei anliegender Strömung als grobe Plausibilitätsprüfung), Abriss
  - Flache Platte quer angeströmt: cw bei hohem Re ca. 2
- [ ] Gitterkonvergenz-Test (gleicher Fall auf 3 Auflösungen) automatisiert

### Phase 4: Komfort und Funktionen
- [ ] Speichern und Laden (Form, Einstellungen, eigene Zeichnung), z. B. als JSON
- [ ] Export: Messwerte als CSV, Bild (PNG), Video oder GIF der Strömung
- [ ] **Polare:** automatischer Durchlauf über den Anstellwinkel, Diagramm ca(α), cw(α), ca/cw, Export
- [ ] Mehr Formen: mehrere Objekte, Tragfläche mit Klappe, Auto mit Rädern und Unterboden, Pfeil/Pfeilspitze, Rohrbündel, Windturbinenprofile, DXF/SVG/Bild-Import als Kontur
- [ ] Bewegte Wand/Boden (Bodeneffekt beim Auto: Boden läuft mit der Anströmung)
- [ ] Mehr Medien: Wasser, andere Temperatur, benutzerdefinierte Dichte und Zähigkeit
- [ ] Tooltips, Hilfetexte, Einheitenwahl (m/s, km/h), Dunkel/Hell
- [ ] Installer signieren oder als MSIX paketieren, Update-Prüfung
- [x] Oberfläche modernisiert: dunkle Titelleiste, Kopfzeile mit Start/Pause, Karten in der Seitenleiste, eigene Regler, Schalter und Auswahllisten, Messwert-Kacheln neben dem Diagramm
- [x] Helles und dunkles Design (Knopf mit Sonne/Mond oben rechts, Wahl wird gemerkt), Startfenster mit Ladeanimation
- [x] Neues Layout im Dashboard-Stil: Karten mit großen Rundungen, Ansicht als Pillen-Navigation oben, Messwert-Karten mit Verlauf als Kapseln, Schrift Outfit (eingebettet)

### Phase 5 (optional, groß): 3D
- D3Q19 oder D3Q27 auf der GPU, **STL-Import** mit Voxelisierung, Schnitt-Darstellung und Stromlinien. Gute Referenz zum Anschauen: das Open-Source-Projekt FluidX3D.
- Speicher: 19 Werte × 2 Puffer × 4 Byte = 152 Byte je Zelle, also ca. 2,4 GB für 16 Mio. Zellen (z. B. 400×200×200). Auf 6 GB Grafikspeicher begrenzt das auf ca. 20 bis 30 Mio. Zellen.
- Erst anfangen, wenn Phase 1 bis 3 sauber laufen.

---

## 7. Projektstruktur und Build

```
.gitignore
README.md
Quellcode/
  build.ps1              Baut App + Installer + ValidationTest.exe (ruft csc.exe von .NET Framework 4.8 auf, erzeugt auch das Icon)
  App/
    Program.cs           Einstieg
    MainForm.cs          Oberfläche, Steuerung, Zeichnen mit der Maus, Messwert-Kacheln, Diagramm
    Ui.cs                Design (hell/dunkel): Farben, Schrift, selbst gezeichnete Knöpfe, Regler, Schalter, Auswahllisten, Karten, Hilfefenster
    Splash.cs            Startfenster mit Ladeanimation (eigener Thread, meldet den Ladefortschritt)
    Solver.cs            LBM-Löser (D2Q9, regularisiert, Smagorinsky), Ränder, Kräfte, ChooseU0, Reset/Kick; nutzt die GPU, wenn vorhanden
    GpuLbm.cs            derselbe Löser als OpenCL-Kernel für die Grafikkarte (P/Invoke auf OpenCL.dll)
    Shapes.cs            Formen (Polygone, NACA-Profil, Auto), Drehen/Skalieren, Rasterung
    Visuals.cs           Rauchpartikel, ForceStats (Mittelwerte, Strouhal), Renderer (Farbtabellen, Bild)
  Fonts/                 Schrift Outfit (SIL Open Font License, siehe OFL.txt), wird beim Bauen in die .exe eingebettet
  Setup/Setup.cs         Installer und Deinstallierer in einer Datei
  Test/ValidationTest.cs Validierung gegen Literaturwerte, Stabilitätstests
```

**Build-Hinweise**
- `build.ps1` braucht **kein .NET SDK**. Es nutzt `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Dieser Compiler versteht nur ältere C#-Syntax (etwa C# 5), also zum Beispiel kein `?.` und keine String-Interpolation `$"..."`. Das gilt auch für `GpuLbm.cs`; der OpenCL-Kernel steht dort als Text und wird beim Start vom Grafiktreiber übersetzt.
- Die App ist 64-Bit (`/platform:x64`), `unsafe` ist aktiviert, `System.Windows.Forms` und `System.Drawing` werden gebraucht.
- Ergebnisse: `Quellcode/bin/Windkanal2D.exe`, `Quellcode/bin/Setup.exe`, im Projektordner `Windkanal2D-Setup.exe` und `Windkanal2D-Deinstallieren.exe`. Alle `.exe` sind per `.gitignore` ausgeschlossen.

---

## 8. Installer und Deinstallierer

Eine einzige `Setup.cs`, die App ist als eingebettete Ressource (`Payload.Windkanal2D.exe`) im Setup enthalten.

- **Ziel:** `%LOCALAPPDATA%\Programs\Windkanal2D\` (nur für den aktuellen Benutzer, keine Admin-Rechte)
- **Angelegt:** `Windkanal2D.exe`, `Deinstallieren.exe` (Kopie des Setups), Startmenü-Verknüpfung, optional Desktop-Verknüpfung, Registry-Eintrag `HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Windkanal2D` (erscheint in Windows unter "Apps")
- **Deinstallation:** wird erkannt, wenn die Datei "deinstall" im Namen hat oder mit `/uninstall` gestartet wird. Löscht Dateien, Verknüpfungen, Registry und den Ordner (der Ordner wird per verzögertem `cmd` entfernt, weil sich die laufende Datei nicht selbst löschen kann).
- **Stiller Modus:** `/silent` (für Tests): `Windkanal2D-Setup.exe /silent` installiert, `Deinstallieren.exe /uninstall /silent` entfernt.
- Läuft die App beim Installieren oder Deinstallieren, wird sie vorher beendet.

---

## 9. Starter-Prompt für Claude auf dem neuen PC

Diesen Text in eine neue Sitzung im geklonten Ordner einfügen:

> Lies README.md komplett, besonders Abschnitt 3 (Technik), 4 (Validierung) und 6 (Roadmap). Das Projekt ist ein 2D-Windkanal
> (Lattice-Boltzmann), die Strömungsrechnung läuft per OpenCL auf der GPU (GpuLbm.cs) mit CPU-Ersatz (Solver.cs). Bauen,
> Validierungstest auf GPU und CPU (WK_CPU=1) laufen lassen und die Zahlen mit Abschnitt 4 vergleichen. Danach mit dem Rest von
> Phase 1 weitermachen (Darstellung direkt auf der GPU, höhere Auflösungen). Wichtig: GPU und CPU müssen bei cw und St innerhalb
> von 1 % übereinstimmen, bevor wir Rauch und Bouzidi angehen. Antworte auf Deutsch.
