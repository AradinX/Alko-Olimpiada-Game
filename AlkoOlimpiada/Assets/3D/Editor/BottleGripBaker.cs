using UnityEditor;
using UnityEngine;

// Strojenie butelki w dłoni na oko, zamiast wpisywania kwaternionu z palca.
//
// Butelka NIE jest dzieckiem kości (kości AccuRig mają nieuniform skalę, która by ją
// rozpłaszczyła — patrz FollowBone), więc w edytorze stoi w miejscu i nie widać, gdzie
// wyląduje w grze. Dlatego: ustawiasz ją myszką tam, gdzie ma być względem dłoni,
// a ta komenda przelicza to na offsety, których FollowBone używa w runtime.
//
// UŻYCIE
//   1. Otwórz Player.prefab (dwuklik) — wejdziesz w tryb prefabu.
//   2. Włącz HandBottle (checkbox obok nazwy), żeby ją zobaczyć.
//   3. Ustaw butelkę w dłoni: przesuń i obróć jak zwykły obiekt.
//   4. Zaznacz HandBottle i odpal Alko/Zapisz ustawienie butelki w dłoni.
//   5. Wyłącz HandBottle z powrotem i zapisz prefab (Ctrl+S).
//
// Offsety liczone są WZGLĘDEM KOŚCI, więc działają w każdej pozie animacji — nie tylko
// w tej, w której akurat stoi prefab.
public static class BottleGripBaker
{
    const string Menu = "Alko/Zapisz ustawienie butelki w dłoni";

    [MenuItem(Menu)]
    static void Bake()
    {
        var fb = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponent<FollowBone>() : null;
        if (fb == null)
        {
            Debug.LogError("[Butelka] Zaznacz obiekt z komponentem FollowBone (HandBottle).");
            return;
        }
        if (fb.bone == null)
        {
            Debug.LogError("[Butelka] FollowBone nie ma przypisanej kości — bez niej nie ma "
                           + "względem czego liczyć. Wskaż CC_Base_R_Hand w polu bone.");
            return;
        }

        // Odwrotność tego, co FollowBone robi w LateUpdate:
        //   rotacja = bone.rotation * rotOffset
        //   pozycja = bone.position + bone.rotation * posOffset - TransformVector(gripLocal)
        // Punkt chwytu modelu ma wylądować w posOffset liczonym od kości.
        Quaternion invBone = Quaternion.Inverse(fb.bone.rotation);
        Quaternion rot = invBone * fb.transform.rotation;
        Vector3 pos = invBone * (fb.transform.TransformPoint(fb.gripLocal) - fb.bone.position);

        Undo.RecordObject(fb, "Zapisz ustawienie butelki");
        fb.posOffset = pos;
        fb.rotOffset = rot;
        EditorUtility.SetDirty(fb);
        if (PrefabUtility.IsPartOfPrefabInstance(fb))
            PrefabUtility.RecordPrefabInstancePropertyModifications(fb);

        Debug.Log($"[Butelka] Zapisano względem {fb.bone.name}: posOffset={pos:F5} "
                  + $"rotOffset={rot.eulerAngles:F2} st. Zapisz prefab (Ctrl+S).");
    }
}
