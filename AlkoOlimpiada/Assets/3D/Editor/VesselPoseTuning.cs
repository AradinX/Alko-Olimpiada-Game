using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Ręczne ustawianie naczyń w garści — jak BottleGripBaker, tylko że dla pozy
// samego naczynia, osobno na stojąco i na szczycie łyku.
//
// UŻYCIE
//   1. Alko/Scena strojenia naczyń — buduje Naczynia_Tuning.unity: dwa rzędy
//      postaci w pozach 1:1 z hubem. Ciało bierze klip Idle (to gra Animator przy
//      Speed = 0), a prawa ręka BeerIdle: rząd STOJAC w klatce 0, rząd PIJAC na
//      końcu klipu, czyli w szczycie animacji picia (DrinkPose = 1).
//      Chwyt HandBottle jest już doklejony do kości, tak jak wyjdzie w grze.
//   2. Ruszaj SAMO NACZYNIE (Butelka/Kufel/Puszka pod HandBottle) — nie postać
//      i nie HandBottle.
//   3. Zaznacz je i odpal Alko/Naczynie: zapisz pozę stania (rząd STOJAC) albo
//      Alko/Naczynie: zapisz pozę picia (rząd PIJAC). Wartość ląduje od razu
//      w Player.prefab.
//   4. Powtórz dla każdego naczynia. Scena jest jednorazowa — można ją kasować
//      i budować od nowa.
public static class VesselPoseTuning
{
    const string PlayerPath = "Assets/Prefabs/Player.prefab";
    const string ScenePath = "Assets/Scenes/Naczynia_Tuning.unity";

    [MenuItem("Alko/Scena strojenia naczyń")]
    public static void BuildScene()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        if (prefab == null || prefab.transform.Find("HandBottle") == null)
        {
            Debug.LogError("[Naczynie] Brak HandBottle w Player.prefab — odpal najpierw "
                           + "Alko/Butelka, kufel i puszka w dłoni.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        new GameObject("README__RUSZAJ_TYLKO_NACZYNIE__potem_Alko_Naczynie_zapisz_poze");
        var names = System.Enum.GetNames(typeof(Vessel));
        for (int i = 0; i < names.Length; i++)
            for (int drinking = 0; drinking < 2; drinking++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                go.name = $"{names[i]}_{(drinking == 1 ? "PIJAC" : "STOJAC")}";
                go.transform.position = new Vector3(i * 1.5f, 0f, drinking * 2f);
                Pose(go, names[i], drinking == 1);
            }
        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"[Naczynie] {ScenePath} gotowa: rząd z przodu STOJAC, z tyłu PIJAC.");
    }

    // postać dokładnie tak, jak stoi na hubie z piwem w garści: Animator gra Idle
    // (Speed = 0 w blend tree), a PlayerLimbs dokłada na to BeerIdle w czasie
    // DrinkPose * length — sama prawa ręka i palce. Tutaj robimy to samo ręcznie,
    // bo w edytorze Animator nie liczy klatek.
    static void Pose(GameObject player, string vessel, bool drinking)
    {
        var body = player.transform.Find("Body");
        var anim = body != null ? body.GetComponentInChildren<Animator>() : null;
        var limbs = player.GetComponent<PlayerLimbs>();
        if (anim != null && limbs != null)
        {
            anim.enabled = false; // bez tego kontroler nadpisałby wyklikaną pozę
            anim.transform.localPosition += Vector3.up * limbs.groundOffset; // jak w grze
            var root = PlayerLimbs.ClipRoot(anim.transform).gameObject;
            var idle = PlayerAnimatorBuilder.Clip("Idle");
            if (idle != null) idle.SampleAnimation(root, 0f); // całe ciało
            if (limbs.beerIdleClip != null)                   // na to prawa ręka z piwem
                limbs.beerIdleClip.SampleAnimation(root,
                    drinking ? limbs.beerIdleClip.length : 0f);
        }

        var hb = player.transform.Find("HandBottle");
        hb.gameObject.SetActive(true);
        var fb = hb.GetComponent<FollowBone>();
        if (fb != null) fb.Snap(); // w edytorze LateUpdate nie leci — ustawiamy raz
        foreach (var n in System.Enum.GetNames(typeof(Vessel)))
        {
            var t = hb.Find(n);
            if (t == null) continue;
            t.gameObject.SetActive(n == vessel);
            if (n == vessel && t.TryGetComponent<VesselPose>(out var pose))
                pose.Apply(drinking ? 1f : 0f); // start od tego, co już zapisane
        }
    }

    [MenuItem("Alko/Naczynie: zapisz pozę stania")]
    static void BakeIdle() => Bake(true);

    [MenuItem("Alko/Naczynie: zapisz pozę picia")]
    static void BakeDrink() => Bake(false);

    static void Bake(bool idle)
    {
        var go = Selection.activeGameObject;
        if (go == null || go.GetComponent<VesselPose>() == null)
        {
            Debug.LogError("[Naczynie] Zaznacz naczynie (Butelka/Kufel/Puszka) spod HandBottle.");
            return;
        }
        Vector3 pos = go.transform.localPosition, rot = go.transform.localEulerAngles;

        // w trybie prefabu piszemy w miejscu (zapis Ctrl+S), ze sceny — prosto do assetu
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
        {
            var pose = go.GetComponent<VesselPose>();
            Undo.RecordObject(pose, "Poza naczynia");
            Set(pose, idle, pos, rot);
            EditorUtility.SetDirty(pose);
        }
        else
        {
            var player = PrefabUtility.LoadPrefabContents(PlayerPath);
            var target = player.transform.Find("HandBottle/" + go.name);
            var pose = target != null ? target.GetComponent<VesselPose>() : null;
            if (pose == null)
            {
                Debug.LogError($"[Naczynie] Nie ma HandBottle/{go.name} w Player.prefab.");
                PrefabUtility.UnloadPrefabContents(player);
                return;
            }
            Set(pose, idle, pos, rot);
            PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
            PrefabUtility.UnloadPrefabContents(player);
        }
        Debug.Log($"[Naczynie] {go.name}: poza {(idle ? "stania" : "picia")} "
                  + $"= {pos:F4} / {rot:F2}°");
    }

    static void Set(VesselPose pose, bool idle, Vector3 pos, Vector3 rot)
    {
        if (idle) { pose.idlePosition = pos; pose.idleRotation = rot; }
        else { pose.drinkPosition = pos; pose.drinkRotation = rot; }
    }
}
