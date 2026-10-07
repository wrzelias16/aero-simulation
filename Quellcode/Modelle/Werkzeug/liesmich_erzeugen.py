# Schreibt Quellcode/Modelle/LIESMICH.md: Dateiformat und eine Tabelle aller Modelle mit Quelle und Lizenz.
# Aufruf nach modelle_erzeugen.py: python Quellcode/Modelle/Werkzeug/liesmich_erzeugen.py
import os
BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')

HEAD = '''# Modelle für Windkanal 2D

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

'''

def read(path):
    d = {}
    for line in open(path, encoding='utf-8'):
        line = line.strip()
        if line.startswith('teil:'): break
        if ':' in line and not line.startswith('#'):
            k, v = line.split(':', 1); d[k.strip()] = v.strip()
    return d

out = [HEAD]
cat = None
for line in open(os.path.join(BASE, 'katalog.txt'), encoding='utf-8'):
    line = line.split('#')[0].strip()
    if not line: continue
    if line.startswith('['):
        cat = line[1:-1]
        out.append('\n### %s\n\n| Modell | Datei | Quelle | Lizenz |\n|---|---|---|---|\n' % cat)
        continue
    if line == 'eigene':
        out.append('| Eigene Zeichnung | – | mit der Maus | – |\n'); continue
    d = read(os.path.join(BASE, line + '.modell'))
    out.append('| %s | `%s.modell` | %s | %s |\n' % (d.get('name', line), line, d.get('quelle', ''), d.get('lizenz', '')))

out.append('''
**Hinweis zur UIUC-Datenbank:** Die Koordinaten stammen von https://m-selig.ae.illinois.edu/ads/coord_database.html
(Prof. Michael Selig, University of Illinois). Die Seite nennt keine ausdrückliche Lizenz; die Profile selbst sind über Jahrzehnte
veröffentlicht (NACA- und NASA-Profile sind als US-Regierungswerke gemeinfrei). Die Original-Dateien liegen unverändert in `Profile/`.
''')
open(os.path.join(BASE, 'LIESMICH.md'), 'w', encoding='utf-8', newline='\n').write(''.join(out))
print('LIESMICH.md geschrieben')
