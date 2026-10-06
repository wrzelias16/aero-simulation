# Geometrie-Hilfen für modelle_erzeugen.py, gleiche Formeln wie App/Models.cs (Profile laden, platzieren, Spalte messen)
import math, os
PROF = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'Profile')

def load_profile(name):
    rows = []
    with open(os.path.join(PROF, name + '.dat'), encoding='latin1') as f:
        lines = f.read().replace('\r', '').split('\n')
    for raw in lines[1:]:
        line = raw.split('#')[0].strip()
        if not line: continue
        v = [float(t) for t in line.replace(';', ' ').split()]
        rows.append(v)
    if rows[0][0] > 1.5 and rows[0][1] > 1.5:
        nu, nl = int(rows[0][0]), int(rows[0][1])
        up = rows[1:1 + nu]; lo = rows[1 + nu:1 + nu + nl]
        poly = [tuple(p[:2]) for p in reversed(up)] + [tuple(p[:2]) for p in lo[1:]]
    else:
        poly = [tuple(p[:2]) for p in rows]
    while len(poly) > 3 and math.dist(poly[0], poly[-1]) < 1e-6: poly.pop()
    le = min(range(len(poly)), key=lambda i: poly[i][0])
    L = poly[le]; T = ((poly[0][0] + poly[-1][0]) / 2, (poly[0][1] + poly[-1][1]) / 2)
    ln = math.dist(L, T)
    return [((x - L[0]) / ln, (y - L[1]) / ln) for x, y in poly]

def naca(code, n=120):
    m = int(code[0]) / 100; p = int(code[1]) / 10; t = int(code[2:]) / 100
    up, lo = [], []
    for i in range(n + 1):
        b = math.pi * i / n; x = 0.5 * (1 - math.cos(b))
        yt = 5 * t * (0.2969 * math.sqrt(x) - 0.1260 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4)
        yc = dyc = 0
        if m > 0 and p > 0:
            if x < p: yc = m / p / p * (2 * p * x - x * x); dyc = 2 * m / p / p * (p - x)
            else: yc = m / (1 - p) ** 2 * ((1 - 2 * p) + 2 * p * x - x * x); dyc = 2 * m / (1 - p) ** 2 * (p - x)
        th = math.atan(dyc)
        up.append((x - yt * math.sin(th), yc + yt * math.cos(th)))
        lo.append((x + yt * math.sin(th), yc - yt * math.cos(th)))
    return list(reversed(up)) + lo[1:n]

def place(src, chord=1, angle=0, x=0, y=0, mirror=False, thick=1):
    a = math.radians(angle); ca, sa = math.cos(a), math.sin(a)
    r = []
    for qx, qy in src:
        px = qx * chord; py = (-qy if mirror else qy) * chord * thick
        r.append((x + px * ca + py * sa, y - px * sa + py * ca))
    return r

def te_le(src, chord, angle, x, y):
    """Vorderkante (= lage) und Hinterkante des platzierten Profils."""
    a = math.radians(angle)
    return (x, y), (x + chord * math.cos(a), y - chord * math.sin(a))

def seg_dist(p, a, b):
    ax, ay = a; bx, by = b; px, py = p
    dx, dy = bx - ax, by - ay; l2 = dx * dx + dy * dy
    t = 0 if l2 == 0 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / l2))
    return math.hypot(px - ax - t * dx, py - ay - t * dy)

def inside(p, poly):
    x, y = p; c = False
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i], poly[i - 1]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1: c = not c
    return c

def min_dist(A, B):
    """Kleinster Abstand zweier Polygone (negativ, wenn sie sich überlappen)."""
    if any(inside(p, B) for p in A[::3]) or any(inside(p, A) for p in B[::3]): return -1
    best = 1e9
    for p in A:
        for i in range(len(B)):
            best = min(best, seg_dist(p, B[i], B[i - 1]))
    for p in B:
        for i in range(len(A)):
            best = min(best, seg_dist(p, A[i], A[i - 1]))
    return best

def solve_gap(make, target, lo, hi, it=40):
    """Sucht den Parameter s in [lo, hi], bei dem make(s) -> (A, B) den Spalt 'target' hat.
    Funktioniert in beide Richtungen (Abstand steigend oder fallend mit s)."""
    d = lambda s: min_dist(*make(s))
    rising = d(hi) > d(lo)
    for _ in range(it):
        mid = (lo + hi) / 2
        if (d(mid) < target) == rising: lo = mid
        else: hi = mid
    return (lo + hi) / 2

def thickness_at(poly, xc):
    """Profildicke senkrecht bei x = xc (Sehne 1, unverdreht)."""
    ys = []
    for i in range(len(poly)):
        (x1, y1), (x2, y2) = poly[i - 1], poly[i]
        if (x1 - xc) * (x2 - xc) <= 0 and x1 != x2:
            ys.append(y1 + (xc - x1) / (x2 - x1) * (y2 - y1))
    return max(ys) - min(ys) if len(ys) >= 2 else 0

def f(v): return ('%.5f' % v).rstrip('0').rstrip('.') if abs(v) > 1e-9 else '0'
