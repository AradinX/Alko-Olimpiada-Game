using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// Buduje Assets/3D/PlayerAnimator.controller z klipów AccuRig i wpina go w
// Body/Guy w prefabie gracza. Kontroler jest budowany od zera przy każdym
// uruchomieniu — ręczne zmiany w oknie Animatora zostaną nadpisane, stan
// maszyny trzymamy tutaj żeby dało się go odtworzyć z repo.
public static class PlayerAnimatorBuilder
{
    const string Ctrl = "Assets/3D/PlayerAnimator.controller";
    const string Model = "Assets/3D/GuyWardrobe.fbx";
    const string Prefab = "Assets/Prefabs/Player.prefab";

    [MenuItem("Alko/Zbuduj Animator gracza")]
    public static void Build()
    {
        var ac = AnimatorController.CreateAnimatorControllerAtPath(Ctrl);
        ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ac.AddParameter("Emote", AnimatorControllerParameterType.Int);
        var sm = ac.layers[0].stateMachine;

        var locomotion = ac.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(Clip("Idle"), 0f);
        tree.AddChild(Clip("Jog"), 4f);
        tree.AddChild(Clip("Sprint"), 7f);
        Debug.Assert(tree.children.Select(c => c.threshold).SequenceEqual(new[] { 0f, 4f, 7f }),
            "[Animator] Zle progi locomotion blend tree");
        locomotion.writeDefaultValues = false;

        var dance = State(sm, "Dance");
        sm.defaultState = locomotion;

        var any = sm.AddAnyStateTransition(dance);
        any.duration = 0.2f;
        any.hasExitTime = false;
        any.canTransitionToSelf = false;
        any.AddCondition(AnimatorConditionMode.Equals, 5f, "Emote");
        Cond(dance, locomotion, 0.2f)
            .AddCondition(AnimatorConditionMode.NotEqual, 5f, "Emote");

        AssetDatabase.SaveAssets();
        WirePrefab(ac);
        Debug.Log("[Animator] Idle/Jog/Sprint zbudowane i wpiete w Player.prefab");
    }

    static AnimatorState State(AnimatorStateMachine sm, string clip)
    {
        var s = sm.AddState(clip);
        s.motion = Clip(clip);
        s.writeDefaultValues = false; // kości spoza klipu zostawiamy emotkom
        return s;
    }

    static AnimationClip Clip(string name)
    {
        string path = $"Assets/3D/GuyWardrobe@{name}.fbx";
        var clip = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null) Debug.LogError($"[Animator] Brak klipu w {path}");
        return clip;
    }

    static AnimatorStateTransition Cond(AnimatorState from, AnimatorState to, float dur)
    {
        var t = from.AddTransition(to);
        t.hasExitTime = false; // warunek ma działać natychmiast, nie po końcu klipu
        t.duration = dur;
        return t;
    }



    static void WirePrefab(AnimatorController ac)
    {
        var player = PrefabUtility.LoadPrefabContents(Prefab);
        var guy = player.transform.Find("Body/Guy");
        if (guy == null)
        {
            Debug.LogError("[Animator] Brak Body/Guy w prefabie — odpal najpierw " +
                           "ProjectBootstrap.SetupCharacterModel");
            PrefabUtility.UnloadPrefabContents(player);
            return;
        }

        var anim = guy.GetComponent<Animator>();
        if (anim == null) anim = guy.gameObject.AddComponent<Animator>();
        player.GetComponent<PlayerLimbs>().beerIdleClip = Clip("BeerIdle");
        // Animator pochodzi ze zagnieżdżonego FBX. Unity 6 zapisuje zwykłe
        // przypisanie do YAML, ale po imporcie prefabu gubi m_Controller.
        var serialized = new SerializedObject(anim);
        serialized.FindProperty("m_Controller").objectReferenceValue = ac;
        serialized.FindProperty("m_Avatar").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Avatar>(Model);
        serialized.FindProperty("m_ApplyRootMotion").boolValue = false;
        // postacie zdalne muszą animować kości nawet poza kadrem — butelka i
        // NameTag wiszą na kościach, a przycięty Animator zostawiłby je w miejscu
        serialized.FindProperty("m_CullingMode").intValue =
            (int)AnimatorCullingMode.AlwaysAnimate;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(anim);

        PrefabUtility.SaveAsPrefabAsset(player, Prefab);
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(Prefab,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        var savedAnimator = AssetDatabase.LoadAssetAtPath<GameObject>(Prefab)
            .GetComponentInChildren<Animator>(true);
        Debug.Assert(savedAnimator.runtimeAnimatorController == ac,
            "[Animator] Player.prefab zgubil kontroler po imporcie");
    }
}
