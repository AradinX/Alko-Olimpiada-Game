using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AkropolBuilder
{
    const string FloorPath = "Assets/3D/MapKit/dodatki/podloga-scalone.glb";
    const string RoofPath = "Assets/3D/MapKit/dodatki/dach-scalone.glb";
    const string ColumnPath = "Assets/3D/MapKit/filary/kolumna-grecka.glb";

    // Skala calej budowli. 1 = tak jak wymodelowane w Blenderze (6.6 x 10.7 m, kolumny 2.15 m).
    // 3.45 daje kolumny wielkosci tych, ktore postawiles recznie (7.4 m) - wtedy podloga ma 23 x 37 m.
    const float Scale = 3.45f;

    // Przeswit miedzy podloga a spodem dachu, w skali modelu. Wyznacza wysokosc kolumn,
    // a dach siada na ich szczycie. Scalone GLB maja pivot kazdy we wlasnej podstawie,
    // wiec wysokosci nie da sie juz odczytac z roznicy pivotow tak jak przy FBX.
    const float ColumnClearance = 2.14f;

    // glTFast rozpakowuje tekstury osadzone w GLB do ARGB32 i nie da sie tego ustawic - jeden
    // akropol kosztowal 395 MB. Dlatego GLB jada bez obrazkow, a materialy podmieniamy tutaj
    // na assety projektu (DXT1, 1024), po nazwie materialu z Blendera.
    static readonly string[,] MaterialMap = {
        { "tripo_material_0df606d5_bake", "Akropol_Kolumna" },
        { "tripo_material_0df606d5-1f56-4577-b9d6-9fc6ad1b5528", "Akropol_Kolumna" },
        { "Podloga_grecka_Images2_v10_Mat", "Akropol_Podloga" },
        { "Schody_greckie_Images2_v10_Mat", "Akropol_Schody" },
        { "Dach_grecki_Images2_v10_Mat", "Akropol_DachKamien" },
        { "Dachowki_greckie_Images2_v10_Mat", "Akropol_Dachowki" },
        { "Tympanon_frontowy_Images2_v10_Mat", "Akropol_FreskPrzod" },
        { "Tympanon_tylny_Images2_v10_Mat", "Akropol_FreskTyl" },
        { "Obramowanie_frontu_dachu_Images2_v10_Mat", "Akropol_Obramowanie" },
        { "Obramowanie_tylu_dachu_Images2_v10_Mat", "Akropol_Obramowanie" },
    };

    const string MaterialFolder = "Assets/3D/MapKit/Materialy/";

    // Static batching sklada meshe w jeden bufor i przy >64k wierzcholkow psuje geometrie
    // (dach ma 422k) - w edytorze wyglada dobrze, rozjezdza sie dopiero po wejsciu w Play.
    const int BatchingVertexLimit = 64000;

    // Naroza sa wspolne dla frontu/tylu i bokow, wiec 3 + 4 + 2x5 daje 13 kolumn, nie 17.
    const int ColumnsFront = 3;
    const int ColumnsBack = 4;
    const int ColumnsSide = 5;

    // Front to strona z tympanonem frontowym. W glTF Blenderowe -Y wychodzi jako +Z.
    // Jesli po zbudowaniu 3 kolumny stoja od zlej strony, przestaw na false.
    const bool FrontOnPlusZ = true;

    // Miejsce budowy - srodek prostokata z twoich recznie postawionych kolumn.
    static readonly Vector2 SiteXZ = new Vector2(10.35f, -35.1f);

    [MenuItem("AlkoOlimpiada/Zbuduj akropol")]
    public static void Build()
    {
        var floorModel = Load(FloorPath);
        var roofModel = Load(RoofPath);
        var columnModel = Load(ColumnPath);

        var scene = SceneManager.GetActiveScene();
        var old = GameObject.Find("Akropol");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);

        var root = new GameObject("Akropol");
        var floor = Spawn(floorModel, root.transform, "Podloga");
        var roof = Spawn(roofModel, root.transform, "Dach");

        // Pivot scalonych GLB wedruje przy kazdym re-eksporcie z Blendera, wiec zamiast mu ufac
        // sadzamy oba elementy srodkiem bryly na osi rootu - i tak maja byc wspolsrodkowe.
        CenterXZ(floor);
        CenterXZ(roof);

        // Kolumny maja zadana wysokosc, a dach siada na ich szczycie.
        float floorTop = RendererBounds(floor).max.y;
        float roofBottom = floorTop + ColumnClearance;
        roof.transform.position += Vector3.up * (roofBottom - RendererBounds(roof).min.y);
        Bounds roofBounds = RendererBounds(roof);

        Bounds columnBounds = RendererBounds(Spawn(columnModel, root.transform, "__pomiar"));
        UnityEngine.Object.DestroyImmediate(root.transform.Find("__pomiar").gameObject);
        float columnScale = ColumnClearance / columnBounds.size.y;
        float columnRadius = columnBounds.size.x * columnScale * 0.5f;

        // Ring kolumn tuz pod krawedzia dachu.
        float ringX = roofBounds.extents.x - columnRadius - 0.1f;
        float ringZ = roofBounds.extents.z - columnRadius - 0.1f;
        float columnY = floorTop + ColumnClearance * 0.5f;

        int placed = 0;
        foreach (Vector2 spot in ColumnSpots(ringX, ringZ))
        {
            var column = Spawn(columnModel, root.transform, $"Kolumna_{placed:00}");
            column.transform.localScale *= columnScale;
            column.transform.position += new Vector3(spot.x, columnY, spot.y) - RendererBounds(column).center;
            AddBoxCollider(column);
            placed++;
        }

        // Podloga potrzebuje MeshCollidera, zeby dalo sie wejsc po schodach; dach jest poza zasiegiem gracza.
        // ponytail: dach bez collidera, dodac dopiero jakby gracz mial na niego wchodzic
        floor.AddComponent<MeshCollider>();

        float ground = HighestGround(SiteXZ, roofBounds.extents.x * Scale, roofBounds.extents.z * Scale);
        root.transform.localScale = Vector3.one * Scale;
        root.transform.position = new Vector3(SiteXZ.x, ground, SiteXZ.y);
        foreach (var t in root.GetComponentsInChildren<Transform>())
        {
            var flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic;
            var mf = t.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null || mf.sharedMesh.vertexCount < BatchingVertexLimit)
                flags |= StaticEditorFlags.BatchingStatic;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, flags);
        }

        ApplyMaterials(root);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[Akropol] Zbudowany na ({SiteXZ.x}, {ground:F2}, {SiteXZ.y}), skala {Scale}, kolumn {placed} " +
                  $"(wys. {ColumnClearance * Scale:F2} m, skala modelu {columnScale * Scale:F2}). Scena NIE jest zapisana.");
    }

    // Front, tyl i oba boki maja rozna liczbe kolumn, ale naroza dziela - stad odsiew duplikatow.
    static List<Vector2> ColumnSpots(float ringX, float ringZ)
    {
        var spots = new List<Vector2>();
        void Add(float x, float z)
        {
            var p = new Vector2(x, z);
            foreach (var s in spots)
                if ((s - p).sqrMagnitude < 0.0001f) return;
            spots.Add(p);
        }

        float frontZ = FrontOnPlusZ ? ringZ : -ringZ;
        for (int i = 0; i < ColumnsFront; i++)
            Add(Mathf.Lerp(-ringX, ringX, i / (float)(ColumnsFront - 1)), frontZ);
        for (int i = 0; i < ColumnsBack; i++)
            Add(Mathf.Lerp(-ringX, ringX, i / (float)(ColumnsBack - 1)), -frontZ);
        for (int i = 0; i < ColumnsSide; i++)
        {
            float z = Mathf.Lerp(-ringZ, ringZ, i / (float)(ColumnsSide - 1));
            Add(-ringX, z);
            Add(ringX, z);
        }
        return spots;
    }

    static GameObject Load(string path)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (model == null) throw new InvalidOperationException("Brak modelu: " + path);
        return model;
    }

    static GameObject Spawn(GameObject model, Transform parent, string name)
    {
        var go = PrefabUtility.InstantiatePrefab(model, parent.gameObject.scene) as GameObject
                 ?? UnityEngine.Object.Instantiate(model);
        go.name = name;
        // Transform prefabu zostaje nietkniety. GLB wchodzi w metrach 1:1, a podloga i dach
        // maja pivot w srodku wlasnej podstawy - w XZ pokrywaja sie, wysokosc dokladamy w Build().
        go.transform.SetParent(parent, false);
        return go;
    }

    static void ApplyMaterials(GameObject root)
    {
        var lookup = new Dictionary<string, Material>();
        for (int i = 0; i < MaterialMap.GetLength(0); i++)
        {
            string asset = MaterialMap[i, 1];
            if (!lookup.ContainsKey(asset))
            {
                var loaded = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + asset + ".mat");
                if (loaded == null) throw new InvalidOperationException("Brak materialu: " + MaterialFolder + asset + ".mat");
                lookup[asset] = loaded;
            }
            lookup[MaterialMap[i, 0]] = lookup[asset];
        }

        int swapped = 0, missed = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>())
        {
            var mats = renderer.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material replacement;
                if (mats[i] != null && lookup.TryGetValue(mats[i].name, out replacement)) { mats[i] = replacement; swapped++; }
                else { Debug.LogWarning($"[Akropol] Brak mapowania dla materialu '{(mats[i] == null ? "null" : mats[i].name)}'"); missed++; }
            }
            renderer.sharedMaterials = mats;
        }
        Debug.Log($"[Akropol] Materialy podmienione: {swapped}, bez mapowania: {missed}.");
    }

    // Root jest jeszcze w zerze i bez skali, wiec swiat = lokalne.
    static void CenterXZ(GameObject go)
    {
        Bounds b = RendererBounds(go);
        go.transform.position -= new Vector3(b.center.x, 0f, b.center.z);
    }

    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException($"Model {go.name} nie ma Renderera.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void AddBoxCollider(GameObject go)
    {
        Bounds bounds = RendererBounds(go);
        var box = go.AddComponent<BoxCollider>();
        box.center = go.transform.InverseTransformPoint(bounds.center);
        Vector3 scale = go.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Abs(scale.x),
            bounds.size.y / Mathf.Abs(scale.y),
            bounds.size.z / Mathf.Abs(scale.z));
    }

    static float HighestGround(Vector2 xz, float halfX, float halfZ)
    {
        var terrain = new System.Collections.Generic.List<Collider>();
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name.StartsWith("Island")) terrain.AddRange(go.GetComponentsInChildren<Collider>());
        Physics.SyncTransforms();

        float highest = float.MinValue;
        foreach (float x in new[] { -halfX, 0f, halfX })
            foreach (float z in new[] { -halfZ, 0f, halfZ })
            {
                var ray = new Ray(new Vector3(xz.x + x, 500f, xz.y + z), Vector3.down);
                foreach (var collider in terrain)
                    if (collider.Raycast(ray, out var hit, 1000f)) highest = Mathf.Max(highest, hit.point.y);
            }
        if (highest == float.MinValue) throw new InvalidOperationException($"Brak gruntu pod ({xz.x}, {xz.y}) - to woda albo poza wyspa.");
        return highest;
    }
}
