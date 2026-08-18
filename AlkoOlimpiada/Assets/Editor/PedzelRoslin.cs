using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class PedzelRoslin : EditorWindow
{
    [SerializeField] List<GameObject> prefaby = new List<GameObject>();
    [SerializeField] float promien = 1.5f;
    [SerializeField] float zageszczenie = 25f;
    [SerializeField] float odstep = 0.15f;
    [SerializeField] float skalaMin = 0.7f;
    [SerializeField] float skalaMax = 1.3f;
    [SerializeField] float dopasujDoNormalnej = 0.5f;
    [SerializeField] float maxNachylenie = 40f;
    [SerializeField] string nazwaKontenera = "Trawa";
    [SerializeField] LayerMask maskaPodloza = ~0;
    [SerializeField] bool statyczne = true;
    [SerializeField] bool maluje;

    static readonly string[] domyslneTrawy =
    {
        "Assets/3D/MapKit/trawa/trawa-krzakowa.glb",
        "Assets/3D/MapKit/trawa/trawa-krzakowa-b.glb",
        "Assets/3D/MapKit/trawa/trawa-krzakowa-c.glb",
    };

    SerializedObject so;
    Transform kontener;
    Dictionary<Vector2Int, List<Vector3>> siatka;

    bool Gotowy => prefaby.Exists(p => p != null);

    [MenuItem("Tools/Pędzel roślin")]
    static void Otworz() => GetWindow<PedzelRoslin>("Pędzel roślin");

    void OnEnable()
    {
        so = new SerializedObject(this);
        if (!Gotowy) WczytajDomyslne();
        SceneView.duringSceneGui += NaScenie;
    }

    void WczytajDomyslne()
    {
        prefaby.Clear();
        foreach (var sciezka in domyslneTrawy)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(sciezka);
            if (go != null) prefaby.Add(go);
            else Debug.LogWarning($"Pędzel roślin: brak {sciezka}");
        }
    }
    void OnDisable() { SceneView.duringSceneGui -= NaScenie; }

    void OnGUI()
    {
        so.Update();
        EditorGUILayout.PropertyField(so.FindProperty("prefaby"), new GUIContent("Prefaby (losowo)"), true);
        EditorGUILayout.PropertyField(so.FindProperty("promien"), new GUIContent("Promień pędzla"));
        EditorGUILayout.PropertyField(so.FindProperty("zageszczenie"), new GUIContent("Zagęszczenie (szt./m²)"));
        EditorGUILayout.PropertyField(so.FindProperty("odstep"), new GUIContent("Min. odstęp"));
        EditorGUILayout.LabelField(" ", $"odstęp {odstep:0.00} m nasyca się przy ~{Nasycenie(odstep):0} szt./m²",
                                   EditorStyles.miniLabel);
        EditorGUILayout.PropertyField(so.FindProperty("skalaMin"), new GUIContent("Skala min"));
        EditorGUILayout.PropertyField(so.FindProperty("skalaMax"), new GUIContent("Skala max"));
        EditorGUILayout.PropertyField(so.FindProperty("dopasujDoNormalnej"), new GUIContent("Pochyl wg podłoża"));
        EditorGUILayout.PropertyField(so.FindProperty("maxNachylenie"), new GUIContent("Max nachylenie (°)"));
        EditorGUILayout.PropertyField(so.FindProperty("maskaPodloza"), new GUIContent("Warstwy podłoża"));
        EditorGUILayout.PropertyField(so.FindProperty("nazwaKontenera"), new GUIContent("Kontener"));
        EditorGUILayout.PropertyField(so.FindProperty("statyczne"), new GUIContent("Oznacz jako Static"));
        so.ApplyModifiedProperties();

        GUILayout.Space(8);
        if (GUILayout.Button("Wstaw domyślne kępki trawy")) { WczytajDomyslne(); so.Update(); }
        if (!Gotowy)
        {
            EditorGUILayout.HelpBox("Lista prefabów jest pusta albo same sloty NULL – kliknij przycisk wyżej "
                                    + "albo przeciągnij tu własne prefaby. Bez tego pędzel nic nie postawi.",
                                    MessageType.Warning);
            maluje = false;
        }
        using (new EditorGUI.DisabledScope(!Gotowy))
        {
            GUI.backgroundColor = maluje ? Color.green : Color.white;
            if (GUILayout.Button(maluje ? "MALUJĘ – kliknij by wyłączyć" : "Włącz malowanie", GUILayout.Height(32)))
            {
                maluje = !maluje;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = Color.white;
        }
        EditorGUILayout.HelpBox("LPM – maluj, Shift+LPM – kasuj. Ctrl+Z cofa.", MessageType.None);
    }

    void NaScenie(SceneView sv)
    {
        if (!maluje || !Gotowy) return;
        var e = Event.current;
        int id = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout) { HandleUtility.AddDefaultControl(id); return; }

        Handles.BeginGUI();
        GUI.Label(new Rect(8, 8, 320, 20), "Pędzel roślin: LPM maluj / Shift+LPM kasuj", EditorStyles.whiteBoldLabel);
        Handles.EndGUI();

        var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        // ponytail: brak trafienia = kursor poza podlozem, ale etykieta juz sie narysowala
        if (!Physics.Raycast(ray, out var hit, 5000f, maskaPodloza)) return;

        Handles.color = e.shift ? new Color(1f, 0.3f, 0.2f, 1f) : new Color(0.3f, 1f, 0.3f, 1f);
        Handles.DrawWireDisc(hit.point, hit.normal, promien);
        sv.Repaint();

        bool start = e.type == EventType.MouseDown && e.button == 0 && !e.alt;
        bool ciag = e.type == EventType.MouseDrag && GUIUtility.hotControl == id;
        if (start) { GUIUtility.hotControl = id; PrzeliczSiatke(); }
        if (start || ciag)
        {
            if (e.shift) Kasuj(hit.point); else Maluj(hit.point, hit.normal);
            e.Use();
        }
        else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
        {
            GUIUtility.hotControl = 0;
            e.Use();
        }
    }

    void Maluj(Vector3 srodek, Vector3 normalna)
    {
        var t = Vector3.Cross(normalna, Vector3.up);
        if (t.sqrMagnitude < 1e-4f) t = Vector3.Cross(normalna, Vector3.forward);
        t.Normalize();
        var b = Vector3.Cross(normalna, t);

        // ponytail: 0.3 celu na zdarzenie drag - jeden powolny przejazd nasyca, szybki mazie rzadziej
        int proby = Mathf.Clamp(Mathf.CeilToInt(zageszczenie * Mathf.PI * promien * promien * 0.3f), 1, 400);
        for (int i = 0; i < proby; i++)
        {
            var d = Random.insideUnitCircle * promien;
            var kandydat = srodek + t * d.x + b * d.y;
            // ponytail: rzut w dół świata, nie wzdłuż normalnej - trawa i tak nie rośnie na pionie
            if (!Physics.Raycast(kandydat + Vector3.up * 2f, Vector3.down, out var h, 6f, maskaPodloza)) continue;
            if (Vector3.Angle(h.normal, Vector3.up) > maxNachylenie) continue;
            if (ZaBlisko(siatka, h.point, odstep)) continue;

            var prefab = prefaby[Random.Range(0, prefaby.Count)];
            if (prefab == null) continue;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, Kontener().gameObject.scene);
            go.transform.SetParent(Kontener(), true);
            go.transform.position = h.point;
            go.transform.rotation = Obrot(h.normal, Random.value * 360f, dopasujDoNormalnej);
            go.transform.localScale = Vector3.one * Random.Range(skalaMin, skalaMax);
            if (statyczne) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            Undo.RegisterCreatedObjectUndo(go, "Malowanie roślin");
            Dodaj(siatka, h.point, odstep);
        }
    }

    void Kasuj(Vector3 srodek)
    {
        var k = Kontener();
        for (int i = k.childCount - 1; i >= 0; i--)
        {
            var c = k.GetChild(i);
            if ((c.position - srodek).sqrMagnitude <= promien * promien)
                Undo.DestroyObjectImmediate(c.gameObject);
        }
    }

    Transform Kontener()
    {
        if (kontener != null && kontener.gameObject.scene.isLoaded) return kontener;
        var go = GameObject.Find(nazwaKontenera);
        if (go == null)
        {
            go = new GameObject(nazwaKontenera);
            Undo.RegisterCreatedObjectUndo(go, "Kontener roślin");
        }
        return kontener = go.transform;
    }

    void PrzeliczSiatke()
    {
        siatka = new Dictionary<Vector2Int, List<Vector3>>();
        var k = Kontener();
        for (int i = 0; i < k.childCount; i++) Dodaj(siatka, k.GetChild(i).position, odstep);
    }

    // --- czysta logika, testowalna ---

    // gestosc nasycenia losowego rozsiewu przy minimalnym odstepie r (~0.75/r^2)
    public static float Nasycenie(float odstep) => odstep <= 0f ? 0f : 0.75f / (odstep * odstep);

    public static Quaternion Obrot(Vector3 normalna, float yaw, float dopasowanie)
    {
        var pochyl = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(Vector3.up, normalna),
                                      Mathf.Clamp01(dopasowanie));
        return pochyl * Quaternion.Euler(0f, yaw, 0f);
    }

    static Vector2Int Komorka(Vector3 p, float bok) => new Vector2Int(Mathf.FloorToInt(p.x / bok), Mathf.FloorToInt(p.z / bok));

    public static bool ZaBlisko(Dictionary<Vector2Int, List<Vector3>> siatka, Vector3 p, float odstep)
    {
        if (odstep <= 0f) return false;
        var c = Komorka(p, Mathf.Max(odstep, 0.01f));
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
                if (siatka.TryGetValue(new Vector2Int(c.x + x, c.y + z), out var lista))
                    foreach (var q in lista)
                        if ((q - p).sqrMagnitude < odstep * odstep) return true;
        return false;
    }

    public static void Dodaj(Dictionary<Vector2Int, List<Vector3>> siatka, Vector3 p, float odstep)
    {
        var c = Komorka(p, Mathf.Max(odstep, 0.01f));
        if (!siatka.TryGetValue(c, out var lista)) siatka[c] = lista = new List<Vector3>();
        lista.Add(p);
    }

    [MenuItem("Tools/Pędzel roślin – autotest")]
    static void Autotest()
    {
        var s = new Dictionary<Vector2Int, List<Vector3>>();
        Debug.Assert(!ZaBlisko(s, Vector3.zero, 0.5f), "pusta siatka nie może blokować");
        Dodaj(s, Vector3.zero, 0.5f);
        Debug.Assert(ZaBlisko(s, new Vector3(0.2f, 0f, 0f), 0.5f), "punkt w promieniu musi blokować");
        Debug.Assert(!ZaBlisko(s, new Vector3(0.6f, 0f, 0f), 0.5f), "punkt poza promieniem nie może blokować");
        Debug.Assert(ZaBlisko(s, new Vector3(-0.2f, 0f, 0f), 0.5f), "sąsiednia komórka (ujemna) musi być sprawdzana");
        Debug.Assert(!ZaBlisko(s, new Vector3(10f, 0f, 0f), 0f), "odstęp 0 = brak blokady");

        var n = new Vector3(1f, 1f, 0f).normalized;
        Debug.Assert(Vector3.Angle(Obrot(n, 90f, 1f) * Vector3.up, n) < 0.01f, "pełne dopasowanie = oś Y wzdłuż normalnej");
        Debug.Assert(Vector3.Angle(Obrot(n, 90f, 0f) * Vector3.up, Vector3.up) < 0.01f, "zero dopasowania = pion");
        Debug.Log("PedzelRoslin: autotest OK");
    }
}
