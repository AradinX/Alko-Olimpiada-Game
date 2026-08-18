using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Naczynie w dłoni — czysta kosmetyka, losowane przy podnoszeniu piwa.
// Nazwy muszą się zgadzać z dziećmi HandBottle z ProjectBootstrap.AddVessel.
public enum Vessel : byte { Butelka, Kufel, Puszka }

// System upojenia: poziom 0-100 replikowany z serwera, bujanie kamery u właściciela,
// Zgon przy 100 (cucenie [E]), etapy psujące sterowanie, ekwipunek piw [E]/[F],
// pigułki [Q], dobrowolne rzyganie [V] (kara za przyłapanie), klątwy z piw specjalnych.
public class DrunkSystem : NetworkBehaviour
{
    public const float DrinkSeconds = 1.4f;
    public float decayPerSecond = 0.2f;      // pkt 2: trzeźwiejesz wolniej
    public float reviveRange = 3f;
    public float reviveTo = 50f;
    public float beerStrength = 12f;         // progi 20/45/64 pkt; Zgon=100 → 3 piwa (36 pkt) od "ligancko" do Zgonu
    public float vomitDrainPerSecond = 6f;   // im dłużej rzygasz, tym więcej schodzi
    public float catchRadius = 25f;          // zasięg wzroku przy przyłapaniu
    public float catchFov = 40f;             // musi mieć cię w kadrze (stopnie od osi patrzenia)
    public int catchPenalty = 2;
    public int maxBeers = 1;                 // jedno piwo w ręce
    public int sipsPerBeer = 3;              // tyle wciśnięć [F] opróżnia butelkę
    public float lieSeconds = 1.2f;          // faza leżenia po powaleniu (zanim zaczniesz wstawać)
    public float floorFraction = 0.5f;       // ile alkoholu z konkurencji zostaje na stałe (podłoga)
    public float spikedExtra = 35f;          // pigułka w piwie
    public float spikedCurseSeconds = 40f;   // klątwa z pigułki działa od razu, przez tyle sekund
    public GameObject beerPrefab;            // wyrzucone piwo ląduje na ziemi (bootstrap)

    // Dłoń i butelka to JEDNO sztywne ciało — dosłownie: dłoń jest DZIECKIEM
    // butelki, więc chwyt trzyma hierarchia Unity i nie ma jak się rozjechać.
    // Animujemy samą butelkę, dłoń jedzie za nią z definicji.
    // Strojenie w POV_Beer_Tuning: przestawiasz butelkę (dłoń leci razem z nią),
    // potem przepisujesz tu jej localPosition/localEulerAngles. To jest ruch
    // UCHWYTU, wspólny dla wszystkich naczyń — poza samego kufla czy puszki
    // (osobno stanie i picie) siedzi na VesselPose w dzieciach HandBottle
    // i stroi się w scenie Naczynia_Tuning.
    [Header("POV piwa")]
    public Vector3 povBottleIdlePosition = new(0.3938577f, -0.2383561f, 0.57f);
    public Vector3 povBottleDrinkPosition = new(0.09247f, 0.00941f, 0.24294f);
    public Vector3 povBottleIdleRotation = new(286.6091f, 209.3117f, 244.036f);
    public Vector3 povBottleDrinkRotation = new(56.0392f, 206.565f, 245.0062f);
    // chwyt: dłoń w LOKALNEJ przestrzeni butelki, jeden raz na starcie
    public Vector3 povHandGripPosition = new(0.165314f, -0.064865f, -0.08583f);
    public Vector3 povHandGripRotation = new(10.9443f, 164.3116f, 100.1501f);
    public float povHandScale = 0.24f;

    // etapy pijaństwa (progi na pasku); bujanie zaczyna się od pierwszego i pogłębia
    public static readonly (float min, string name)[] Stages =
    { (20f, "Szumi"), (45f, "Lekko chycony"), (64f, "Jest ligancko") };

    // aktualne mapowanie ruchu (owner); etap 2 zamienia A/D, etap 3 losuje ukryte klawisze
    public Key keyW = Key.W, keyS = Key.S, keyA = Key.A, keyD = Key.D;

    static readonly Key[] scramblePool = // klawisze nieużywane w grze (bez V/Q/E/F/R/G)
    { Key.P, Key.L, Key.M, Key.K, Key.O, Key.I, Key.J, Key.N, Key.B,
      Key.H, Key.T, Key.Y, Key.U, Key.C, Key.X, Key.Z };

    public NetworkVariable<float> Drunk = new();     // 0-100, zapis: serwer
    public NetworkVariable<float> Floor = new();     // pkt 1: alkohol z konkurencji na stałe
    public NetworkVariable<bool> PassedOut = new();
    public NetworkVariable<bool> Vomiting = new();
    public NetworkVariable<bool> Down = new();       // powalony pchnięciem; wstawanie automatyczne
    public NetworkVariable<int> Beers = new();       // piwo w ręce (max 1)
    public NetworkVariable<int> Sips = new();        // łyki do końca butelki w ręce
    public NetworkVariable<double> DrinkUntil = new(); // serwerowy koniec animacji picia
    public NetworkVariable<SpecialBeer> HeldSpecial = new(); // typ piwa w ręce
    public NetworkVariable<Vessel> HeldVessel = new();       // butelka / kufel / puszka
    public NetworkVariable<bool> Shield = new();             // Tarcza Ateny na następną grę
    public NetworkVariable<int> Pills = new();       // ekwipunek pigułek
    // klątwy jako maska bitowa — efekty się kumulują:
    // 1=do góry nogami 2=lowres 4=zoom 8=mały obraz (ekran)
    // 16=mały 32=wielki (Badland) 64=odwrócone sterowanie 128=octodad (WSAD losuje się co 3 s)
    public NetworkVariable<byte> Curse = new();        // z piwa specjalnego, na następną konkurencję
    public NetworkVariable<byte> InstantCurse = new(); // z pigułek, działa od razu
    public NetworkVariable<double> CurseUntil = new(); // do kiedy działa InstantCurse
    public NetworkVariable<byte> StageCurse = new();   // pkt 2: losowy gag przy etapie "Jest ligancko"
    public NetworkVariable<bool> Steady = new();       // papieros: pewna ręka na następną konkurencję

    // utrudnienia w konkurencjach liczą z tego zamiast surowego Drunk — papieros je połowi
    public float Handicap01() => Drunk.Value / 100f * (Steady.Value ? 0.5f : 1f);

    // powalenie: DownPose 0 (stoi) → 1 (leży), animowane u wszystkich klientów;
    // upadek jest szybki, wstawanie powolne — im bardziej pijany, tym dłużej
    public float DownPose { get; private set; }
    public bool Downed => Down.Value || DownPose > 0.05f;
    public bool IsDrinking => IsSpawned && DrinkUntil.Value > NetworkManager.ServerTime.Time;
    public float DrinkPose
    {
        get
        {
            if (!IsDrinking) return 0f;
            float k = 1f - (float)((DrinkUntil.Value - NetworkManager.ServerTime.Time) / DrinkSeconds);
            return Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI);
        }
    }
    public float GetUpSeconds => 0.6f + 2f * (Drunk.Value / 100f);
    double getUpAt; // serwer: koniec fazy leżenia

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
    Vector3 bodyHome; // poza spoczynkowa Body z prefabu (patrz PlayerLimbs.bodyHome)
    Transform handBottle; // butelka w ręce, widoczna gdy Beers > 0
    Transform povBottle;  // lokalny model pod kamerą, bez problemów z near plane
    Transform povArm;
    Camera cam;
    LensDistortion lens; ChromaticAberration chroma; Vignette vig; // post-process upojenia
    DrunkSystem reviveTarget; // pobliski leżący gracz (tylko u właściciela)
    BeerPickup nearBeer;
    PillPickup nearPill;
    CigarettePickup nearCig;
    SnackPickup nearSnack;
    int lastStage;
    float nextOcto; bool wasOcto;
    string msg; float msgUntil; // komunikaty ("coś było w tym piwie" itp.)

    // serwer
    int spikedBeers;      // piwa z pigułką w ekwipunku — celowo niereplikowane
    bool caughtThisVomit;

    void Awake()
    {
        body = transform.Find("Body");
        bodyHome = body.localPosition;
        handBottle = transform.Find("HandBottle");
        if (handBottle == null) // butelka podpięta pod kość dłoni modelu
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name == "HandBottle") { handBottle = t; break; }
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
        if (IsOwner) SetupPovBottle();
        Beers.OnValueChanged += (_, v) => SetBottleVisible(v > 0);
        SetBottleVisible(Beers.Value > 0);
        HeldVessel.OnValueChanged += (_, v) => ApplyVessel(v);
        ApplyVessel(HeldVessel.Value);
        if (IsOwner) SetupPost();

        // ponytail: headless smoke test pętli Zgon->cucenie->wyrzucenie piwa (-autodrink)
        if (IsServer && System.Array.IndexOf(
                System.Environment.GetCommandLineArgs(), "-autodrink") >= 0)
        {
            Invoke(nameof(AutoDrink), 3f);
            Invoke(nameof(ReviveRpc), 6f);
            Invoke(nameof(AutoDrop), 8f);
            Invoke(nameof(AutoSips), 10f);
        }
        // smoke test powalenia: pełny cykl leżenie -> automatyczne wstawanie (-autoknock)
        if (IsServer && System.Array.IndexOf(
                System.Environment.GetCommandLineArgs(), "-autoknock") >= 0)
            Invoke(nameof(KnockDown), 3f);
    }

    void SetupPovBottle()
    {
        if (handBottle == null) return;
        // W pierwszej osobie renderujemy tylko osobną dłoń i butelkę POV.
        // forceRenderingOff nie koliduje z garderobą, która nadal przełącza GameObjecty.
        foreach (var r in body.GetComponentsInChildren<Renderer>(true))
            r.forceRenderingOff = true;
        povBottle = Instantiate(handBottle.gameObject, cam.transform, false).transform;
        povBottle.name = "PovBottle";
        var follow = povBottle.GetComponent<FollowBone>();
        if (follow != null) { follow.enabled = false; Destroy(follow); } // POV nie chodzi za kością
        povArm = transform.Find("PovRealArm");
        if (povArm != null)
        {
            // dłoń pod butelkę — od teraz chwytu pilnuje hierarchia, nie kod
            povArm.SetParent(povBottle, false);
            povArm.SetLocalPositionAndRotation(povHandGripPosition,
                Quaternion.Euler(povHandGripRotation));
            povArm.localScale = Vector3.one * (povHandScale / povBottle.localScale.x);
        }
        cam.nearClipPlane = 0.03f;
    }

    void SetBottleVisible(bool visible)
    {
        if (handBottle != null)
        {
            handBottle.gameObject.SetActive(visible);
            if (IsOwner)
                foreach (var r in handBottle.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        }
        if (povBottle != null) povBottle.gameObject.SetActive(visible);
        if (povArm != null) povArm.gameObject.SetActive(visible);
    }

    // pod HandBottle (i w jego kopii POV) wiszą wszystkie naczynia — widać to wylosowane.
    // ponytail: gdy prefab jest jeszcze sprzed AddVessel, Find nic nie znajdzie i zostaje butelka
    void ApplyVessel(Vessel vessel)
    {
        foreach (var holder in new[] { handBottle, povBottle })
        {
            if (holder == null) continue;
            foreach (Vessel v in System.Enum.GetValues(typeof(Vessel)))
            {
                var t = holder.Find(v.ToString());
                if (t != null) t.gameObject.SetActive(v == vessel);
            }
        }
    }

    // poza naczynia w garści: w trzeciej osobie jedzie stanie→picie razem z animacją
    // (tu pilnujesz, żeby kufel nie wszedł w twarz), w POV zostaje sama poza stania —
    // ruch pod kamerą robi już holder (povBottleIdle/Drink). Liczą wszyscy klienci,
    // bo naczynie w cudzej dłoni widać tak samo.
    void ApplyVesselPose()
    {
        if (!IsSpawned || Beers.Value <= 0) return;
        string name = HeldVessel.Value.ToString();
        if (handBottle != null && handBottle.Find(name) is Transform h
            && h.TryGetComponent<VesselPose>(out var hp)) hp.Apply(DrinkPose);
        if (povBottle != null && povBottle.Find(name) is Transform p
            && p.TryGetComponent<VesselPose>(out var pp)) pp.Apply(0f);
    }

    void UpdatePovBottle()
    {
        if (povBottle == null) return;
        float k = DrinkPose;
        povBottle.SetLocalPositionAndRotation(
            Vector3.Lerp(povBottleIdlePosition, povBottleDrinkPosition, k),
            Quaternion.Slerp(Quaternion.Euler(povBottleIdleRotation),
                             Quaternion.Euler(povBottleDrinkRotation), k));
    }

    // pijacki post-process (GDD 10): profil budowany w kodzie — zero assetów w scenach
    void SetupPost()
    {
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        lens = profile.Add<LensDistortion>();
        lens.intensity.overrideState = true;
        chroma = profile.Add<ChromaticAberration>();
        chroma.intensity.overrideState = true;
        vig = profile.Add<Vignette>();
        vig.intensity.overrideState = true;
        var vol = cam.gameObject.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.profile = profile;
        cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    }

    void AutoDrink() => AddDrink(120f);
    void AutoDrop() { Beers.Value = 1; DiscardBeerRpc(); } // test spawnu butelki na ziemi

    // ponytail: smoke test łyków — butelka ma zniknąć dopiero po sipsPerBeer wciśnięciach
    void AutoSips() => StartCoroutine(SipTest());

    IEnumerator SipTest()
    {
        PickUpBeer(false, SpecialBeer.None);
        for (int i = 1; i <= sipsPerBeer; i++)
        {
            DrinkBeerRpc();
            yield return new WaitForSeconds(DrinkSeconds + 0.2f);
            Debug.Log($"[Drunk] test: łyk {i}/{sipsPerBeer}, piwo w ręce={Beers.Value},"
                + $" łyków zostało={Sips.Value}, upojenie={Drunk.Value:0.0}");
            Debug.Assert(Beers.Value == (i < sipsPerBeer ? 1 : 0), "Zła liczba łyków na butelkę");
        }
    }

    // leżący pijak (Zgon) albo pochylony rzygacz — widoczne u wszystkich
    void UpdateBodyPose()
    {
        float pitch = PassedOut.Value ? 90f : Vomiting.Value ? 40f : 0f;
        body.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        body.localPosition = PassedOut.Value ? bodyHome + Vector3.down * 0.5f : bodyHome;
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

    // powalenie pchnięciem: leżysz lieSeconds, potem wstajesz automatycznie (GetUpSeconds)
    public void KnockDown()
    {
        if (PassedOut.Value || Down.Value) return;
        Down.Value = true;
        getUpAt = NetworkManager.ServerTime.Time + lieSeconds;
        Sfx.Play("slap", transform.position);
        Debug.Log($"[Drunk] gracz {OwnerClientId} powalony: leży {lieSeconds:0.0} s"
                  + $" + wstaje {GetUpSeconds:0.0} s");
    }

    // picie w konkurencji podnosi też podłogę — tego już nie wytrzeźwiejesz.
    // Na stałe zostaje tylko część (floorFraction), inaczej olimpiada kończyła
    // się seryjnym Zgonem
    public void AddPermanent(float amount)
    {
        Floor.Value = Mathf.Min(90f, Floor.Value + amount * floorFraction);
        AddDrink(amount);
    }

    // picie przymusowe w konkurencji: pasek pełny = wymioty (spadek do 60/podłogi), nie Zgon
    // Zwraca true, gdy przepełnienie wywołało wymioty.
    public bool AddCompetitionDrink(float amount)
    {
        if (Drunk.Value + amount < 100f)
        {
            AddPermanent(amount);
            return false;
        }

        if (Shield.Value)
        {
            Shield.Value = false;
            Drunk.Value = 99f;
            MsgOwner("TARCZA ATENY zablokowała wymioty");
            return false;
        }

        Drunk.Value = Mathf.Max(Floor.Value, 60f);
        return true;
    }

    static string SpecialEffect(SpecialBeer type) => type switch
    {
        SpecialBeer.DoublePoints => "punkty x2 w następnej konkurencji",
        SpecialBeer.Spartan => "x3 za 1. miejsce, 0 pkt za pozostałe",
        SpecialBeer.Nike => "+2 pkt za podium",
        SpecialBeer.Nemesis => "pokonaj lidera i ukradnij mu 2 pkt",
        SpecialBeer.Shield => "blokuje następną klątwę albo wymioty",
        SpecialBeer.Tyche => "po grze losuje od -1 do +4 pkt",
        _ => ""
    };

    // sips <= 0 → pełna butelka; napoczęta wraca z ziemi z resztą łyków
    public void PickUpBeer(bool spiked, SpecialBeer special, int sips = 0)
    {
        Beers.Value++;
        Sips.Value = sips > 0 ? sips : sipsPerBeer;
        HeldSpecial.Value = special;
        // naczynie losuje się przy każdym podniesieniu — na ziemi zawsze leży butelka
        HeldVessel.Value = (Vessel)Random.Range(0, System.Enum.GetValues(typeof(Vessel)).Length);
        if (spiked) spikedBeers++;
        if (special != SpecialBeer.None)
            MsgOwner($"PIWO {BeerPickup.SpecialName(special)}: {SpecialEffect(special)}."
                + $" [F] wypij ({Lyki(Sips.Value)}), [G] wyrzuć");
    }

    // 1 łyk / 2-4 łyki / 5 łyków
    static string Lyki(int n) => n == 1 ? "1 łyk"
        : n % 10 >= 2 && n % 10 <= 4 && n / 10 % 10 != 1 ? $"{n} łyki" : $"{n} łyków";

    static byte RandomCurseBit() => (byte)(1 << Random.Range(0, 8));

    // pkt 2: gagi etapu "ligancko" — tylko ekranowe/skala (bez 64/128, klawisze i tak się już mieszają)
    static readonly byte[] stageCurseBits = { 1, 2, 4, 8, 16, 32 };
    static byte RandomStageCurse() => stageCurseBits[Random.Range(0, stageCurseBits.Length)];

    // maska aktywnych klątw: ze specjalnego (podczas konkurencji) + z pigułek (na czas)
    public byte ActiveCurses()
    {
        var comp = Competition.Current;
        byte a = StageCurse.Value; // pkt 2: gag etapu "ligancko" działa zawsze (też na hubie)
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
        if (PassedOut.Value || Beers.Value <= 0 || IsDrinking) return;
        DrinkUntil.Value = NetworkManager.ServerTime.Time + DrinkSeconds;
        StartCoroutine(FinishDrink());
    }

    IEnumerator FinishDrink()
    {
        yield return new WaitForSeconds(DrinkSeconds);
        DrinkUntil.Value = 0;
        if (PassedOut.Value || Beers.Value <= 0) yield break;

        // każde [F] to jeden łyk; alkohol schodzi po równo, efekty piwa dopiero na dnie
        float sip = beerStrength / Mathf.Max(1, sipsPerBeer);
        Sips.Value--;
        if (Sips.Value > 0) { AddDrink(sip); yield break; }

        Beers.Value--;
        SpecialBeer special = HeldSpecial.Value;
        HeldSpecial.Value = SpecialBeer.None;
        bool spiked = spikedBeers > 0;
        if (spiked) spikedBeers--;

        if (special != SpecialBeer.None)
        {
            Olympics.SetBeerBonus(OwnerClientId, special);
            if (special == SpecialBeer.Shield)
            {
                if (Curse.Value != 0 || InstantCurse.Value != 0)
                {
                    Curse.Value = 0;
                    InstantCurse.Value = 0;
                    CurseUntil.Value = 0;
                    Shield.Value = false;
                    MsgOwner("TARCZA ATENY zablokowała klątwę");
                }
                else
                {
                    Shield.Value = true;
                    MsgOwner("TARCZA ATENY gotowa na następną klątwę albo wymioty");
                }
            }
            else
            {
                Curse.Value |= RandomCurseBit();
                MsgOwner($"Wypito {BeerPickup.SpecialName(special)}: {SpecialEffect(special)} + klątwa");
            }
        }
        float amount = sip;
        if (spiked && Shield.Value)
        {
            Shield.Value = false;
            spiked = false;
            MsgOwner("TARCZA ATENY zablokowała pigułkę");
        }
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
        if (PassedOut.Value || Beers.Value <= 0 || IsDrinking) return;
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
            bp.sipsLeft = Sips.Value;             // napoczęta butelka nie odrasta
            bp.SetSpiked(spikedBeers > 0);
        }
        HeldSpecial.Value = SpecialBeer.None;
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

            // koniec fazy leżenia — od tej pory klienci animują wstawanie
            if (Down.Value && NetworkManager.ServerTime.Time >= getUpAt)
            {
                Down.Value = false;
                Debug.Log($"[Drunk] gracz {OwnerClientId} wstaje ({GetUpSeconds:0.0} s)");
            }

            if (Vomiting.Value)
            {
                Drunk.Value = Mathf.Max(Floor.Value, Drunk.Value - vomitDrainPerSecond * Time.deltaTime);
                if (!caughtThisVomit) CheckCaught();
            }
            else if (!PassedOut.Value)
                Drunk.Value = Mathf.Max(Floor.Value, Drunk.Value - decayPerSecond * Time.deltaTime);

            // pkt 2: etap "Jest ligancko" losuje gag ekranowy (stały póki jesteś w etapie), nie tylko klawisze
            bool ligancko = !PassedOut.Value && Drunk.Value >= Stages[Stages.Length - 1].min;
            if (ligancko && StageCurse.Value == 0) StageCurse.Value = RandomStageCurse();
            else if (!ligancko && StageCurse.Value != 0) StageCurse.Value = 0;
        }

        // skala postaci z klątw — widzą wszyscy
        // ponytail: CharacterController nie skaluje collidera, to wizualny żart
        byte act = ActiveCurses();
        float sc = (act & 16) != 0 ? 0.45f : (act & 32) != 0 ? 1.7f : 1f;
        if (!Mathf.Approximately(transform.localScale.x, sc))
            transform.localScale = Vector3.one * sc;

        // powalony: upadek szybki, wstawanie powolne i automatyczne (PlayerLimbs rysuje pozę)
        DownPose = Mathf.MoveTowards(DownPose, Down.Value ? 1f : 0f,
            (Down.Value ? 4f : 1f / GetUpSeconds) * Time.deltaTime);

        if (!IsOwner || PassedOut.Value) { reviveTarget = null; return; }

        int st = Stage;
        if (st != lastStage)
        {
            bool wasScrambled = lastStage >= 2;
            lastStage = st;
            ApplyControls(st);
            // komunikat o zamianie klawiszy — bez niego wyglądało na buga
            if (st == 2) { msg = "LEKKO CHYCONY: [A] i [D] się pozamieniały!"; msgUntil = Time.time + 4f; }
            else if (st >= 3) { msg = "JEST LIGANCKO: klawisze ruchu totalnie pomieszane!"; msgUntil = Time.time + 4f; }
            else if (wasScrambled) { msg = "Sterowanie wraca do normy"; msgUntil = Time.time + 4f; }
        }

        // octodad: klawisze ruchu losują się co 3 s, po klątwie wraca mapowanie z etapu
        bool octo = CurseActive(128);
        if (octo && Time.time >= nextOcto) { nextOcto = Time.time + 3f; ApplyControls(3); }
        else if (!octo && wasOcto) ApplyControls(st);
        wasOcto = octo;

        var kb = Keyboard.current;
        if (kb == null) return;

        // rzyganie na życzenie — trzymaj [V] (pkt 1: działa też w konkurencjach, by zbić pasek przed Zgonem)
        bool wantVomit = kb.vKey.isPressed;
        if (wantVomit != Vomiting.Value) SetVomitRpc(wantVomit);
        if (Vomiting.Value) { reviveTarget = null; return; }

        // OnTriggerExit bywa gubiony (teleporty, wyłączane collidery) — waliduj dystansem
        if (nearBeer != null &&
            Vector3.Distance(nearBeer.transform.position, transform.position) > 3f) nearBeer = null;
        if (nearPill != null &&
            Vector3.Distance(nearPill.transform.position, transform.position) > 3f) nearPill = null;
        if (nearCig != null &&
            Vector3.Distance(nearCig.transform.position, transform.position) > 3f) nearCig = null;
        if (nearSnack != null &&
            Vector3.Distance(nearSnack.transform.position, transform.position) > 3f) nearSnack = null;

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
            else if (nearCig != null && nearCig.Available.Value) nearCig.RequestSmokeRpc();
            else if (nearSnack != null && nearSnack.Available.Value) nearSnack.RequestEatRpc();
            else if (nearBeer != null && nearBeer.Available.Value) nearBeer.RequestPickupRpc();
        }
        if (kb.qKey.wasPressedThisFrame && Pills.Value > 0
            && nearBeer != null && nearBeer.Available.Value)
            nearBeer.SpikeRpc();
        if (kb.fKey.wasPressedThisFrame && Beers.Value > 0 && !IsDrinking
            && !Competition.InputLocked)
        { Sfx.Play("gulp"); DrinkBeerRpc(); }
        if (kb.gKey.wasPressedThisFrame && Beers.Value > 0 && !IsDrinking
            && !Competition.InputLocked)
            DiscardBeerRpc();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsOwner) return;
        var beer = other.GetComponentInParent<BeerPickup>();
        if (beer != null) nearBeer = beer;
        var pill = other.GetComponentInParent<PillPickup>();
        if (pill != null) nearPill = pill;
        var cig = other.GetComponentInParent<CigarettePickup>();
        if (cig != null) nearCig = cig;
        var snack = other.GetComponentInParent<SnackPickup>();
        if (snack != null) nearSnack = snack;
    }

    void OnTriggerExit(Collider other)
    {
        if (!IsOwner) return;
        if (other.GetComponentInParent<BeerPickup>() == nearBeer) nearBeer = null;
        if (other.GetComponentInParent<PillPickup>() == nearPill) nearPill = null;
        if (other.GetComponentInParent<CigarettePickup>() == nearCig) nearCig = null;
        if (other.GetComponentInParent<SnackPickup>() == nearSnack) nearSnack = null;
    }

    void LateUpdate()
    {
        ApplyVesselPose(); // też u nie-właścicieli: kufel w cudzej garści
        if (!IsOwner) return;
        UpdatePovBottle();

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
        if (IsDrinking)
            cam.transform.localRotation *= Quaternion.Euler(-10f * DrinkPose, 0f, 0f);
        float t = Mathf.InverseLerp(Stages[0].min, 100f, Drunk.Value); // "Szumi" otwiera bujanie

        // post-process rośnie z upojeniem; soczewka powoli faluje (GDD: obraz faluje)
        if (lens != null)
        {
            float p = t * GameSettings.Sway;
            lens.intensity.value = (-0.32f - 0.15f * Mathf.Sin(Time.time * 0.7f)) * p;
            chroma.intensity.value = p;
            vig.intensity.value = 0.32f * p;
        }

        if (t <= 0f) return;
        // ponytail: bujanie = szum Perlina na rotacji kamery; post-process URP dojdzie,
        // gdy sam sway przestanie wystarczać
        t *= GameSettings.Sway; // suwak dostępności (choroba lokomocyjna, GDD 10)
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
            : (HeldSpecial.Value != SpecialBeer.None ? $"Piwo: {BeerPickup.SpecialName(HeldSpecial.Value)}" : "Piwo: zwykłe")
              + $" ({Lyki(Sips.Value)})  [F] pij  [G] wyrzuć";
        GUI.Label(new Rect(Screen.width - 250f, back.yMax + 6f, 240f, 56f),
            beerLine + $"\nPigułki: {Pills.Value}" + (Pills.Value > 0 ? "  [Q] dosyp" : "")
            + (Steady.Value ? "\nSzlug: pewna ręka w nast. konkurencji" : ""),
            new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperRight });

        var style = new GUIStyle(GUI.skin.label)
        { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var center = new Rect(0, Screen.height * 0.4f, Screen.width, 40);
        if (PassedOut.Value) GUI.Label(center, "ZGON — czekaj aż cię ocucą", style);
        else if (Vomiting.Value) GUI.Label(center, "RZYGASZ... (trzeźwiejesz, byle nikt nie widział)", style);
        else if (Down.Value) GUI.Label(center, "POWALONY — leżysz na ziemi...", style);
        else if (DownPose > 0.05f) GUI.Label(center, "Zbierasz się z ziemi...", style);
        else if (reviveTarget != null) GUI.Label(center, "[E] Ocuć kolegę", style);
        else if (nearPill != null && nearPill.Available.Value)
            GUI.Label(center, "[E] Podnieś pigułkę", style);
        else if (nearCig != null && nearCig.Available.Value)
            GUI.Label(center, Steady.Value
                ? "Już kopcisz — pewna ręka gotowa na następną konkurencję"
                : "[E] Zapal szluga — pewna ręka w następnej grze, ale dokopie na stałe", style);
        else if (nearSnack != null && nearSnack.Available.Value)
            GUI.Label(center, "[E] Zjedz kurczaka — trzeźwiejesz (do podłogi)", style);
        else if (nearBeer != null && nearBeer.Available.Value)
        {
            string hint = Beers.Value >= maxBeers
                ? "Masz już piwo w ręce — wypij [F] albo wyrzuć [G]"
                : nearBeer.Special.Value != SpecialBeer.None
                    ? $"[E] PIWO {BeerPickup.SpecialName(nearBeer.Special.Value)} — {SpecialEffect(nearBeer.Special.Value)}"
                    : "[E] Podnieś piwo";
            if (Pills.Value > 0) hint += "   [Q] Dosyp pigułkę";
            GUI.Label(center, hint, style);
        }

        if (Time.time < msgUntil)
            GUI.Label(new Rect(0, Screen.height * 0.55f, Screen.width, 40), msg,
                new GUIStyle(style) { normal = { textColor = new Color(1f, 0.9f, 0.3f) } });
    }
}
