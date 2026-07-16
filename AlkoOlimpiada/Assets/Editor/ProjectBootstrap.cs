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
        pds.beerStrength = 12f; // 2 piwa Szumi / 4 Lekko chycony / 8 Ligancko / 9 Zgon
        pds.beerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Beer.prefab");
        if (player.transform.Find("HandBottle") == null) // butelka w ręce (Beers > 0)
        {
            var hb = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(hb.GetComponent<Collider>());
            hb.name = "HandBottle";
            hb.transform.SetParent(player.transform, false);
            hb.transform.localPosition = new Vector3(0.38f, 1.15f, 0.3f);
            hb.transform.localScale = new Vector3(0.12f, 0.18f, 0.12f);
            var bm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/BeerMat.mat");
            if (bm != null) hb.GetComponent<Renderer>().sharedMaterial = bm;
            hb.SetActive(false);
        }
        // klockowa postać: głowa/tułów/ręce/nogi zamiast samej kapsuły (idempotentnie)
        var body = player.transform.Find("Body");
        if (body != null && body.Find("Torso") == null)
        {
            Object.DestroyImmediate(body.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(body.GetComponent<MeshFilter>());
            GameObject Part(string n, Vector3 pos, Vector3 sc, Transform parent)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(g.GetComponent<Collider>());
                g.name = n;
                g.transform.SetParent(parent, false);
                g.transform.localPosition = pos;
                g.transform.localScale = sc;
                return g;
            }
            Transform Pivot(string n, Vector3 pos)
            {
                var p = new GameObject(n).transform;
                p.SetParent(body, false);
                p.localPosition = pos;
                return p;
            }
            Part("Torso", new Vector3(0f, 0.15f, 0f), new Vector3(0.5f, 0.62f, 0.28f), body);
            Part("Head", new Vector3(0f, 0.62f, 0f), new Vector3(0.3f, 0.3f, 0.3f), body);
            // pivoty w barkach/biodrach — PlayerLimbs kręci nimi przy chodzie
            Part("ArmCube", new Vector3(0f, -0.28f, 0f), new Vector3(0.13f, 0.55f, 0.13f),
                Pivot("ArmL", new Vector3(-0.33f, 0.42f, 0f)));
            Part("ArmCube", new Vector3(0f, -0.28f, 0f), new Vector3(0.13f, 0.55f, 0.13f),
                Pivot("ArmR", new Vector3(0.33f, 0.42f, 0f)));
            Part("LegCube", new Vector3(0f, -0.35f, 0f), new Vector3(0.16f, 0.68f, 0.16f),
                Pivot("LegL", new Vector3(-0.13f, -0.16f, 0f)));
            Part("LegCube", new Vector3(0f, -0.35f, 0f), new Vector3(0.16f, 0.68f, 0.16f),
                Pivot("LegR", new Vector3(0.13f, -0.16f, 0f)));
        }
        if (player.GetComponent<PlayerLimbs>() == null) player.AddComponent<PlayerLimbs>();
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");

        // wyrzucane piwo: ConnectionUI rejestruje prefab w NGO przed startem sieci
        var connUi = Object.FindFirstObjectByType<ConnectionUI>();
        if (connUi != null)
            connUi.beerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Beer.prefab");

        // hub jako wyspa: dysk piasku + woda dookoła (bez collidera — wpadasz i respawn)
        foreach (var n in new[] { "Ground", "Island", "Water" })
        {
            var g = GameObject.Find(n);
            if (g != null) Object.DestroyImmediate(g);
        }
        var island = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        island.name = "Island";
        Object.DestroyImmediate(island.GetComponent<Collider>());
        island.AddComponent<MeshCollider>();
        island.transform.position = new Vector3(0f, -0.5f, 0f);
        island.transform.localScale = new Vector3(95f, 0.5f, 95f); // promień ~47 m, wierzch na y=0
        var sand = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.87f, 0.78f, 0.55f) };
        AssetDatabase.CreateAsset(sand, "Assets/Prefabs/IslandMat.mat");
        island.GetComponent<Renderer>().sharedMaterial = sand;

        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Water";
        Object.DestroyImmediate(water.GetComponent<Collider>());
        water.transform.position = new Vector3(0f, -0.8f, 0f);
        water.transform.localScale = new Vector3(60f, 1f, 60f); // 600x600 m
        var sea = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.15f, 0.45f, 0.8f) };
        AssetDatabase.CreateAsset(sea, "Assets/Prefabs/WaterMat.mat");
        water.GetComponent<Renderer>().sharedMaterial = sea;

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
        BuildStation("FLANKI", "Arena_Flanki", "-autoflanki",
            new Vector3(26f, 0.25f, 13f), new Color(0.95f, 0.55f, 0.1f));
        BuildStation("BEER PONG", "Arena_BeerPong", "-autopong",
            new Vector3(-26f, 0.25f, 13f), new Color(0.7f, 0.2f, 0.6f));

        var vmGo = new GameObject("VoteManager");
        vmGo.AddComponent<NetworkObject>();
        var vm = vmGo.AddComponent<VoteManager>();
        vm.scenes = new[] { "Arena_Sprint500", "Arena_Rzutki", "Arena_NaPol",
                            "Arena_LuckyShot", "Arena_Spacer", "Arena_Flanki", "Arena_BeerPong" };
        vm.titles = new[] { "SPRINT NA 500", "RZUTKI", "NA PÓŁ",
                            "LUCKY SHOT", "SPACER", "FLANKI", "BEER PONG" };

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
                    "Arena_LuckyShot", "Arena_Spacer", "Arena_Flanki", "Arena_BeerPong" }
            .Select(n => new EditorBuildSettingsScene($"Assets/Scenes/{n}.unity", true))
            .ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] Prototype4 OK");
    }

    // Prototyp 7: papierosy na hubie + konkurencja OCZKO (hazard lite). Idempotentny.
    public static void SetupPrototype7()
    {
        var cig = BuildCigarettePrefab();

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");
        foreach (var old in Object.FindObjectsByType<CigarettePickup>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject); // idempotentny rerun
        for (int i = 0; i < 4; i++)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(cig);
            float a = i * Mathf.PI / 2f; // między pigułkami (te stoją na +45°)
            g.transform.position = new Vector3(Mathf.Cos(a) * 15f, 0f, Mathf.Sin(a) * 15f);
        }

        var snack = BuildSnackPrefab();
        foreach (var old in Object.FindObjectsByType<SnackPickup>(FindObjectsSortMode.None))
            Object.DestroyImmediate(old.gameObject);
        for (int i = 0; i < 3; i++)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(snack);
            float a = (i + 0.25f) * Mathf.PI * 2f / 3f;
            g.transform.position = new Vector3(Mathf.Cos(a) * 21f, 0f, Mathf.Sin(a) * 21f);
        }

        var net = GameObject.Find("NetworkManager");
        if (net != null && net.GetComponent<PauseMenu>() == null)
            net.AddComponent<PauseMenu>(); // ustawienia po ESC (przeżywa zmiany scen — DDOL)

        // stretch z GOAL: szum fal + pływające etykiety stanowisk
        var water = GameObject.Find("Water");
        if (water != null && water.GetComponent<HubAmbience>() == null)
            water.AddComponent<HubAmbience>();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        foreach (var st in Object.FindObjectsByType<CompetitionStation>(FindObjectsSortMode.None))
        {
            // luzem, nie pod stanowiskiem — jego niejednorodna skala (4,0.5,4) skośiłaby billboard
            string lblName = "Label_" + st.sceneName;
            if (GameObject.Find(lblName) != null) continue;
            var lbl = new GameObject(lblName);
            lbl.transform.position = st.transform.position + Vector3.up * 3.2f;
            var tmp = lbl.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = st.title;
            tmp.fontSize = 10;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(12f, 1.6f);
            lbl.AddComponent<Billboard>();
        }

        if (GameObject.Find("Station_Arena_Oczko") == null)
            BuildStation("OCZKO", "Arena_Oczko", "-autooczko",
                new Vector3(0f, 0.25f, -18f), new Color(0.05f, 0.4f, 0.2f)); // kasynowa zieleń
        var vm = Object.FindFirstObjectByType<VoteManager>();
        if (!vm.scenes.Contains("Arena_Oczko"))
        {
            vm.scenes = vm.scenes.Append("Arena_Oczko").ToArray();
            vm.titles = vm.titles.Append("OCZKO").ToArray();
            EditorUtility.SetDirty(vm);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        // arena: zielony stół, gracze w kręgu wokół (domyślne GetPose)
        var s = NewArena();
        var c = new GameObject("Oczko");
        c.AddComponent<NetworkObject>();
        var oc = c.AddComponent<Oczko>();
        oc.timeoutSeconds = 150f; // 3 rozdania × (stawka 8 + gra 20 + rozliczenie 6) z zapasem
        var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        table.name = "CardTable";
        table.transform.position = new Vector3(0f, 0.45f, 0f);
        table.transform.localScale = new Vector3(2.2f, 0.45f, 2.2f);
        var felt = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.05f, 0.35f, 0.15f) };
        AssetDatabase.CreateAsset(felt, "Assets/Prefabs/FeltMat.mat");
        table.GetComponent<Renderer>().sharedMaterial = felt;
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_Oczko.unity");

        if (!EditorBuildSettings.scenes.Any(x => x.path.Contains("Arena_Oczko")))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes
                .Append(new EditorBuildSettingsScene("Assets/Scenes/Arena_Oczko.unity", true))
                .ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] Prototype7 OK");
    }

    static GameObject BuildCigarettePrefab()
    {
        var root = new GameObject("Cigarette");
        root.AddComponent<NetworkObject>();
        root.AddComponent<CigarettePickup>();
        var trig = root.AddComponent<SphereCollider>();
        trig.isTrigger = true;
        trig.radius = 0.7f;
        trig.center = new Vector3(0, 0.2f, 0);
        // leżący biały walec + pomarańczowy żar (BeerMat jest pomarańczowy — reużyty)
        var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(body.GetComponent<Collider>());
        body.name = "CigBody";
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0, 0.2f, 0);
        body.transform.localRotation = Quaternion.Euler(0, 0, 90);
        body.transform.localScale = new Vector3(0.06f, 0.22f, 0.06f);
        var white = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = Color.white };
        AssetDatabase.CreateAsset(white, "Assets/Prefabs/CigMat.mat");
        body.GetComponent<Renderer>().sharedMaterial = white;
        var tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(tip.GetComponent<Collider>());
        tip.name = "CigTip";
        tip.transform.SetParent(root.transform, false);
        tip.transform.localPosition = new Vector3(-0.24f, 0.2f, 0);
        tip.transform.localScale = new Vector3(0.07f, 0.07f, 0.07f);
        var bm = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/BeerMat.mat");
        if (bm != null) tip.GetComponent<Renderer>().sharedMaterial = bm;
        var p = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Cigarette.prefab");
        Object.DestroyImmediate(root);
        return p;
    }

    static GameObject BuildSnackPrefab()
    {
        var root = new GameObject("Snack");
        root.AddComponent<NetworkObject>();
        root.AddComponent<SnackPickup>();
        var trig = root.AddComponent<SphereCollider>();
        trig.isTrigger = true;
        trig.radius = 0.7f;
        trig.center = new Vector3(0, 0.2f, 0);
        // biały talerz + rumiany kurczak
        var plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(plate.GetComponent<Collider>());
        plate.name = "Plate";
        plate.transform.SetParent(root.transform, false);
        plate.transform.localPosition = new Vector3(0, 0.05f, 0);
        plate.transform.localScale = new Vector3(0.5f, 0.02f, 0.5f);
        var white = AssetDatabase.LoadAssetAtPath<Material>("Assets/Prefabs/CigMat.mat");
        if (white != null) plate.GetComponent<Renderer>().sharedMaterial = white;
        var chicken = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.DestroyImmediate(chicken.GetComponent<Collider>());
        chicken.name = "Chicken";
        chicken.transform.SetParent(root.transform, false);
        chicken.transform.localPosition = new Vector3(0, 0.17f, 0);
        chicken.transform.localScale = new Vector3(0.3f, 0.22f, 0.4f);
        var roast = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.7f, 0.42f, 0.15f) };
        AssetDatabase.CreateAsset(roast, "Assets/Prefabs/RoastMat.mat");
        chicken.GetComponent<Renderer>().sharedMaterial = roast;
        var p = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Snack.prefab");
        Object.DestroyImmediate(root);
        return p;
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

        // Lucky Shot: linia graczy przed stołem z kieliszkami
        s = NewArena();
        c = new GameObject("LuckyShot");
        c.AddComponent<NetworkObject>();
        var lk = c.AddComponent<LuckyShot>();
        lk.timeoutSeconds = 90f; // 6 rund × (pokaz + odliczanie 3-2-1 + odpowiedź)
        lk.naturalDrunkGain = 12f;
        var shotTable = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shotTable.name = "ShotTable";
        shotTable.transform.position = new Vector3(0f, 0.75f, -0.5f);
        shotTable.transform.localScale = new Vector3(18f, 0.3f, 0.8f);
        for (int i = 0; i < 8; i++) // sloty zgodne z LuckyShot.GetPose
        {
            var glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(glass.GetComponent<Collider>());
            glass.name = "Shot_" + i;
            glass.transform.position = new Vector3(i * 2f - 7f, 0.95f, -0.5f);
            glass.transform.localScale = new Vector3(0.06f, 0.05f, 0.06f);
        }
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

        // Flanki: puszka na środku, drużyny naprzeciw (z=-6 / z=6)
        s = NewArena();
        c = new GameObject("Flanki");
        c.AddComponent<NetworkObject>();
        var fl = c.AddComponent<Flanki>();
        fl.timeoutSeconds = 150f;
        var can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        can.name = "Puszka";
        can.transform.position = new Vector3(0f, 0.25f, 0f);
        can.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_Flanki.unity");

        // Beer Pong: stół z kubkami po obu stronach (dekoracja; stan liczy UI)
        s = NewArena();
        c = new GameObject("BeerPong");
        c.AddComponent<NetworkObject>();
        var bp = c.AddComponent<BeerPong>();
        bp.turnSeconds = 12f; // celowanie + ładowanie siły trwa dłużej niż timing kółka
        // timeoutSeconds i kubki ustawia BeerPong w runtime (drabinka 1v1, 10 kubków 4-3-2-1)
        Cube("Table", new Vector3(0f, 0.5f, 0f), new Vector3(2f, 1f, 8f));
        // dekoracyjne kubki sceny — BeerPong.BuildCups i tak je podmienia na 10 w runtime
        for (int t = 0; t < 2; t++)
        {
            int i = 0;
            for (int row = 0; row < 5; row++)
                for (int k = 0; k < 5 - row; k++, i++)
                {
                    var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(cup.GetComponent<Collider>());
                    cup.name = $"Cup_{t}_{i}";
                    float x = (k - (5 - row - 1) / 2f) * 0.2f;
                    float z = 3.55f - row * 0.174f; // rzędy co r*sqrt(3) jak w trójkącie
                    cup.transform.position = new Vector3(x, 1.12f, (t == 0 ? -1f : 1f) * z);
                    cup.transform.localScale = new Vector3(0.2f, 0.11f, 0.2f);
                }
        }
        EditorSceneManager.SaveScene(s, "Assets/Scenes/Arena_BeerPong.unity");
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

    // Prototyp 8: model postaci (Assets/3D/Guy.fbx, rig AccuRig) zamiast klocków.
    // Zdejmuje klockowe części z Body, wstawia model, skaluje do 1.8 m, stopy na
    // ziemi. PlayerLimbs sam znajduje kości CC_Base_*. Idempotentne.
    public static void SetupCharacterModel()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D/Guy.fbx");
        if (model == null) { Debug.LogError("[Bootstrap] Brak Assets/3D/Guy.fbx"); EditorApplication.Exit(1); return; }

        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        var body = player.transform.Find("Body");
        foreach (var n in new[] { "Torso", "Head", "ArmL", "ArmR", "LegL", "LegR", "Guy" })
        {
            var t = body.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, body);
        inst.name = "Guy";
        inst.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        // Poza bazowa przychodzi WYPIECZONA z Blendera (export3: pose mode -> rest);
        // bootstrap ani gra niczego nie doginają. Ręczne poprawki: kości CC_Base_*
        // w prefabie albo Pose Mode w Blenderze + reeksport.

        // Unity nie podpina tekstur osadzonych w FBX z Blendera — jawny URP Lit
        // z albedo wyciągniętym z .blend (GuyAlbedo.png)
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/3D/GuyMat.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/3D/GuyAlbedo.png") };
            AssetDatabase.CreateAsset(mat, "Assets/3D/GuyMat.mat");
        }
        foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;

        Bounds B()
        {
            var b = new Bounds(inst.transform.position, Vector3.zero);
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            return b;
        }
        var b0 = B();
        inst.transform.localScale *= 1.8f / b0.size.y;
        var b1 = B();
        // stopy na y=0 roota; b1.min.y to świat == lokal Body przesunięty o +1,
        // więc wystarczy zniwelować sam b1.min.y (Body bez obrotu i skali).
        // Dodatkowo w dół o skinWidth — CharacterController zatrzymuje kapsułę
        // tyle nad podłożem i bez tego stopy lewitują. X/Z na środek bounds —
        // pivot z Blendera nie musi siedzieć w środku bryły.
        float skin = player.GetComponent<CharacterController>().skinWidth;
        inst.transform.localPosition = new Vector3(-b1.center.x, -b1.min.y - skin, -b1.center.z);
        Debug.Log($"[Bootstrap] Guy: przed={b0.size}, po={B().size}, scale={inst.transform.localScale.x:F4}");

        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] SetupCharacterModel OK");
    }

    // Wyśrodkowanie istniejącego modelu w prefabie na osi postaci (X/Z wg środka
    // bounds; Y nie ruszamy — wysokość stroi użytkownik ręcznie w prefabie).
    public static void CenterCharacterModel()
    {
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        var guy = player.transform.Find("Body/Guy");
        var rs = guy.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        var lp = guy.localPosition;
        guy.localPosition = new Vector3(lp.x - b.center.x, lp.y, lp.z - b.center.z);
        Debug.Log($"[Bootstrap] Center: oś była zbita o ({b.center.x:F3}, {b.center.z:F3}), Guy teraz {guy.localPosition}");
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
    }

    // Arena Lucky Shot pod "MENEL MÓWI": jeden okrągły stół na środku, kieliszek
    // na blacie, gracze dookoła (domyślny krąg Competition). Idempotentne.
    public static void SetupLuckyShotArena()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Arena_LuckyShot.unity");
        foreach (var n in new[] { "ShotTable", "Shot_0", "Shot_1", "Shot_2", "Shot_3",
                                  "Shot_4", "Shot_5", "Shot_6", "Shot_7", "BigTable" })
        {
            var g = GameObject.Find(n);
            if (g != null) Object.DestroyImmediate(g);
        }
        var table = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        table.name = "BigTable";
        table.transform.position = new Vector3(0f, 0.45f, 0f);
        table.transform.localScale = new Vector3(3f, 0.45f, 3f); // średnica 3 m
        var glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.DestroyImmediate(glass.GetComponent<Collider>());
        glass.name = "Shot_0"; // LuckyShot.StartDrinkAnim szuka Shot_i
        glass.transform.position = new Vector3(0f, 0.98f, 0f);
        glass.transform.localScale = new Vector3(0.07f, 0.06f, 0.07f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] SetupLuckyShotArena OK");
    }

    // Butelka.fbx jako wizual piwa: podmienia walec w Beer.prefab oraz butelkę
    // w ręce gracza (HandBottle) — podpiętą pod kość dłoni CC_Base_R_Hand,
    // więc rusza się razem z ręką (chód, emotki). Idempotentne.
    public static void SetupBottleAssets()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D/Butelka.fbx");
        if (model == null) { Debug.LogError("[Bootstrap] Brak Assets/3D/Butelka.fbx"); EditorApplication.Exit(1); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/3D/ButelkaMat.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/3D/ButelkaAlbedo.png") };
            AssetDatabase.CreateAsset(mat, "Assets/3D/ButelkaMat.mat");
        }

        Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }
        // model butelki leży w pliku wzdłuż Z — obróć najdłuższą oś do pionu,
        // potem skaluj do zadanej wysokości
        GameObject Spawn(Transform parent, float height)
        {
            var g = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            foreach (var r in g.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
            var b = BoundsOf(g);
            if (b.size.z >= b.size.x && b.size.z >= b.size.y)
                g.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f) * g.transform.localRotation;
            else if (b.size.x >= b.size.y && b.size.x >= b.size.z)
                g.transform.localRotation = Quaternion.Euler(0f, 0f, 90f) * g.transform.localRotation;
            b = BoundsOf(g);
            g.transform.localScale *= height / b.size.y;
            return g;
        }

        // --- Beer.prefab: butelka zamiast walca, stoi na ziemi, 45 cm ---
        var beer = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Beer.prefab");
        foreach (var n in new[] { "Bottle", "Butelka" })
        {
            var t = beer.transform.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        var vis = Spawn(beer.transform, 0.45f);
        vis.name = "Butelka";
        var vb = BoundsOf(vis);
        vis.transform.localPosition -= new Vector3(vb.center.x, vb.min.y, vb.center.z);
        PrefabUtility.SaveAsPrefabAsset(beer, "Assets/Prefabs/Beer.prefab");
        PrefabUtility.UnloadPrefabContents(beer);

        // --- Player.prefab: butelka w dłoni (kość CC_Base_R_Hand) ---
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        Transform oldHb = null, hand = null;
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "HandBottle") oldHb = t;
            if (t.name == "CC_Base_R_Hand") hand = t;
        }
        if (oldHb != null) Object.DestroyImmediate(oldHb.gameObject);
        if (hand == null)
        {
            Debug.LogError("[Bootstrap] Brak kości CC_Base_R_Hand w prefabie gracza");
            PrefabUtility.UnloadPrefabContents(player);
            EditorApplication.Exit(1);
            return;
        }
        // pod rootem gracza (uniform skala!), FollowBone dokleja do kości w LateUpdate
        var hb = Spawn(player.transform, 0.3f); // 30 cm butelka w garści
        hb.name = "HandBottle";
        var hbB = BoundsOf(hb);
        // dłoń trzyma za szyjkę (punkt 1/3 od góry), lekko przed dłonią
        Vector3 grip = hbB.center + Vector3.up * (hbB.size.y / 6f);
        Vector3 wantPos = hand.position + player.transform.forward * 0.08f;
        hb.transform.position += wantPos - grip;
        var fb = hb.AddComponent<FollowBone>();
        fb.bone = hand;
        fb.posOffset = Quaternion.Inverse(hand.rotation) * (hb.transform.position - hand.position);
        fb.rotOffset = Quaternion.Inverse(hand.rotation) * hb.transform.rotation;
        Debug.Log($"[Bootstrap] HandBottle: bounds={BoundsOf(hb).size}");
        hb.SetActive(false); // DrunkSystem włącza, gdy Beers > 0
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] SetupBottleAssets OK");
    }

    // Podłoga referencyjna w prefabie gracza: pokazuje, gdzie w grze jest ziemia
    // (CharacterController wisi skinWidth nad podłożem, więc płaszczyzna siedzi
    // skinWidth POD rootem). Stopy modelu (Body/Guy) ustawiaj tak, żeby jej
    // dotykały. Uwaga: tag EditorOnly NIE wycina obiektów z prefabów spawnowanych
    // w runtime — dlatego PlayerController.Awake niszczy GroundRef w grze.
    public static void AddGroundRef()
    {
        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        if (player.transform.Find("GroundRef") == null)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(g.GetComponent<Collider>()); // sama grafika, zero fizyki
            g.name = "GroundRef";
            g.tag = "EditorOnly";
            g.transform.SetParent(player.transform, false);
            g.transform.localPosition =
                new Vector3(0f, -player.GetComponent<CharacterController>().skinWidth, 0f);
            g.transform.localScale = new Vector3(0.3f, 1f, 0.3f); // 3x3 m
        }
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] GroundRef OK");
    }

    // Podgląd prefabu gracza do PNG (przód i bok) — weryfikacja skali/orientacji
    // bez odpalania gry. Wymaga -batchmode BEZ -nographics.
    public static void RenderPlayerPreview()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
            if (t.name == "HandBottle") t.gameObject.SetActive(true); // pokaż butelkę w dłoni
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        // światło od przodu — twarz ma być widoczna na podglądzie
        GameObject.Find("Directional Light").transform.rotation = Quaternion.Euler(40f, 190f, 0f);

        var camGO = new GameObject("PreviewCam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.3f, 0.5f, 0.7f);
        var rt = new RenderTexture(768, 1024, 24);
        cam.targetTexture = rt;

        void Shot(Vector3 pos, string file)
        {
            camGO.transform.position = pos;
            camGO.transform.LookAt(new Vector3(0f, 1f, 0f));
            cam.Render(); // rozgrzewka: pierwszy render bywa bez dogranych tekstur
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[Bootstrap] Podgląd: " + file);
        }
        Shot(new Vector3(0f, 1.2f, 3.2f), "preview_front.png");  // od +Z: widać przód gracza (gracz patrzy w +Z)
        Shot(new Vector3(3.2f, 1.2f, 0f), "preview_side.png");
        EditorApplication.Exit(0);
    }

    // Prototyp 9: model z garderobą (Assets/3D/GuyWardrobe.fbx = Guy-final3-dodattki-bones)
    // — goła postać + 12 ciuchów przełączanych w Szatni na hubie. Nazwy węzłów FBX to
    // GUID-y tripo; mapowanie na czytelne nazwy zdjęte z Guy-final3-dodatki.blend
    // (parowanie po GUID + liczbie wierzchołków). Tekstury: Assets/3D/Wardrobe/*.png.
    // Idempotentne.
    public static void SetupWardrobe()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D/GuyWardrobe.fbx");
        if (model == null) { Debug.LogError("[Bootstrap] Brak Assets/3D/GuyWardrobe.fbx"); EditorApplication.Exit(1); return; }

        (string node, int item, string tex)[] map =
        {
            ("tripo_mesh_ce2ee2a1_bb47_4019_9aad_9705bb4ff2c9_001", -1, "Body"), // goła postać
            ("tripo_mesh_9768b4b7_3ed7_40cb_9ddb_4435c65f2467_001",  0, "Zbroja"),
            ("tripo_mesh_1fb15a8b_e833_4799_80cb_c2692d23f125_002",  1, "Szata"),
            ("tripo_mesh_8ed0fdbf_7ab3_4bb9_916c_4c29f158191f_001",  2, "Cezar"),
            ("tripo_mesh_781fccbd_390a_4e0d_ae7b_dc193de96952_001",  3, "Sparta"),
            ("tripo_mesh_2948f07b_de3b_4616_a559_874049ebe5fd_001",  4, "Laur"),
            ("tripo_mesh_e76df342_49f8_41b2_8e87_7965c9e0f816_001",  5, "WieniecOliwny"),
            ("tripo_mesh_aed41164_918c_4a06_a925_b86c9bec5ec2_001",  6, "Asterix"),
            ("tripo_mesh_e3b31c4b_c0ba_4135_a2d4_40ab57ccbba2_002",  7, "AsterixUbranie"), // koszulka
            ("tripo_mesh_e3b31c4b_c0ba_4135_a2d4_40ab57ccbba2_003",  8, "AsterixUbranie"), // spodnie
            ("tripo_mesh_2304081b_0a10_4a98_9448_b516f51fdd4d_001",  9, "AsterixCzapka"),
            ("tripo_mesh_778a004b_b44b_4f5a_8bd9_7e078e04b38b_002", 10, "Niewolnik"), // góra
            ("tripo_mesh_778a004b_b44b_4f5a_8bd9_7e078e04b38b_003", 10, "Niewolnik"), // dół
            ("tripo_mesh_c78ada05_622f_40d0_8087_08b2b845e972_002", 11, "Buty"), // lewy
            ("tripo_mesh_c78ada05_622f_40d0_8087_08b2b845e972_003", 11, "Buty"), // prawy
        };
        string[] itemNames = { "Zbroja", "Szata", "Cezar", "Sparta", "Laur", "Wieniec oliwny",
            "Asterix", "Koszulka Asterixa", "Spodnie Asterixa", "Czapka Asterixa",
            "Strój niewolnika", "Buty" };
        // sloty: 0=Głowa 1=Strój 2=Spodnie 3=Pas 4=Buty — jedna rzecz na slot (PlayerOutfit.Toggle)
        int[] itemSlots = { 1, 1, 1, 0, 0, 0, 3, 1, 2, 0, 1, 4 };

        var player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/Player.prefab");
        var body = player.transform.Find("Body");
        foreach (var n in new[] { "Torso", "Head", "ArmL", "ArmR", "LegL", "LegR", "Guy" })
        {
            var t = body.Find(n);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }

        var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, body);
        inst.name = "Guy";
        inst.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        // URP Lit z albedo per ciuch; Cull Off — cienkie ubrania widoczne od środka
        Material Mat(string tex)
        {
            string path = $"Assets/3D/Wardrobe/{tex}Mat.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/3D/Wardrobe/{tex}.png");
                if (t == null) Debug.LogError($"[Bootstrap] Brak tekstury Assets/3D/Wardrobe/{tex}.png");
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { mainTexture = t };
                m.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
                AssetDatabase.CreateAsset(m, path);
            }
            return m;
        }

        var rs = inst.GetComponentsInChildren<Renderer>(true);
        Renderer bodyR = null;
        var pieces = new System.Collections.Generic.List<GameObject>();
        var pieceItem = new System.Collections.Generic.List<int>();
        foreach (var (node, item, tex) in map)
        {
            var r = rs.FirstOrDefault(x => x.name == node);
            if (r == null)
            {
                Debug.LogError("[Bootstrap] Brak mesha " + node);
                PrefabUtility.UnloadPrefabContents(player);
                EditorApplication.Exit(1);
                return;
            }
            r.sharedMaterial = Mat(tex);
            if (item < 0) bodyR = r;
            else { pieces.Add(r.gameObject); pieceItem.Add(item); }
        }

        // skala i pozycja po samym "body" — ciuchy (hełmy!) wystają ponad głowę
        // i liczone razem zaniżyłyby wzrost postaci
        var b0 = bodyR.bounds;
        inst.transform.localScale = Vector3.one * (1.8f / b0.size.y);
        var b1 = bodyR.bounds;
        float skin = player.GetComponent<CharacterController>().skinWidth;
        inst.transform.localPosition = new Vector3(-b1.center.x, -b1.min.y - skin, -b1.center.z);

        // FBX z AccuRig przychodzi w T-pozie — opuść ramiona (na zewnątrz tułowia,
        // znak strony liczony z pozycji barku, nie zgadywany z osi)
        Transform Deep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
        foreach (var side in new[] { "L", "R" })
        {
            Transform up = Deep(inst.transform, $"CC_Base_{side}_Upperarm");
            Transform hand = Deep(inst.transform, $"CC_Base_{side}_Hand");
            if (up == null || hand == null)
            { Debug.LogWarning($"[Bootstrap] Brak kości ramienia {side}"); continue; }
            Vector3 cur = hand.position - up.position;
            if (cur.normalized.y < -0.85f) continue; // już opuszczone (rerun)
            float ox = Mathf.Sign(up.position.x - body.position.x);
            Vector3 want = new Vector3(ox * 0.25f, -1f, 0f).normalized;
            up.rotation = Quaternion.FromToRotation(cur.normalized, want) * up.rotation;
        }

        var outfit = player.GetComponent<PlayerOutfit>();
        if (outfit == null) outfit = player.AddComponent<PlayerOutfit>();
        outfit.itemNames = itemNames;
        outfit.itemSlot = itemSlots;
        outfit.pieces = pieces.ToArray();
        outfit.pieceItem = pieceItem.ToArray();
        foreach (var p in pieces) p.SetActive(false); // domyślnie goły

        Debug.Log($"[Bootstrap] GuyWardrobe: body={b0.size}, scale={inst.transform.localScale.x:F4}, ciuchów={pieces.Count}");
        PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/Player.prefab");
        PrefabUtility.UnloadPrefabContents(player);

        // butelka w dłoni od nowa — stara referencja FollowBone.bone umarła razem
        // ze starym modelem, a nowy rig ma świeżą kość CC_Base_R_Hand
        SetupBottleAssets();

        // budka Szatni na hubie (z dala od stanowisk konkurencji)
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");
        if (Object.FindFirstObjectByType<WardrobeShop>() == null)
        {
            var stall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stall.name = "Szatnia";
            stall.transform.position = new Vector3(-6f, 1f, -26f);
            stall.transform.localScale = new Vector3(3f, 2f, 1.5f);
            var sm = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { color = new Color(0.55f, 0.35f, 0.75f) };
            AssetDatabase.CreateAsset(sm, "Assets/Prefabs/SzatniaMat.mat");
            stall.GetComponent<Renderer>().sharedMaterial = sm;
            stall.AddComponent<WardrobeShop>();

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var lbl = new GameObject("Label_Szatnia");
            lbl.transform.position = stall.transform.position + Vector3.up * 2.2f;
            var tmp = lbl.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = "SZATNIA";
            tmp.fontSize = 10;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(12f, 1.6f);
            lbl.AddComponent<Billboard>();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[Bootstrap] SetupWardrobe OK");
    }

    // Wyspa Michałka jako podłoże huba: mesh z wypieczonym reliefem (Assets/3D/Wyspa.fbx
    // — bake displacementu i koloru z "Wyspa_michałka .blend", patrz scripts w scratchpadzie
    // sesji; shader był proceduralny, więc zwykły eksport dałby płaską płytę). Teren ma
    // ~18 m wzniesienia: dno morza ląduje na y=-2.2 (pod wodą i pod progiem respawnu -1.5),
    // a wszystko co stało na płaskiej podłodze (stanowiska, pickupy, kolumny, szatnia,
    // szyldy) jest doklejane do powierzchni raycastem na stałych ofsetach. Idempotentne.
    public static void SetupIslandHub()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/3D/Wyspa.fbx");
        if (model == null) { Debug.LogError("[Bootstrap] Brak Assets/3D/Wyspa.fbx"); EditorApplication.Exit(1); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/3D/WyspaMat.mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            { mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/3D/WyspaAlbedo.png") };
            AssetDatabase.CreateAsset(mat, "Assets/3D/WyspaMat.mat");
        }

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");
        var old = GameObject.Find("Island");
        if (old != null) Object.DestroyImmediate(old);

        var island = (GameObject)PrefabUtility.InstantiatePrefab(model);
        island.name = "Island";
        island.transform.position = new Vector3(0f, -2.2f, 0f); // płaskie dno pod wodą (-0.8)
        foreach (var r in island.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
        MeshCollider col = null;
        foreach (var mf in island.GetComponentsInChildren<MeshFilter>())
            col = mf.gameObject.AddComponent<MeshCollider>();
        Physics.SyncTransforms();

        float H(float x, float z) =>
            col.Raycast(new Ray(new Vector3(x, 500f, z), Vector3.down), out var hit, 1000f)
                ? hit.point.y : 0f;

        // stały ofset nad terenem per typ obiektu (idempotentnie — bez dziedziczenia po starym y)
        float Off(GameObject go) =>
            go.GetComponent<CompetitionStation>() != null ? 0.25f
            : go.name == "Szatnia" ? 1f
            : go.name == "Label_Szatnia" ? 3.2f
            : go.name.StartsWith("Label_") ? 3.45f
            : go.name.StartsWith("Column_") ? 2f
            : go.name == "TempleBlock" ? 1.5f
            : 0f; // pickupy stoją na ziemi

        foreach (var go in scene.GetRootGameObjects())
        {
            bool snap = go.GetComponent<CompetitionStation>() != null
                || go.GetComponent<BeerPickup>() != null || go.GetComponent<PillPickup>() != null
                || go.GetComponent<CigarettePickup>() != null || go.GetComponent<SnackPickup>() != null
                || go.name == "Szatnia" || go.name == "TempleBlock"
                || go.name.StartsWith("Label_") || go.name.StartsWith("Column_");
            if (!snap) continue;
            var p = go.transform.position;
            go.transform.position = new Vector3(p.x, H(p.x, p.z) + Off(go), p.z);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[Bootstrap] SetupIslandHub OK: środek={H(0f, 0f):F2}, spawn={H(0f, -5f):F2}, "
            + $"stacja(0,18)={H(0f, 18f):F2}, stacja(26,13)={H(26f, 13f):F2}, szatnia={H(-6f, -26f):F2}");
    }

    // Podgląd huba z lotu ptaka do PNG — weryfikacja terenu bez odpalania gry.
    // Wymaga -batchmode BEZ -nographics.
    public static void RenderHubPreview()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Hub.unity");
        var camGO = new GameObject("HubPreviewCam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.4f, 0.6f, 0.85f);
        cam.farClipPlane = 2000f;
        var rt = new RenderTexture(1280, 960, 24);
        cam.targetTexture = rt;
        void Shot(Vector3 pos, Vector3 look, string file)
        {
            camGO.transform.position = pos;
            camGO.transform.LookAt(look);
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[Bootstrap] Podgląd: " + file);
        }
        Shot(new Vector3(0f, 120f, -220f), new Vector3(0f, 10f, 0f), "hub_far.png");
        Shot(new Vector3(0f, 45f, -70f), new Vector3(0f, 14f, 0f), "hub_near.png");
        Shot(new Vector3(-20f, 25f, -40f), new Vector3(-6f, 15f, -26f), "hub_szatnia.png");
        EditorApplication.Exit(0);
    }

    // Podgląd garderoby do PNG: goły / wszystko naraz / zestaw Asterixa.
    // Wymaga -batchmode BEZ -nographics.
    public static void RenderWardrobePreview()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        GameObject.CreatePrimitive(PrimitiveType.Plane);
        GameObject.Find("Directional Light").transform.rotation = Quaternion.Euler(40f, 190f, 0f);

        var camGO = new GameObject("PreviewCam");
        var cam = camGO.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.3f, 0.5f, 0.7f);
        var rt = new RenderTexture(768, 1024, 24);
        cam.targetTexture = rt;

        var outfit = player.GetComponent<PlayerOutfit>();
        void Dress(params int[] items)
        {
            for (int i = 0; i < outfit.pieces.Length; i++)
                outfit.pieces[i].SetActive(System.Array.IndexOf(items, outfit.pieceItem[i]) >= 0);
        }
        void Shot(Vector3 pos, string file)
        {
            camGO.transform.position = pos;
            camGO.transform.LookAt(new Vector3(0f, 1f, 0f));
            cam.Render();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            Debug.Log("[Bootstrap] Podgląd: " + file);
        }
        Dress(); // goły (domyślny stan i tak ma wszystko zgaszone)
        Shot(new Vector3(0f, 1.2f, 3.2f), "preview_naked.png");
        Shot(new Vector3(3.2f, 1.2f, 0f), "preview_naked_side.png");
        Dress(0, 3, 11); // zbroja + hełm sparty + buty
        Shot(new Vector3(0f, 1.2f, 3.2f), "preview_warrior.png");
        Dress(6, 7, 8, 9); // pełny Asterix
        Shot(new Vector3(0f, 1.2f, 3.2f), "preview_asterix.png");
        Dress(1, 4); // szata + laur
        Shot(new Vector3(0f, 1.2f, 3.2f), "preview_cezar.png");
        EditorApplication.Exit(0);
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
