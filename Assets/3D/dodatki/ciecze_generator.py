# Buduje ciecz (piwo z pianka / wodke) dopasowana do wnetrza naczynia.
# Wnetrze mierzone raycastami, wiec dziala na dowolnym meshu z Tripo, bez recznego modelowania.
# Kazda ciecz dostaje shape key "Poziom": 0 = pelne, 1 = puste (Unity: SetBlendShapeWeight).
import bpy, bmesh, math, os, statistics
from mathutils import Vector, noise

ZRODLA = r"C:\Users\xarad\Alko-Olimpiada-Game\AlkoOlimpiada\Assets\3D\MapKit\dodatki"
WYJSCIE = r"C:\Users\xarad\Alko-Olimpiada-Game\Assets\3D\dodatki"

NSEG = 56          # segmenty na obwodzie cieczy
NZ = 14            # pierscienie w pionie
NCAP = 3           # pierscienie na tafli - zeby shader fal mial co przesuwac
NPIANA = 16        # pierscienie czapy piany - im wiecej, tym drobniejszy relief da sie wyrzezbic
PIANA_PONAD = 0.16 # o ile piana wystaje ponad krawedz, w ulamku glebokosci jamy
PIANA_WYPLYW = 0.07# rozlanie piany na boki tuz nad krawedzia
PIANA_RELIEF = 0.50   # sila pofalowania i babli, w ulamku wysokosci czapy
# Gestosc babli. Uwaga: przy NSEG=56 odstep punktow na obwodzie to ~1/9 promienia,
# wiec powyzej ~4 bable robia sie mniejsze od oczka siatki i uśredniaja sie w nic.
PIANA_BABLE = 3.4
KIERUNKI = 256     # promieni na pomiar promienia wewnetrznego (duzo, bo naczynia sa wielokatne)
LUZ = 0.02         # margines od scianki, w ulamku promienia


def czysc():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def wczytaj(sciezka):
    bpy.ops.import_scene.gltf(filepath=sciezka)
    obs = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if len(obs) != 1:
        raise RuntimeError("oczekiwano 1 mesha w %s, jest %d" % (sciezka, len(obs)))
    ob = obs[0]
    # do przestrzeni korzenia modelu - w Unity ciecz wejdzie jako dziecko na (0,0,0)
    for o in bpy.context.scene.objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    return ob


def zmierz_wnetrze(ob):
    """Zwraca (cx, cy, z_dno, z_krawedz, profil). Profil to [(z, promien_wewnetrzny), ...]."""
    bb = [Vector(v) for v in ob.bound_box]
    x0 = min(v.x for v in bb); x1 = max(v.x for v in bb)
    y0 = min(v.y for v in bb); y1 = max(v.y for v in bb)
    z0 = min(v.z for v in bb); z1 = max(v.z for v in bb)
    h = z1 - z0

    # --- 1. mapa pierwszych trafien z gory ---
    N = 56
    mapa = {}
    for i in range(N):
        for j in range(N):
            x = x0 + (x1 - x0) * (i + 0.5) / N
            y = y0 + (y1 - y0) * (j + 0.5) / N
            ok, loc, nor, idx = ob.ray_cast(Vector((x, y, z1 + h)), Vector((0, 0, -1)))
            if ok:
                mapa[(i, j)] = (loc.z, x, y)
    if not mapa:
        raise RuntimeError("zaden promien nie trafil w mesh")
    z_gora = max(v[0] for v in mapa.values())

    # --- 2. otwor = NAJWIEKSZY spojny obszar niskich trafien.
    # Flood fill, bo samo kryterium wysokosci lapie tez ucho kufla. A z kilku klastrow trzeba
    # brac najwiekszy, nie ten przy najglebszym punkcie: najglebsze trafienie potrafi wypasc
    # w szczelinie miedzy uchem a korpusem, i wtedy "jama" to szczelina szerokosci 9 mm.
    prog = z_gora - 0.15 * h
    nisko = {k for k, v in mapa.items() if v[0] < prog}
    if not nisko:
        raise RuntimeError("nie znaleziono otworu")
    zostalo, klastry = set(nisko), []
    while zostalo:
        stos, kl = [zostalo.pop()], set()
        while stos:
            k = stos.pop()
            if k in kl:
                continue
            kl.add(k)
            i, j = k
            for s in ((i+1, j), (i-1, j), (i, j+1), (i, j-1)):
                if s in zostalo:
                    zostalo.discard(s); stos.append(s)
        klastry.append(kl)
    otwor = max(klastry, key=len)
    if len(otwor) < 16:
        raise RuntimeError("otwor za maly (%d komorek)" % len(otwor))

    cx = sum(mapa[k][1] for k in otwor) / len(otwor)
    cy = sum(mapa[k][2] for k in otwor) / len(otwor)

    # dno: mediana z SRODKA otworu. Komorki przy brzegu klastra trafiaja juz w scianke,
    # wiec liczone razem z nimi dno wychodzilo wyzej niz jest naprawde.
    R = max(math.hypot(mapa[k][1] - cx, mapa[k][2] - cy) for k in otwor)
    srodek = [mapa[k][0] for k in otwor if math.hypot(mapa[k][1] - cx, mapa[k][2] - cy) < 0.5 * R]
    z_dno = statistics.median(srodek if len(srodek) >= 4 else [mapa[k][0] for k in otwor])

    # krawedz: najwyzsze trafienie w komorkach stykajacych sie z otworem
    obrys = []
    for (i, j) in otwor:
        for s in ((i+1, j), (i-1, j), (i, j+1), (i, j-1)):
            if s in mapa and s not in otwor:
                obrys.append(mapa[s][0])
    z_krawedz = max(obrys) if obrys else z_gora

    # --- 3. promien wewnetrzny na kolejnych wysokosciach ---
    profil = []
    KROKI = 48
    for k in range(KROKI + 1):
        z = z_dno + (z_krawedz - z_dno) * k / KROKI
        z = min(max(z, z_dno + 1e-4), z_krawedz - 1e-4)
        dist = []
        for s in range(KIERUNKI):
            a = 2 * math.pi * s / KIERUNKI
            ok, loc, nor, idx = ob.ray_cast(Vector((cx, cy, z)), Vector((math.cos(a), math.sin(a), 0)), distance=h * 3)
            if ok:
                dist.append((loc - Vector((cx, cy, z))).length)
        if len(dist) < KIERUNKI * 0.9:
            break
        profil.append((z, min(dist)))
    if len(profil) < 4:
        raise RuntimeError("nie udalo sie zmierzyc jamy (%d pierscieni)" % len(profil))
    # Zdrowy rozsadek: jama plytsza niz 20% naczynia albo wezsza niz 10% szerokosci
    # znaczy, ze pomiar poszedl w szczeline albo w artefakt - lepiej wysypac sie glosno.
    glebokosc = profil[-1][0] - z_dno
    if glebokosc < 0.20 * h:
        raise RuntimeError("jama za plytka: %.4f przy wysokosci %.4f" % (glebokosc, h))
    if profil[0][1] < 0.10 * max(x1 - x0, y1 - y0):
        raise RuntimeError("jama za waska: promien %.4f" % profil[0][1])
    return cx, cy, z_dno, profil[-1][0], profil


def promien_bezpieczny(profil, z, polowa_kroku):
    """Najciasniejsze miejsce scianki w otoczeniu z. Sama interpolacja przestrzeliwuje tam,
    gdzie scianka zweza sie miedzy probkami profilu - np. na zaokragleniu przy dnie kufla."""
    r = promien_na(profil, z)
    for zp, rp in profil:
        if abs(zp - z) <= polowa_kroku:
            r = min(r, rp)
    return min(r, promien_na(profil, z - polowa_kroku), promien_na(profil, z + polowa_kroku))


def promien_na(profil, z):
    if z <= profil[0][0]:
        return profil[0][1]
    if z >= profil[-1][0]:
        return profil[-1][1]
    for i in range(len(profil) - 1):
        za, ra = profil[i]; zb, rb = profil[i + 1]
        if za <= z <= zb:
            t = (z - za) / max(zb - za, 1e-9)
            return ra + (rb - ra) * t
    return profil[-1][1]


def szum_xy(x, y, r_odn, skala):
    """Szum w plaszczyznie, znormalizowany promieniem naczynia - ten sam wzor plam
    niezaleznie od tego, czy naczynie ma 4 cm czy 40 cm."""
    return noise.noise(Vector((x / max(r_odn, 1e-9) * skala, y / max(r_odn, 1e-9) * skala, 0.5)))


def relief_piany(x, y, r_odn):
    """Powierzchnia piany: duze fale + drobne babelki. Zwraca 0..1.
    Same fale daja gladki kopczyk, wiec babelki dokladam z voronoia - jego komorki po
    odwroceniu wygladaja jak zlepek okraglych banieczek, a nie jak pomiete plotno."""
    u, v = x / max(r_odn, 1e-9), y / max(r_odn, 1e-9)
    fale = (0.55 * noise.noise(Vector((u * 2.4, v * 2.4, 0.5)))
            + 0.30 * noise.noise(Vector((u * 5.3, v * 5.3, 1.7)))
            + 0.15 * noise.noise(Vector((u * 10.5, v * 10.5, 3.1))))
    try:
        d = noise.voronoi(Vector((u * PIANA_BABLE, v * PIANA_BABLE, 0.0)))[0][0]
        bable = max(0.0, 1.0 - d * 1.5) ** 0.65
    except Exception:
        # gdyby API voronoi sie zmienilo - zostaja same fale, model dalej sie zbuduje
        bable = 0.5 + 0.5 * noise.noise(Vector((u * PIANA_BABLE, v * PIANA_BABLE, 0.0)))
    return 0.5 + 0.5 * fale * 0.9 + (bable - 0.35) * 0.55


def material(nazwa, kolor, alpha, rough, transmission=0.0):
    m = bpy.data.materials.new(nazwa)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (kolor[0], kolor[1], kolor[2], 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Alpha"].default_value = alpha
    if "Transmission Weight" in b.inputs:
        b.inputs["Transmission Weight"].default_value = transmission
    if hasattr(m, "surface_render_method") and alpha < 1.0:
        m.surface_render_method = 'BLENDED'
    return m


def zbuduj_ciecz(nazwa, cx, cy, z_dno, z_tafla, z_krawedz, profil, mat_plyn, mat_tafla, kopula_piany):
    """Bryla obrotowa wypelniajaca jame + shape key opuszczajacy poziom.
    Przy kopula_piany=True tafla plynu dostaje material plynu, a nad nia siada
    osobna czapa piany z dziurami - przez dziury widac wlasnie ta tafle."""
    me = bpy.data.meshes.new(nazwa)
    ob = bpy.data.objects.new(nazwa, me)
    bpy.context.scene.collection.objects.link(ob)

    dno_z = z_dno + (z_tafla - z_dno) * 0.01     # mikroskopijnie nad dnem, przeciw z-fightingowi
    krok = (z_tafla - dno_z) / NZ
    r_dno = promien_bezpieczny(profil, dno_z, krok) * (1 - LUZ)

    verts, verts_puste, faces, mat_id = [], [], [], []

    def dodaj(pelne, puste):
        verts.append(pelne); verts_puste.append(puste)
        return len(verts) - 1

    # --- sciana boczna ---
    pierscienie = []
    for i in range(NZ + 1):
        z = dno_z + (z_tafla - dno_z) * i / NZ
        r = promien_bezpieczny(profil, z, krok) * (1 - LUZ)
        idxs = []
        for s in range(NSEG):
            a = 2 * math.pi * s / NSEG
            ca, sa = math.cos(a), math.sin(a)
            # stan pusty: ten sam pierscien zsuniety na dno i sciagniety do promienia dna
            idxs.append(dodaj((cx + r * ca, cy + r * sa, z),
                              (cx + r_dno * ca, cy + r_dno * sa, dno_z)))
        pierscienie.append(idxs)

    for i in range(NZ):
        a_r, b_r = pierscienie[i], pierscienie[i + 1]
        for s in range(NSEG):
            s2 = (s + 1) % NSEG
            faces.append((a_r[s], a_r[s2], b_r[s2], b_r[s])); mat_id.append(0)

    # --- dno ---
    srodek_dna = dodaj((cx, cy, dno_z), (cx, cy, dno_z))
    for s in range(NSEG):
        s2 = (s + 1) % NSEG
        faces.append((pierscienie[0][s2], pierscienie[0][s], srodek_dna)); mat_id.append(0)

    # --- tafla: pierscienie koncentryczne, zeby shader mial na czym robic fale ---
    r_top = promien_bezpieczny(profil, z_tafla, krok) * (1 - LUZ)
    cap = [pierscienie[NZ]]
    for c in range(1, NCAP + 1):
        f = 1.0 - c / (NCAP + 1.0)
        idxs = []
        for s in range(NSEG):
            a = 2 * math.pi * s / NSEG
            ca, sa = math.cos(a), math.sin(a)
            idxs.append(dodaj((cx + r_top * f * ca, cy + r_top * f * sa, z_tafla),
                              (cx + r_dno * f * ca, cy + r_dno * f * sa, dno_z)))
        cap.append(idxs)
    mat_tafli = 0 if kopula_piany else 1   # pod piana tafla ma byc piwem, bo to ja widac przez dziury
    for c in range(NCAP):
        a_r, b_r = cap[c], cap[c + 1]
        for s in range(NSEG):
            s2 = (s + 1) % NSEG
            faces.append((a_r[s], b_r[s], b_r[s2], a_r[s2])); mat_id.append(mat_tafli)
    srodek_tafli = dodaj((cx, cy, z_tafla), (cx, cy, dno_z))
    for s in range(NSEG):
        s2 = (s + 1) % NSEG
        faces.append((cap[NCAP][s], srodek_tafli, cap[NCAP][s2])); mat_id.append(mat_tafli)

    # --- czapa piany: siada na tafli, wystaje ponad krawedz naczynia, dziurawa ---
    if kopula_piany:
        glebokosc = z_krawedz - z_dno
        z_szczyt = z_krawedz + glebokosc * PIANA_PONAD
        r_kraw = promien_bezpieczny(profil, z_krawedz, krok) * (1 - LUZ)
        piana, piana_r = [], []
        for i in range(NPIANA + 1):
            z_baza = z_tafla + (z_szczyt - z_tafla) * i / NPIANA
            if z_baza <= z_krawedz:
                # ponizej krawedzi piana wciaz musi sie miescic w sciance
                r_baza = promien_bezpieczny(profil, z_baza, krok) * (1 - LUZ)
            else:
                # powyzej krawedzi nie ma juz scianki - piana wybrzusza sie i zamyka w czubku
                v = (z_baza - z_krawedz) / max(z_szczyt - z_krawedz, 1e-9)
                # 1-v^4 zamiast 1-v^2: kopula ma szeroki, prawie plaski wierzch zamiast szpica.
                # Szpic zbiegal wszystkie pierscienie w jeden punkt i nie bylo gdzie rzezbic piany.
                r_baza = r_kraw * (1.0 + PIANA_WYPLYW * math.sin(math.pi * v)) * math.sqrt(max(1.0 - v ** 4, 0.0))
            idxs = []
            for s in range(NSEG):
                a = 2 * math.pi * s / NSEG
                ca, sa = math.cos(a), math.sin(a)
                # relief tylko nad krawedzia, nizej wypchnalby piane w scianke naczynia.
                # Tuz nad krawedzia jeszcze wygaszony, zeby piana nie odstawala od brzegu.
                gr = 0.0
                if z_baza > z_krawedz:
                    v_rel = (z_baza - z_krawedz) / max(z_szczyt - z_krawedz, 1e-9)
                    # Rosnie od krawedzi i trzyma pelna sile na calym wierzchu. Gasnie dopiero
                    # na ostatnim pierscieniu, ktory i tak zbiega w punkt - inaczej robil sie
                    # tam wieniec ostrych zebow.
                    waga = min(1.0, v_rel * 2.5) * (1.0 - v_rel ** 8 * 0.9)
                    gr = ((relief_piany(r_baza * ca, r_baza * sa, r_kraw) - 0.5) * 2.0
                          * (z_szczyt - z_krawedz) * PIANA_RELIEF * waga)
                idxs.append(dodaj((cx + r_baza * ca, cy + r_baza * sa, z_baza + gr),
                                  (cx + r_dno * (r_baza / max(r_kraw, 1e-9)) * ca,
                                   cy + r_dno * (r_baza / max(r_kraw, 1e-9)) * sa, dno_z)))
            piana.append(idxs)
            piana_r.append(r_baza)

        for i in range(NPIANA):
            a_r, b_r = piana[i], piana[i + 1]
            for s in range(NSEG):
                s2 = (s + 1) % NSEG
                faces.append((a_r[s], b_r[s], b_r[s2], a_r[s2])); mat_id.append(1)

    me.from_pydata(verts, [], faces)
    me.update()
    me.materials.append(mat_plyn)
    me.materials.append(mat_tafla)
    for p, mid in zip(me.polygons, mat_id):
        p.material_index = mid
    # Kolejnosc wierzcholkow z pydata zostawia czesc scian odwrocona (tafla wychodzila czarna).
    # Materialy przypisane wyzej, przed bmeshem, bo bmesh niesie material_index przy sobie,
    # a poleganie na kolejnosci polygonow po to_mesh() bylo by zakladem.
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    if kopula_piany:
        # Czapa piany jest otwarta powloka (ma dziury), wiec recalc nie ma z czego wyliczyc
        # "na zewnatrz" i czesc scian zostawia odwrocona. Kopula jest wypukla wzgledem srodka
        # tafli, wiec orientacje wymuszam wprost.
        srodek = Vector((cx, cy, z_tafla))
        odwrotne = [f for f in bm.faces
                    if f.material_index == 1 and (f.calc_center_median() - srodek).dot(f.normal) < 0]
        if odwrotne:
            bmesh.ops.reverse_faces(bm, faces=odwrotne)
    # Reguly niezalezne od tego, ktora to sciana: kazda pozioma sciana na poziomie tafli lub
    # wyzej patrzy w gore, kazda przy dnie patrzy w dol. Kopula jest wypukla, wiec nie ma
    # poziomych scian skierowanych inaczej, a to lapie tez tafle plynu.
    zle = []
    for f in bm.faces:
        if abs(f.normal.z) < 0.9:
            continue
        c = f.calc_center_median().z
        if c > z_tafla - 1e-5 and f.normal.z < 0:
            zle.append(f)
        elif c < dno_z + 1e-5 and f.normal.z > 0:
            zle.append(f)
    if zle:
        bmesh.ops.reverse_faces(bm, faces=zle)
    bm.to_mesh(me); bm.free()
    me.update()
    me.shade_smooth()

    uv = me.uv_layers.new(name="UVMap")
    for poly in me.polygons:
        plaska_gora = poly.center.z > z_tafla - 1e-5
        for li in poly.loop_indices:
            x, y, z = me.vertices[me.loops[li].vertex_index].co
            if plaska_gora:
                uv.data[li].uv = (0.5 + (x - cx) / max(2 * r_top, 1e-9),
                                  0.5 + (y - cy) / max(2 * r_top, 1e-9))
            else:
                a = math.atan2(y - cy, x - cx)
                uv.data[li].uv = (a / (2 * math.pi) + 0.5,
                                  (z - dno_z) / max(z_tafla - dno_z, 1e-9))

    # shape key indeksuje po numerze wierzcholka - upewniam sie, ze bmesh niczego nie przestawil
    for i, p in enumerate(verts):
        if (Vector(p) - me.vertices[i].co).length > 1e-5:
            raise RuntimeError("bmesh przestawil wierzcholki, shape key bylby przypisany na slepo")

    ob.shape_key_add(name="Basis", from_mix=False)
    kk = ob.shape_key_add(name="Poziom", from_mix=False)
    for i, p in enumerate(verts_puste):
        kk.data[i].co = p
    kk.value = 0.0

    # Jedna kosc, do ktorej przypiete sa wszystkie wierzcholki. Bez skinu glTFast robi w Unity
    # SkinnedMeshRenderer bez kosci - taki renderer rysuje mesh statycznie i NIGDY nie stosuje
    # morphu, wiec shape key jest w danych, ale poziom nie schodzi. Kosc niczego nie animuje,
    # jest tylko po to, zeby Unity poszlo sciezka skinningu.
    dane = bpy.data.armatures.new(nazwa + "_szkielet")
    szkielet = bpy.data.objects.new(nazwa + "_szkielet", dane)
    bpy.context.scene.collection.objects.link(szkielet)
    bpy.context.view_layer.objects.active = szkielet
    bpy.ops.object.mode_set(mode='EDIT')
    kosc = dane.edit_bones.new("Poziom_kosc")
    kosc.head = (cx, cy, dno_z)
    kosc.tail = (cx, cy, z_tafla)
    bpy.ops.object.mode_set(mode='OBJECT')

    ob.parent = szkielet
    grupa = ob.vertex_groups.new(name="Poziom_kosc")
    grupa.add(list(range(len(me.vertices))), 1.0, 'REPLACE')
    mod = ob.modifiers.new("Szkielet", 'ARMATURE')
    mod.object = szkielet
    return ob, szkielet


def skontroluj(ciecz, naczynie, cx, cy, z_dno, z_krawedz):
    """Twardy test: kazdy wierzcholek cieczy musi lezec wewnatrz scianki i w zakresie jamy."""
    bledy = []
    najciasniej = 1e9
    # tafla musi patrzec w gore, dno w dol - odwrocone normalne renderowaly sie na czarno
    for p in ciecz.data.polygons:
        if p.center.z > z_krawedz - 1e-6:
            continue
        gora = p.center.z > (z_dno + z_krawedz) * 0.5
        if abs(p.normal.z) > 0.9:
            oczekiwana = 1 if gora else -1
            if p.normal.z * oczekiwana < 0:
                bledy.append("odwrocona normalna na z=%.4f (n.z=%.2f)" % (p.center.z, p.normal.z))
    for v in ciecz.data.vertices:
        x, y, z = v.co
        if z > z_krawedz:
            continue   # czapa piany ma wystawac ponad krawedz, tam nie ma juz czego przebijac
        if z < z_dno - 1e-4:
            bledy.append("wierzcholek pod dnem: z=%.4f (dno %.4f)" % (z, z_dno))
            continue
        r = math.hypot(x - cx, y - cy)
        if r < 1e-6:
            continue
        d = Vector((x - cx, y - cy, 0)).normalized()
        ok, loc, nor, idx = naczynie.ray_cast(Vector((cx, cy, z)), d, distance=1e4)
        if not ok:
            bledy.append("brak scianki na z=%.4f" % z)
            continue
        zapas = (loc - Vector((cx, cy, z))).length - r
        najciasniej = min(najciasniej, zapas)
        if zapas < 0:
            bledy.append("przebicie scianki o %.5f na z=%.4f" % (-zapas, z))
    return najciasniej, bledy[:5], len(bledy)


NACZYNIA = [
    # (plik glb, nazwa, typ, wypelnienie jamy 0..1)
    # przy piwie tafla siega niemal krawedzi, bo cala piana siedzi juz ponad nia
    ("Kufel.glb",          "Piwo_Kufel",      "piwo",  0.97),
    ("kubek-czerwony.glb", "Piwo_Kubek",      "piwo",  0.97),
    ("kieliszek.glb",      "Wodka_Kieliszek", "wodka", 0.80),
]

raport = []
for plik, nazwa, typ, wype in NACZYNIA:
    czysc()
    naczynie = wczytaj(os.path.join(ZRODLA, plik))
    cx, cy, z_dno, z_kraw, profil = zmierz_wnetrze(naczynie)
    z_tafla = z_dno + (z_kraw - z_dno) * wype

    if typ == "piwo":
        mp = material("Piwo",  (0.85, 0.42, 0.04), 0.94, 0.16, transmission=0.30)
        mt = material("Piana", (0.98, 0.95, 0.86), 1.00, 0.85)
    else:
        mp = material("Wodka",       (0.86, 0.92, 0.98), 0.30, 0.03, transmission=1.0)
        mt = material("Wodka_Tafla", (0.90, 0.95, 1.00), 0.45, 0.02, transmission=0.9)

    ciecz, szkielet = zbuduj_ciecz(nazwa, cx, cy, z_dno, z_tafla, z_kraw, profil, mp, mt, typ == "piwo")
    zapas, przyklady, ile_bledow = skontroluj(ciecz, naczynie, cx, cy, z_dno, z_kraw)

    bpy.ops.object.select_all(action='DESELECT')
    ciecz.select_set(True)
    szkielet.select_set(True)
    bpy.context.view_layer.objects.active = ciecz
    bpy.ops.export_scene.gltf(filepath=os.path.join(WYJSCIE, nazwa + ".glb"),
                              export_format='GLB', use_selection=True,
                              export_morph=True, export_apply=False, export_yup=True)
    raport.append({
        "plik": nazwa + ".glb",
        "os": [round(cx, 4), round(cy, 4)],
        "jama_z": [round(z_dno, 4), round(z_kraw, 4)],
        "tafla_z": round(z_tafla, 4),
        "promien_dol_gora": [round(profil[0][1], 4), round(promien_na(profil, z_tafla), 4)],
        "najmniejszy_zapas_do_scianki": round(zapas, 5),
        "bledy": ile_bledow, "przyklady": przyklady,
        "tris": len(ciecz.data.polygons),
    })

# --- szklany kufel: ta sama geometria, material szkla, bez tekstury ---
czysc()
kufel = wczytaj(os.path.join(ZRODLA, "Kufel.glb"))
kufel.name = "Kufel_szklo"
kufel.data.materials.clear()
kufel.data.materials.append(material("Szklo", (0.92, 0.96, 0.97), 0.18, 0.03, transmission=1.0))
bpy.ops.object.select_all(action='DESELECT')
kufel.select_set(True)
bpy.context.view_layer.objects.active = kufel
bpy.ops.export_scene.gltf(filepath=os.path.join(WYJSCIE, "Kufel_szklo.glb"),
                          export_format='GLB', use_selection=True, export_apply=False, export_yup=True)
raport.append({"plik": "Kufel_szklo.glb", "tris": len(kufel.data.polygons)})

# kontrola, czy relief ma realna amplitude i czy voronoi nie wpadl w fallback
probki = [relief_piany(math.cos(k * 0.37) * k * 0.01, math.sin(k * 0.53) * k * 0.011, 0.28)
          for k in range(200)]
try:
    noise.voronoi(Vector((0.1, 0.2, 0.0)))[0][0]
    zrodlo = "voronoi"
except Exception as e:
    zrodlo = "FALLBACK (" + type(e).__name__ + ")"
print("RELIEF: min=%.3f max=%.3f zrodlo babli=%s" % (min(probki), max(probki), zrodlo))

print("RAPORT_START")
for r in raport:
    print(r)
print("RAPORT_KONIEC")
