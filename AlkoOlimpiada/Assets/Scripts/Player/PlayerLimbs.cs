using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Animacje postaci gracza. Chód (start/pętla/stop), idle i taniec lecą z klipów
// AccuRig przez Animator na Body/Guy — kontroler buduje PlayerAnimatorBuilder.
// Klipy są "in place", pozycję trzyma CharacterController.
// Reszta — emotki [1-4,6], pozy pijackie i trzymanie butelki — jest dalej
// proceduralna i MUSI iść w LateUpdate: Animator ewaluuje kości po Update, więc
// cokolwiek wpisane wcześniej zostałoby nadpisane w tej samej klatce.
// Prędkość liczymy z obserwowanego przesunięcia, więc postacie zdalne (przesuwane
// przez NetworkTransform) też chodzą, bez dodatkowej replikacji.
public class PlayerLimbs : NetworkBehaviour
{
    public float moveThreshold = 0.3f; // od jakiej prędkości (m/s) Animator wchodzi w chód
    public float groundOffset = 0.074f; // poza AccuRig stawia stopy niżej niż poza bind
                                        // z prefabu — o tyle podnosimy model, żeby nie
                                        // zapadał się w podłoże
    public float headCamDist = 0.45f;  // ile kamera odjeżdża nad kość głowy przy pozach
                                       // ruszających Body; bryła głowy ma promień 0.376
    public float armOutDeg = 0f;       // DODATKOWE odchylenie rąk od tułowia ponad pozę modelu
                                       // (sama poza to już 27 st.); rządzi wartość z Player.prefab,
                                       // ta tu jest tylko dla nowych instancji

    // Kończyna przyjmuje obroty w osiach Body i przelicza je na lokalną przestrzeń
    // kości. Pozą spoczynkową jest DOKŁADNIE to, co widać w prefabie gracza
    // (bind = localRotation kości z prefabu) — pozę ustawia SetupCharacterModel
    // i można ją poprawiać obracając kości CC_Base_* w edytorze.
    class Limb
    {
        readonly Transform t, body;
        readonly Quaternion bind;
        public Limb(Transform t, Transform body)
        {
            this.t = t;
            this.body = body;
            bind = t.localRotation;
        }
        public void Set(Quaternion q)
        {
            // m liczone przy każdym wywołaniu, a nie raz przy spawnie: rodzic kości
            // jest teraz animowany klipem, więc zapamiętana wartość rozjeżdżała się
            // po pierwszej klatce animacji i emotki dryfowały razem z klatą
            Quaternion m = Quaternion.Inverse(t.parent.rotation) * body.rotation;
            t.localRotation = m * q * Quaternion.Inverse(m) * bind;
        }
    }

    // 0 = brak; 1 machanie, 2 leżenie, 3 fikołek, 4 salut, 5 taniec (klip), 6 wskazanie
    public NetworkVariable<byte> Emote = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    static readonly string[] emoteNames =
    { "", "machanie", "leżenie", "fikołek", "salut", "taniec", "wskazanie" };

    const byte DanceEmote = 5;
    static readonly int EmoteId = Animator.StringToHash("Emote");
    static readonly int SpeedId = Animator.StringToHash("Speed");

    Transform body, head, cam, handR;
    Animator anim;
    FollowBone bottleFollow;
    Limb armL, armR, legL, legR;
    Quaternion handRHome;
    Vector3 camHome;  // domyślna pozycja kamery (przy oku)
    Vector3 bodyHome; // poza spoczynkowa Body Z PREFABU — nie wpisywać na sztywno,
                      // bo to od niej zależy, czy stopy modelu stoją na ziemi
    const float LyingDrop = 0.65f; // o tyle Body zjeżdża przy leżeniu (płasko na ziemi)
    DrunkSystem drunk;
    PlayerController pc;
    Vector3 lastPos;
    float emoteT, speed;
    bool downDirty; // poza powalenia wymaga resetu po wstaniu

    public override void OnNetworkSpawn()
    {
        body = transform.Find("Body");
        bodyHome = body.localPosition;
        anim = body.GetComponentInChildren<Animator>();
        if (anim == null)
            Debug.LogError("[PlayerLimbs] Brak Animatora pod Body — chód, idle i taniec "
                           + "nie zadziałają. Odpal menu Alko/Zbuduj Animator gracza.");
        // ponytail: korekta na instancji, nie w prefabie — wysokość modelu w prefabie
        // stroi się ręcznie (patrz CenterCharacterModel) i nie chcę tego nadpisywać
        else anim.transform.localPosition += Vector3.up * groundOffset;

        // ponytail: tylko rig modelu (CC_Base_*) — klockowe pivoty poszły razem
        // z proceduralnym chodem, prefab i tak stoi na GuyWardrobe.fbx
        var bUpL = Bone("CC_Base_L_Upperarm"); var bUpR = Bone("CC_Base_R_Upperarm");
        var bThL = Bone("CC_Base_L_Thigh"); var bThR = Bone("CC_Base_R_Thigh");
        head = Bone("CC_Base_Head");
        if (!enabled) return; // brakuje kości — Bone() już zgłosiło błąd
        armL = new Limb(bUpL, body); armR = new Limb(bUpR, body);
        legL = new Limb(bThL, body); legR = new Limb(bThR, body);
        handR = Deep(body, "CC_Base_R_Hand");
        if (handR != null) handRHome = handR.localRotation;
        var bottle = Deep(transform, "HandBottle");
        if (bottle != null) bottleFollow = bottle.GetComponent<FollowBone>();
        drunk = GetComponent<DrunkSystem>();
        lastPos = transform.position;
        if (IsOwner)
        {
            pc = GetComponent<PlayerController>();
            cam = pc.playerCamera.transform;
            camHome = cam.localPosition;
        }
        Emote.OnValueChanged += (_, _) => { emoteT = 0f; ResetPose(); };
    }

    Transform Bone(string n)
    {
        var t = Deep(body, n);
        if (t == null)
        {
            Debug.LogError($"[PlayerLimbs] Brak kości {n} w modelu — wyłączam animacje");
            enabled = false;
        }
        return t;
    }

    bool DrunkPose => drunk != null && (drunk.PassedOut.Value || drunk.Vomiting.Value);

    // taniec gra z klipu tylko wtedy, gdy nic ważniejszego nie zajmuje ciała
    bool DanceClip => Emote.Value == DanceEmote && !DrunkPose
                      && (drunk == null || !drunk.Downed);

    // każda poza rąk dostaje odchylenie na zewnątrz — model ma szeroki tułów
    // i choreografia strojona na klockach wchodziła w brzuch.
    // Znak: dla rigu CC_Base_* obrót wokół Z Body DODATNI dociska rękę do tułowia,
    // więc odchylenie na zewnątrz to minus dla lewej i plus dla prawej. Mierzone na
    // kości Upperarm->Forearm: armOutDeg 0 = 27 st. od tułowia, każdy +1 to +1 st.
    void SetArmL(Quaternion q) => armL.Set(q * Quaternion.Euler(0f, 0f, -armOutDeg));
    void SetArmR(Quaternion q) => armR.Set(q * Quaternion.Euler(0f, 0f, armOutDeg));

    void ResetPose()
    {
        if (cam != null) cam.localPosition = camHome; // kamera wraca do oka
        if (handR != null) handR.localRotation = handRHome;
        if (DrunkPose) return; // Zgon/rzyganie ustawia Body po swojemu — nie ruszaj
        body.SetLocalPositionAndRotation(bodyHome, Quaternion.identity);
    }

    static Transform Deep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    void Update()
    {
        Vector3 d = transform.position - lastPos;
        lastPos = transform.position;
        d.y = 0f;
        speed = d.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);

        if (IsOwner) OwnerInput(speed);

        if (anim == null) return;
        bool frozen = DrunkPose || (drunk != null && drunk.Downed);
        anim.SetInteger(EmoteId, DanceClip ? DanceEmote : 0);
        float locomotionSpeed = speed > moveThreshold && !frozen && !DanceClip ? speed : 0f;
        anim.SetFloat(SpeedId, locomotionSpeed, 0.1f, Time.deltaTime);
    }

    // Wszystkie proceduralne pozy idą tutaj, PO Animatorze. Kolejność w klatce:
    // Update (input) -> Animator (kości z klipu) -> LateUpdate (to poniżej) ->
    // FollowBone (butelka, DefaultExecutionOrder 100).
    void LateUpdate()
    {
        // Zgon/rzyganie: DrunkSystem ustawia Body, my rozkładamy ręce —
        // bez tego zostawały wbite w tułów w pozie z ostatniej klatki
        if (DrunkPose)
        {
            SetArmL(Quaternion.Euler(0f, 0f, 45f));
            SetArmR(Quaternion.Euler(0f, 0f, -45f));
            legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
            return;
        }

        // powalony pchnięciem: poza jak emotka "leżenie", DownPose animuje upadek/wstawanie
        float dp = drunk != null ? drunk.DownPose : 0f;
        if (dp > 0f)
        {
            if (IsOwner && Emote.Value != 0) Emote.Value = 0;
            body.SetLocalPositionAndRotation(bodyHome + Vector3.down * (LyingDrop * dp),
                Quaternion.Euler(-88f * dp, 0f, 0f));
            SetArmL(Quaternion.identity);
            SetArmR(Quaternion.identity);
            legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
            downDirty = true;
            OwnerCamera(true);
            return;
        }
        if (downDirty) { downDirty = false; ResetPose(); } // wstał — kamera i Body do domu

        bool holdingBeer = drunk != null && drunk.Beers.Value > 0;
        if (holdingBeer && IsOwner && Emote.Value != 0) Emote.Value = 0;
        if (Emote.Value != 0)
        {
            emoteT += Time.deltaTime;
            ApplyEmote(Emote.Value, emoteT);
            OwnerCamera(Emote.Value is 2 or 3 or DanceEmote);
            return;
        }
        // bez emotki: chód/idle rysuje Animator, my dokładamy tylko rękę z butelką
        if (holdingBeer) ApplyBeerPose(drunk.DrinkPose);
    }

    // kamera przypięta do głowy przy pozach ruszających Body (leżenie/fikołek/taniec)
    // i przy powaleniu: pozycja tuż nad głową — poza bryłą postaci. Rozglądanie
    // w poziomie działa (yaw siedzi na korpusie). Leżąc na plecach patrzysz w górę
    // (bez pitchu myszki — nie da się spojrzeć przez własne ciało).
    // Offset obraca się razem z Body, więc POV faktycznie podąża za fikołkiem,
    // zamiast stać w miejscu i tylko obracać obraz.
    void OwnerCamera(bool attachToHead)
    {
        if (!IsOwner || cam == null || DrunkPose || !attachToHead) return;
        cam.position = head.position + body.up * headCamDist;
        cam.localRotation = body.localRotation;
    }

    void ApplyBeerPose(float drink)
    {
        SetArmR(Quaternion.Slerp(
            Quaternion.Euler(-80f, -40f, -25f),
            Quaternion.Euler(-110f, 0f, -75f), drink));
        if (bottleFollow == null || handR == null) return;

        Vector3 grip = handR.position + handR.rotation * bottleFollow.posOffset;
        Vector3 mouth = head.position + transform.forward * 0.12f - transform.up * 0.06f;
        Vector3 bottleUp = Vector3.Slerp(transform.up, (mouth - grip).normalized, drink);
        Quaternion upright = transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
        Quaternion wanted = Quaternion.FromToRotation(transform.up, bottleUp) * upright;
        bottleFollow.rotOffset = Quaternion.Inverse(handR.rotation) * wanted;
    }

    static float EmoteDur(byte e) => e switch
    { 1 => 2.5f, 3 => 1.6f, 4 => 2.2f, 6 => 2.5f, _ => float.MaxValue }; // 2 i 5 = pętle

    void OwnerInput(float speed)
    {
        if (Emote.Value != 0)
        {
            // ruch albo koniec czasu przerywa emotkę
            if (speed > 0.5f || emoteT > EmoteDur(Emote.Value)) Emote.Value = 0;
        }
        var kb = Keyboard.current;
        if (kb == null || DrunkPose || speed > 0.5f) return;
        if (drunk != null && drunk.Downed) return; // leżysz po popchnięciu — bez emotek
        if (Competition.Current != null || Cursor.lockState != CursorLockMode.Locked) return;
        for (int i = 0; i < 6; i++)
            if (kb[Key.Digit1 + i].wasPressedThisFrame)
                Emote.Value = Emote.Value == i + 1 ? (byte)0 : (byte)(i + 1); // toggle
    }

    void ApplyEmote(byte e, float t)
    {
        switch (e)
        {
            case 1: // machanie prawą ręką obok głowy
                // Obrót MUSI iść po Z (płaszczyzna czołowa) — po X ręka szła do przodu
                // przed klatę zamiast w górę. Zmierzone na kości Hand: Z~120 (efektywnie,
                // z armOutDeg) daje dłoń x=+0.15 y=+0.29, ok. 0.45 m od głowy.
                SetArmR(Quaternion.Euler(0f, 0f, 110f + Mathf.Sin(t * 8f) * 15f));
                if (handR != null)
                    handR.localRotation = handRHome * Quaternion.Euler(0f, -90f, 0f);
                break;
            case 2: // leżenie na plecach, ręce jak w pozycji stojącej
                body.SetLocalPositionAndRotation(bodyHome + Vector3.down * LyingDrop,
                    Quaternion.Euler(-88f, 0f, 0f));
                SetArmL(Quaternion.identity);
                SetArmR(Quaternion.identity);
                legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
                break;
            case 3: // fikołek w tył z podskokiem
            {
                float k = Mathf.Clamp01(t / 1.6f);
                body.SetLocalPositionAndRotation(
                    bodyHome + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 0.6f),
                    Quaternion.Euler(-360f * k, 0f, 0f));
                break;
            }
            case 4: // salut: prawa ręka do czoła, na baczność
                SetArmR(Quaternion.Euler(-150f, 0f, -55f));
                SetArmL(Quaternion.identity);
                legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
                break;
            case DanceEmote:
                break; // taniec leci z klipu AccuRig — nic nie dopisujemy do kości
            case 6: // wskazanie palcem przed siebie
                SetArmR(Quaternion.Euler(-90f, 0f, 0f));
                break;
        }
    }

    void OnGUI()
    {
        if (!IsOwner || !IsSpawned || Competition.Current != null
            || Cursor.lockState != CursorLockMode.Locked) return;
        GUI.Label(new Rect(10f, Screen.height - 26f, 600f, 22f),
            Emote.Value != 0
                ? $"Emotka: {emoteNames[Emote.Value]} (ruch przerywa)"
                : "[1-6] emotki: machaj / leż / fikołek / salut / taniec / wskaż",
            new GUIStyle(GUI.skin.label) { fontSize = 13 });
    }
}
