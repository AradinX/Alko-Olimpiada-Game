using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// System upojenia: poziom 0-100 replikowany z serwera, bujanie kamery u właściciela,
// Zgon przy 100 (cucenie [E]), etapy psujące sterowanie, ekwipunek piw [E]/[F],
// pigułki [Q], dobrowolne rzyganie [V] (kara za przyłapanie), klątwy z piw specjalnych.
public class DrunkSystem : NetworkBehaviour
{
    public float decayPerSecond = 0.2f;      // pkt 2: trzeźwiejesz wolniej
    public float reviveRange = 3f;
    public float reviveTo = 50f;
    public float beerStrength = 12f;         // 2 piwa Szumi / 4 Lekko chycony / 8 Ligancko / 9 Zgon
    public float vomitDrainPerSecond = 6f;   // im dłużej rzygasz, tym więcej schodzi
    public float catchRadius = 25f;          // zasięg wzroku przy przyłapaniu
    public float catchFov = 40f;             // musi mieć cię w kadrze (stopnie od osi patrzenia)
    public int catchPenalty = 2;
    public int maxBeers = 1;                 // jedno piwo w ręce
    public float spikedExtra = 35f;          // pigułka w piwie
    public float spikedCurseSeconds = 40f;   // klątwa z pigułki działa od razu, przez tyle sekund
    public GameObject beerPrefab;            // wyrzucone piwo ląduje na ziemi (bootstrap)

    // etapy pijaństwa (progi na pasku); bujanie zaczyna się od pierwszego i pogłębia
    public static readonly (float min, string name)[] Stages =
    { (20f, "Szumi"), (45f, "Lekko chycony"), (90f, "Jest ligancko") };

    // aktualne mapowanie ruchu (owner); etap 2 zamienia A/D, etap 3 losuje ukryte klawisze
    public Key keyW = Key.W, keyS = Key.S, keyA = Key.A, keyD = Key.D;

    static readonly Key[] scramblePool = // klawisze nieużywane w grze (bez V/Q/E/F/R/G)
    { Key.P, Key.L, Key.M, Key.K, Key.O, Key.I, Key.J, Key.N, Key.B,
      Key.H, Key.T, Key.Y, Key.U, Key.C, Key.X, Key.Z };

    public NetworkVariable<float> Drunk = new();     // 0-100, zapis: serwer
    public NetworkVariable<float> Floor = new();     // pkt 1: alkohol z konkurencji na stałe
    public NetworkVariable<bool> PassedOut = new();
    public NetworkVariable<bool> Vomiting = new();
    public NetworkVariable<int> Beers = new();       // piwo w ręce (max 1)
    public NetworkVariable<bool> HeldSpecial = new(); // trzymane piwo jest złote
    public NetworkVariable<int> Pills = new();       // ekwipunek pigułek
    // klątwy jako maska bitowa — efekty się kumulują:
    // 1=do góry nogami 2=lowres 4=zoom 8=mały obraz (ekran)
    // 16=mały 32=wielki (Badland) 64=odwrócone sterowanie 128=octodad (WSAD losuje się co 3 s)
    public NetworkVariable<byte> Curse = new();        // z piwa specjalnego, na następną konkurencję
    public NetworkVariable<byte> InstantCurse = new(); // z pigułek, działa od razu
    public NetworkVariable<double> CurseUntil = new(); // do kiedy działa InstantCurse

    public int Stage
    {
        get
        {
            for (int i = Stages.Length - 1; i >= 0; i--)
                if (Drunk.Value >= Stages[i].min) return i + 1;
            return 0;
        }
    }

    Transform body;
    Transform handBottle; // butelka w ręce, widoczna gdy Beers > 0
    Camera cam;
    DrunkSystem reviveTarget; // pobliski leżący gracz (tylko u właściciela)
    BeerPickup nearBeer;
    PillPickup nearPill;
    int lastStage;
    float nextOcto; bool wasOcto;
    string msg; float msgUntil; // komunikaty ("coś było w tym piwie" itp.)

    // serwer
    int spikedBeers;      // piwa z pigułką w ekwipunku — celowo niereplikowane
    bool caughtThisVomit;

    void Awake()
    {
        body = transform.Find("Body");
        handBottle = transform.Find("HandBottle");
        cam = GetComponent<PlayerController>().playerCamera;
    }

    public override void OnNetworkSpawn()
    {
        PassedOut.OnValueChanged += (_, _) => UpdateBodyPose();
        Vomiting.OnValueChanged += (_, _) => UpdateBodyPose();
        // dźwięki 3D — słyszą wszyscy w pobliżu
        PassedOut.OnValueChanged += (_, v) => Sfx.Play(v ? "zgon" : "slap", transform.position);
        Vomiting.OnValueChanged += (_, v) => { if (v) Sfx.Play("vomit", transform.position); };
        UpdateBodyPose();
        Beers.OnValueChanged += (_, v) => { if (handBottle) handBottle.gameObject.SetActive(v > 0); };
        if (handBottle) handBottle.gameObject.SetActive(Beers.Value > 0);

        // ponytail: headless smoke test pętli Zgon->cucenie->wyrzucenie piwa (-autodrink)
        if (IsServer && System.Array.IndexOf(
                System.Environment.GetCommandLineArgs(), "-autodrink") >= 0)
        {
            Invoke(nameof(AutoDrink), 3f);
            Invoke(nameof(ReviveRpc), 6f);
            Invoke(nameof(AutoDrop), 8f);
        }
    }

    void AutoDrink() => AddDrink(120f);
    void AutoDrop() { Beers.Value = 1; DiscardBeerRpc(); } // test spawnu butelki na ziemi

    // leżący pijak (Zgon) albo pochylony rzygacz — widoczne u wszystkich
    void UpdateBodyPose()
    {
        float pitch = PassedOut.Value ? 90f : Vomiting.Value ? 40f : 0f;
        body.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        body.localPosition = new Vector3(0f, PassedOut.Value ? 0.5f : 1f, 0f);
    }

    // ---------- serwerowe API ----------

    public void AddDrink(float amount)
    {
        Drunk.Value = Mathf.Min(100f, Drunk.Value + amount);
        if (Drunk.Value >= 100f && !PassedOut.Value)
        {
            PassedOut.Value = true;
            Debug.Log($"[Drunk] gracz {OwnerClientId}: 100 ZGON");
        }
    }

    // pkt 1: picie w konkurencji podnosi też podłogę — tego już nie wytrzeźwiejesz
    public void AddPermanent(float amount)
    {
        Floor.Value = Mathf.Min(90f, Floor.Value + amount);
        AddDrink(amount);
    }

    // picie przymusowe w konkurencji: pasek pełny = wymioty (spadek do 60/podłogi), nie Zgon
    public void AddCompetitionDrink(float amount)
    {
        if (Drunk.Value + amount >= 100f) Drunk.Value = Mathf.Max(Floor.Value, 60f);
        else AddPermanent(amount);
    }

    public void PickUpBeer(bool spiked, bool special)
    {
        Beers.Value++;
        HeldSpecial.Value = special;
        if (spiked) spikedBeers++;
        if (special)
            MsgOwner("PIWO SPECJALNE w ręce: [F] = x2 punkty w następnej konkurencji"
                     + " + losowa klątwa ekranu, [G] wyrzuć");
    }

    static byte RandomCurseBit() => (byte)(1 << Random.Range(0, 8));

    // maska aktywnych klątw: ze specjalnego (podczas konkurencji) + z pigułek (na czas)
    public byte ActiveCurses()
    {
        var comp = Competition.Current;
        byte a = 0;
        if (comp != null && comp.State.Value == Competition.Phase.Running) a |= Curse.Value;
        if (CurseUntil.Value > 0 && NetworkManager.ServerTime.Time < CurseUntil.Value)
            a |= InstantCurse.Value;
        return a;
    }

    public bool CurseActive(byte bit) => (ActiveCurses() & bit) != 0;

    // pijacki zygzak (Sea of Thieves): ruch znosi na boki tym mocniej, im bardziej pijany
    public float VeerAngle() =>
        (Mathf.PerlinNoise(Time.time * 0.35f, 42f) - 0.5f) * 2f * 30f
        * Mathf.InverseLerp(Stages[0].min, 100f, Drunk.Value);

    public void MsgOwner(string m) => MsgRpc(m);

    [Rpc(SendTo.Owner)]
    void MsgRpc(string m) { msg = m; msgUntil = Time.time + 4f; }

    [Rpc(SendTo.Server)]
    public void ReviveRpc()
    {
        if (!PassedOut.Value) return;
        Drunk.Value = Mathf.Max(reviveTo, Floor.Value);
        PassedOut.Value = false;
        Debug.Log($"[Drunk] gracz {OwnerClientId} ocucony, poziom {Drunk.Value:0}");
    }

    [Rpc(SendTo.Server)]
    public void DrinkBeerRpc()
    {
        if (PassedOut.Value || Beers.Value <= 0) return;
        Beers.Value--;
        bool special = HeldSpecial.Value;
        HeldSpecial.Value = false;
        bool spiked = spikedBeers > 0;
        if (spiked) spikedBeers--;

        if (special) // x2 punkty + klątwa na następną konkurencję (kumuluje się)
        {
            Olympics.SetMultiplier(OwnerClientId, 2);
            Curse.Value |= RandomCurseBit();
            MsgOwner("PIWO SPECJALNE wypite: punkty x2 w następnej konkurencji + klątwa ekranu");
        }
        float amount = beerStrength;
        if (spiked) // pigułka: efekt od razu, BEZ komunikatu — ofiara ma się domyślić
        {
            int effect = Random.Range(0, 5);
            if (effect == 0) amount += spikedExtra; // mocny kop
            else
            {
                InstantCurse.Value |= (byte)(1 << (effect - 1)); // kumulacja klątw
                CurseUntil.Value = NetworkManager.ServerTime.Time + spikedCurseSeconds;
            }
            Debug.Log($"[Drunk] {Olympics.Nick(OwnerClientId)} wypił piwo z pigułką (efekt {effect})");
        }
        AddDrink(amount);
    }

    [Rpc(SendTo.Server)]
    public void DiscardBeerRpc()
    {
        if (PassedOut.Value || Beers.Value <= 0) return;
        Beers.Value--;
        // butelka ląduje na ziemi przed graczem — zachowuje specjalność i pigułkę
        if (beerPrefab != null)
        {
            Vector3 at = transform.position + transform.forward * 0.9f;
            var go = Instantiate(beerPrefab, new Vector3(at.x, 0f, at.z), Quaternion.identity);
            var bp = go.GetComponent<BeerPickup>();
            bp.respawns = false; // to nie spawner — podniesiona znika na dobre
            go.GetComponent<NetworkObject>().Spawn();
            bp.Special.Value = HeldSpecial.Value; // nadpisz losowanie z OnNetworkSpawn
            bp.SetSpiked(spikedBeers > 0);
        }
        HeldSpecial.Value = false;
        spikedBeers = 0;
        MsgOwner("Wyrzuciłeś piwo");
        Debug.Log($"[Drunk] {Olympics.Nick(OwnerClientId)} wyrzucił piwo");
    }

    [Rpc(SendTo.Server)]
    void SetVomitRpc(bool on)
    {
        if (PassedOut.Value) return;
        if (on && !Vomiting.Value) caughtThisVomit = false;
        Vomiting.Value = on;
    }

    // przyłapany, gdy inny gracz ma cię na widoku: w kadrze i bez przeszkód; raz na rzyganie
    // ponytail: kierunek patrzenia = yaw korpusu (pitch kamery nie jest replikowany)
    void CheckCaught()
    {
        foreach (var c in NetworkManager.ConnectedClients.Values)
        {
            var po = c.PlayerObject;
            if (po == null || c.ClientId == OwnerClientId) continue;
            Vector3 eye = po.transform.position + Vector3.up * 1.7f;
            Vector3 to = transform.position + Vector3.up * 1f - eye;
            if (to.magnitude > catchRadius) continue;
            if (Vector3.Angle(po.transform.forward, to) > catchFov) continue;
            if (Physics.Raycast(eye, to.normalized, out var hit, to.magnitude)
                && hit.collider.GetComponentInParent<DrunkSystem>() != this) continue; // zasłonięty

            caughtThisVomit = true;
            int before = Olympics.PointsOf(OwnerClientId);
            Olympics.AddPoints(OwnerClientId, -catchPenalty);
            int lost = before - Olympics.PointsOf(OwnerClientId);
            if (VoteManager.Instance != null) VoteManager.Instance.Scoreboard.Value = Olympics.Text();
            MsgOwner(lost > 0 ? $"PRZYŁAPALI CIĘ NA RZYGANIU! -{lost} pkt"
                              : "PRZYŁAPALI CIĘ NA RZYGANIU! (nie masz punktów do stracenia)");
            Debug.Log($"[Drunk] {Olympics.Nick(OwnerClientId)} przyłapany na rzyganiu, -{lost} pkt");
            break;
        }
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

    void Update()
    {
        if (IsServer)
        {
            // klątwy z pigułek wygasają po czasie (wszystkie naraz)
            if (InstantCurse.Value != 0 && CurseUntil.Value > 0
                && NetworkManager.ServerTime.Time >= CurseUntil.Value)
            { InstantCurse.Value = 0; CurseUntil.Value = 0; }

            if (Vomiting.Value)
            {
                Drunk.Value = Mathf.Max(Floor.Value, Drunk.Value - vomitDrainPerSecond * Time.deltaTime);
                if (!caughtThisVomit) CheckCaught();
            }
            else if (!PassedOut.Value)
                Drunk.Value = Mathf.Max(Floor.Value, Drunk.Value - decayPerSecond * Time.deltaTime);
        }

        // skala postaci z klątw — widzą wszyscy
        // ponytail: CharacterController nie skaluje collidera, to wizualny żart
        byte act = ActiveCurses();
        float sc = (act & 16) != 0 ? 0.45f : (act & 32) != 0 ? 1.7f : 1f;
        if (!Mathf.Approximately(transform.localScale.x, sc))
            transform.localScale = Vector3.one * sc;

        if (!IsOwner || PassedOut.Value) { reviveTarget = null; return; }

        int st = Stage;
        if (st != lastStage) { lastStage = st; ApplyControls(st); }

        // octodad: klawisze ruchu losują się co 3 s, po klątwie wraca mapowanie z etapu
        bool octo = CurseActive(128);
        if (octo && Time.time >= nextOcto) { nextOcto = Time.time + 3f; ApplyControls(3); }
        else if (!octo && wasOcto) ApplyControls(st);
        wasOcto = octo;

        var kb = Keyboard.current;
        if (kb == null) return;

        // rzyganie na życzenie — tylko na hubie, trzymaj [V]
        bool wantVomit = kb.vKey.isPressed && Competition.Current == null;
        if (wantVomit != Vomiting.Value) SetVomitRpc(wantVomit);
        if (Vomiting.Value) { reviveTarget = null; return; }

        // OnTriggerExit bywa gubiony (teleporty, wyłączane collidery) — waliduj dystansem
        if (nearBeer != null &&
            Vector3.Distance(nearBeer.transform.position, transform.position) > 3f) nearBeer = null;
        if (nearPill != null &&
            Vector3.Distance(nearPill.transform.position, transform.position) > 3f) nearPill = null;

        // ponytail: FindObjectsByType co klatkę — graczy jest max 10, wystarczy
        reviveTarget = null;
        foreach (var d in FindObjectsByType<DrunkSystem>(FindObjectsSortMode.None))
        {
            if (d == this || !d.PassedOut.Value) continue;
            if (Vector3.Distance(d.transform.position, transform.position) <= reviveRange)
            { reviveTarget = d; break; }
        }

        if (kb.eKey.wasPressedThisFrame)
        {
            if (reviveTarget != null) reviveTarget.ReviveRpc();
            else if (nearPill != null && nearPill.Available.Value) nearPill.RequestPickupRpc();
            else if (nearBeer != null && nearBeer.Available.Value) nearBeer.RequestPickupRpc();
        }
        if (kb.qKey.wasPressedThisFrame && Pills.Value > 0
            && nearBeer != null && nearBeer.Available.Value)
            nearBeer.SpikeRpc();
        if (kb.fKey.wasPressedThisFrame && Beers.Value > 0 && !Competition.InputLocked)
        { Sfx.Play("gulp"); DrinkBeerRpc(); }
        if (kb.gKey.wasPressedThisFrame && Beers.Value > 0 && !Competition.InputLocked)
            DiscardBeerRpc();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsOwner) return;
        var beer = other.GetComponentInParent<BeerPickup>();
        if (beer != null) nearBeer = beer;
        var pill = other.GetComponentInParent<PillPickup>();
        if (pill != null) nearPill = pill;
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsOwner) return;
        if (other.GetComponentInParent<BeerPickup>() == nearBeer) nearBeer = null;
        if (other.GetComponentInParent<PillPickup>() == nearPill) nearPill = null;
    }

    void LateUpdate()
    {
        if (!IsOwner) return;

        // klątwy ekranowe — maska bitowa, efekty się kumulują
        byte active = ActiveCurses();
        cam.fieldOfView = (active & 4) != 0 ? 20f : 60f;                         // zoom
        cam.rect = (active & 8) != 0
            ? new Rect(0.3f, 0.3f, 0.4f, 0.4f) : new Rect(0f, 0f, 1f, 1f);       // mały obraz
        SetRenderScale((active & 2) != 0 ? 0.15f : 1f);                          // lowres
        if ((active & 1) != 0)
            cam.transform.localRotation *= Quaternion.Euler(0f, 0f, 180f);       // do góry nogami

        if (PassedOut.Value)
        {
            cam.transform.localRotation = Quaternion.Euler(0f, 0f, 75f); // leżysz
            return;
        }
        if (Vomiting.Value)
        {
            cam.transform.localEulerAngles = new Vector3(65f, 0f, Mathf.Sin(Time.time * 6f) * 4f);
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

    static void SetRenderScale(float scale)
    {
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset rp
            && !Mathf.Approximately(rp.renderScale, scale))
            rp.renderScale = scale;
    }

    void OnGUI()
    {
        if (!IsOwner || !IsSpawned) return;

        // pionowy pasek upojenia po prawej (GDD sekcja 6); podłoga = ciemniejszy słupek
        float h = Screen.height * 0.5f;
        var back = new Rect(Screen.width - 44f, (Screen.height - h) / 2f, 24f, h);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        float fill = h * Drunk.Value / 100f;
        GUI.color = Color.Lerp(Color.green, Color.red, Drunk.Value / 100f);
        GUI.DrawTexture(new Rect(back.x, back.yMax - fill, back.width, fill), Texture2D.whiteTexture);
        float floorH = h * Floor.Value / 100f;
        GUI.color = new Color(0.4f, 0f, 0f, 0.9f); // pkt 1: to już zostaje
        GUI.DrawTexture(new Rect(back.x, back.yMax - floorH, back.width, floorH), Texture2D.whiteTexture);

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
        string beerLine = Beers.Value <= 0 ? "Piwo: brak"
            : (HeldSpecial.Value ? "Piwo: SPECJALNE" : "Piwo: zwykłe") + "  [F] pij  [G] wyrzuć";
        GUI.Label(new Rect(Screen.width - 250f, back.yMax + 6f, 240f, 40f),
            beerLine + $"\nPigułki: {Pills.Value}" + (Pills.Value > 0 ? "  [Q] dosyp" : ""),
            new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight });

        var style = new GUIStyle(GUI.skin.label)
        { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var center = new Rect(0, Screen.height * 0.4f, Screen.width, 40);
        if (PassedOut.Value) GUI.Label(center, "ZGON — czekaj aż cię ocucą", style);
        else if (Vomiting.Value) GUI.Label(center, "RZYGASZ... (trzeźwiejesz, byle nikt nie widział)", style);
        else if (reviveTarget != null) GUI.Label(center, "[E] Ocuć kolegę", style);
        else if (nearPill != null && nearPill.Available.Value)
            GUI.Label(center, "[E] Podnieś pigułkę", style);
        else if (nearBeer != null && nearBeer.Available.Value)
        {
            string hint = Beers.Value >= maxBeers
                ? "Masz już piwo w ręce — wypij [F] albo wyrzuć [G]"
                : nearBeer.Special.Value
                    ? "[E] PIWO SPECJALNE — x2 punkty + klątwa ekranu w następnej grze"
                    : "[E] Podnieś piwo";
            if (Pills.Value > 0) hint += "   [Q] Dosyp pigułkę";
            GUI.Label(center, hint, style);
        }

        if (Time.time < msgUntil)
            GUI.Label(new Rect(0, Screen.height * 0.55f, Screen.width, 40), msg,
                new GUIStyle(style) { normal = { textColor = new Color(1f, 0.9f, 0.3f) } });
    }
}
