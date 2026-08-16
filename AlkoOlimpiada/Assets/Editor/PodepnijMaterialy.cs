using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// ponytail: jednorazowy skrypt. Tekstury lodki i wagi wyjechaly z GLB do MapKit/Tekstury
// (kompresja BC/DXT), wiec glTFast robi teraz gole materialy - ten skrypt podmienia je na
// Lodka.mat / Waga.mat w otwartych scenach. Po zapisaniu sceny mozna go skasowac.
public static class PodepnijMaterialy
{
    const string LodkaMat = "tripo_material_7695c2cb-eb27-4c21-ae7c-04f6482aad4f";
    const string WagaMat = "tripo_material_545a1f22-fa26-4195-9a15-1b8de6d5dc7d";

    [MenuItem("Tools/Podepnij materialy lodki i wagi")]
    static void Run()
    {
        var lodka = AssetDatabase.LoadAssetAtPath<Material>("Assets/3D/MapKit/Materialy/Lodka.mat");
        var waga = AssetDatabase.LoadAssetAtPath<Material>("Assets/3D/MapKit/Materialy/Waga.mat");
        if (lodka == null || waga == null)
        {
            Debug.LogError("Brak Lodka.mat albo Waga.mat w Assets/3D/MapKit/Materialy/");
            return;
        }

        int zmienione = 0;
        var renderery = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var r in renderery)
        {
            var mats = r.sharedMaterials;
            bool trafiony = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (mats[i].name.StartsWith(LodkaMat)) { mats[i] = lodka; trafiony = true; }
                else if (mats[i].name.StartsWith(WagaMat)) { mats[i] = waga; trafiony = true; }
            }
            if (!trafiony) continue;
            r.sharedMaterials = mats;
            EditorUtility.SetDirty(r);
            zmienione++;
        }

        if (zmienione > 0) EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"Podpieto materialy na {zmienione} rendererach. Zapisz scene (Ctrl+S).");
    }
}
