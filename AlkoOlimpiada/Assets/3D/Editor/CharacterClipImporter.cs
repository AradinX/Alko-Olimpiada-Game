using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Klipy z AccuRig (GuyWardrobe@*.fbx) konfigurują się same przy imporcie, bez
// klikania w Inspectorze — inaczej po każdym reimporcie trzeba by je przestawiać.
// Klipy są "in place": kość root stoi w zerze przez cały czas, biodra bujają się
// o <1.5 cm (sprawdzone na krzywych w Blenderze), więc nie ma czego wypiekać —
// ruchem steruje CharacterController, Animator ma applyRootMotion = false.
class CharacterClipImporter : AssetPostprocessor
{
    const string Model = "Assets/3D/GuyWardrobe.fbx";
    const string ClipPrefix = "Assets/3D/GuyWardrobe@";
    static readonly string[] Looping = { "Idle", "Jog", "Sprint", "Dance" };

    // PODBIJ przy każdej zmianie reguł poniżej — bez tego Unity nie reimportuje
    // plików, które już raz przeszły import, i nowe ustawienia po prostu nie wejdą
    public override uint GetVersion() => 3;

    // Blender eksportuje też transform samego obiektu armatury. W Unity ląduje on
    // jako krzywe o pustej ścieżce, czyli NA ROOCIE Animatora (Body/Guy) — klip
    // nadpisywał skalę i pozycję policzone przez SetupCharacterModel i postać
    // odlatywała w powietrze. Kasujemy je: pozycją steruje CharacterController.
    void OnPostprocessAnimation(GameObject root, AnimationClip clip)
    {
        if (!assetPath.StartsWith(ClipPrefix)) return;
        int cut = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
            if (string.IsNullOrEmpty(b.path))
            {
                AnimationUtility.SetEditorCurve(clip, b, null);
                cut++;
            }
        if (cut > 0) Debug.Log($"[Klipy] {clip.name}: usunięto {cut} krzywych roota");
    }

    static string ClipName(string path) =>
        Path.GetFileNameWithoutExtension(path).Split('@').Last();

    void OnPreprocessModel()
    {
        var im = (ModelImporter)assetImporter;

        // model musi wystawić Avatar, inaczej klipy nie mają go skąd skopiować
        if (assetPath == Model)
        {
            im.animationType = ModelImporterAnimationType.Generic;
            im.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            return;
        }
        if (!assetPath.StartsWith(ClipPrefix)) return;

        im.animationType = ModelImporterAnimationType.Generic;
        // pliki klipów to sama armatura, bez meshy i materiałów
        im.materialImportMode = ModelImporterMaterialImportMode.None;

        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(Model);
        if (avatar != null)
        {
            im.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            im.sourceAvatar = avatar;
        }
        else
        {
            // świeży klon repo: klip potrafi wejść przed modelem. Własny Avatar
            // wystarcza do zaimportowania klipu, a reimport po batchu przestawi
            // go na kopię z modelu.
            im.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            if (!pending.Contains(assetPath)) pending.Add(assetPath);
        }
    }

    // AssetDatabase.ImportAsset jest w trakcie importu zabronione (Unity pilnuje
    // determinizmu), więc dobicie klipów zlecamy dopiero po zakończeniu batcha.
    static readonly List<string> pending = new();

    static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                       string[] moved, string[] movedFrom)
    {
        if (pending.Count == 0 || AssetDatabase.LoadAssetAtPath<Avatar>(Model) == null) return;
        var clips = pending.ToArray();
        pending.Clear();
        // po reimporcie Avatar już istnieje, więc drugi przebieg nie wraca tutaj
        EditorApplication.delayCall += () =>
        {
            foreach (var c in clips)
                AssetDatabase.ImportAsset(c, ImportAssetOptions.ForceUpdate);
        };
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(ClipPrefix)) return;
        var im = (ModelImporter)assetImporter;
        string name = ClipName(assetPath);

        var clips = im.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = name; // "Walk", a nie "catwalk-loop-378982"
            c.loopTime = Looping.Contains(name);
        }
        im.clipAnimations = clips;
    }
}
