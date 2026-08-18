# Eksportuje RECZNIE poprawione ciecze z ciecze.blend do GLB - do zrodla i do projektu Unity.
#
# Uzycie (Blender musi byc zamkniety na tym pliku albo miec zapisane zmiany):
#   blender.exe --background "...\Assets\3D\dodatki\ciecze.blend" --python "...\eksport_ciecze.py"
#
# Rozne od ciecze_generator.py: generator buduje ciecze OD ZERA i nadpisuje GLB, kasujac
# reczne rzezbienie. Ten skrypt bierze to, co jest w ciecze.blend, i tylko wypuszcza na zewnatrz.
import bpy, os

DOD = r"C:\Users\xarad\Alko-Olimpiada-Game\Assets\3D\dodatki"
UNITY = r"C:\Users\xarad\Alko-Olimpiada-Game\AlkoOlimpiada\Assets\3D\MapKit\dodatki"

# obiekt mesha -> nazwa pliku. Szkielet dolaczany automatycznie, bo bez niego Unity
# nie zrobi dzialajacego morphu (SkinnedMeshRenderer bez kosci rysuje mesh statycznie).
CIECZE = ["Piwo_Kufel", "Piwo_Kubek", "Wodka_Kieliszek", "Kufel_szklo"]

raport = []
for nazwa in CIECZE:
    ob = bpy.data.objects.get(nazwa)
    if ob is None:
        raport.append(nazwa + ": BRAK OBIEKTU, pomijam")
        continue

    # kolekcja moze byc wylaczona w widoku - eksport jej nie widzi, wiec wlaczam na czas pracy
    schowane = [k for k in bpy.context.scene.collection.children if k.hide_viewport]
    for k in schowane:
        k.hide_viewport = False
    ob.hide_select = False

    # Nazwa mesha musi byc stabilna miedzy eksportami. Import glTF dokleja ".001" i po
    # kolejnym round-tripie Unity nie odnajduje starego mesha - instancje w scenie zostaja
    # z pustym MeshFilterem i obiekt znika z widoku.
    ob.data.name = nazwa

    # Odznaczam przez API, nie operatorem: select_all(DESELECT) pomija obiekty w ukrytych
    # kolekcjach, wiec zaznaczenie z poprzedniej iteracji zostawalo i eksport kieliszka
    # zabieral ze soba kubek.
    for x in bpy.data.objects:
        x.select_set(False)
    ob.select_set(True)
    if ob.parent is not None and ob.parent.type == 'ARMATURE':
        ob.parent.select_set(True)
    bpy.context.view_layer.objects.active = ob

    for katalog in (DOD, UNITY):
        bpy.ops.export_scene.gltf(filepath=os.path.join(katalog, nazwa + ".glb"),
                                  export_format='GLB', use_selection=True,
                                  export_morph=True, export_apply=False, export_yup=True)

    for k in schowane:
        k.hide_viewport = True

    klucze = [x.name for x in ob.data.shape_keys.key_blocks] if ob.data.shape_keys else []
    raport.append("%s: %d tris, klucze=%s" % (nazwa, len(ob.data.polygons), klucze))

print("EKSPORT_START")
for r in raport:
    print(" ", r)
print("EKSPORT_KONIEC  (w Unity: Assets > Refresh)")
