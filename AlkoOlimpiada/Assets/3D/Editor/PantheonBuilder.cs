using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PantheonBuilder
{
    const string HubScene = "Assets/Scenes/Hub.unity";
    const string ColumnPath = "Assets/3D/MapKit/filary/kolumna-full.glb";
    const string LongStonePath = "Assets/3D/MapKit/Kamienie/kamien-podluzny.glb";
    const string SquareStonePath = "Assets/3D/MapKit/Kamienie/kamien-kwadrat.glb";

    [MenuItem("AlkoOlimpiada/Zbuduj panteon za szatnią")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(HubScene, OpenSceneMode.Single);
        var wardrobe = GameObject.Find("Szatnia");
        var island = GameObject.Find("Island");
        var column = AssetDatabase.LoadAssetAtPath<GameObject>(ColumnPath);
        var longStone = AssetDatabase.LoadAssetAtPath<GameObject>(LongStonePath);
        var squareStone = AssetDatabase.LoadAssetAtPath<GameObject>(SquareStonePath);
        if (wardrobe == null || island == null || column == null || longStone == null || squareStone == null)
            throw new InvalidOperationException("Brak Szatni, Island albo jednego z modeli MapKit.");

        var old = GameObject.Find("Pantheon");
        if (old != null) UnityEngine.Object.DestroyImmediate(old);

        Vector3 outward = new Vector3(wardrobe.transform.position.x, 0f, wardrobe.transform.position.z).normalized;
        Vector3 center = wardrobe.transform.position + outward * 11f;
        Quaternion facingWardrobe = Quaternion.LookRotation(-outward, Vector3.up);
        var terrain = island.GetComponentsInChildren<Collider>();
        Physics.SyncTransforms();
        float ground = HighestGround(terrain, center, facingWardrobe);

        var root = new GameObject("Pantheon");

        for (int x = 0; x < 4; x++)
            for (int z = 0; z < 5; z++)
                Add(squareStone, root.transform, $"Podest_{x}_{z}",
                    new Vector3((x - 1.5f) * 2.05f, 0.225f, (z - 2f) * 1.65f),
                    new Vector3(2f, 0.45f, 1.6f));

        Add(longStone, root.transform, "Schod_dol", new Vector3(0f, 0.125f, 5.15f), new Vector3(8.3f, 0.25f, 0.9f));
        Add(longStone, root.transform, "Schod_srodek", new Vector3(0f, 0.25f, 4.45f), new Vector3(8.1f, 0.25f, 0.9f));
        Add(longStone, root.transform, "Schod_gora", new Vector3(0f, 0.375f, 3.75f), new Vector3(7.9f, 0.25f, 0.9f));

        float[] columnX = { -3f, -1f, 1f, 3f };
        foreach (float x in columnX)
        {
            Add(column, root.transform, $"Kolumna_przod_{x:+0;-0}",
                new Vector3(x, 3.35f, 2.9f), new Vector3(0.9f, 5.8f, 0.9f));
            Add(column, root.transform, $"Kolumna_tyl_{x:+0;-0}",
                new Vector3(x, 3.35f, -2.9f), new Vector3(0.9f, 5.8f, 0.9f));
        }

        for (int z = 0; z < 6; z++)
            Add(longStone, root.transform, $"Dach_{z}",
                new Vector3(0f, 6.48f, (z - 2.5f) * 1.28f), new Vector3(8.4f, 0.46f, 1.32f));

        AddGableRow(squareStone, root.transform, 5, 6.98f, 3.18f);
        AddGableRow(squareStone, root.transform, 3, 7.43f, 3.18f);
        AddGableRow(squareStone, root.transform, 1, 7.88f, 3.18f);
        Add(squareStone, root.transform, "Oltarz", new Vector3(0f, 1.05f, -0.4f), new Vector3(1.6f, 1.2f, 1.6f));

        root.transform.SetPositionAndRotation(new Vector3(center.x, ground + 0.04f, center.z), facingWardrobe);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        RenderPreview(root);
        Debug.Log($"[Pantheon] Zbudowany: pozycja={root.transform.position}, elementów={root.transform.childCount}");
    }

    static float HighestGround(Collider[] terrain, Vector3 center, Quaternion rotation)
    {
        float highest = float.MinValue;
        foreach (float x in new[] { -4.2f, 0f, 4.2f })
            foreach (float z in new[] { -4.2f, 0f, 4.2f })
            {
                Vector3 point = center + rotation * new Vector3(x, 0f, z);
                var ray = new Ray(new Vector3(point.x, 500f, point.z), Vector3.down);
                foreach (var collider in terrain)
                    if (collider.Raycast(ray, out var hit, 1000f)) highest = Mathf.Max(highest, hit.point.y);
            }
        return highest == float.MinValue ? center.y : highest;
    }

    static void AddGableRow(GameObject model, Transform parent, int count, float y, float z)
    {
        for (int i = 0; i < count; i++)
            Add(model, parent, $"Fronton_{count}_{i}",
                new Vector3((i - (count - 1) * 0.5f) * 1.3f, y, z), new Vector3(1.25f, 0.42f, 0.72f));
    }

    static GameObject Add(GameObject model, Transform parent, string name, Vector3 center, Vector3 size)
    {
        var go = PrefabUtility.InstantiatePrefab(model, parent.gameObject.scene) as GameObject;
        if (go == null) go = UnityEngine.Object.Instantiate(model);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        Bounds bounds = RendererBounds(go);
        go.transform.localScale = new Vector3(
            size.x / Mathf.Max(0.001f, bounds.size.x),
            size.y / Mathf.Max(0.001f, bounds.size.y),
            size.z / Mathf.Max(0.001f, bounds.size.z));
        bounds = RendererBounds(go);
        go.transform.position += center - bounds.center;

        bounds = RendererBounds(go);
        var colliderObject = new GameObject("Kolizja");
        colliderObject.transform.SetParent(go.transform, false);
        var box = colliderObject.AddComponent<BoxCollider>();
        box.center = colliderObject.transform.InverseTransformPoint(bounds.center);
        Vector3 scale = colliderObject.transform.lossyScale;
        box.size = new Vector3(
            bounds.size.x / Mathf.Abs(scale.x),
            bounds.size.y / Mathf.Abs(scale.y),
            bounds.size.z / Mathf.Abs(scale.z));
        return go;
    }

    static Bounds RendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new InvalidOperationException($"Model {go.name} nie ma Renderera.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void RenderPreview(GameObject root)
    {
        var cameraObject = new GameObject("__PantheonPreviewCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.fieldOfView = 52f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 300f;
        camera.transform.position = root.transform.TransformPoint(new Vector3(13f, 9f, 15f));
        camera.transform.LookAt(root.transform.TransformPoint(new Vector3(0f, 3.2f, 0f)));

        var target = new RenderTexture(960, 720, 24);
        var texture = new Texture2D(960, 720, TextureFormat.RGB24, false);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        texture.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
        texture.Apply();

        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "PantheonPreview.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, texture.EncodeToPNG());

        RenderTexture.active = null;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(target);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        Debug.Log("[Pantheon] Podgląd: " + path);
    }
}
