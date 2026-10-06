# Windkanal 2D

Virtueller 2D-Windkanal für Windows: Strömung um Zylinder, Tragflächen, ein Auto-Profil oder eigene
Formen, mit Rauchlinien, Druck- und Wirbelansicht sowie Messwerten für Widerstand und Auftrieb.

## Funktionen

- **Objekte:** Zylinder, Quadrat, Platte, NACA-Tragflächen (0012, 2412, 4412), Auto-Seitenprofil,
  eigene Formen mit der Maus zeichnen (links = Wand, rechts = radieren)
- **Einstellbar:** Anstellwinkel, Größe, Reynoldszahl (10 bis 20.000), Auflösung, Tunnelwände mit/ohne Reibung
- **Ansichten:** Geschwindigkeit, Druck (cp), Wirbelstärke, nur Rauch
- **Messwerte:** cw, ca, Gleitzahl, Strouhal-Zahl, Versperrung, Verlauf über der Zeit
- **Umrechnung auf Luft (20 °C):** echte Objektgröße eingeben, Anströmgeschwindigkeit und Kräfte in N/m

## Physik

- Lattice-Boltzmann-Methode (D2Q9) mit regularisierter Kollision
- Smagorinsky-Turbulenzmodell (LES)
- Körperoberfläche: Bounce-Back, Kräfte über Impulsaustausch
- Einlass mit fester Geschwindigkeit (sanftes Hochfahren), Auslass mit festem Druck und Beruhigungsstrecke

### Validierung (Zylinder, Re = 100)

| Größe | Literatur | Diese App |
|---|---|---|
| Strouhal-Zahl | 0,164 – 0,167 | 0,164 – 0,172 |
| Widerstandsbeiwert cw | 1,33 – 1,40 | 1,50 – 1,54 |

Die Wirbelfrequenz stimmt sehr gut. Der Widerstand liegt ca. 10 % zu hoch (Versperrung durch die
Tunnelwände und treppenförmige Körperkontur im Gitter).

### Grenzen

- Nur 2D: das Objekt ist ein unendlich langer Profilschnitt.
- Ab Re ≈ 1.000 ist echte Turbulenz dreidimensional, die Werte sind dann Richtwerte
  (gut für Vergleiche "Form A gegen Form B").

## Bauen

Voraussetzung: Windows mit dem mitgelieferten .NET Framework 4.8 (kein SDK nötig).

```powershell
powershell -ExecutionPolicy Bypass -File Quellcode\build.ps1
```

Das erzeugt `Windkanal2D-Setup.exe` (Installer, per Benutzer, ohne Admin-Rechte) und
`Windkanal2D-Deinstallieren.exe` im Projektordner sowie die App in `Quellcode\bin\`.

## Struktur

```
Quellcode/
  App/Solver.cs       Strömungslöser (LBM)
  App/Shapes.cs       Formen und Rasterung
  App/Visuals.cs      Rauch, Kraftstatistik, Darstellung
  App/MainForm.cs     Oberfläche
  Setup/Setup.cs      Installer und Deinstallierer
  Test/               Validierungstest gegen Literaturwerte
  build.ps1           Build-Skript
```

## Ideen für Version 2

- Simulation auf der Grafikkarte (GPU) für ein Vielfaches an Geschwindigkeit und Auflösung
- Echter Rauch als Dichtefeld mit Rauchrechen, Rauchsonde und Lichtschnitt-Darstellung
- Gekrümmte Oberflächen genau abbilden (interpolierte Randbedingung)
