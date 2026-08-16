"""Zamienia mur dachowek na plyty i prostuje gzyms na dlugich bokach dachu.

Problem 1 - dachowki: byly tysiacem osobnych klockow z unikalnym, zapieczonym
UV. Po decimate 0.12 to UV sie rozjechalo - na scianach bocznych klockow
tekstura szla "srodkiem" bryly, a z daleka calosc wygladala jak zgnieciona.
Kazdy klocek mial tez wlasny losowy obrot, wiec kierunek rysunku byl przypadkowy.

Problem 2 - obramowanie: gzyms i belkowanie na dlugich bokach to ten sam mur z
klockow. Decimate zjadl pojedyncze bloki i zostaly wneki (jeden siega tylko
x=2.09 zamiast 2.48 - metrowa dziura w scianie) oraz zapadniete wierzchy
(2.62 zamiast 2.73). To sa te "pare miejsc".

Rozwiazanie: jedna zagieta plyta kamienna (kalenica = zagiecie) i na to nalozona
plyta dachowek, wcieta o `INSET`, zeby dolna zostawila widoczny rant. Do tego
licowka na oba dlugie boki, cofnieta o `GZYMS_LUZ` za lico dobrych blokow -
wneki znikaja, a fugi miedzy dobrymi blokami zostaja widoczne jako relief.

UV liczone planarnie z normalnej KAZDEJ sciany, w metrach modelu - nie da sie
zgniesc, nie zalezy od obrotu obiektu i nie ma szwow cube_project na spadku.

Uruchomienie: otworz "Koloseum razem.blend" i wykonaj ten plik w Blenderze.
Skrypt jest jednorazowy - po nim slot dachowek ma juz plyty, nie mur.
"""

import bpy
import bmesh
from mathutils import Vector

OBJ = "Dachowki_greckie"
MAT_DACHOWKI = "Dachowki_greckie_Images2_v10_Mat"
MAT_KAMIEN = "Dach_grecki_Images2_v10_Mat"
MAT_OBRAM = ("Obramowanie_frontu_dachu_Images2_v10_Mat",
             "Obramowanie_tylu_dachu_Images2_v10_Mat")

SCALE = 3.45                 # AkropolBuilder.Scale - modele licza sie w metrach gry
TILE_DACHOWKI = 3.0 / SCALE  # jedno powtorzenie tekstury = 3 m w grze (~7 dachowek)
TILE_KAMIEN = 2.0 / SCALE    # tak jak reszta kamienia, patrz cube_project w pipeline
INSET = 0.05                 # o ile plyta dachowek jest mniejsza od kamiennej
TILES_SHARE = 0.35           # jaka czesc grubosci powloki zajmuje plyta dachowek
GZYMS_LUZ = 0.02             # o ile licowka chowa sie za lico dobrych blokow
GZYMS_GLEB = 0.48            # grubosc licowki - musi wypelnic najglebsza wneke
RAKE_ZAPAS = 0.05            # o ile gzyms szczytowy stoi nad najwyzszym blokiem muru
RAKE_GRUB = 0.25             # wysokosc pasa gzymsu - musi siegnac pod najglebszy wrab
RAKE_WYSTAJ = 0.01           # o ile gzyms wysuniety przed lico szczytu


def shell_profile(me, slot):
    """Mierzy powloke dachu: (x_okap, z_kalenicy, spadek, grubosc pionowa, y0, y1).

    Gorna powierzchnia to z = z_kalenicy - spadek * |x|; dopasowanie najmniejszych
    kwadratow po kubelkach |x|, zeby jeden odstajacy klocek nie przekrzywil dachu.
    """
    xs, ys = [], []
    bins = {}
    for p in me.polygons:
        if p.material_index != slot:
            continue
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            xs.append(abs(v.x))
            ys.append(v.y)
            k = round(abs(v.x) * 20)
            b = bins.setdefault(k, [-1e9, 1e9])
            b[0] = max(b[0], v.z)
            b[1] = min(b[1], v.z)
    if not xs:
        raise RuntimeError("Slot %d nie ma juz scian - skrypt juz raz poszedl?" % slot)

    x_eave = max(xs)
    # skraje odpadaja: przy kalenicy siedzi gasior, przy okapie ucieta krawedz
    fit = [(k / 20.0, b[0]) for k, b in bins.items() if 0.2 < k / 20.0 < x_eave - 0.1]
    n = len(fit)
    sx = sum(x for x, _ in fit)
    sz = sum(z for _, z in fit)
    sxx = sum(x * x for x, _ in fit)
    sxz = sum(x * z for x, z in fit)
    slope = -(n * sxz - sx * sz) / (n * sxx - sx * sx)
    z_ridge = (sz + slope * sx) / n

    thick = sorted(b[0] - b[1] for b in bins.values() if b[0] - b[1] > 1e-4)
    return x_eave, z_ridge, slope, thick[len(thick) // 2], min(ys), max(ys)


def bent_plate(bm, x_eave, z_ridge, slope, thickness, y0, y1, mat_idx):
    """Zagieta w kalenicy plyta: dwa spadki jako jedna zamknieta bryla.

    Osobne plyty na kazdy spadek dawalyby dwie pokrywajace sie sciany w kalenicy
    (z-fighting), stad jedna bryla z zagieciem.
    """
    top = [[bm.verts.new((x, y, z_ridge - slope * abs(x)))
            for y in (y0, y1)]
           for x in (-x_eave, 0.0, x_eave)]
    bot = [[bm.verts.new((v.co.x, v.co.y, v.co.z - thickness)) for v in col] for col in top]

    faces = []
    for i in (0, 1):                                        # gora i dol, po dwa spadki
        faces.append(bm.faces.new((top[i][0], top[i][1], top[i + 1][1], top[i + 1][0])))
        faces.append(bm.faces.new((bot[i][0], bot[i][1], bot[i + 1][1], bot[i + 1][0])))
    for i in (0, 2):                                        # czola okapow
        faces.append(bm.faces.new((top[i][0], top[i][1], bot[i][1], bot[i][0])))
    for j in (0, 1):                                        # szczyty pod tympanonami
        faces.append(bm.faces.new((top[0][j], top[1][j], bot[1][j], bot[0][j])))
        faces.append(bm.faces.new((top[1][j], top[2][j], bot[2][j], bot[1][j])))

    for f in faces:
        f.material_index = mat_idx
    return faces


def side_span(me):
    """Zasieg licowki: (x_zewnetrzny, z_dolu, y_od, y_do).

    x mierzony tylko na srodku dlugosci - przy szczytach sciana schodzi sie do
    tympanonu i zaniza wynik. y konczy sie na licach obu obramowan, zeby licowka
    ich nie polknela; jej czola chowaja sie wtedy w scianach szczytowych.
    """
    names = [m.name if m else "" for m in me.materials]
    slot_front, slot_back = (names.index(n) for n in MAT_OBRAM)

    x_out, z_bot = 0.0, 1e9
    y_lo, y_hi = 1e9, -1e9
    y_front, y_back = -1e9, 1e9
    for p in me.polygons:
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            if p.material_index == 1:
                z_bot = min(z_bot, v.z)
                y_lo, y_hi = min(y_lo, v.y), max(y_hi, v.y)
                if -6.5 < v.y < 0.2:
                    x_out = max(x_out, abs(v.x))
            elif p.material_index == slot_front:
                y_front = max(y_front, v.y)
            elif p.material_index == slot_back:
                y_back = min(y_back, v.y)
    return x_out, z_bot, y_front, y_back, y_lo, y_hi


def gable_profile(me, lo, hi):
    """(z_szczytu, spadek) gornej krawedzi sciany szczytowej w pasie y (lo, hi).

    Krawedz faluje o +-0.05 do -0.18 wokol prostej - to sa te poszarpane bloki.
    Prosta bierze sie z najmniejszych kwadratow po kubelkach |x|, wiec wrab
    ciagnie ja w dol tylko o tyle, ile wazy jeden kubelek.
    """
    bins = {}
    for p in me.polygons:
        if p.material_index != 1:
            continue
        for li in p.loop_indices:
            v = me.vertices[me.loops[li].vertex_index].co
            if lo < v.y < hi:
                k = round(abs(v.x) * 8) / 8
                bins[k] = max(bins.get(k, -9.0), v.z)
    fit = [(x, z) for x, z in bins.items() if 0.25 < x < 2.3]
    n = len(fit)
    sx = sum(x for x, _ in fit)
    sz = sum(z for _, z in fit)
    sxx = sum(x * x for x, _ in fit)
    sxz = sum(x * z for x, z in fit)
    slope = -(n * sxz - sx * sz) / (n * sxx - sx * sx)
    return (sz + slope * sx) / n, slope


def facing(bm, x_out, z_bot, z_top, y0, y1, mat_idx):
    """Licowka na oba dlugie boki - prostopadloscian wtopiony w mur od zewnatrz."""
    faces = []
    for sgn in (-1, 1):
        xa, xb = sgn * (x_out - GZYMS_LUZ), sgn * (x_out - GZYMS_LUZ - GZYMS_GLEB)
        corners = [(xa, y0), (xa, y1), (xb, y1), (xb, y0)]
        top = [bm.verts.new((x, y, z_top)) for x, y in corners]
        bot = [bm.verts.new((x, y, z_bot)) for x, y in corners]
        faces.append(bm.faces.new(top))
        faces.append(bm.faces.new(bot))
        for i in range(4):
            j = (i + 1) % 4
            faces.append(bm.faces.new((top[i], top[j], bot[j], bot[i])))
    for f in faces:
        f.material_index = mat_idx
    return faces


def retexture():
    """Dachowki na kafel zamiast bake'a - bake byl w 90% plaska pomarancza."""
    base = bpy.path.abspath("//GeneratedTextures/")
    nt = bpy.data.materials[MAT_DACHOWKI].node_tree

    def load(rel, colorspace):
        img = bpy.data.images.load(base + rel, check_existing=True)
        img.colorspace_settings.name = colorspace
        return img

    nt.nodes["BaseColor_v10"].image = load("Akropo/Akropo_Dachowki_Source_v10.png", 'sRGB')
    nt.nodes["Normal_v10"].image = load("Koloseum_v2/Tiled/Dachowki_Normal_tile.png", 'Non-Color')
    rough = nt.nodes["Principled BSDF"].inputs["Roughness"]
    for link in list(rough.links):        # zapieczony roughness byl na starym UV
        nt.links.remove(link)
    rough.default_value = 0.65


def planar_uv(faces, uv_layer, tile):
    """UV z normalnej kazdej sciany - rzut na jej wlasna plaszczyzne, w skali modelu.

    cube_project rzuca wzdluz osi swiata, wiec na spadku dachu sciska rysunek o
    cos(kata). Rzut na plaszczyzne sciany nie sciska nic, a kierunek bierze sie z
    geometrii, nie z obrotu obiektu.
    """
    y_axis, x_axis = Vector((0, 1, 0)), Vector((1, 0, 0))
    for f in faces:
        n = f.normal.normalized()
        t = y_axis - n * n.dot(y_axis)
        if t.length < 1e-4:                     # sciana prostopadla do kalenicy
            t = x_axis - n * n.dot(x_axis)
        t.normalize()
        b = n.cross(t)
        for loop in f.loops:
            p = loop.vert.co
            loop[uv_layer].uv = (p.dot(t) / tile, p.dot(b) / tile)


def main():
    ob = bpy.data.objects[OBJ]
    me = ob.data
    names = [s.material.name if s.material else "" for s in ob.material_slots]
    slot_tiles = names.index(MAT_DACHOWKI)
    slot_stone = names.index(MAT_KAMIEN)

    x_eave, z_ridge, slope, thickness, y0, y1 = shell_profile(me, slot_tiles)
    t_tiles = thickness * TILES_SHARE
    t_stone = thickness - t_tiles
    x_out, z_bot, y_front, y_back, y_lo, y_hi = side_span(me)
    # gora licowki = gora plyty kamiennej na okapie, wiec spadek dachu przechodzi
    # w plaski gzyms bez uskoku i bez dwoch scian w jednej plaszczyznie
    z_gzyms = z_ridge - t_tiles - slope * x_eave

    bm = bmesh.new()
    bm.from_mesh(me)
    bm.faces.ensure_lookup_table()
    uv_layer = bm.loops.layers.uv.verify()

    old = [f for f in bm.faces if f.material_index == slot_tiles]
    bmesh.ops.delete(bm, geom=old, context='FACES')
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context='VERTS')

    stone = bent_plate(bm, x_eave, z_ridge - t_tiles, slope, t_stone, y0, y1, slot_stone)
    tiles = bent_plate(bm, x_eave - INSET, z_ridge, slope, t_tiles,
                       y0 + INSET, y1 - INSET, slot_tiles)
    # ponytail: mur pod licowka zostaje. Bloki siedza w 162 wielkich shellach,
    # wiec nie da sie wyciac samego gzymsu bez dziurawienia scian szczytowych.
    lico = facing(bm, x_out, z_bot - 0.01, z_gzyms, y_front, y_back, slot_stone)

    # Gzyms szczytowy: pas wzdluz obu skosow frontonu, przykrywa poszarpana
    # korone muru. Stoi nad dachowkami - tak jak geison stoi nad tympanonem.
    rake = []
    for y_out, y_in in ((y_lo, y0 + INSET), (y_hi, y1 - INSET)):
        wystaj = -RAKE_WYSTAJ if y_out < y_in else RAKE_WYSTAJ
        apex, k = gable_profile(me, min(y_out, y_in) - 0.2, max(y_out, y_in) + 0.2)
        rake += bent_plate(bm, x_out, apex + RAKE_ZAPAS, k, RAKE_GRUB,
                           y_out + wystaj, y_in, slot_stone)

    bmesh.ops.recalc_face_normals(bm, faces=stone + tiles + lico + rake)
    bm.normal_update()
    planar_uv(stone + lico + rake, uv_layer, TILE_KAMIEN)
    planar_uv(tiles, uv_layer, TILE_DACHOWKI)

    bm.to_mesh(me)
    bm.free()
    me.update()
    retexture()

    print("[dachowka] okap x=%.3f kalenica z=%.3f spadek=%.3f (%.1f st.) "
          "grubosc=%.3f (kamien %.3f + dachowki %.3f), y %.2f..%.2f"
          % (x_eave, z_ridge, slope, __import__("math").degrees(__import__("math").atan(slope)),
             thickness, t_stone, t_tiles, y0, y1))
    print("[dachowka] scian po zamianie: %d (bylo o %d wiecej)"
          % (len(me.polygons), len(old) - len(stone) - len(tiles) - len(lico)))
    print("[gzyms] licowka x=%.3f (lico blokow %.3f), z %.3f..%.3f, y %.3f..%.3f"
          % (x_out - GZYMS_LUZ, x_out, z_bot - 0.01, z_gzyms, y_front, y_back))


if __name__ == "__main__":
    main()
