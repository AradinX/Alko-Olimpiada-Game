using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// System upojenia: poziom 0-100 replikowany z serwera, bujanie kamery u właściciela,
// Zgon przy 100 (utrata kontroli), cucenie przez innego gracza klawiszem E.
public class DrunkSystem : NetworkBehaviour
{
    public float decayPerSecond = 0.4f;
    public float reviveRange = 3f;
    public float reviveTo = 50f;

    // etapy pijaństwa (progi na pasku); bujanie zaczyna się od pierwszego i pogłębia
    public static readonly (float min, string name)[] Stages =
    { (20f, "Szumi"), (45f, "Lekko chycony"), (70f, "Jest ligancko") };

    // aktualne mapowanie ruchu (owner); etap 2 zamienia A/D, etap 3 losuje ukryte klawisze
    public Key keyW = Key.W, keyS = Key.S, keyA = Key.A, keyD = Key.D;

    static readonly Key[] scramblePool = // klawisze nieużywane w grze
    { Key.P, Key.L, Key.M, Key.K, Key.O, Key.I, Key.J, Key.N, Key.B,
      Key.H, Key.G, Key.T, Key.Y, Key.U, Key.V, Key.C, Key.X, Key.Z, Key.Q };

    public int Stage
    {
        get
        {
            for (int i = Stages.Length - 1; i >= 0; i--)
                if (Drunk.Value >= Stages[i].min) return i + 1;
            return 0;
        }
    }

    public float beerStrength = 15f;

    public NetworkVariable<float> Drunk = new();       // 0-100, zapis: serwer
    public NetworkVariable<bool> PassedOut = new();
    public NetworkVariable<int> Beers = new();          // ekwipunek piw

    Transform body;
    Camera cam;
    DrunkSystem reviveTarget; // pobliski leżący gracz (tylko u właściciela)
    BeerPickup nearBeer;      // butelka w zasięgu (tylko u właściciela)
    int lastStage;

    void Awake()
    {
        body = transform.Find("Body");
        cam = GetComponent<PlayerController>().playerCamera;
    }

    public override void OnNetworkSpawn()
    {
        PassedOut.OnValueChanged += (_, v) => SetBodyTipped(v);
        SetBodyTipped(PassedOut.Value);

        // ponytail: headless smoke test pętli Zgon->cucenie (flaga -autodrink)
        if (IsServer && System.Array.IndexOf(
                System.Environment.GetCommandLineArgs(), "-autodrink") >= 0)
        {
            Invoke(nameof(AutoDrink), 3f);
            Invoke(nameof(ReviveRpc), 6f);
        }
    }

    void AutoDrink() => AddDrink(120f);

    // przewrócona kapsuła = leżący pijak, widoczne u wszystkich
    void SetBodyTipped(bool tipped)
    {
        body.localRotation = tipped ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        body.localPosition = new Vector3(0f, tipped ? 0.5f : 1f, 0f);
    }

    // wołane tylko na serwerze (pickupy, później konkurencje)
    public void AddDrink(float amount)
    {
        Drunk.Value = Mathf.Min(100f, Drunk.Value + amount);
        if (Drunk.Value >= 100f) PassedOut.Value = true;
        Debug.Log($"[Drunk] gracz {OwnerClientId}: {Drunk.Value:0}"
                  + (PassedOut.Value ? " ZGON" : ""));
    }

    [Rpc(SendTo.Server)]
    public void ReviveRpc()
    {
        if (!PassedOut.Value) return;
        Drunk.Value = reviveTo;
        PassedOut.Value = false;
        Debug.Log($"[Drunk] gracz {OwnerClientId} ocucony, poziom {Drunk.Value:0}");
    }

    void Update()
    {
        if (IsServer && !PassedOut.Value)
            Drunk.Value = Mathf.Max(0f, Drunk.Value - decayPerSecond * Time.deltaTime);

        if (!IsOwner || PassedOut.Value) { reviveTarget = null; return; }

        int st = Stage;
        if (st != lastStage) { lastStage = st; ApplyControls(st); }

        // ponytail: FindObjectsByType co klatkę — graczy jest max 10, wystarczy
        reviveTarget = null;
        foreach (var d in FindObjectsByType<DrunkSystem>(FindObjectsSortMode.None))
        {
            if (d == this || !d.PassedOut.Value) continue;
            if (Vector3.Distance(d.transform.position, transform.position) <= reviveRange)
            { reviveTarget = d; break; }
        }
        var kb = Keyboard.current;
        if (kb == null) return;
        // [E]: cucenie ma priorytet nad podnoszeniem piwa
        if (kb.eKey.wasPressedThisFrame)
        {
            if (reviveTarget != null) reviveTarget.ReviveRpc();
            else if (nearBeer != null && nearBeer.Available.Value) nearBeer.RequestPickupRpc();
        }
        // [F]: pijesz piwo z ekwipunku; w trakcie konkurencji zablokowane
        if (Beers.Value > 0 && !Competition.InputLocked && kb.fKey.wasPressedThisFrame)
            DrinkBeerRpc();
    }

    void ApplyControls(int stage)
    {
        keyW = Key.W; keyS = Key.S; keyA = Key.A; keyD = Key.D;
        if (stage == 2) { keyA = Key.D; keyD = Key.A; }   // lekko chycony: A/D na odwrót
        else if (stage >= 3)                              // jest ligancko: losowe, ukryte
        {
            var pool = new System.Collections.Generic.List<Key>(scramblePool);
            Key Take() { var k = pool[Random.Range(0, pool.Count)]; pool.Remove(k); return k; }
            keyW = Take(); keyS = Take(); keyA = Take(); keyD = Take();
        }
    }

    [Rpc(SendTo.Server)]
    public void DrinkBeerRpc()
    {
        if (PassedOut.Value || Beers.Value <= 0) return;
        Beers.Value--;
        AddDrink(beerStrength);
    }

    // trigger łapie właściciel (CC.Move działa tylko u niego), serwer waliduje przy piciu
    void OnTriggerEnter(Collider other)
    {
        if (!IsOwner) return;
        var beer = other.GetComponentInParent<BeerPickup>();
        if (beer != null) nearBeer = beer;
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsOwner) return;
        if (other.GetComponentInParent<BeerPickup>() == nearBeer) nearBeer = null;
    }

    void LateUpdate()
    {
        if (!IsOwner) return;
        if (PassedOut.Value)
        {
            cam.transform.localRotation = Quaternion.Euler(0f, 0f, 75f); // leżysz
            return;
        }
        float t = Mathf.InverseLerp(Stages[0].min, 100f, Drunk.Value); // "Szumi" otwiera bujanie
        if (t <= 0f) return;
        // ponytail: bujanie = szum Perlina na rotacji kamery; post-process URP dojdzie,
        // gdy sam sway przestanie wystarczać
        float s = Time.time;
        float roll  = (Mathf.PerlinNoise(s * 0.5f, 0f) - 0.5f) * 24f * t;
        float yaw   = (Mathf.PerlinNoise(0f, s * 0.4f) - 0.5f) * 16f * t;
        float pitch = (Mathf.PerlinNoise(s * 0.3f, 7f) - 0.5f) * 10f * t;
        cam.transform.localRotation *= Quaternion.Euler(pitch, yaw, roll);
    }

    void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;

        // pionowy pasek upojenia po prawej (GDD sekcja 6)
        float h = Screen.height * 0.5f;
        var back = new Rect(Screen.width - 44f, (Screen.height - h) / 2f, 24f, h);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        float fill = h * Drunk.Value / 100f;
        GUI.color = Color.Lerp(Color.green, Color.red, Drunk.Value / 100f);
        GUI.DrawTexture(new Rect(back.x, back.yMax - fill, back.width, fill), Texture2D.whiteTexture);

        // znaczniki etapów na pasku + nazwa etapu (aktywny podświetlony)
        int stage = Stage;
        for (int i = 0; i < Stages.Length; i++)
        {
            float ty = back.yMax - h * Stages[i].min / 100f;
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(back.x - 4f, ty - 1f, back.width + 8f, 2f), Texture2D.whiteTexture);
            bool active = stage == i + 1;
            GUI.color = Color.white;
            GUI.Label(new Rect(back.x - 156f, ty - 11f, 148f, 22f), Stages[i].name,
                new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleRight,
                    fontStyle = active ? FontStyle.Bold : FontStyle.Normal,
                    normal = { textColor = active ? Color.white : new Color(1f, 1f, 1f, 0.45f) }
                });
        }
        GUI.color = Color.white;

        // ekwipunek pod paskiem
        GUI.Label(new Rect(Screen.width - 110f, back.yMax + 6f, 100f, 22f),
            $"Piwa: {Beers.Value}" + (Beers.Value > 0 ? "  [F] pij" : ""),
            new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight });

        var style = new GUIStyle(GUI.skin.label)
        { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var center = new Rect(0, Screen.height * 0.4f, Screen.width, 40);
        if (PassedOut.Value) GUI.Label(center, "ZGON — czekaj aż cię ocucą", style);
        else if (reviveTarget != null) GUI.Label(center, "[E] Ocuć kolegę", style);
        else if (nearBeer != null && nearBeer.Available.Value)
            GUI.Label(center, "[E] Podnieś piwo", style);
    }
}
