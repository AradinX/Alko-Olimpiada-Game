"""Zamienia lity model (podloga / dach panteonu) na mur z pojedynczych kamiennych blokow.

Metoda: dla kazdej kolumny (x,y) raycast z gory i z dolu daje grubosc bryly.
Bryla jest ciecia na poziome warstwy (levels) i na nieregularne bloki w planie.
Gorny blok kazdej kolumny dostaje wierzcholki na powierzchni oryginalu, wiec
sylwetka (spadek dachu, stopnie) zostaje zachowana, a reszta to mur.

Uruchomienie: otworz Koloseum.blend i wykonaj ten plik w Blenderze.
"""

import bpy
import bmesh
import random
from mathutils import Vector

# ponytail: staly seed - ten sam uklad blokow po kazdym uruchomieniu
OFFSET = Vector((-9.0, 0.0, 0.0))  # gdzie postawic kopie obok oryginalu


def make_sampler(src_name):
    """Zwraca f(x,y) -> (z_dol, z_gora) albo None gdy poza bryla."""
    ob = bpy.data.objects[src_name]
    mw = ob.matrix_world
    inv = mw.inverted()
    m3 = inv.to_3x3()
    down = (m3 @ Vector((0, 0, -1))).normalized()
    up = (m3 @ Vector((0, 0, 1))).normalized()

    def sample(x, y):
        hi, lhi, _, _ = ob.ray_cast(inv @ Vector((x, y, 100.0)), down)
        lo, llo, _, _ = ob.ray_cast(inv @ Vector((x, y, -100.0)), up)
        if not (hi and lo):
            return None
        zt = (mw @ lhi).z
        zb = (mw @ llo).z
        return (zb, zt) if zt - zb > 1e-4 else None

    return sample


def partition(lo, hi, dmin, dmax, rng, lead=0.0, forced=()):
    """Tnie odcinek na kawalki losowej dlugosci; lead = losowe przesuniecie fugi.

    forced = progi bryly (krawedz portyku itp.); zaden blok ich nie przekracza,
    inaczej wychodza wielkie skosne kliny na uskoku.
    """
    edges = [lo] + sorted(v for v in forced if lo + 1e-6 < v < hi - 1e-6) + [hi]
    cuts = [lo]
    for a, b in zip(edges, edges[1:]):
        p = a - rng.uniform(0.0, lead)
        while True:
            p += rng.uniform(dmin, dmax)
            if p >= b - dmin * 0.5:
                break
            if p > a + dmin * 0.5:
                cuts.append(p)
        cuts.append(b)
    return cuts


def clip_rect(sample, xa, xb, ya, yb):
    """Sciaga kazdy bok prostokata do prawdziwej krawedzi bryly. Blok zostaje prostokatem."""
    cx, cy = (xa + xb) * 0.5, (ya + yb) * 0.5

    def edge(val, fixed, axis, toward):
        # val jest poza bryla -> bisekcja w strone srodka
        probe = (lambda v: (v, fixed)) if axis == 'x' else (lambda v: (fixed, v))
        if sample(*probe(val)) is not None:
            return val
        a, b = val, toward
        for _ in range(7):
            m = (a + b) * 0.5
            if sample(*probe(m)) is None:
                a = m
            else:
                b = m
        return b

    xa = edge(xa, cy, 'x', cx)
    xb = edge(xb, cy, 'x', cx)
    ya = edge(ya, cx, 'y', cy)
    yb = edge(yb, cx, 'y', cy)
    return xa, xb, ya, yb


def add_block(bm, corners, zb, zt, gap, rng, jit=0.004, wear_amp=0.005):
    """Jeden blok: 4 naroznikami w planie, wlasne z gorne i dolne w kazdym rogu."""
    cx = sum(c[0] for c in corners) / 4.0
    cy = sum(c[1] for c in corners) / 4.0
    quad = []
    for x, y in corners:
        d = Vector((x - cx, y - cy))
        if d.length > gap * 2:
            d -= d.normalized() * gap
        else:
            d *= 0.5
        quad.append((cx + d.x + rng.uniform(-jit, jit),
                     cy + d.y + rng.uniform(-jit, jit)))
    wear = rng.uniform(-wear_amp, wear_amp)  # kazdy blok siedzi odrobine inaczej
    bot = [bm.verts.new((x, y, z)) for (x, y), z in zip(quad, zb)]
    top = [bm.verts.new((x, y, z + wear)) for (x, y), z in zip(quad, zt)]
    bm.faces.new(bot[::-1])
    bm.faces.new(top)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bot[i], bot[j], top[j], top[i]))


def blockify(src_name, out_name, levels, rows, cols, gap=0.018, seed=7,
             y_min=None, guides=None, extra=None):
    guides = guides or {}
    rng = random.Random(seed)
    sample = make_sampler(src_name)
    src = bpy.data.objects[src_name]
    ws = [src.matrix_world @ v.co for v in src.data.vertices]
    x0, x1 = min(v.x for v in ws), max(v.x for v in ws)
    y0, y1 = min(v.y for v in ws), max(v.y for v in ws)
    if y_min is not None:
        y0 = max(y0, y_min)          # przod obslugiwany osobno (schody)

    # rzedy (spoiny wozkowe) wspolne dla wszystkich warstw, jak w prawdziwym murze
    ycuts = []
    y = y0
    while y < y1 - 0.05:
        nxt = y + rng.uniform(*rows)
        for g in guides.get('y', ()):  # przyciagnij koniec rzedu do progu bryly
            if y + 0.08 < g < nxt + 0.30:
                nxt = g
                break
        ycuts.append(y)
        y = nxt
    ycuts.append(y1)

    bm = bmesh.new()
    for r in range(len(ycuts) - 1):
        ya, yb = ycuts[r], ycuts[r + 1]
        lv = levels((ya + yb) * 0.5) if callable(levels) else levels
        # warstwy poziome (rdzen) + opcjonalny "skin": plyty rownolegle do gory,
        # inaczej pochyly dach wychodzi jako klinowe okruchy
        for pi in range(len(lv) - 1):
            lz0, lz1 = lv[pi], lv[pi + 1]
            # kazda warstwa przesunieta w y - inaczej spoiny ustawiaja sie w jedna
            # pionowa szczeline i widac przez plyte na wylot
            off = (pi % 3 - 1) * (yb - ya) * 0.34
            ry0, ry1 = max(y0, ya + off), min(y1, yb + off)
            if ry1 - ry0 < 0.05:
                continue
            # rozne rzedy = rozne wielkosci blokow (waskie / szerokie / mieszane)
            lo, hi = rng.choice((cols[0], cols[1], (cols[0][0], cols[1][1])))
            xcuts = partition(x0, x1, lo, hi, rng, lead=hi, forced=guides.get('x', ()))
            for c in range(len(xcuts) - 1):
                cx, cy = (xcuts[c] + xcuts[c + 1]) * 0.5, (ry0 + ry1) * 0.5
                if sample(cx, cy) is None:
                    continue
                xa, xb, ya2, yb2 = clip_rect(sample, xcuts[c], xcuts[c + 1], ry0, ry1)
                if xb - xa < 0.12 or yb2 - ya2 < 0.06:
                    continue
                corners = [(xa, ya2), (xb, ya2), (xb, yb2), (xa, yb2)]
                mid = sample(cx, cy)
                spans = [sample(px * 0.94 + cx * 0.06, py * 0.94 + cy * 0.06) or mid
                         for px, py in corners]
                zb = [min(max(s[0], lz0), lz1) for s in spans]
                zt = [min(max(s[1], lz0), lz1) for s in spans]
                if max(t - b for b, t in zip(zb, zt)) < 0.03:
                    continue
                add_block(bm, corners, zb, zt, gap + rng.uniform(0, 0.008), rng)

    if extra:
        extra(bm, rng)
    return finish(bm, out_name)


def finish(bm, out_name, bevel=0.014):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(out_name)
    bm.to_mesh(me)
    bm.free()

    old = bpy.data.objects.get(out_name)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    ob = bpy.data.objects.new(out_name, me)
    ob.location = OFFSET
    bpy.context.scene.collection.objects.link(ob)
    bev = ob.modifiers.new("Bevel", 'BEVEL')
    bev.width = bevel
    bev.segments = 2
    bev.limit_method = 'ANGLE'
    bev.angle_limit = 0.52
    return ob, len(me.polygons)


# ---------------------------------------------------------------- dach
# Prosty grecki szczyt: kamien tylko dookola (gzyms + tympanon + krokwie),
# a cala polac pokryta cegłowkami. Bez portykowego garbu z oryginalu.
X = 2.48          # polowa szerokosci
Y0, Y1 = -7.70, -0.44
Z_SOF = 2.344     # spod dachu (jak w oryginale)
Z_CORN = 2.62     # gora gzymsu
Z_APEX = 3.30     # kalenica konstrukcji (bez dachowek); spadek ~15 st. jak w referencji
SLOPE = (Z_APEX - Z_CORN) / X
CORN = 0.40       # szerokosc kamiennego pasa dookola
TILE = 0.065      # grubosc cegłowki


def zp(x):
    """Gorna plaszczyzna konstrukcji dachu - dwuspadowa, kalenica na x=0."""
    return Z_APEX - SLOPE * abs(x)


def build_dach(seed=5, name="Dach_grecki"):
    rng = random.Random(seed)
    bm = bmesh.new()
    dx = X - CORN                      # zasieg deskowania / dachowek

    def blk(corners, zb, zt, gap=0.012, **kw):
        if isinstance(zb, (int, float)):
            zb = [zb] * 4
        if isinstance(zt, (int, float)):
            zt = [zt] * 4
        add_block(bm, corners, zb, zt, gap, rng, **kw)

    # 1. deskowanie - jednolity klin pod dachowkami. Schowany w calosci pod
    #    warstwa blokow (2 cm nizej, wsuniety 5 cm), zeby nigdzie nie bylo
    #    scian w tej samej plaszczyznie ani szczelin na wylot.
    dk, ya, yb = X - 0.05, Y0 + 0.12, Y1 - 0.12   # za licem tympanonu (Y0+0.07)
    for sx in (-1, 1):
        blk([(0, ya), (sx * dk, ya), (sx * dk, yb), (0, yb)],
            Z_SOF + 0.04, [zp(0) - 0.02, zp(dk) - 0.02, zp(dk) - 0.02, zp(0) - 0.02],
            gap=0.0, jit=0.0, wear_amp=0.0)

    # 2. boki - kamienny pas przy okapie; gora idzie po polaci, wiec dachowki
    #    schodza na niego rowno, bez czarnej szczeliny
    for sx in (-1, 1):
        cuts = partition(Y0 + CORN, Y1 - CORN, 0.55, 1.05, rng, lead=0.9)
        xa, xb = sx * dx, sx * X
        for a, b in zip(cuts, cuts[1:]):
            blk([(xa, a), (xb, a), (xb, b), (xa, b)],
                Z_SOF, [zp(xa), zp(xb), zp(xb), zp(xa)])

    # 3. szczyty: belkowanie (dwie warstwy, gorna wysunieta) + tympanon
    for y_out, y_in in ((Y0, Y0 + CORN), (Y1, Y1 - CORN)):
        d = 1 if y_in > y_out else -1                    # kierunek do srodka bryly
        for z0, z1, ins in ((Z_SOF, 2.50, 0.07), (2.50, Z_CORN, 0.0)):
            ya = y_out + d * ins
            cuts = partition(-X, X, 0.55, 1.05, rng, lead=0.9)
            for a, b in zip(cuts, cuts[1:]):
                blk([(a, ya), (b, ya), (b, y_in), (a, y_in)], z0, z1)
        # tympanon cofniety, zeby gzyms i krokwie go ramowaly
        ya = y_out + d * 0.07
        z = Z_CORN
        while z < Z_APEX - 0.02:
            z1 = min(z + 0.13, Z_APEX)
            xm = min(X, (Z_APEX - z) / SLOPE)
            cuts = partition(-xm, xm, 0.40, 0.80, rng, lead=0.6)
            for a, b in zip(cuts, cuts[1:]):
                zt = [min(max(zp(v), z), z1) for v in (a, b, b, a)]
                if max(zt) - z < 0.03:
                    continue
                blk([(a, ya), (b, ya), (b, y_in), (a, y_in)], z, zt)
            z = z1

    # 4. dachowki - cala polac, rzedy wzdluz kalenicy, przewiazane
    y = Y0 + CORN
    r = 0
    while y < Y1 - CORN - 0.05:
        yb = min(Y1 - CORN, y + rng.uniform(0.26, 0.36))
        for sx in (-1, 1):
            cuts = partition(0.24, dx, 0.20, 0.32, rng, lead=0.25 * (r % 2))
            for a, b in zip(cuts, cuts[1:]):
                xa, xb = sx * a, sx * b
                blk([(xa, y), (xb, y), (xb, yb), (xa, yb)],
                    [zp(xa) - 0.05, zp(xb) - 0.05, zp(xb) - 0.05, zp(xa) - 0.05],
                    [zp(xa) + TILE, zp(xb) + TILE, zp(xb) + TILE, zp(xa) + TILE],
                    gap=0.008, jit=0.002, wear_amp=0.003)
        # gasior kalenicowy
        blk([(-0.28, y), (0.28, y), (0.28, yb), (-0.28, yb)],
            zp(0.28) - 0.05, Z_APEX + TILE, gap=0.008)
        y = yb
        r += 1

    # 5. gzyms ukosny - kamienny profil wienczacy trojkat, wystaje przed tympanon
    for y_out, y_in in ((Y0, Y0 + CORN), (Y1, Y1 - CORN)):
        d = 1 if y_in > y_out else -1
        ya = y_out - d * 0.06
        for sx in (-1, 1):
            cuts = partition(0.0, X, 0.45, 0.85, rng, lead=0.5)
            for a, b in zip(cuts, cuts[1:]):
                xa, xb = sx * a, sx * b
                blk([(xa, ya), (xb, ya), (xb, y_in), (xa, y_in)],
                    [zp(xa), zp(xb), zp(xb), zp(xa)],
                    [zp(xa) + 0.11, zp(xb) + 0.11, zp(xb) + 0.11, zp(xa) + 0.11])

    return finish(bm, name)


# schody z przodu: oryginal ma tam zwezajaca sie rampe, wiec zamiast ja
# odwzorowywac budujemy 3 proste prostokatne stopnie na calej szerokosci portyku
ST_X = (-1.165, 1.20)
ST_Y = (-7.99, -7.615)
ST_Z = (-0.192, 0.192)
ST_N = 3


def schody(bm, rng):
    depth = (ST_Y[1] - ST_Y[0]) / ST_N
    rise = (ST_Z[1] - ST_Z[0]) / ST_N
    for i in range(ST_N):
        z0, z1 = ST_Z[0] + i * rise, ST_Z[0] + (i + 1) * rise
        ya, yb = ST_Y[0] + i * depth, ST_Y[1]
        cuts = partition(ST_X[0], ST_X[1], 0.35, 0.60, rng, lead=0.45)
        for a, b in zip(cuts, cuts[1:]):
            add_block(bm, [(a, ya), (b, ya), (b, yb), (a, yb)],
                      [z0] * 4, [z1] * 4, 0.018 + rng.uniform(0, 0.008), rng)


def build():
    out = []
    out.append(blockify(
        "Cube", "Podloga_grecka",
        levels=[-0.192, 0.0, 0.192],
        rows=(0.45, 0.80), cols=((0.35, 0.60), (0.75, 1.20)),
        seed=11, y_min=ST_Y[1], extra=schody,
        guides={'x': (-1.165, 1.20), 'y': (-6.135,)}))
    out.append(build_dach(seed=5))
    return out


if __name__ == "__main__":
    for ob, n in build():
        print(ob.name, n, "faces")
