using System.IO;
using System.Linq;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Jednorazowy generator projektu: URP, prefab gracza, scena Hub, build.
public static class ProjectBootstrap
{
    public static void Setup()
    {
        PlayerSettings.runInBackground = true; // dwie instancje na jednej maszynie
        ImportTMPEssentials();
        SetupURP();
        var playerPrefab = BuildPlayerPrefab();
        BuildHubScene(playerPrefab);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] Setup OK");
    }

    // ImportPackage jest asynchroniczny — osobny przebieg bez -quit, czekamy na callback
    public static void ImportTMPAndFixFont()
    {
        AssetDatabase.importPackageCompleted += _ =>
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            if (font == null) { Debug.LogError("[Bootstrap] Brak fontu TMP po imporcie"); EditorApplication.Exit(1); return; }

            var prefab = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
            prefab.GetComponent<PlayerNameTag>().label.font = font;
            PrefabUtility.SaveAsPrefabAsset(prefab, "Assets/Prefabs/Player.prefab");
            PrefabUtility.UnloadPrefabContents(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log("[Bootstrap] TMP OK, font przypisany");
            EditorApplication.Exit(0);
        };
        AssetDatabase.importPackageFailed += (_, err) =>
        {
            Debug.LogError("[Bootstrap] Import TMP nieudany: " + err);
            EditorApplication.Exit(1);
        };
        var ugui = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.ugui");
        var pkg = Directory.GetFiles(ugui.resolvedPath, "TMP Essential Resources.unitypackage",
            SearchOption.AllDirectories).FirstOrDefault();
        if (pkg == null) { Debug.LogError("[Bootstrap] Brak paczki TMP Essentials"); EditorApplication.Exit(1); return; }
        AssetDatabase.ImportPackage(pkg, false);
    }

    static void ImportTMPEssentials()
    {
        if (Directory.Exists("Assets/TextMesh Pro")) return;
        var ugui = UnityEditor.PackageManager.PackageInfo.FindForPackageName("com.unity.ugui");
        var pkg = Directory.GetFiles(ugui.resolvedPath, "TMP Essential Resources.unitypackage",
            SearchOption.AllDirectories).FirstOrDefault();
        if (pkg == null) { Debug.LogError("[Bootstrap] Brak paczki TMP Essentials"); return; }
        AssetDatabase.ImportPackage(pkg, false);
        AssetDatabase.Refresh();
        Debug.Log("[Bootstrap] TMP Essentials zaimportowane");
    }

    static void SetupURP()
    {
        if (GraphicsSettings.defaultRenderPipeline != null) return;
        Directory.CreateDirectory("Assets/Settings");

        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/URP_Renderer.asset");
        try
        {
            ResourceReloader.ReloadAllNullIn(rendererData,
                "Packages/com.unity.render-pipelines.universal");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Bootstrap] ResourceReloader: " + e.Message);
        }

        var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
        AssetDatabase.CreateAsset(pipeline, "Assets/Settings/URP_Asset.asset");
        GraphicsSettings.defaultRenderPipeline = pipeline;
        Debug.Log("[Bootstrap] URP ustawione");
    }

    static GameObject BuildPlayerPrefab()
    {
        Directory.CreateDirectory("Assets/Prefabs");

        var root = new GameObject("Player");
        var cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0, 1f, 0);
        cc.height = 2f;
        cc.radius = 0.4f;
        root.AddComponent<NetworkObject>();
        root.AddComponent<ClientNetworkTransform>();
        var pc = root.AddComponent<PlayerController>();
        var tag = root.AddComponent<PlayerNameTag>();

        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.name = "Body";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0, 1f, 0);

        var camGO = new GameObject("PlayerCamera") { tag = "MainCamera" };
        camGO.transform.SetParent(root.transform, false);
        camGO.transform.localPosition = new Vector3(0, 1.7f, 0);
        pc.playerCamera = camGO.AddComponent<Camera>();
        camGO.AddComponent<AudioListener>();
        camGO.SetActive(false); // właściciel włącza po spawnie

        var labelGO = new GameObject("NameTag");
        labelGO.transform.SetParent(root.transform, false);
        labelGO.transform.localPosition = new Vector3(0, 2.4f, 0);
        var tmp = labelGO.AddComponent<TextMeshPro>();
        tmp.text = "Gracz";
        tmp.fontSize = 4;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(4, 1);
        tag.label = tmp;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Player.prefab");
        Object.DestroyImmediate(root);
        Debug.Log("[Bootstrap] Prefab gracza zapisany");
        return prefab;
    }

    static void BuildHubScene(GameObject playerPrefab)
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // kamera menu (z DefaultGameObjects) — patrzy na hub przed połączeniem
        var menuCam = GameObject.Find("Main Camera");
        menuCam.transform.position = new Vector3(0, 10, -22);
        menuCam.transform.rotation = Quaternion.Euler(22, 0, 0);

        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(8, 1, 8); // 80x80 m

        // greybox: krąg kolumn + blok "świątyni"
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.PI * 2f / 8f;
            var col = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            col.name = "Column_" + i;
            col.transform.position = new Vector3(Mathf.Cos(a) * 14f, 2f, Mathf.Sin(a) * 14f);
            col.transform.localScale = new Vector3(1f, 2f, 1f);
        }
        var temple = GameObject.CreatePrimitive(PrimitiveType.Cube);
        temple.name = "TempleBlock";
        temple.transform.position = new Vector3(0, 1.5f, 25f);
        temple.transform.localScale = new Vector3(12, 3, 6);

        var net = new GameObject("NetworkManager");
        var nm = net.AddComponent<NetworkManager>();
        var utp = net.AddComponent<UnityTransport>();
        utp.SetConnectionData("127.0.0.1", 7777, "0.0.0.0"); // nasłuch na LAN
        nm.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = utp,
            PlayerPrefab = playerPrefab,
        };
        net.AddComponent<ConnectionUI>();
        net.AddComponent<VivoxVoice>();

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Hub.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Hub.unity", true) };
        Debug.Log("[Bootstrap] Scena Hub zapisana");
    }

    // Prototyp 2: DrunkSystem na graczu + butelki piwa na hubie (in-scene NetworkObjects,
    // nie wymagają rejestracji w NetworkConfig)
    public static void SetupPrototype2()
    {
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        if (player.GetComponent<DrunkSystem>() == null) player.AddComponent<DrunkSystem>();
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);

        var beer = BuildBeerPrefab();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");
        foreach (var old in Object.FindObjectsByType<BeerPickup>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject); // idempotentny rerun
        for (int i = 0; i < 12; i++)
        {
            var b = (GameObject)PrefabUtility.InstantiatePrefab(beer);
            float a = i * Mathf.PI * 2f / 12f;
            float r = i % 2 == 0 ? 8f : 18f;
            b.transform.position = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] Prototype2 OK");
    }

    // Prototyp 4/5: stanowiska konkurencji, areny, pigułki, VoteManager, build settings
    public static void SetupPrototype4()
    {
        // prefab gracza: nadpisz zserializowane wartości ze starszych prototypów
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        var pds = player.GetComponent<DrunkSystem>();
        pds.decayPerSecond = 0.2f;
        pds.catchRadius = 25f; // przyłapanie po widoku, nie po bliskości
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");
        foreach (var o in Object.FindObjectsByType<CompetitionStation>(FindObjectsSortMode.None))
            Object.DestroyImmediate(o.gameObject); // idempotentny rerun
        var legacy = GameObject.Find("Station_Sprint500"); // stanowisko z P3
        if (legacy != null) Object.DestroyImmediate(legacy);
        foreach (var v in Object.FindObjectsByType<VoteManager>(FindObjectsSortMode.None))
            Object.DestroyImmediate(v.gameObject);

        BuildStation("SPRINT NA 500", "Arena_Sprint500", "-autosprint",
            new Vector3(0f, 0.25f, 18f), new Color(0.2f, 0.5f, 0.9f));
        BuildStation("RZUTKI", "Arena_Rzutki", "-autorzutki",
            new Vector3(18f, 0.25f, 0f), new Color(0.9f, 0.3f, 0.2f));
        BuildStation("NA PÓŁ", "Arena_NaPol", "-autonapol",
            new Vector3(-18f, 0.25f, 0f), new Color(0.2f, 0.8f, 0.3f));
        BuildStation("LUCKY SHOT", "Arena_LuckyShot", "-autolucky",
            new Vector3(13f, 0.25f, -13f), new Color(0.8f, 0.7f, 0.1f));
        BuildStation("SPACER DO MONOPOLOWEGO", "Arena_Spacer", "-autospacer",
            new Vector3(-13f, 0.25f, -13f), new Color(0.6f, 0.3f, 0.8f));

        var vmGo = new GameObject("VoteManager");
        vmGo.AddComponent<NetworkObject>();
        var vm = vmGo.AddComponent<VoteManager>();
        vm.scenes = new[] { "Arena_Sprint500", "Arena_Rzutki", "Arena_NaPol",
                            "Arena_LuckyShot", "Arena_Spacer" };
        vm.titles = new[] { "SPRINT NA 500", "RZUTKI", "NA PÓŁ",
                            "LUCKY SHOT", "SPACER" };

        // pigułki (pkt 5)
        foreach (var old in Object.FindObjectsByType<PillPickup>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        var pill = BuildPillPrefab();
        for (int i = 0; i < 4; i++)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(pill);
            float a = (i + 0.5f) * Mathf.PI / 2f;
            g.transform.position = new Vector3(Mathf.Cos(a) * 12f, 0f, Mathf.Sin(a) * 12f);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        BuildArenas();

        EditorBuildSettings.scenes =
            new[] { "Hub", "Arena_Sprint500", "Arena_Rzutki", "Arena_NaPol",
                    "Arena_LuckyShot", "Arena_Spacer" }
            .Select(n => new EditorBuildSettingsScene($"Assets/Scenes/{n}.unity", true))
            .ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] Prototype4 OK");
    }

    static void BuildStation(string title, string sceneName, string autoFlag,
        Vector3 pos, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Station_" + sceneName;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(4f, 0.5f, 4f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        AssetDatabase.CreateAsset(mat, $"Assets/Prefabs/StationMat_{sceneName}.mat");
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.AddComponent<NetworkObject>();
        var st = go.AddComponent<CompetitionStation>();
        st.title = title;
        st.sceneName = sceneName;
        st.autoFlag = autoFlag;
    }

    static void BuildArenas()
    {
        // Sprint: beczka pośrodku, gracze w kręgu naprzeciwko siebie
        var s = NewArena();
        var keg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        keg.name = "Keg";
        keg.transform.position = new Vector3(0f, 0.5f, 0f);
        keg.transform.localScale = new Vector3(1f, 0.5f, 1f);
        var c = new GameObject("Sprint500");
        c.AddComponent<NetworkObject>();
        c.AddComponent<Sprint500>();
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_Sprint500.unity");

        // Rzutki: linia graczy, po tarczy 6 m przed każdym
        s = NewArena();
        c = new GameObject("Rzutki");
        c.AddComponent<NetworkObject>();
        var rz = c.AddComponent<Rzutki>();
        rz.timeoutSeconds = 30f;
        rz.naturalDrunkGain = 12f;
        for (int i = 0; i < 8; i++)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            b.name = "Board_" + i;
            Object.DestroyImmediate(b.GetComponent<Collider>());
            b.AddComponent<BoxCollider>(); // płaska ściana do raycastu
            b.transform.position = new Vector3(i * 4f - 14f, 1.6f, 6f);
            b.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            b.transform.localScale = new Vector3(1.2f, 0.05f, 1.2f);
        }
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_Rzutki.unity");

        // Na pół: sama arena, gra jest w UI
        s = NewArena();
        c = new GameObject("NaPol");
        c.AddComponent<NetworkObject>();
        var np = c.AddComponent<NaPol>();
        np.timeoutSeconds = 20f;
        np.naturalDrunkGain = 12f;
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_NaPol.unity");

        // Lucky Shot: gra w UI
        s = NewArena();
        c = new GameObject("LuckyShot");
        c.AddComponent<NetworkObject>();
        var lk = c.AddComponent<LuckyShot>();
        lk.timeoutSeconds = 25f;
        lk.naturalDrunkGain = 12f;
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_LuckyShot.unity");

        // Spacer: belki nad ziemią, meta na końcu
        s = NewArena();
        c = new GameObject("Spacer");
        c.AddComponent<NetworkObject>();
        var sp = c.AddComponent<Spacer>();
        sp.timeoutSeconds = 60f;
        sp.naturalDrunkGain = 12f;
        GameObject Cube(string n, Vector3 p, Vector3 sc)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = n; g.transform.position = p; g.transform.localScale = sc;
            return g;
        }
        Cube("StartPlatform", new Vector3(0f, 2.75f, -3f), new Vector3(6f, 0.5f, 4f));
        Cube("Beam1", new Vector3(0f, 2.75f, 4f), new Vector3(0.7f, 0.5f, 10f));
        Cube("Beam2", new Vector3(0f, 2.75f, 14.8f), new Vector3(0.7f, 0.5f, 9f));   // przerwa 1.3 m
        Cube("Beam3", new Vector3(0f, 2.75f, 22.6f), new Vector3(0.7f, 0.5f, 4.6f)); // przerwa 1.3 m
        Cube("MetaPlatform", new Vector3(0f, 2.75f, 27.5f), new Vector3(6f, 0.5f, 5f));
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_Spacer.unity");
    }

    static GameObject BuildPillPrefab()
    {
        var root = new GameObject("Pill");
        root.AddComponent<NetworkObject>();
        root.AddComponent<PillPickup>();
        var trig = root.AddComponent<SphereCollider>();
        trig.isTrigger = true;
        trig.radius = 0.7f;
        trig.center = new Vector3(0, 0.25f, 0);
        var vis = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(vis.GetComponent<Collider>());
        vis.name = "PillVis";
        vis.transform.SetParent(root.transform, false);
        vis.transform.localPosition = new Vector3(0, 0.25f, 0);
        vis.transform.localScale = new Vector3(0.14f, 0.14f, 0.14f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };
        AssetDatabase.CreateAsset(mat, "Assets/Prefabs/PillMat.mat");
        vis.GetComponent<Renderer>().sharedMaterial = mat;
        var p = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Pill.prefab");
        Object.DestroyImmediate(root);
        return p;
    }

    static UnityEngine.SceneManagement.Scene NewArena()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Object.DestroyImmediate(GameObject.Find("Main Camera")); // gracze przynoszą kamery
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 4f);
        return scene;
    }

    static GameObject BuildBeerPrefab()
    {
        var root = new GameObject("Beer");
        root.AddComponent<NetworkObject>();
        root.AddComponent<BeerPickup>();
        var trig = root.AddComponent<SphereCollider>();
        trig.isTrigger = true;
        trig.radius = 0.8f;
        trig.center = new Vector3(0, 0.4f, 0);

        var bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(bottle.GetComponent<Collider>());
        bottle.name = "Bottle";
        bottle.transform.SetParent(root.transform, false);
        bottle.transform.localPosition = new Vector3(0, 0.35f, 0);
        bottle.transform.localScale = new Vector3(0.25f, 0.35f, 0.25f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.95f, 0.65f, 0.1f) };
        AssetDatabase.CreateAsset(mat, "Assets/Prefabs/BeerMat.mat");
        bottle.GetComponent<Renderer>().sharedMaterial = mat;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Beer.prefab");
        Object.DestroyImmediate(root);
        Debug.Log("[Bootstrap] Prefab piwa zapisany");
        return prefab;
    }

    // Pobiera poświadczenia Vivox z dashboardu — normalnie robi to strona GUI
    // Project Settings > Services > Vivox; refleksja, bo VivoxApiClient jest internal.
    // Uruchamiać BEZ -quit (czekamy na callback), wymaga zalogowanego edytora.
    public static void FetchVivoxCredentials()
    {
        double deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += () =>
        {
            if (EditorApplication.timeSinceStartup > deadline)
            {
                Debug.LogError("[Bootstrap] Vivox creds TIMEOUT");
                EditorApplication.Exit(1);
            }
        };

        var asm = System.AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Unity.Services.Vivox.Editor");
        var clientType = asm.GetType("Unity.Services.Vivox.Editor.VivoxApiClient");
        const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Static;
        var instance = clientType.GetProperty("Instance", any).GetValue(null);
        var method = clientType.GetMethod("GetAndSetVivoxCredentials", any);
        var okType = method.GetParameters()[0].ParameterType;
        var ok = System.Delegate.CreateDelegate(okType,
            typeof(ProjectBootstrap).GetMethod(nameof(VivoxFetched), any)
                .MakeGenericMethod(okType.GetGenericArguments()[0]));
        System.Action<System.Exception> err = e =>
        {
            Debug.LogError("[Bootstrap] Vivox creds FAIL: " + e.Message);
            EditorApplication.Exit(1);
        };
        method.Invoke(instance, new object[] { ok, err });
    }

    static void VivoxFetched<T>(T _)
    {
        AssetDatabase.SaveAssets();
        var json = File.ReadAllText("ProjectSettings/Packages/com.unity.services.vivox/Settings.json");
        bool filled = json.Contains("vivox.com") || json.Contains("https://");
        Debug.Log("[Bootstrap] Vivox creds: " + (filled ? "OK" : "PUSTE"));
        EditorApplication.Exit(filled ? 0 : 1);
    }

    public static void Build()
    {
        var report = BuildPipeline.BuildPlayer(
            EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
            "Builds/Win/AlkoOlimpiada.exe",
            BuildTarget.StandaloneWindows64,
            BuildOptions.None);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError("[Bootstrap] Build FAILED");
            EditorApplication.Exit(1);
        }
        Debug.Log("[Bootstrap] Build OK: " + report.summary.outputPath);
    }
}
