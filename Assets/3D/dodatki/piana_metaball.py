# Buduje piane z metaballi i doklada ja do gotowej cieczy w ciecze.blend.
#
# Uzycie:
#   blender.exe --background "...\ciecze.blend" --python "...\piana_metaball.py" -- --zapisz
# Bez --zapisz tylko liczy i raportuje, nie dotyka pliku.
#
# Piane zawsze buduje OD ZERA: najpierw kasuje to, co siedzi w slocie materialu "Piana",
# wiec mozna puszczac wielokrotnie i regulowac ponizsze pokretla.
import bpy, bmesh, math, random, sys
from mathutils import Vector

R_WZGL      = 0.105   # promien babla w ulamku glebokosci naczynia - steruje gruboscia warstwy
ZAPAS_POZA  = 1.15    # jak daleko poza promien tafli sypac bable (potem i tak docisniete do scianki)
WYPLYW      = 1.10    # maksymalny promien piany nad krawedzia, w ulamku promienia tafli
DECIMATE    = 0.45    # redukcja siatki po konwersji metaballi
ROZRZUT_Z   = 0.22    # nierownosc gory, w ulamku promienia babla
ZIARNO      = 7

# (ciecz, naczynie, wysokosc krawedzi naczynia w przestrzeni modelu)
ZESTAWY = [("Piwo_Kufel", "tripo_node_8e5343e8-97d8-4d3c-acd2-beec52eede3d", 0.4598),
           ("Piwo_Kubek", "Cylinder", 0.1002)]


def znajdz_lc(lc, nazwa):
    if lc.collection.name == nazwa:
        return lc
    for d in lc.children:
        w = znajdz_lc(d, nazwa)
        if w:
            return w
    return None


def odsloniete(nazwy):
    """Operatory Blendera nie ruszaja obiektow w ukrytych kolekcjach, a ray_cast nie ma
    na czym pracowac - wiec na czas roboty wszystko musi byc widoczne."""
    stan = []
    for n in nazwy:
        kol = bpy.data.collections.get(n)
        lc = znajdz_lc(bpy.context.view_layer.layer_collection, n)
        if kol and lc:
            stan.append((kol, lc, kol.hide_viewport, lc.hide_viewport))
            kol.hide_viewport = False
            lc.hide_viewport = False
    bpy.context.view_layer.update()
    return stan


def parametry(ciecz):
    """Mierzy piwo BEZ piany - piana to slot 1, wiec liczy sie tylko reszta."""
    me = ciecz.data
    w_piwa = {v for p in me.polygons if p.material_index != 1 for v in p.vertices}
    co = [me.vertices[i].co for i in w_piwa]
    z_tafla = max(c.z for c in co)
    dno_z = min(c.z for c in co)
    g = [c for c in co if c.z > z_tafla - 1e-4]
    cx = sum(c.x for c in g) / len(g)
    cy = sum(c.y for c in g) / len(g)
    r_tafli = max(math.hypot(c.x - cx, c.y - cy) for c in g)
    d = [c for c in co if c.z < dno_z + 1e-4]
    r_dno = max(math.hypot(c.x - cx, c.y - cy) for c in d)
    return cx, cy, dno_z, z_tafla, r_tafli, r_dno


def skasuj_piane(ciecz):
    me = ciecz.data
    if not any(p.material_index == 1 for p in me.polygons):
        return 0
    przed = len(me.polygons)
    for x in bpy.data.objects:
        x.select_set(False)
    ciecz.select_set(True)
    bpy.context.view_layer.objects.active = ciecz
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.mesh.select_mode(type='FACE')
    ciecz.active_material_index = 1
    bpy.ops.object.material_slot_select()
    bpy.ops.mesh.delete(type='FACE')
    bpy.ops.object.mode_set(mode='OBJECT')
    return przed - len(me.polygons)


def zrob_piane(ciecz, naczynie, z_krawedz):
    cx, cy, dno_z, z_tafla, r_tafli, r_dno = parametry(ciecz)
    R = (z_krawedz - dno_z) * R_WZGL
    z_srodek = z_tafla + R * 0.60

    random.seed(ZIARNO)
    dane = bpy.data.metaballs.new("PianaMB")
    dane.resolution = R * 0.28
    dane.render_resolution = R * 0.28
    mb = bpy.data.objects.new("PianaMB_tmp", dane)
    bpy.context.scene.collection.objects.link(mb)
    krok = R * 0.85
    p = 0
    while p * krok <= r_tafli * ZAPAS_POZA + 1e-9:
        r = p * krok
        ile = 1 if p == 0 else max(6, int(round(2 * math.pi * r / krok)))
        for k in range(ile):
            a = 2 * math.pi * (k + (0.5 if p % 2 else 0)) / ile
            el = dane.elements.new()
            el.co = (cx + r * math.cos(a), cy + r * math.sin(a),
                     z_srodek + random.uniform(-ROZRZUT_Z, ROZRZUT_Z) * R)
            el.radius = R * random.uniform(0.85, 1.15)
        p += 1
    ile_el = len(dane.elements)

    for x in bpy.data.objects:
        x.select_set(False)
    mb.select_set(True)
    bpy.context.view_layer.objects.active = mb
    bpy.ops.object.convert(target='MESH')
    piana = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    # odetnij to, co tonie w piwie
    z_ciecia = z_tafla - (z_tafla - dno_z) * 0.02
    bm = bmesh.new()
    bm.from_mesh(piana.data)
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts) + list(bm.edges) + list(bm.faces),
                           plane_co=(0, 0, z_ciecia), plane_no=(0, 0, 1), clear_inner=True)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if len(e.link_faces) == 1])
    bm.to_mesh(piana.data)
    bm.free()
    piana.data.update()

    m = piana.modifiers.new("Decimate", 'DECIMATE')
    m.decimate_type = 'COLLAPSE'
    m.ratio = DECIMATE
    bpy.ops.object.modifier_apply(modifier=m.name)

    # Docisniecie do scianki. Ponizej krawedzi liczy sie realny promien wnetrza mierzony
    # raycastem - kufel zweza sie przy wylocie i sam promien tafli by nie wystarczyl.
    pamiec = {}

    def r_wewn(z):
        k = round(z, 4)
        if k not in pamiec:
            d = []
            for s in range(64):
                a = 2 * math.pi * s / 64
                ok, loc, nor, idx = naczynie.ray_cast(
                    Vector((cx, cy, z)), Vector((math.cos(a), math.sin(a), 0)), distance=100)
                if ok:
                    d.append((loc - Vector((cx, cy, z))).length)
            pamiec[k] = min(d) if len(d) > 48 else None
        return pamiec[k]

    dociagniete = 0
    for v in piana.data.vertices:
        q = v.co
        r = math.hypot(q.x - cx, q.y - cy)
        if r < 1e-9:
            continue
        if q.z <= z_krawedz:
            rw = r_wewn(q.z)
            limit = (rw * 0.97) if rw else r_tafli
        else:
            limit = r_tafli * WYPLYW
        if r > limit:
            f = limit / r
            v.co.x = (q.x - cx) * f + cx
            v.co.y = (q.y - cy) * f + cy
            dociagniete += 1

    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.shade_smooth()

    n_piwa = len(ciecz.data.vertices)
    for x in bpy.data.objects:
        x.select_set(False)
    piana.select_set(True)
    ciecz.select_set(True)
    bpy.context.view_layer.objects.active = ciecz
    bpy.ops.object.join()

    me = ciecz.data
    nowe = list(range(n_piwa, len(me.vertices)))
    for p in me.polygons:
        if all(v >= n_piwa for v in p.vertices):
            p.material_index = 1
    grp = ciecz.vertex_groups.get("Poziom_kosc") or ciecz.vertex_groups.new(name="Poziom_kosc")
    grp.add(nowe, 1.0, 'REPLACE')
    # Bez tego join wpisalby pianie te same pozycje w kazdym kluczu i przy Poziom=1
    # wisialaby w powietrzu nad pustym naczyniem.
    kb = me.shape_keys.key_blocks
    sk = r_dno / max(r_tafli, 1e-9)
    for i in nowe:
        q = kb["Basis"].data[i].co
        kb["Poziom"].data[i].co = ((q.x - cx) * sk + cx, (q.y - cy) * sk + cy, dno_z)
    while len(me.materials) > 2:
        me.materials.pop()
    me.update()

    zs = [kb["Basis"].data[i].co.z for i in nowe]
    return {"metaballi": ile_el, "R": round(R, 4), "dociagnietych": dociagniete,
            "wierzcholkow_piany": len(nowe), "trojkaty": len(me.polygons),
            "piana_z": [round(min(zs), 4), round(max(zs), 4)],
            "wnika_pod_tafle": round(z_tafla - min(zs), 4),
            "ponad_krawedz": round(max(zs) - z_krawedz, 4)}


def sprawdz(ciecz, naczynie, z_krawedz):
    """Piana nie moze przebijac scianki i ma siegac brzegu po CALYM obwodzie."""
    me = ciecz.data
    wp = {v for p in me.polygons if p.material_index == 1 for v in p.vertices}
    wp -= {v for p in me.polygons if p.material_index != 1 for v in p.vertices}
    cx, cy, dno_z, z_tafla, r_tafli, r_dno = parametry(ciecz)
    przebic = 0
    for i in wp:
        q = me.vertices[i].co
        if q.z > z_krawedz:
            continue
        r = math.hypot(q.x - cx, q.y - cy)
        if r < 1e-9:
            continue
        d = Vector((q.x - cx, q.y - cy, 0)).normalized()
        ok, loc, nor, idx = naczynie.ray_cast(Vector((cx, cy, q.z)), d, distance=100)
        if ok and (loc - Vector((cx, cy, q.z))).length - r < 0:
            przebic += 1
    zasieg = []
    for s in range(36):
        a = 2 * math.pi * s / 36
        kier = Vector((math.cos(a), math.sin(a)))
        best = 0.0
        for i in wp:
            q = me.vertices[i].co
            v = Vector((q.x - cx, q.y - cy))
            if v.length > 1e-9 and v.normalized().dot(kier) > 0.985:
                best = max(best, v.length)
        zasieg.append(best / r_tafli)
    return {"przebic": przebic, "zasieg_min": round(min(zasieg), 3),
            "zasieg_sredni": round(sum(zasieg) / len(zasieg), 3)}


stan = odsloniete(["kubek-czerwony", "kieliszek", "Kufel"])
raport = {}
for ciecz_n, nacz_n, z_kraw in ZESTAWY:
    ciecz = bpy.data.objects[ciecz_n]
    nacz = bpy.data.objects[nacz_n]
    usuniete = skasuj_piane(ciecz)
    r = zrob_piane(ciecz, nacz, z_kraw)
    r["skasowano_starej_piany_scian"] = usuniete
    r.update(sprawdz(ciecz, nacz, z_kraw))
    raport[ciecz_n] = r

for kol, lc, a, b in stan:
    kol.hide_viewport = a
    lc.hide_viewport = b

print("PIANA_START")
for k, v in raport.items():
    print(" ", k, v)
print("PIANA_KONIEC")

if "--zapisz" in sys.argv:
    bpy.ops.wm.save_mainfile()
    print("zapisano", bpy.data.filepath)
