# Modelle für Windkanal 2D

Jedes Objekt im Windkanal ist eine Textdatei `*.modell` in diesem Ordner. Beim Bauen bettet `build.ps1` alle Dateien
in die `.exe` ein. Die Reihenfolge in der Auswahl steht in `katalog.txt` (Kategorien in eckigen Klammern).

**Eigene Modelle ohne neu zu bauen:** einen Ordner `Modelle` neben `Windkanal2D.exe` anlegen und dort `.modell`-Dateien
(und bei Bedarf eigene Profil-`.dat`-Dateien) ablegen. Sie erscheinen beim nächsten Start unter ihrer `kategorie`.

## Dateiformat

```
# Kommentar
name: Clark Y
kategorie: Flugzeug · Profile        (nur für Dateien, die nicht in katalog.txt stehen)
beschreibung: ein Satz für die Auswahl
quelle: woher die Koordinaten stammen
lizenz: unter welchen Bedingungen
bezug: Sehnenlänge                   (Name der Bezugslänge für Re, cw, ca)
bezug-ist-groesse: ja                (ja = Formgröße ist die Bezugslänge, nein = gemessene Stirnhöhe; Standard: ja, bei Bodenmodellen nein)
groesse: 25                          (Startgröße in Prozent der Kanalhöhe)
winkel: 5                            (Start-Anstellwinkel in Grad)
reynolds: 1000                       (Start-Reynoldszahl)
detail: 0.02                         (feinstes Detail, z. B. engster Spalt, als Anteil der Formgröße; die App zeigt es in Zellen)
boden: ja                            (steht am Kanalboden statt mittig)
bodenabstand: 0.05                   (Abstand zum Boden in Formgrößen; 0 = aufgesetzt; ohne Angabe 3 Zellen)
drehpunkt: 0.25 0                    (um diesen Punkt wird gedreht; er liegt in der Kanalmitte)
bewegung-name: DRS                   (Modelle mit beweglichen Teilen: Name für den Knopf im Strömungsbild)
bewegung-dauer: 0.4                  (Dauer der Bewegung in Sekunden am echten Fahrzeug)
bewegung-tempo: 300                  (Geschwindigkeit in km/h, für die die Dauer gilt)
bezug-meter: 0.5                     (echte Bezugslänge in Metern; Dauer in Rechenschritten = gleich viele Überströmungen)

teil: Hauptflügel                    (beliebig viele Teile; sie dürfen sich überdecken)
profil: clarky                       (Profil aus Profile/clarky.dat)   oder
naca: 2412 160                       (NACA-4-Ziffern-Formel, Punktzahl)   oder
kreis: x y r [n]  |  ellipse: x y rx ry [n]  |  rechteck: x0 y0 x1 y1  |  stab: x0 y0 x1 y1 dicke   oder
punkte:                              (danach Zeilen „x y“, „x y !“ = scharfe Ecke, „bogen cx cy r start ende [n]“)
glatt: 6                             (Punkte als glatte Kurve verbinden, Unterteilungen je Abschnitt)
sehne: 0.3   winkel: 25   lage: 0.69 -0.05   spiegeln: ja   dicke: 1
                                     (Profil skalieren, um die Vorderkante drehen, verschieben, umdrehen für Abtrieb)
bewegung: 0.89 0.35 26.5             (bewegliches Teil: dreht sich um diesen Punkt um so viele Grad, Sinn wie „winkel“)
```

Koordinaten: x nach rechts (Strömung kommt von links), y nach oben, Einheit = Formgröße.
Profil-Dateien in `Profile/` sind die **unveränderten Originale** der UIUC-Datenbank (Selig- oder Lednicer-Format);
der Loader normiert sie auf Sehne 1 mit der Vorderkante im Ursprung.

## Feine Teile auf dem Gitter

Teile aus `profil`, `naca`, `punkte` und `stab` werden mit `Shapes.FillFine` gerastert: zusätzlich zu den Zellen im Inneren
gehört jede Zelle dazu, deren Mitte dem Umriss am nächsten liegt. Dünne Hinterkanten, Flaps und Leitbleche bleiben so eine
lückenlose Wand von mindestens einer Zelle. Spalte bleiben offen, wenn sie gut eine Zelle breit sind. Die Zeile
„Feinstes Detail“ in der App zeigt, wie viele Zellen das feinste Detail bei der aktuellen Auflösung hat (orange unter 2 Zellen).
`kreis`, `ellipse` und `rechteck` werden wie bisher gerastert, damit die Validierungsfälle unverändert bleiben.

`Quellcode/bin/ModellVorschau.exe [Bild.png] [Auflösung 0..3] [Modell]` prüft alle Dateien und zeichnet, wie jedes Modell
auf dem Gitter liegt.

## Neu erzeugen

Die `.modell`-Dateien werden von `Werkzeug/modelle_erzeugen.py` geschrieben (Python 3, nur Standardbibliothek). Das Skript
platziert mehrteilige Flügel so, dass die Spalte genau die angegebene Breite haben. Danach `Werkzeug/liesmich_erzeugen.py`
für diese Datei. Für die App ist Python nicht nötig.

## Alle Modelle, Quellen und Lizenzen


### Grundformen

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Zylinder | `zylinder.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Quadrat | `quadrat.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Flache Platte | `platte.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Ellipse 2 : 1 | `ellipse.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Halbzylinder (D-Profil) | `halbzylinder.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Dreieckskeil | `keil.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Zwei Zylinder hintereinander | `tandem.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Auto (einfaches Seitenprofil) | `auto.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |

### Flugzeug · Profile

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| NACA 0006 (dünn, symmetrisch) | `naca0006.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 0012 (symmetrisch) | `naca0012.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 0018 (dick, symmetrisch) | `naca0018.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 2412 (Cessna 172) | `naca2412.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 4412 (stark gewölbt) | `naca4412.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 4415 | `naca4415.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 6409 (sehr stark gewölbt) | `naca6409.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |
| NACA 23012 (Fünfziffern-Reihe) | `naca23012.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei naca23012.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NACA 63-215 (Laminarprofil) | `n63215.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei n63215.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NACA 65(2)-415 (Laminarprofil) | `naca652415.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei naca652415.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NACA 64A010 (symmetrisch, laminar) | `naca64a010.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei naca64a010.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Clark Y | `clarky.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei clarky.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Göttingen 387 | `goe387.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei goe387.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NASA LS(1)-0417 / GA(W)-1 | `ls417.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei ls417.dat, Original unverändert in Modelle/Profile | US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank |
| Selig S1223 (Hochauftrieb) | `s1223.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei s1223.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Selig S1210 | `s1210.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei s1210.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Selig-Donovan SD7037 | `sd7037.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei sd7037.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Eppler E387 | `e387.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei e387.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Eppler E423 (Hochauftrieb) | `e423.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei e423.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Eppler E205 | `e205.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei e205.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Hepperle MH 32 | `mh32.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei mh32.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Drela AG35 | `ag35.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei ag35.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Wortmann FX 63-137 | `fx63137.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei fx63137.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Wortmann FX 60-126 | `fx60126.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei fx60126.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Delft DU 86-084/18 | `du8608418.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei du8608418.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |

### Flugzeug · Superkritisch

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| NASA SC(2)-0410 | `sc20410.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei sc20410.dat, Original unverändert in Modelle/Profile | US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank |
| NASA SC(2)-0612 | `sc20612.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei sc20612.dat, Original unverändert in Modelle/Profile | US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank |
| NASA SC(2)-0714 | `sc20714.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei sc20714.dat, Original unverändert in Modelle/Profile | US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank |
| Whitcomb (integral superkritisch) | `whitcomb.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei whitcomb.dat, Original unverändert in Modelle/Profile | US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank |
| RAE 2822 | `rae2822.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei rae2822.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NLR 7301 | `nlr7301.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei nlr7301.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |

### Flugzeug · Klappen & Leitwerk

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Flügel mit Spaltklappe | `spaltklappe.modell` | Eigene Konstruktion für Windkanal 2D (Profile nach NACA-Formeln) | Teil dieses Projekts |
| Dreiteiliger Landeflügel (Vorflügel, Klappe) | `landekonfiguration.modell` | Hauptflügel: NASA SC(2)-0612 (UIUC-Datenbank); Vorflügel NACA 6412 und Klappe NACA 4412 (Formeln). Winkel und Spalte nach dem Messfall 30P30N (AIAA High Lift Prediction Workshop), Elemente vereinfacht | US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank, Anordnung Eigene Konstruktion für Windkanal 2D |
| NLR 7301 mit Klappe (Zweielement) | `nlr7301-klappe.modell` | Hauptflügel NLR 7301 (UIUC-Datenbank, nlr7301.dat); Klappe vereinfacht als NACA 4415; Spalt und Winkel nach der NLR-7301-Messung (AGARD) | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Höhenleitwerk mit Ruder (15°) | `hoehenleitwerk.modell` | Eigene Konstruktion für Windkanal 2D (NACA 0012 nach Formel, am Scharnier geteilt) | Teil dieses Projekts |
| NACA 4412 mit Gurney-Klappe | `gurney.modell` | NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet) | gemeinfrei (NACA, US-Regierungswerk) |

### Rotor, Propeller & Windkraft

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Boeing-Vertol VR-12 (Hubschrauber) | `vr12.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei vr12.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NACA 64(3)-618 (NREL 5 MW) | `naca643618.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei naca643618.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| NACA 63(3)-618 (Windkraft) | `naca633618.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei naca633618.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Propellerblatt-Schnitt (Clark Y, 70 % Radius) | `propeller.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei clarky.dat, Original unverändert in Modelle/Profile | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Verdichtergitter (3 Schaufeln) | `verdichtergitter.modell` | UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei naca652415.dat, Original unverändert in Modelle/Profile; Anordnung Eigene Konstruktion für Windkanal 2D | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Turbinenschaufel-Gitter (3 Schaufeln) | `turbinengitter.modell` | Eigene Konstruktion für Windkanal 2D (Kreisbogen-Skelettlinie mit NACA-Dickenverteilung), kein bestimmtes Triebwerk | Teil dieses Projekts |
| Vertikalachsen-Windrad (H-Rotor) | `darrieus.modell` | Eigene Konstruktion für Windkanal 2D (NACA 0018 nach Formel) | Teil dieses Projekts |

### Formel 1

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Frontflügel (4 Elemente) | `f1-frontfluegel.modell` | Elemente: Selig S1223 (UIUC-Datenbank, s1223.dat), umgedreht; Anordnung Eigene Konstruktion für Windkanal 2D nach typischen Maßen (Reglement 2022–2025) | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Heckflügel, DRS geschlossen | `f1-heckfluegel-zu.modell` | Selig S1223 umgedreht (UIUC-Datenbank); Anordnung Eigene Konstruktion für Windkanal 2D nach typischen Maßen | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Heckflügel, DRS offen | `f1-heckfluegel-offen.modell` | Selig S1223 umgedreht (UIUC-Datenbank); Schlitz 85 mm nach FIA-Reglement 2022–2025; Anordnung Eigene Konstruktion für Windkanal 2D | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Heckflügel mit DRS (animiert, 2025) | `f1-heckfluegel-drs.modell` | Selig S1223 umgedreht (UIUC-Datenbank); DRS-Schlitz offen 85 mm, Wechsel in weniger als 400 ms nach FIA-Reglement 2025 (Art. 3.10.10); Flap dreht um ihre Hinterkante; Anordnung Eigene Konstruktion für Windkanal 2D | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Heckflügel 2026 mit „Macarena“-Flap (animiert) | `f1-heckfluegel-macarena.modell` | Prinzip nach Berichten zum Ferrari SF-26 (Testfahrten Bahrain 2026); aktive Aerodynamik, Wechsel höchstens 400 ms nach FIA-Reglement 2026 (Art. 3.11.6); Drehpunkt in der Sehnenmitte und Flügelform angenommen (nicht veröffentlicht); Selig S1223 umgedreht (UIUC-Datenbank) | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Beam Wing (2 Elemente) | `f1-beamwing.modell` | Eppler E423 umgedreht (UIUC-Datenbank, e423.dat); Anordnung Eigene Konstruktion für Windkanal 2D | Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen |
| Unterboden mit Venturi-Kanal und Diffusor | `f1-unterboden.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Maßen (Kanal-Einlauf etwa 250 mm, Kehle etwa 35 mm über der Bahn, Diffusor 1 m lang) | Teil dieses Projekts |
| Formel-1-Wagen komplett (DRS zu) | `f1-komplett.modell` | Eigene Konstruktion nach den öffentlichen Maßen des FIA-Reglements 2022–2025 (Radstand 3600 mm, Raddurchmesser 720 mm, Höhe bis 950 mm, DRS-Schlitz 85 mm); Flügelelemente Selig S1223 und Eppler E423 (UIUC-Datenbank). Kein bestimmtes Team-Fahrzeug | Teil dieses Projekts |
| Formel-1-Wagen komplett (DRS offen) | `f1-komplett-drs.modell` | Eigene Konstruktion nach den öffentlichen Maßen des FIA-Reglements 2022–2025 (Radstand 3600 mm, Raddurchmesser 720 mm, Höhe bis 950 mm, DRS-Schlitz 85 mm); Flügelelemente Selig S1223 und Eppler E423 (UIUC-Datenbank). Kein bestimmtes Team-Fahrzeug | Teil dieses Projekts |
| Formel-1-Wagen mit DRS (animiert) | `f1-komplett-drs-animiert.modell` | Eigene Konstruktion nach den öffentlichen Maßen des FIA-Reglements 2022–2025 (Radstand 3600 mm, Raddurchmesser 720 mm, Höhe bis 950 mm, DRS-Schlitz 85 mm); Flügelelemente Selig S1223 und Eppler E423 (UIUC-Datenbank). Kein bestimmtes Team-Fahrzeug; DRS-Wechsel in weniger als 400 ms (Reglement 2025, Art. 3.10.10) | Teil dieses Projekts |

### Straßenfahrzeuge

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Limousine (Stufenheck) | `limousine.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |
| Fließheck-Limousine | `fliessheck.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |
| Kombi | `kombi.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |
| Sportwagen-Coupé | `sportwagen.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |
| Transporter | `transporter.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |
| Lkw-Sattelzug | `sattelzug.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell (Gesamtlänge 16,5 m, Höhe 4 m) | Teil dieses Projekts |
| Lkw-Sattelzug aerodynamisch | `sattelzug-aero.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell (Anbauteile nach üblichen Nachrüstsätzen) | Teil dieses Projekts |
| Reisebus | `reisebus.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |
| Hochgeschwindigkeitszug | `zug.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell | Teil dieses Projekts |

### Benchmarks, Sport & Segel

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Ahmed-Körper 25° | `ahmed25.modell` | Ahmed, Ramm, Faltin: „Some salient features of the time-averaged ground vehicle wake“, SAE 840300 (1984): Länge 1044 mm, Höhe 288 mm, Radius vorn 100 mm, Schräge 222 mm, Bodenabstand 50 mm (Stelzen weggelassen) | Maße aus der Veröffentlichung, Koordinaten selbst berechnet |
| Ahmed-Körper 35° | `ahmed35.modell` | Ahmed, Ramm, Faltin: „Some salient features of the time-averaged ground vehicle wake“, SAE 840300 (1984): Länge 1044 mm, Höhe 288 mm, Radius vorn 100 mm, Schräge 222 mm, Bodenabstand 50 mm (Stelzen weggelassen) | Maße aus der Veröffentlichung, Koordinaten selbst berechnet |
| Ahmed-Körper 0° (Kastenheck) | `ahmed0.modell` | Ahmed, Ramm, Faltin: „Some salient features of the time-averaged ground vehicle wake“, SAE 840300 (1984): Länge 1044 mm, Höhe 288 mm, Radius vorn 100 mm, Schräge 222 mm, Bodenabstand 50 mm (Stelzen weggelassen) | Maße aus der Veröffentlichung, Koordinaten selbst berechnet |
| Radfahrer in Zeitfahrhaltung | `radfahrer.modell` | Eigene Konstruktion für Windkanal 2D nach typischen Körpermaßen (Fahrer etwa 1,80 m, Laufrad 700C) | Teil dieses Projekts |
| Mast mit Großsegel | `segel.modell` | Eigene Konstruktion für Windkanal 2D (Segel als dünnes gewölbtes NACA-8402-Profil) | Teil dieses Projekts |
| Fock und Großsegel | `fock-gross.modell` | Eigene Konstruktion für Windkanal 2D (Segel als NACA-8402-Profile) | Teil dieses Projekts |

### Bauwerke & Brücken

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Hochhaus | `hochhaus.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Haus mit Satteldach | `haus.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Straßenschlucht | `strassenschlucht.modell` | Eigene Konstruktion für Windkanal 2D | Teil dieses Projekts |
| Brückenquerschnitt Tacoma Narrows (1940) | `tacoma.modell` | Maße aus öffentlichen Berichten zum Einsturz 1940 (Breite 11,9 m, Trägerhöhe 2,44 m); Blechdicken auf 0,15 m vergrößert, damit sie aufs Gitter passen | Maße öffentlich, Koordinaten selbst berechnet |
| Brückenquerschnitt Hohlkasten (Großer Belt) | `kastentraeger.modell` | Maße nach öffentlichen Angaben zur Ostbrücke über den Großen Belt (Storebælt, 1998), vereinfacht ohne Geländer | Maße öffentlich, Koordinaten selbst berechnet |

### Eigene

| Modell | Datei | Quelle | Lizenz |
|---|---|---|---|
| Eigene Zeichnung | – | mit der Maus | – |

**Hinweis zur UIUC-Datenbank:** Die Koordinaten stammen von https://m-selig.ae.illinois.edu/ads/coord_database.html
(Prof. Michael Selig, University of Illinois). Die Seite nennt keine ausdrückliche Lizenz; die Profile selbst sind über Jahrzehnte
veröffentlicht (NACA- und NASA-Profile sind als US-Regierungswerke gemeinfrei). Die Original-Dateien liegen unverändert in `Profile/`.
