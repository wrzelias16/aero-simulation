# Erzeugt die .modell-Dateien und katalog.txt in Quellcode/Modelle. Nur nötig, wenn Modelle neu berechnet werden sollen;
# die App liest nur die erzeugten Dateien. Aufruf: python Quellcode/Modelle/Werkzeug/modelle_erzeugen.py
import math, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from geo import *

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
catalog = []   # (Kategorie, id)

UIUC = 'UIUC Airfoil Coordinates Database (Prof. M. Selig, University of Illinois), Datei {}.dat, Original unverändert in Modelle/Profile'
UIUC_LIC = 'Koordinaten frei abrufbar, ohne ausdrückliche Lizenz; Profilkoordinaten sind veröffentlichte Messgrößen'
NASA_LIC = 'US-Regierungswerk (NASA), gemeinfrei; Datei aus der UIUC-Datenbank'
NACA_SRC = 'NACA-Formeln aus Abbott/von Doenhoff, „Theory of Wing Sections“ bzw. NACA Report 824 (im Programm berechnet)'
NACA_LIC = 'gemeinfrei (NACA, US-Regierungswerk)'
OWN = 'Eigene Konstruktion für Windkanal 2D'
OWN_LIC = 'Teil dieses Projekts'

def write(cat, id, name, desc, src, lic, bezug, groesse, parts, winkel=0, re=1000, detail=None,
          boden=False, bodenabstand=None, drehpunkt=None, note=None, extra=None):
    L = ['# ' + name, '# erzeugt mit Werkzeug/modelle_erzeugen.py']
    if note: L += ['# ' + n for n in note.split('\n')]
    L += ['name: ' + name, 'beschreibung: ' + desc, 'quelle: ' + src, 'lizenz: ' + lic,
          'bezug: ' + bezug, 'groesse: %d' % groesse, 'winkel: %d' % winkel, 'reynolds: %d' % re]
    if detail is not None: L.append('detail: ' + f(detail))
    if boden: L.append('boden: ja')
    if boden and bezug in ('Flügeltiefe', 'Sehnenlänge'): L.append('bezug-ist-groesse: ja')
    if bodenabstand is not None: L.append('bodenabstand: ' + f(bodenabstand))
    if drehpunkt: L.append('drehpunkt: %s %s' % (f(drehpunkt[0]), f(drehpunkt[1])))
    if extra: L += extra
    for pname, lines in parts:
        L.append('')
        L.append('teil: ' + pname)
        L += lines
    with open(os.path.join(OUT, id + '.modell'), 'w', encoding='utf-8', newline='\n') as fh:
        fh.write('\n'.join(L) + '\n')
    catalog.append((cat, id))

def prof_lines(src_key, chord=1, angle=0, x=0, y=0, mirror=False, thick=None):
    L = [src_key]
    if chord != 1: L.append('sehne: ' + f(chord))
    if thick: L.append('dicke: ' + f(thick))
    if angle: L.append('winkel: ' + f(angle))
    if x or y: L.append('lage: %s %s' % (f(x), f(y)))
    if mirror: L.append('spiegeln: ja')
    return L

def pts_lines(pts, corners=(), smooth=0):
    L = []
    if smooth: L.append('glatt: %d' % smooth)
    L.append('punkte:')
    for i, (x, y) in enumerate(pts):
        L.append('  %s %s%s' % (f(x), f(y), ' !' if i in corners else ''))
    return L

def rect_pts(x0, y0, x1, y1):
    return pts_lines([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], corners=(0, 1, 2, 3))

def te_thick(poly):
    return thickness_at(poly, 0.95)

# ====================================================================== Grundformen
G = 'Grundformen'
write(G, 'zylinder', 'Zylinder', 'Kreiszylinder: der Klassiker der Strömungslehre mit Kármánscher Wirbelstraße ab Re ≈ 47.',
      OWN, OWN_LIC, 'Durchmesser', 10, [('Zylinder', ['kreis: 0 0 0.5 160'])], re=100)
write(G, 'quadrat', 'Quadrat', 'Scharfkantiger Vierkant: die Ablösung sitzt fest an den Vorderkanten.',
      OWN, OWN_LIC, 'Kantenlänge', 10, [('Quadrat', ['rechteck: -0.5 -0.5 0.5 0.5'])], re=100)
write(G, 'platte', 'Flache Platte', 'Dünne Platte (5 % dick); quer gestellt hoher Widerstand, längs fast keiner.',
      OWN, OWN_LIC, 'Sehnenlänge', 20, [('Platte', ['rechteck: -0.5 -0.025 0.5 0.025'])])
write(G, 'ellipse', 'Ellipse 2 : 1', 'Elliptischer Zylinder, halb so dick wie lang: Übergang zwischen Zylinder und Profil.',
      OWN, OWN_LIC, 'Länge', 18, [('Ellipse', ['ellipse: 0 0 0.5 0.25 160'])], re=500)
write(G, 'halbzylinder', 'Halbzylinder (D-Profil)', 'Rund nach vorn, flacher Rücken: feste Ablösung an den Kanten, breites Totwasser.',
      OWN, OWN_LIC, 'Durchmesser', 12, [('Halbzylinder', ['punkte:', '  bogen 0 0 0.5 90 270 40'])], re=200)
write(G, 'keil', 'Dreieckskeil', 'Gleichseitiges Dreieck mit der Spitze gegen die Strömung (Flammenhalter in Brennkammern).',
      OWN, OWN_LIC, 'Kantenlänge', 12,
      [('Keil', pts_lines([(-0.433, 0), (0.433, -0.5), (0.433, 0.5)]))], re=200)
write(G, 'tandem', 'Zwei Zylinder hintereinander', 'Tandem im Abstand von 3 Durchmessern: der hintere sitzt im Nachlauf des vorderen.',
      OWN, OWN_LIC, 'Durchmesser', 8, [('vorn', ['kreis: -1.5 0 0.5 120']), ('hinten', ['kreis: 1.5 0 0.5 120'])], re=150)
write(G, 'auto', 'Auto (einfaches Seitenprofil)', 'Grobe Limousine aus 13 Punkten, ohne Räder.',
      OWN, OWN_LIC, 'Stirnhöhe', 45,
      [('Karosserie', pts_lines([(0.00, 0.08), (0.00, 0.14), (0.02, 0.17), (0.08, 0.19), (0.30, 0.21), (0.45, 0.31), (0.70, 0.31),
                                 (0.88, 0.24), (0.98, 0.22), (1.00, 0.18), (1.00, 0.08), (0.95, 0.04), (0.05, 0.04)]))],
      re=5000, boden=True)

# ====================================================================== Profile
P = 'Flugzeug · Profile'
SK = 'Flugzeug · Superkritisch'
def nacaprof(cat, code, title, desc, size=25):
    poly = naca(code)
    write(cat, 'naca' + code, title, desc, NACA_SRC, NACA_LIC, 'Sehnenlänge', size,
          [('Profil', ['naca: %s 160' % code])], winkel=5, drehpunkt=(0.25, 0))

def uiuc(cat, key, title, desc, lic=UIUC_LIC, size=25, angle=5, srcnote=''):
    poly = load_profile(key)
    write(cat, key, title, desc, UIUC.format(key) + srcnote, lic, 'Sehnenlänge', size,
          [('Profil', ['profil: ' + key])], winkel=angle, drehpunkt=(0.25, 0))

nacaprof(P, '0006', 'NACA 0006 (dünn, symmetrisch)', 'Symmetrisch, 6 % dick: Leitwerke und Flügel schneller Flugzeuge.')
nacaprof(P, '0012', 'NACA 0012 (symmetrisch)', 'Symmetrisch, 12 % dick: das meistvermessene Profil überhaupt, Leitwerke und Rotorblätter.')
nacaprof(P, '0018', 'NACA 0018 (dick, symmetrisch)', 'Symmetrisch, 18 % dick: dicke Leitwerke und Vertikalachsen-Windräder.')
nacaprof(P, '2412', 'NACA 2412 (Cessna 172)', 'Leicht gewölbt, 12 % dick: Tragfläche der Cessna 172 und vieler Sportflugzeuge.')
nacaprof(P, '4412', 'NACA 4412 (stark gewölbt)', 'Stark gewölbt, 12 % dick: klassisches Messprofil mit hohem Auftrieb.')
nacaprof(P, '4415', 'NACA 4415', 'Gewölbt, 15 % dick: Langsamflug, Ultraleicht und kleine Windräder.')
nacaprof(P, '6409', 'NACA 6409 (sehr stark gewölbt)', 'Sehr stark gewölbt und dünn: Modellflug und Segel-ähnliche Profile.')
uiuc(P, 'naca23012', 'NACA 23012 (Fünfziffern-Reihe)', 'Wölbung weit vorn, geringes Moment: viele Propellerflugzeuge der 1930er bis 1950er.')
uiuc(P, 'n63215', 'NACA 63-215 (Laminarprofil)', 'Sechser-Reihe: lange laminare Grenzschicht bei kleinem Anstellwinkel.')
uiuc(P, 'naca652415', 'NACA 65(2)-415 (Laminarprofil)', 'Laminarprofil der Sechser-Reihe, auch in Verdichtergittern verbreitet.')
uiuc(P, 'naca64a010', 'NACA 64A010 (symmetrisch, laminar)', 'Dünnes symmetrisches Laminarprofil für Leitwerke schneller Flugzeuge.')
uiuc(P, 'clarky', 'Clark Y', 'Flache Unterseite, 11,7 % dick (1922): Propeller, Modellflug, Spirit of St. Louis.')
uiuc(P, 'goe387', 'Göttingen 387', 'Dickes, stark gewölbtes Profil der Göttinger Versuchsanstalt aus der Frühzeit der Luftfahrt.')
uiuc(P, 'ls417', 'NASA LS(1)-0417 / GA(W)-1', 'NASA-Profil für die allgemeine Luftfahrt (1970er), 17 % dick, viel Auftrieb.', lic=NASA_LIC)
uiuc(P, 's1223', 'Selig S1223 (Hochauftrieb)', 'Extrem gewölbt, entworfen für sehr hohen Auftrieb bei kleinen Reynoldszahlen.')
uiuc(P, 's1210', 'Selig S1210', 'Hochauftriebsprofil, etwas dünner als S1223.')
uiuc(P, 'sd7037', 'Selig-Donovan SD7037', 'Bewährtes Segelflugmodell-Profil, 9,2 % dick.')
uiuc(P, 'e387', 'Eppler E387', 'Referenzprofil für kleine Reynoldszahlen, ausführlich bei der NASA vermessen.')
uiuc(P, 'e423', 'Eppler E423 (Hochauftrieb)', 'Stark gewölbtes Hochauftriebsprofil von Richard Eppler.')
uiuc(P, 'e205', 'Eppler E205', 'Modellflugprofil, 10,5 % dick.')
uiuc(P, 'mh32', 'Hepperle MH 32', 'Dünnes Profil (8,7 %) für schnelle Segelflugmodelle (F3B) von Martin Hepperle.')
uiuc(P, 'ag35', 'Drela AG35', 'Profil von Mark Drela (MIT) für leichte Handstart-Segler.')
uiuc(P, 'fx63137', 'Wortmann FX 63-137', 'Langsamflug-Profil von F. X. Wortmann: Muskelkraft- und Solarflugzeuge.')
uiuc(P, 'fx60126', 'Wortmann FX 60-126', 'Klassisches Segelflugzeug-Profil der Stuttgarter Schule.')
uiuc(P, 'du8608418', 'Delft DU 86-084/18', 'Segelflugzeug-Profil der TU Delft mit langer Laminarlaufstrecke.')
uiuc(SK, 'sc20410', 'NASA SC(2)-0410', 'Superkritisch, 10 % dick, für Reisefluggeschwindigkeit knapp unter Mach 1.', lic=NASA_LIC)
uiuc(SK, 'sc20612', 'NASA SC(2)-0612', 'Superkritisch, 12 % dick: flache Oberseite, starke Hinterkanten-Wölbung.', lic=NASA_LIC)
uiuc(SK, 'sc20714', 'NASA SC(2)-0714', 'Superkritisch, 14 % dick, für hohen Auftrieb im Reiseflug.', lic=NASA_LIC)
uiuc(SK, 'whitcomb', 'Whitcomb (integral superkritisch)', 'Das ursprüngliche superkritische Profil von Richard Whitcomb (NASA).', lic=NASA_LIC)
uiuc(SK, 'rae2822', 'RAE 2822', 'Transsonisches Referenzprofil des Royal Aircraft Establishment, Standardfall der CFD.')
uiuc(SK, 'nlr7301', 'NLR 7301', 'Superkritisches Profil des niederländischen NLR, bekannt als Zweielement-Messfall.')

# ====================================================================== Hochauftrieb und Leitwerk
H = 'Flugzeug · Klappen & Leitwerk'

# Spaltklappe wie bisher (NACA 2413 + NACA 4415 bei 25°)
write(H, 'spaltklappe', 'Flügel mit Spaltklappe', 'NACA-2413-Flügel mit 30-%-Spaltklappe (NACA 4415), 25° ausgefahren: Landeanflug.',
      OWN + ' (Profile nach NACA-Formeln)', OWN_LIC, 'Sehnenlänge', 40,
      [('Hauptflügel', prof_lines('naca: 2413 160', 0.78)),
       ('Klappe', prof_lines('naca: 4415 120', 0.30, 25, 0.69, -0.052))],
      winkel=5, detail=min_dist(place(naca('2413'), 0.78), place(naca('4415'), 0.30, 25, 0.69, -0.052)), drehpunkt=(0.25, 0))

# Dreiteilige Landekonfiguration nach Vorbild 30P30N: Vorflügel 15 % / 30°, Klappe 30 % / 30°, Spalte 2,95 % und 1,27 %
main = load_profile('sc20612')
mc = 0.84
M = place(main, mc)
slat_src = naca('6412')
def mk_slat(s):
    # Vorflügel: Hinterkante über der Nase des Hauptflügels, Spalt s senkrecht nach oben verschoben
    return M, place(slat_src, 0.15, -30, -0.115, -0.040 + s)
slat_dy = solve_gap(mk_slat, 0.0295, -0.05, 0.08)
S = mk_slat(slat_dy)[1]
flap_src = naca('4412')
def mk_flap(s):
    return M, place(flap_src, 0.30, 30, 0.73, -0.06 + s)
flap_dy = solve_gap(mk_flap, 0.0127, -0.06, 0.03)
F = mk_flap(flap_dy)[1]
gap = min(min_dist(M, S), min_dist(M, F))
write(H, 'landekonfiguration', 'Dreiteiliger Landeflügel (Vorflügel, Klappe)',
      'Verkehrsflugzeug im Landeanflug: Vorflügel 30°, Hauptflügel, Fowler-Klappe 30°, Spalte nach Vorbild des NASA-Messfalls 30P30N.',
      'Hauptflügel: NASA SC(2)-0612 (UIUC-Datenbank); Vorflügel NACA 6412 und Klappe NACA 4412 (Formeln). Winkel und Spalte nach dem Messfall 30P30N (AIAA High Lift Prediction Workshop), Elemente vereinfacht',
      NASA_LIC + ', Anordnung ' + OWN, 'Sehnenlänge', 42,
      [('Vorflügel', prof_lines('naca: 6412 100', 0.15, -30, -0.115, -0.040 + slat_dy)),
       ('Hauptflügel', prof_lines('profil: sc20612', mc)),
       ('Klappe', prof_lines('naca: 4412 120', 0.30, 30, 0.73, -0.06 + flap_dy))],
      winkel=8, detail=gap, drehpunkt=(0.25, 0))

# Zweielement NLR 7301 mit Klappe: Klappe 32 %, 20°, Spalt 2,6 %
nm = load_profile('nlr7301')
NM = place(nm, 1)
def mk_nf(s):
    return NM, place(naca('4415'), 0.32, 20, 0.947, -0.08 + s)
nf_dy = solve_gap(mk_nf, 0.026, -0.08, 0.06)
write(H, 'nlr7301-klappe', 'NLR 7301 mit Klappe (Zweielement)',
      'Superkritischer Flügel mit 32-%-Klappe bei 20° und 2,6 % Spalt, nach dem bekannten NLR-Zweielement-Messfall.',
      'Hauptflügel NLR 7301 (UIUC-Datenbank, nlr7301.dat); Klappe vereinfacht als NACA 4415; Spalt und Winkel nach der NLR-7301-Messung (AGARD)',
      UIUC_LIC, 'Sehnenlänge', 36,
      [('Hauptflügel', prof_lines('profil: nlr7301')),
       ('Klappe', prof_lines('naca: 4415 120', 0.32, 20, 0.947, -0.08 + nf_dy))],
      winkel=6, detail=min_dist(NM, mk_nf(nf_dy)[1]), drehpunkt=(0.25, 0))

# Höhenleitwerk NACA 0012 mit Ruder (Scharnier bei 70 %), Ruder 15° nach unten
def split_profile(poly, xh):
    up = [p for p in poly[:len(poly) // 2 + 1]]  # Hinterkante -> Vorderkante (Oberseite)
    n = len(poly)
    le = min(range(n), key=lambda i: poly[i][0])
    upper = poly[:le + 1][::-1]   # Vorderkante -> Hinterkante
    lower = poly[le:] + [poly[0]]
    def y_at(side, x):
        for i in range(1, len(side)):
            (x1, y1), (x2, y2) = side[i - 1], side[i]
            if (x1 - x) * (x2 - x) <= 0 and x1 != x2: return y1 + (x - x1) / (x2 - x1) * (y2 - y1)
        return 0
    return upper, lower, y_at
p12 = naca('0012', 160)
upper, lower, y_at = split_profile(p12, 0.7)
xh = 0.72
yu, yl = y_at(upper, xh), y_at(lower, xh)
rh = (yu - yl) / 2
# Flosse: Vorderkante bis 0,705, hinten gerade abgeschnitten mit Aussparung für die Ruder-Nase
fin = [p for p in upper if p[0] <= xh - rh - 0.004][::-1]
yu2, yl2 = y_at(upper, xh - rh - 0.004), y_at(lower, xh - rh - 0.004)
fin = [(xh - rh - 0.004, yu2)] + fin + [p for p in lower if p[0] <= xh - rh - 0.004][1:] + [(xh - rh - 0.004, yl2)]
# Ruder: Nase als Halbkreis um das Scharnier, dann Ober- und Unterseite bis zur Hinterkante
rud = []
for i in range(13):
    a = math.radians(90 + 180 * i / 12)
    rud.append((xh + (rh - 0.002) * math.cos(a), (rh - 0.002) * math.sin(a)))
rud += [p for p in lower if p[0] > xh]
rud += [p for p in upper if p[0] > xh][::-1]
defl = 15
rud = [(xh + (x - xh) * math.cos(math.radians(defl)) + y * math.sin(math.radians(defl)),
        -(x - xh) * math.sin(math.radians(defl)) + y * math.cos(math.radians(defl))) for x, y in rud]
write(H, 'hoehenleitwerk', 'Höhenleitwerk mit Ruder (15°)',
      'Symmetrisches Leitwerk (NACA 0012) mit 28-%-Ruder, 15° ausgeschlagen: so steuert ein Flugzeug die Nicklage.',
      OWN + ' (NACA 0012 nach Formel, am Scharnier geteilt)', OWN_LIC, 'Sehnenlänge', 30,
      [('Flosse', pts_lines(fin)), ('Ruder', pts_lines(rud))], winkel=0, drehpunkt=(0.25, 0),
      note='Der Scharnierspalt ist schmaler als eine Zelle und wird beim Rastern geschlossen (wie ein abgedichtetes Ruder).')

# Gurney-Klappe an NACA 4412
g = naca('4412', 160)
gt = [(0.996, 0.0), (1.004, 0.0), (1.004, -0.02), (0.996, -0.02)]
write(H, 'gurney', 'NACA 4412 mit Gurney-Klappe',
      'Winzige Abrisskante (2 % der Sehne) an der Hinterkante: mehr Auftrieb, bekannt aus dem Rennsport (Dan Gurney).',
      NACA_SRC, NACA_LIC, 'Sehnenlänge', 30,
      [('Profil', ['naca: 4412 160']), ('Gurney-Klappe', pts_lines(gt, corners=(0, 1, 2, 3)))],
      winkel=4, detail=0.02, drehpunkt=(0.25, 0))

# ====================================================================== Rotor, Propeller, Windkraft
R = 'Rotor, Propeller & Windkraft'
uiuc(R, 'vr12', 'Boeing-Vertol VR-12 (Hubschrauber)', 'Rotorblatt-Profil von Boeing-Vertol mit kleiner Abrisskante an der Hinterkante.')
uiuc(R, 'naca643618', 'NACA 64(3)-618 (NREL 5 MW)', 'Profil der äußeren Blatthälfte der NREL-5-MW-Referenz-Windkraftanlage.')
uiuc(R, 'naca633618', 'NACA 63(3)-618 (Windkraft)', 'Verbreitetes, dickes Laminarprofil für Rotorblätter von Windkraftanlagen.')
write(R, 'propeller', 'Propellerblatt-Schnitt (Clark Y, 70 % Radius)',
      'Schnitt durch ein Propellerblatt: Clark Y mit 8° Anstellung zur Relativ-Anströmung aus Flug- und Drehgeschwindigkeit.',
      UIUC.format('clarky'), UIUC_LIC, 'Sehnenlänge', 25, [('Blatt', ['profil: clarky'])], winkel=8, drehpunkt=(0.25, 0))

# Verdichtergitter: 3 Schaufeln NACA 65(2)-415, Teilung 0,9 Sehnen, Staffelungswinkel 30°
blades = []
for k in (-1, 0, 1):
    blades.append(('Schaufel %d' % (k + 2), prof_lines('profil: naca652415', 1, 10, k * 0.45, k * 0.78)))
write(R, 'verdichtergitter', 'Verdichtergitter (3 Schaufeln)',
      'Ebenes Schaufelgitter wie in einem Axialverdichter: drei NACA-65-Profile übereinander, 30° gestaffelt.',
      UIUC.format('naca652415') + '; Anordnung ' + OWN, UIUC_LIC, 'Sehnenlänge', 20, blades, winkel=0)

# Turbinengitter: stark gewölbte, dicke Schaufeln (Kreisbogen-Skelett 100° + NACA-Dicke 20 %)
def turbine_blade(turn=100, t=0.20, n=60):
    th = math.radians(turn)
    R0 = 0.5 / math.sin(th / 2)
    up, lo = [], []
    for i in range(n + 1):
        b = math.pi * i / n; s = 0.5 * (1 - math.cos(b))
        yt = 5 * t * (0.2969 * math.sqrt(s) - 0.1260 * s - 0.3516 * s * s + 0.2843 * s ** 3 - 0.1015 * s ** 4)
        a = -th / 2 + th * s
        cx, cy = 0.5 + R0 * math.sin(a), -R0 * math.cos(th / 2) + R0 * math.cos(a)
        nx_, ny_ = math.sin(a), math.cos(a)
        up.append((cx + yt * nx_, cy + yt * ny_)); lo.append((cx - yt * nx_, cy - yt * ny_))
    return list(reversed(up)) + lo[1:n]
tb = turbine_blade()
tl = []
for k in (-1, 0, 1):
    tl.append(('Schaufel %d' % (k + 2), pts_lines(place(tb, 1, 35, 0, k * 0.85))))
write(R, 'turbinengitter', 'Turbinenschaufel-Gitter (3 Schaufeln)',
      'Stark umlenkende Turbinenschaufeln (100° Skelettwinkel): die Strömung wird kräftig umgelenkt und beschleunigt.',
      OWN + ' (Kreisbogen-Skelettlinie mit NACA-Dickenverteilung), kein bestimmtes Triebwerk', OWN_LIC, 'Sehnenlänge', 20, tl)

# Vertikalachsen-Windrad (Darrieus/H-Rotor), 3 Blätter NACA 0018, Sehne 0,25 R
vl = []
for k in range(3):
    phi = math.radians(90 + 120 * k)
    cx, cy = 0.5 * math.cos(phi), 0.5 * math.sin(phi)
    # Blatt tangential (Sehne senkrecht zum Radius), Mitte der Sehne auf dem Kreis
    tang = phi + math.pi / 2
    ang = -math.degrees(tang)
    lx, ly = cx - 0.125 * math.cos(tang), cy - 0.125 * math.sin(tang)
    vl.append(('Blatt %d' % (k + 1), prof_lines('naca: 0018 100', 0.25, ang, lx, ly)))
vl.append(('Welle', ['kreis: 0 0 0.04 48']))
write(R, 'darrieus', 'Vertikalachsen-Windrad (H-Rotor)',
      'Querschnitt eines H-Darrieus-Rotors: drei NACA-0018-Blätter auf einem Kreis, hier stehend (nicht drehend).',
      OWN + ' (NACA 0018 nach Formel)', OWN_LIC, 'Rotordurchmesser', 30, vl, re=500)

# ====================================================================== Formel 1
F1 = 'Formel 1'
s1223 = load_profile('s1223')
def stack(elements, gaps, mirror=True):
    """Mehrteiliger umgedrehter Flügel: Element i+1 sitzt mit Spalt gaps[i] hinter und über der Hinterkante von Element i.
    elements: Liste (Profil-Schlüssel, Profil-Punkte, Sehne, Winkel, Überlappung)."""
    placed, lines = [], []
    x, y = 0.0, 0.0
    for i, (key, src, c, ang, ovl) in enumerate(elements):
        if i == 0:
            P0 = place(src, c, ang, x, y, mirror)
        else:
            pk, pc, pang, px, py = prev
            te = (px + pc * math.cos(math.radians(pang)), py - pc * math.sin(math.radians(pang)))
            dirx, diry = math.cos(math.radians(pang)), -math.sin(math.radians(pang))
            bx, by = te[0] - ovl * dirx, te[1] - ovl * diry
            def mk(s, src=src, c=c, ang=ang, bx=bx, by=by):
                return placed[-1], place(src, c, ang, bx, by + s, mirror)
            dy = solve_gap(mk, gaps[i - 1], -0.05, 0.2)
            x, y = bx, by + dy
            P0 = place(src, c, ang, x, y, mirror)
        placed.append(P0)
        lines.append(prof_lines('profil: ' + key, c, ang, x, y, mirror))
        prev = (key, c, ang, x, y)
    return placed, lines

# Frontflügel: Hauptblatt + 3 Flaps, Gesamttiefe etwa 1, Spalte 1,5 %
fw_el = [('s1223', s1223, 0.42, -3, 0), ('s1223', s1223, 0.26, -13, 0.03), ('s1223', s1223, 0.21, -23, 0.03), ('s1223', s1223, 0.18, -33, 0.025)]
fw_p, fw_l = stack(fw_el, [0.02, 0.02, 0.02])
write(F1, 'f1-frontfluegel', 'Frontflügel (4 Elemente)',
      'Mehrteiliger Frontflügel im Bodeneffekt: Hauptblatt und drei Flaps, umgedreht für Abtrieb, Spalte 2 % der Tiefe (etwa 12 mm).',
      'Elemente: Selig S1223 (UIUC-Datenbank, s1223.dat), umgedreht; Anordnung ' + OWN + ' nach typischen Maßen (Reglement 2022–2025)',
      UIUC_LIC, 'Flügeltiefe', 40, [('Hauptblatt', fw_l[0]), ('Flap 1', fw_l[1]), ('Flap 2', fw_l[2]), ('Flap 3', fw_l[3])],
      winkel=0, re=1000, detail=min(min_dist(fw_p[i], fw_p[i + 1]) for i in range(3)), boden=True, bodenabstand=0.12)

# Heckflügel DRS zu / offen: Hauptblatt 60 %, Flap 40 %; offen: Flap um die Hinterkante gedreht, Schlitz 85 mm (bei 500 mm Tiefe: 0,17)
rw_el = [('s1223', s1223, 0.60, -6, 0), ('s1223', s1223, 0.42, -38, 0.035)]
rw_p, rw_l = stack(rw_el, [0.02])
write(F1, 'f1-heckfluegel-zu', 'Heckflügel, DRS geschlossen',
      'Hauptblatt und steil angestellte Flap mit schmalem Spalt: maximaler Abtrieb, aber viel Widerstand.',
      'Selig S1223 umgedreht (UIUC-Datenbank); Anordnung ' + OWN + ' nach typischen Maßen', UIUC_LIC, 'Flügeltiefe', 34,
      [('Hauptblatt', rw_l[0]), ('Flap', rw_l[1])], detail=min_dist(rw_p[0], rw_p[1]))
# offen: Flap-Hinterkante bleibt, Vorderkante hebt sich, bis der Spalt 0,17 ist
k, c, ang, x, y = 's1223', 0.42, -38, None, None
fl = rw_l[1]
fx, fy = [float(v) for v in [l for l in fl if l.startswith('lage')][0].split(':')[1].split()]
te = (fx + 0.42 * math.cos(math.radians(-38)), fy - 0.42 * math.sin(math.radians(-38)))
def mk_open(a):
    lx, ly = te[0] - 0.42 * math.cos(math.radians(a)), te[1] + 0.42 * math.sin(math.radians(a))
    return rw_p[0], place(s1223, 0.42, a, lx, ly, True)
# a von -38 (zu) Richtung 0 (flach): Abstand wächst
a_open = solve_gap(mk_open, 0.17, -38.0, 5.0)
lx, ly = te[0] - 0.42 * math.cos(math.radians(a_open)), te[1] + 0.42 * math.sin(math.radians(a_open))
# Achtung Vorzeichen: mehr "Nase hoch" bedeutet kleineren Betrag des negativen Winkels -> wir suchen den Winkel, bei dem der Spalt 0,17 erreicht
write(F1, 'f1-heckfluegel-offen', 'Heckflügel, DRS offen',
      'Die Flap klappt auf (Schlitz 85 mm bei 500 mm Tiefe): Abtrieb und Widerstand sinken, das Auto wird auf der Geraden schneller.',
      'Selig S1223 umgedreht (UIUC-Datenbank); Schlitz 85 mm nach FIA-Reglement 2022–2025; Anordnung ' + OWN, UIUC_LIC, 'Flügeltiefe', 34,
      [('Hauptblatt', rw_l[0]), ('Flap (offen)', prof_lines('profil: s1223', 0.42, a_open, lx, ly, True))],
      detail=0.02)

# Animierte Flügel: die Flap steht in der Datei in der Ausgangslage, "bewegung" sagt, um welchen Punkt und
# wie weit sie sich dreht (Winkel im Sinn von "winkel"). Dauer und Tempo legen fest, wie viele Rechenschritte
# die Bewegung dauert (gleiche Zahl von Überströmungen wie am echten Auto).
DRS_SRC = ('Selig S1223 umgedreht (UIUC-Datenbank); DRS-Schlitz offen 85 mm, Wechsel in weniger als 400 ms nach FIA-Reglement 2025 '
           '(Art. 3.10.10); Flap dreht um ihre Hinterkante; Anordnung ' + OWN)
def motion_extra(name, seconds, kmh, ref_m):
    return ['bewegung-name: ' + name, 'bewegung-dauer: ' + f(seconds), 'bewegung-tempo: ' + f(kmh), 'bezug-meter: ' + f(ref_m)]
write(F1, 'f1-heckfluegel-drs', 'Heckflügel mit DRS (animiert, 2025)',
      'Knopf „DRS öffnen“ oder Taste D: die Flap öffnet in 0,4 s (bei 300 km/h) bis zum 85-mm-Schlitz.',
      DRS_SRC, UIUC_LIC, 'Flügeltiefe', 34,
      [('Hauptblatt', rw_l[0]), ('Flap', rw_l[1] + ['bewegung: %s %s %s' % (f(te[0]), f(te[1]), f(a_open + 38.0))])],
      detail=min_dist(rw_p[0], rw_p[1]), extra=motion_extra('DRS', 0.4, 300, 0.5))
# Macarena (Ferrari 2026): die Flap dreht sich um 180° nach hinten und steht danach auf dem Kopf.
# Lage des Drehpunkts ist nicht veröffentlicht; Annahme: Mitte der Flap-Sehne.
mc = (fx + 0.21 * math.cos(math.radians(-38)), fy - 0.21 * math.sin(math.radians(-38)))
write(F1, 'f1-heckfluegel-macarena', 'Heckflügel 2026 mit „Macarena“-Flap (animiert)',
      'Ferrari 2026, Knopf „Geraden-Modus an“: die Flap dreht sich in 0,4 s um 180° nach hinten.',
      'Prinzip nach Berichten zum Ferrari SF-26 (Testfahrten Bahrain 2026); aktive Aerodynamik, Wechsel höchstens 400 ms nach FIA-Reglement 2026 '
      '(Art. 3.11.6); Drehpunkt in der Sehnenmitte und Flügelform angenommen (nicht veröffentlicht); Selig S1223 umgedreht (UIUC-Datenbank)',
      UIUC_LIC, 'Flügeltiefe', 34,
      [('Hauptblatt', rw_l[0]), ('Flap', rw_l[1] + ['bewegung: %s %s 180' % (f(mc[0]), f(mc[1]))])],
      detail=min_dist(rw_p[0], rw_p[1]), extra=motion_extra('Geraden-Modus', 0.4, 300, 0.5))

# Beam Wing: zwei schlanke Elemente
bw_el = [('e423', load_profile('e423'), 0.55, -2, 0), ('e423', load_profile('e423'), 0.45, -20, 0.03)]
bw_p, bw_l = stack(bw_el, [0.022])
write(F1, 'f1-beamwing', 'Beam Wing (2 Elemente)',
      'Der untere Heckflügel über dem Diffusor: zieht die Strömung aus dem Diffusor und verstärkt so den Unterboden.',
      'Eppler E423 umgedreht (UIUC-Datenbank, e423.dat); Anordnung ' + OWN, UIUC_LIC, 'Flügeltiefe', 30,
      [('Element 1', bw_l[0]), ('Element 2', bw_l[1])], detail=min_dist(bw_p[0], bw_p[1]))

# Unterboden mit Venturi-Kanal und Diffusor (Schnitt durch den Kanal), Länge 1 = 3,5 m
uf = [(0.0, 0.080), (0.005, 0.098), (0.02, 0.112), (0.08, 0.130), (0.30, 0.160), (0.55, 0.170), (0.75, 0.160), (0.92, 0.140), (1.0, 0.128),
      (1.0, 0.105), (0.985, 0.096), (0.97, 0.100), (0.80, 0.045), (0.62, 0.006), (0.45, 0.0), (0.30, 0.004), (0.15, 0.026), (0.04, 0.060), (0.01, 0.066)]
write(F1, 'f1-unterboden', 'Unterboden mit Venturi-Kanal und Diffusor',
      'Schnitt durch einen Venturi-Kanal des Unterbodens (ab 2022): Einlauf, enge Kehle knapp über dem Boden, ansteigender Diffusor.',
      OWN + ' nach typischen Maßen (Kanal-Einlauf etwa 250 mm, Kehle etwa 35 mm über der Bahn, Diffusor 1 m lang)', OWN_LIC,
      'Stirnhöhe', 55, [('Unterboden', pts_lines(uf, corners=(9, 10, 11), smooth=6))],
      re=1000, detail=0.01, boden=True, bodenabstand=0.01)

# Komplettes Seitenprofil (mm, Länge 5600)
MM = 5600.0
def mm(pts): return [(x / MM, y / MM) for x, y in pts]
body = mm([(170, 205), (130, 228), (160, 250), (420, 300), (800, 380), (1150, 460), (1500, 540), (1820, 610), (1900, 660),  # Nase, Chassis
           (1980, 690), (2040, 760), (2120, 805), (2220, 808), (2300, 770), (2330, 720),   # Helm
           (2420, 740), (2470, 880), (2540, 945), (2700, 940), (3000, 860),   # Airbox
           (3500, 700), (4000, 580), (4400, 520), (4800, 470), (5020, 450), (5080, 420),  # Motorabdeckung, Getriebe
           (5100, 330), (4950, 285), (4500, 150), (4200, 45), (3000, 30), (1600, 30), (1450, 45), (1350, 120),  # Diffusor, Boden
           (1150, 150), (700, 175), (300, 190)])
body_c = (8, 14, 25, 26, 29, 30, 31, 32)
halo = ['stab: %s %s %s %s %s' % (f(1830 / MM), f(700 / MM), f(2120 / MM), f(890 / MM), f(34 / MM)),
        ]
halo2 = ['stab: %s %s %s %s %s' % (f(2120 / MM), f(890 / MM), f(2380 / MM), f(880 / MM), f(34 / MM))]
R_WH = 360 / MM
# Für das ganze Auto: Frontflügel mit 3 Elementen, Spalte 45 mm (= 2 Zellen bei sehr hoher Auflösung)
car_fw_p, car_fw_l = stack([('s1223', s1223, 0.48, -4, 0), ('s1223', s1223, 0.32, -20, 0.03), ('s1223', s1223, 0.26, -38, 0.03)],
                           [45 / 620, 45 / 620])
car_rw_p, car_rw_l = stack([('s1223', s1223, 0.60, -6, 0), ('s1223', s1223, 0.42, -38, 0.035)], [45 / 500])
cf = car_rw_l[1]
cfx, cfy = [float(v) for v in [l for l in cf if l.startswith('lage')][0].split(':')[1].split()]
cte = (cfx + 0.42 * math.cos(math.radians(-38)), cfy - 0.42 * math.sin(math.radians(-38)))
def mk_copen(a):
    return car_rw_p[0], place(s1223, 0.42, a, cte[0] - 0.42 * math.cos(math.radians(a)), cte[1] + 0.42 * math.sin(math.radians(a)), True)
ca_open = solve_gap(mk_copen, 85 / 500, -38.0, 5.0)
car_rw_open = prof_lines('profil: s1223', 0.42, ca_open, cte[0] - 0.42 * math.cos(math.radians(ca_open)), cte[1] + 0.42 * math.sin(math.radians(ca_open)), True)
car_bw_p, car_bw_l = stack([('e423', load_profile('e423'), 0.55, -2, 0), ('e423', load_profile('e423'), 0.45, -20, 0.03)], [45 / 330])

def f1_car(drs_open, motion=None):
    parts = [('Karosserie', pts_lines(body, corners=body_c, smooth=5)),
             ('Halo vorn', halo), ('Halo oben', halo2),
             ('Vorderrad', ['kreis: %s %s %s 96' % (f(1150 / MM), f(R_WH), f(R_WH))]),
             ('Hinterrad', ['kreis: %s %s %s 96' % (f(4750 / MM), f(R_WH), f(R_WH))])]
    # Frontflügel: 620 mm tief, Vorderkante bei x = 0, 85 mm über der Bahn
    s = 620 / MM
    for (nm_, l) in zip(['Frontflügel Hauptblatt', 'Frontflügel Flap 1', 'Frontflügel Flap 2'], car_fw_l):
        parts.append((nm_, scale_lines(l, s, 0, 85 / MM)))
    # Heckflügel: 500 mm tief, Vorderkante bei x = 5060, 790 mm hoch
    s = 500 / MM
    rl = car_rw_l if not drs_open else [car_rw_l[0], car_rw_open]
    for (nm_, l) in zip(['Heckflügel Hauptblatt', 'Heckflügel Flap'], rl):
        lines = scale_lines(l, s, 5060 / MM, 800 / MM)
        if motion and nm_ == 'Heckflügel Flap':
            (px, py), deg = motion
            lines.append('bewegung: %s %s %s' % (f(5060 / MM + px * s), f(800 / MM + py * s), f(deg)))
        parts.append((nm_, lines))
    s = 330 / MM
    for (nm_, l) in zip(['Beam Wing 1', 'Beam Wing 2'], car_bw_l):
        parts.append((nm_, scale_lines(l, s, 5180 / MM, 470 / MM)))
    return parts

def scale_lines(lines, s, ox, oy):
    r = []
    for l in lines:
        if l.startswith('sehne:'): r.append('sehne: ' + f(float(l.split(':')[1]) * s))
        elif l.startswith('lage:'):
            a, b = [float(v) for v in l.split(':')[1].split()]
            r.append('lage: %s %s' % (f(ox + a * s), f(oy + b * s)))
        else: r.append(l)
    if not any(l.startswith('sehne:') for l in r): r.insert(1, 'sehne: ' + f(s))
    if not any(l.startswith('lage:') for l in r): r.append('lage: %s %s' % (f(ox), f(oy)))
    return r

F1_SRC = ('Eigene Konstruktion nach den öffentlichen Maßen des FIA-Reglements 2022–2025 (Radstand 3600 mm, Raddurchmesser 720 mm, '
          'Höhe bis 950 mm, DRS-Schlitz 85 mm); Flügelelemente Selig S1223 und Eppler E423 (UIUC-Datenbank). Kein bestimmtes Team-Fahrzeug')
NOTE_F1 = 'Am ganzen Auto sind die Flügelspalte auf 45 mm vergrößert (sonst schmaler als eine Gitterzelle); die einzelnen Flügelmodelle haben die echten Spalte.\nRäder, Flügel und Seitenkasten liegen in Wirklichkeit nebeneinander; im 2D-Schnitt sind sie so angeordnet, dass sich die Flügel nicht mit den Rädern überdecken.'
write(F1, 'f1-komplett', 'Formel-1-Wagen komplett (DRS zu)',
      'Seitenprofil eines aktuellen Formel-1-Autos: mehrteiliger Frontflügel, Nase, Halo, Helm, Airbox, Diffusor, Beam Wing und Heckflügel.',
      F1_SRC, OWN_LIC, 'Stirnhöhe', 55, f1_car(False), re=1000, detail=45 / MM, boden=True, note=NOTE_F1)
write(F1, 'f1-komplett-drs', 'Formel-1-Wagen komplett (DRS offen)',
      'Wie oben, aber mit geöffnetem DRS-Schlitz am Heckflügel: Vergleich von Abtrieb und Widerstand.',
      F1_SRC, OWN_LIC, 'Stirnhöhe', 55, f1_car(True), re=1000, detail=45 / MM, boden=True, note=NOTE_F1)
write(F1, 'f1-komplett-drs-animiert', 'Formel-1-Wagen mit DRS (animiert)',
      'Ganzes Auto, Knopf „DRS öffnen“ oder Taste D: das DRS öffnet in 0,4 s (bei 300 km/h).',
      F1_SRC + '; DRS-Wechsel in weniger als 400 ms (Reglement 2025, Art. 3.10.10)', OWN_LIC, 'Stirnhöhe', 55,
      f1_car(False, (cte, ca_open + 38.0)), re=1000, detail=45 / MM, boden=True, note=NOTE_F1,
      extra=motion_extra('DRS', 0.4, 300, 0.95))

# ====================================================================== Straßenfahrzeuge
V = 'Straßenfahrzeuge'
VEH_SRC = OWN + ' nach typischen Abmessungen, kein bestimmtes Fahrzeugmodell'
limo = [(0.030, 0.068), (0.006, 0.095), (0.000, 0.125), (0.008, 0.158), (0.040, 0.180), (0.130, 0.196), (0.290, 0.214),
        (0.320, 0.222), (0.420, 0.298), (0.470, 0.314), (0.560, 0.318), (0.630, 0.308), (0.760, 0.252), (0.800, 0.240),
        (0.900, 0.236), (0.945, 0.240), (0.958, 0.246), (0.962, 0.236), (0.985, 0.205), (1.000, 0.160), (0.998, 0.115),
        (0.985, 0.090), (0.950, 0.084), (0.880, 0.062), (0.700, 0.058), (0.300, 0.058), (0.100, 0.060)]
wheels = lambda xf, xr, r: [('Vorderrad', ['kreis: %s %s %s 64' % (f(xf), f(r), f(r))]), ('Hinterrad', ['kreis: %s %s %s 64' % (f(xr), f(r), f(r))])]
write(V, 'limousine', 'Limousine (Stufenheck)', 'Mittelklasse-Limousine mit Motorhaube, Frontscheibe, Dach, Heckscheibe, Kofferraum mit Abrisskante und Diffusor.',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 50, [('Karosserie', pts_lines(limo, corners=(16,), smooth=6))] + wheels(0.19, 0.79, 0.075), boden=True)
fast = limo[:12] + [(0.700, 0.282), (0.800, 0.250), (0.900, 0.226), (0.960, 0.214), (0.972, 0.218)] + limo[18:]
write(V, 'fliessheck', 'Fließheck-Limousine', 'Wie die Limousine, aber mit flach abfallendem Dach bis zur Abrisskante (Fastback).',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 50, [('Karosserie', pts_lines(fast, corners=(16,), smooth=6))] + wheels(0.19, 0.79, 0.075), boden=True)
kombi = limo[:11] + [(0.800, 0.314), (0.960, 0.306), (0.985, 0.296), (0.990, 0.282)] + limo[18:]
write(V, 'kombi', 'Kombi', 'Langes, gerades Dach bis zum steilen Heck: großes Totwasser hinter dem Fahrzeug.',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 50, [('Karosserie', pts_lines(kombi, corners=(12, 13), smooth=6))] + wheels(0.19, 0.79, 0.075), boden=True)
sport = [(0.012, 0.042), (0.000, 0.070), (0.006, 0.092), (0.060, 0.118), (0.250, 0.150), (0.340, 0.168), (0.470, 0.252),
         (0.550, 0.270), (0.640, 0.262), (0.800, 0.205), (0.930, 0.170), (0.965, 0.178), (0.972, 0.165), (0.995, 0.140),
         (1.000, 0.105), (0.990, 0.085), (0.950, 0.062), (0.860, 0.040), (0.600, 0.035), (0.300, 0.035), (0.080, 0.040), (0.010, 0.036)]
write(V, 'sportwagen', 'Sportwagen-Coupé', 'Flaches Coupé mit Frontsplitter, langer Haube, Fließheck mit Abrisskante und Diffusor.',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 50, [('Karosserie', pts_lines(sport, corners=(0, 11, 21), smooth=6))] + wheels(0.20, 0.79, 0.071), boden=True)
van = [(0.010, 0.060), (0.000, 0.100), (0.005, 0.180), (0.060, 0.230), (0.200, 0.330), (0.260, 0.345), (0.950, 0.345),
       (0.990, 0.335), (1.000, 0.320), (1.000, 0.080), (0.980, 0.055), (0.900, 0.050), (0.100, 0.050)]
write(V, 'transporter', 'Transporter', 'Kastenwagen mit kurzer Haube und senkrechtem Heck.',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 45, [('Karosserie', pts_lines(van, corners=(8, 9), smooth=5))] + wheels(0.17, 0.80, 0.060), boden=True)

# Sattelzug, Länge 16,5 m
T = 16.5
def tm(pts): return [(x / T, y / T) for x, y in pts]
cab = tm([(0.0, 0.45), (0.0, 2.60), (0.08, 3.25), (0.25, 3.60), (2.20, 3.70), (2.30, 3.60), (2.30, 0.80), (2.10, 0.45)])
trailer = tm([(3.0, 1.15), (3.0, 4.00), (16.5, 4.00), (16.5, 1.15)])
chassis = tm([(0.6, 0.55), (0.6, 1.05), (4.8, 1.05), (4.8, 0.55)])
under = tm([(12.4, 0.70), (12.4, 1.15), (16.2, 1.15), (16.2, 0.70)])
r = 0.5 / T
truck_w = [('Rad %d' % (i + 1), ['kreis: %s %s %s 40' % (f(x / T), f(r), f(r))]) for i, x in enumerate([1.3, 4.0, 13.0, 14.3, 15.6])]
truck = [('Fahrerhaus', pts_lines(cab, corners=(0, 5, 6, 7), smooth=4)), ('Rahmen', pts_lines(chassis, corners=(0, 1, 2, 3))),
         ('Auflieger', pts_lines(trailer, corners=(0, 1, 2, 3))), ('Achsaggregat', pts_lines(under, corners=(0, 1, 2, 3)))] + truck_w
write(V, 'sattelzug', 'Lkw-Sattelzug', 'Fahrerhaus, Spalt zum Auflieger, kantiger Kofferaufbau: viel Druckwiderstand vorn und großes Totwasser hinten.',
      VEH_SRC + ' (Gesamtlänge 16,5 m, Höhe 4 m)', OWN_LIC, 'Stirnhöhe', 58, truck, re=2000, boden=True, detail=0.6 / T)
deflector = tm([(0.30, 3.60), (2.20, 3.70), (2.80, 4.00), (2.95, 4.00), (2.95, 3.70), (2.30, 3.40)])
skirt = tm([(5.2, 0.45), (5.2, 1.15), (12.2, 1.15), (12.2, 0.45)])
boat_top = ['stab: %s %s %s %s %s' % (f(16.5 / T), f(3.94 / T), f(17.3 / T), f(3.73 / T), f(0.12 / T))]
boat_bot = ['stab: %s %s %s %s %s' % (f(16.5 / T), f(1.21 / T), f(17.3 / T), f(1.42 / T), f(0.12 / T))]
truck_aero = [('Fahrerhaus', pts_lines(cab, corners=(0, 5, 6, 7), smooth=4)), ('Dachspoiler', pts_lines(deflector, corners=(2, 3, 4, 5), smooth=3)),
              ('Rahmen', pts_lines(chassis, corners=(0, 1, 2, 3))), ('Auflieger', pts_lines(trailer, corners=(0, 1, 2, 3))),
              ('Seitenverkleidung', pts_lines(skirt, corners=(0, 1, 2, 3))), ('Achsaggregat', pts_lines(under, corners=(0, 1, 2, 3))),
              ('Heckklappe oben', boat_top), ('Heckklappe unten', boat_bot)] + truck_w
write(V, 'sattelzug-aero', 'Lkw-Sattelzug aerodynamisch', 'Mit Dachspoiler, Seitenverkleidung und Heck-Leitblechen („Boat Tail“): deutlich weniger Widerstand.',
      VEH_SRC + ' (Anbauteile nach üblichen Nachrüstsätzen)', OWN_LIC, 'Stirnhöhe', 58, truck_aero, re=2000, boden=True, detail=0.12 / T)
B = 12.0
bus = [(x / B, y / B) for x, y in [(0.05, 0.40), (0.0, 0.60), (0.0, 2.30), (0.10, 2.90), (0.40, 3.20), (11.8, 3.25), (12.0, 3.10), (12.0, 0.45), (11.7, 0.35), (0.3, 0.35)]]
write(V, 'reisebus', 'Reisebus', 'Zwölf Meter langer Bus mit gerundeter Front und senkrechtem Heck.',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 55, [('Aufbau', pts_lines(bus, corners=(6, 7), smooth=4))] + wheels(2.6 / B, 9.0 / B, 0.5 / B), re=2000, boden=True)
# Hochgeschwindigkeitszug: zwei Triebköpfe, 50 m, Höhe 3,9 m
Z = 50.0
nose = [(0.0, 0.55), (0.25, 1.10), (1.0, 1.70), (2.5, 2.40), (4.5, 3.10), (6.5, 3.60), (9.0, 3.85)]
train = [(x / Z, y / Z) for x, y in nose] + [((Z - x) / Z, y / Z) for x, y in reversed(nose)] + [((Z - 0.4) / Z, 0.40 / Z), (0.4 / Z, 0.40 / Z)]
write(V, 'zug', 'Hochgeschwindigkeitszug', 'Kurzer Zug mit zwei stromlinienförmigen Triebköpfen (50 m), dicht über dem Gleis.',
      VEH_SRC, OWN_LIC, 'Stirnhöhe', 58, [('Zug', pts_lines(train, corners=(14, 15), smooth=4))], re=3000, boden=True, bodenabstand=0.3 / Z)

# ====================================================================== Benchmarks & Sport
K = 'Benchmarks, Sport & Segel'
def ahmed(phi):
    dx, dy = 222 * math.cos(math.radians(phi)), 222 * math.sin(math.radians(phi))
    p = []
    L = ['punkte:']
    L.append('  bogen 100 150 100 180 270 16'.replace('100 150 100', '%s %s %s' % (f(100 / 1044), f(150 / 1044), f(100 / 1044))))
    pts = [(1044, 50, True)]
    if phi > 0: pts += [(1044, 338 - dy, True), (1044 - dx, 338, True)]
    else: pts += [(1044, 338, True)]
    for x, y, c in pts: L.append('  %s %s%s' % (f(x / 1044), f(y / 1044), ' !' if c else ''))
    L.append('  bogen %s %s %s 90 180 16' % (f(100 / 1044), f(238 / 1044), f(100 / 1044)))
    return L
for phi, title, d in [(25, 'Ahmed-Körper 25°', 'Kritischer Heckwinkel knapp unter 30°: die Strömung liegt auf der Schräge noch an, starke Längswirbel.'),
                      (35, 'Ahmed-Körper 35°', 'Steiler Heckwinkel: die Strömung reißt an der Dachkante ab, das Heck ist voll abgelöst.'),
                      (0, 'Ahmed-Körper 0° (Kastenheck)', 'Ohne Schräge: senkrechtes Heck mit großem Totwasser.')]:
    write(K, 'ahmed%d' % phi, title, d,
          'Ahmed, Ramm, Faltin: „Some salient features of the time-averaged ground vehicle wake“, SAE 840300 (1984): Länge 1044 mm, Höhe 288 mm, '
          'Radius vorn 100 mm, Schräge 222 mm, Bodenabstand 50 mm (Stelzen weggelassen)', 'Maße aus der Veröffentlichung, Koordinaten selbst berechnet',
          'Stirnhöhe', 45, [('Körper', ahmed(phi))], re=2000, boden=True, bodenabstand=50 / 1044)

# Radfahrer in Zeitfahrhaltung (fährt nach links, gegen die Strömung), Maße in m, Länge 1,7 m
CY = 1.7
def cap(x0, y0, x1, y1, d): return ['stab: %s %s %s %s %s' % (f(x0 / CY), f(y0 / CY), f(x1 / CY), f(y1 / CY), f(d / CY))]
torso = [(c[0] / CY, c[1] / CY) for c in [(0.60, 1.02), (0.66, 1.10), (0.80, 1.14), (1.00, 1.13), (1.10, 1.08), (1.12, 0.98), (1.04, 0.90), (0.80, 0.94), (0.64, 0.95)]]
helmet = [(c[0] / CY, c[1] / CY) for c in [(0.46, 1.10), (0.50, 1.18), (0.58, 1.21), (0.68, 1.19), (0.86, 1.15), (0.66, 1.08), (0.52, 1.04)]]
rider = [('Hinterrad (Scheibe)', ['kreis: %s %s %s 80' % (f(1.34 / CY), f(0.34 / CY), f(0.34 / CY))]),
         ('Vorderrad', ['kreis: %s %s %s 80' % (f(0.36 / CY), f(0.34 / CY), f(0.34 / CY))]),
         ('Unterrohr', cap(0.53, 0.64, 0.92, 0.30, 0.05)), ('Oberrohr', cap(0.53, 0.70, 1.00, 0.76, 0.035)),
         ('Sitzrohr', cap(1.00, 0.80, 0.92, 0.30, 0.05)), ('Gabel', cap(0.53, 0.66, 0.36, 0.34, 0.035)),
         ('Hinterbau', cap(0.92, 0.30, 1.34, 0.34, 0.03)), ('Lenker', cap(0.53, 0.70, 0.44, 0.92, 0.035)),
         ('Rumpf', pts_lines(torso, smooth=5)), ('Helm', pts_lines(helmet, corners=(4,), smooth=5)),
         ('Oberarm', cap(0.64, 1.00, 0.60, 0.90, 0.09)), ('Unterarm', cap(0.60, 0.90, 0.40, 0.95, 0.075)),
         ('Oberschenkel vorn', cap(1.02, 0.95, 0.78, 0.70, 0.15)), ('Unterschenkel vorn', cap(0.78, 0.70, 0.83, 0.32, 0.10)),
         ('Oberschenkel hinten', cap(1.04, 0.95, 0.98, 0.62, 0.15)), ('Unterschenkel hinten', cap(0.98, 0.62, 1.06, 0.24, 0.10))]
write(K, 'radfahrer', 'Radfahrer in Zeitfahrhaltung', 'Fahrer auf dem Zeitfahrrad mit Scheibenrad, Aerolenker und Tropfenhelm (Seitenansicht).',
      OWN + ' nach typischen Körpermaßen (Fahrer etwa 1,80 m, Laufrad 700C)', OWN_LIC, 'Stirnhöhe', 50, rider, re=2000, boden=True,
      detail=0.03 / CY)

# Segel: Mast + Großsegel (Schnitt in Waagerechter, Wind von links)
sail = naca('8402')
write(K, 'segel', 'Mast mit Großsegel', 'Waagerechter Schnitt durch Mast und Segel (Wölbung 8 %): der Mast stört die Anströmung des Vorlieks.',
      OWN + ' (Segel als dünnes gewölbtes NACA-8402-Profil)', OWN_LIC, 'Segeltiefe', 32,
      [('Mast', ['ellipse: -0.015 0 0.03 0.022 48']), ('Segel', ['naca: 8402 120'])], winkel=12, re=1500, detail=0.02, drehpunkt=(0.3, 0))
MS = place(sail, 1)
def mk_jib(s):
    return MS, place(sail, 0.75, 6, -0.62, 0.06 + s)
jdy = solve_gap(mk_jib, 0.05, -0.1, 0.3)
write(K, 'fock-gross', 'Fock und Großsegel', 'Zwei Segel mit Spalt: die Fock beschleunigt die Strömung an der Leeseite des Großsegels (Düseneffekt).',
      OWN + ' (Segel als NACA-8402-Profile)', OWN_LIC, 'Segeltiefe', 26,
      [('Mast', ['ellipse: -0.015 0 0.03 0.022 48']), ('Großsegel', ['naca: 8402 120']), ('Fock', prof_lines('naca: 8402 120', 0.75, 6, -0.62, 0.06 + jdy))],
      winkel=12, re=1500, detail=0.02, drehpunkt=(0.1, 0))

# ====================================================================== Bauwerke
W = 'Bauwerke & Brücken'
write(W, 'hochhaus', 'Hochhaus', 'Scharfkantiges Hochhaus, dreimal so hoch wie breit, steht auf dem Boden.',
      OWN, OWN_LIC, 'Gebäudehöhe', 45, [('Gebäude', ['rechteck: 0 0 0.33 1'])], re=2000, boden=True, bodenabstand=0)
house = [(0, 0), (1, 0), (1, 0.55), (1.08, 0.50), (1.10, 0.53), (0.5, 0.95), (-0.10, 0.53), (-0.08, 0.50), (0, 0.55)]
write(W, 'haus', 'Haus mit Satteldach', 'Einfamilienhaus mit 35°-Satteldach und Dachüberstand: Sog auf der Leeseite des Dachs.',
      OWN, OWN_LIC, 'Gebäudehöhe', 30, [('Haus', pts_lines(house, corners=tuple(range(9))))], re=2000, boden=True, bodenabstand=0)
write(W, 'strassenschlucht', 'Straßenschlucht', 'Zwei gleich hohe Häuserblöcke mit einer Straße dazwischen (Breite = Höhe): ein Wirbel füllt die Straße.',
      OWN, OWN_LIC, 'Gebäudehöhe', 30, [('Block 1', ['rechteck: 0 0 1 1']), ('Block 2', ['rechteck: 2 0 3 1'])], re=2000, boden=True, bodenabstand=0)
TN = 11.9
write(W, 'tacoma', 'Brückenquerschnitt Tacoma Narrows (1940)',
      'H-förmiger Querschnitt der eingestürzten Tacoma-Narrows-Brücke: 2,4 m hohe Vollwandträger am Rand, Fahrbahn dazwischen.',
      'Maße aus öffentlichen Berichten zum Einsturz 1940 (Breite 11,9 m, Trägerhöhe 2,44 m); Blechdicken auf 0,15 m vergrößert, damit sie aufs Gitter passen',
      'Maße öffentlich, Koordinaten selbst berechnet', 'Brückenbreite', 50,
      [('Träger links', rect_pts(-0.5, -1.22 / TN, -0.5 + 0.15 / TN, 1.22 / TN)),
       ('Träger rechts', rect_pts(0.5 - 0.15 / TN, -1.22 / TN, 0.5, 1.22 / TN)),
       ('Fahrbahn', rect_pts(-0.5, 0.55 / TN, 0.5, 0.75 / TN))], re=1000, detail=0.15 / TN)
SB = 31.0
box = [(x / SB - 0.5, y / SB - 2.2 / SB) for x, y in [(0, 2.2), (3.2, 4.4), (27.8, 4.4), (31, 2.2), (25.5, 0), (5.5, 0)]]
write(W, 'kastentraeger', 'Brückenquerschnitt Hohlkasten (Großer Belt)',
      'Stromlinienförmiger Hohlkastenträger einer großen Hängebrücke: 31 m breit, 4,4 m hoch, spitze Windnasen.',
      'Maße nach öffentlichen Angaben zur Ostbrücke über den Großen Belt (Storebælt, 1998), vereinfacht ohne Geländer', 'Maße öffentlich, Koordinaten selbst berechnet',
      'Brückenbreite', 50, [('Kasten', pts_lines(box, corners=tuple(range(6))))], re=1000)

# ====================================================================== Katalog
catalog.append(('Eigene', 'eigene'))
cats = []
for c, i in catalog:
    if c not in cats: cats.append(c)
with open(os.path.join(OUT, 'katalog.txt'), 'w', encoding='utf-8', newline='\n') as fh:
    fh.write('# Reihenfolge der Kategorien und Modelle in der Auswahl. „eigene“ = Zeichnen mit der Maus.\n')
    for c in cats:
        fh.write('\n[%s]\n' % c)
        for cc, i in catalog:
            if cc == c: fh.write(i + '\n')
print(len(catalog), 'Modelle')
