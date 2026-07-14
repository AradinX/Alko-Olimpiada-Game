using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Proceduralny chód + emotki [1-6] na postaci gracza. Napędza kości modelu
// (Guy.fbx, rig AccuRig CC_Base_*) albo stare klockowe pivoty — Limb liczy
// retarget z bind pose, więc oba rigi dostają identyczną choreografię.
// Chód: nogi i ręce machają w przeciwfazie proporcjonalnie do obserwowanej
// prędkości — działa też dla postaci zdalnych, bo NetworkTransform je przesuwa.
// Emotki: choreografia na kończynach i Body, replikowana NetworkVariable;
// ruch przerywa emotkę, pozy pijackie (Zgon/rzyganie) mają priorytet.
public class PlayerLimbs : NetworkBehaviour
{
    // pokrętła ruchu — do stroju w Inspectorze na prefabie gracza
    public float stepPerMeter = 5f;    // fazy chodu na metr drogi (częstotliwość kroków)
    public float swingDeg = 60f;       // maksymalny wymach nóg przy chodzie
    public float armSwingScale = 0.8f; // wymach rąk jako ułamek wymachu nóg
    public float armOutDeg = 35f;      // stałe odchylenie rąk od tułowia (żeby nie wchodziły w brzuch)

    // Kończyna przyjmuje obroty w osiach Body (jak stare klocki) i przelicza je
    // na lokalną przestrzeń kości. Pozą spoczynkową jest DOKŁADNIE to, co widać
    // w prefabie gracza (bind = localRotation kości z prefabu) — pozę ustawia
    // SetupCharacterModel i można ją ręcznie poprawiać obracając kości CC_Base_*
    // w edytorze. Gra niczego nie prostuje sama. m/mi konwertują osie
    // Body<->parent kości; dla klockowego pivota całość degeneruje się do
    // starego `localRotation = q`.
    class Limb
    {
        readonly Transform t;
        readonly Quaternion bind, m, mi;
        public Limb(Transform t, Transform body)
        {
            this.t = t;
            bind = t.localRotation;
            m = Quaternion.Inverse(t.parent.rotation) * body.rotation;
            mi = Quaternion.Inverse(m);
        }
        public void Set(Quaternion q) => t.localRotation = m * q * mi * bind;
    }

    // 0 = brak; 1 machanie, 2 leżenie, 3 fikołek, 4 salut, 5 taniec, 6 wskazanie
    public NetworkVariable<byte> Emote = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    static readonly string[] emoteNames =
    { "", "machanie", "leżenie", "fikołek", "salut", "taniec", "wskazanie" };

    Transform body, head, cam;
    Limb armL, armR, legL, legR;
    Vector3 camHome; // domyślna pozycja kamery (przy oku)
    DrunkSystem drunk;
    PlayerController pc;
    Vector3 lastPos;
    float phase, amp, emoteT;
    bool downDirty; // poza powalenia wymaga resetu po wstaniu

    public override void OnNetworkSpawn()
    {
        body = transform.Find("Body");
        // kości modelu (Guy.fbx), a gdy ich nie ma — klockowe pivoty
        Transform Bone(string cc, string blocky) { var b = Deep(body, cc); return b != null ? b : body.Find(blocky); }
        armL = new Limb(Bone("CC_Base_L_Upperarm", "ArmL"), body);
        armR = new Limb(Bone("CC_Base_R_Upperarm", "ArmR"), body);
        legL = new Limb(Bone("CC_Base_L_Thigh", "LegL"), body);
        legR = new Limb(Bone("CC_Base_R_Thigh", "LegR"), body);
        head = Bone("CC_Base_Head", "Head");
        drunk = GetComponent<DrunkSystem>();
        lastPos = transform.position;
        if (IsOwner)
        {
            pc = GetComponent<PlayerController>();
            cam = pc.playerCamera.transform;
            camHome = cam.localPosition;
        }
        Emote.OnValueChanged += (_, _) => { emoteT = 0f; ResetPose(); };

        // kolor koszulki z id gracza — rozpoznawalność w greyboxie
        var torso = body.Find("Torso");
        if (torso != null)
            torso.GetComponent<Renderer>().material.color =
                Color.HSVToRGB(OwnerClientId * 0.618f % 1f, 0.65f, 0.9f);
    }

    bool DrunkPose => drunk != null && (drunk.PassedOut.Value || drunk.Vomiting.Value);

    // każda poza rąk dostaje odchylenie na zewnątrz — model ma szeroki tułów
    // i choreografia strojona na klockach wchodziła w brzuch
    void SetArmL(Quaternion q) => armL.Set(q * Quaternion.Euler(0f, 0f, armOutDeg));
    void SetArmR(Quaternion q) => armR.Set(q * Quaternion.Euler(0f, 0f, -armOutDeg));

    void ResetPose()
    {
        if (cam != null) cam.localPosition = camHome; // kamera wraca do oka
        if (DrunkPose) return; // Zgon/rzyganie ustawia Body po swojemu — nie ruszaj
        body.SetLocalPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
        SetArmL(Quaternion.identity); SetArmR(Quaternion.identity);
        legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
    }

    static Transform Deep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // kamera przypięta do głowy przy emotkach ruszających Body (leżenie/fikołek/taniec)
    // i przy powaleniu: pozycja tuż nad głową — poza bryłą postaci. Rozglądanie
    // w poziomie działa (yaw siedzi na korpusie). Leżąc na plecach patrzysz w górę
    // (bez pitchu myszki — nie da się spojrzeć przez własne ciało).
    // Po emotce PlayerController/ResetPose przywracają widok.
    void LateUpdate()
    {
        if (!IsOwner || cam == null || DrunkPose) return;
        bool lying = Emote.Value == 2 || (drunk != null && drunk.DownPose > 0f);
        if (!lying && Emote.Value is not (3 or 5)) return;
        cam.position = head.position + (head.position - body.position).normalized * 0.22f;
        cam.localRotation = body.localRotation;
    }

    static float EmoteDur(byte e) => e switch
    { 1 => 2.5f, 3 => 1.6f, 4 => 2.2f, 6 => 2.5f, _ => float.MaxValue }; // 2 i 5 = pętle

    void Update()
    {
        Vector3 d = transform.position - lastPos;
        lastPos = transform.position;
        d.y = 0f;
        float speed = d.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);

        if (IsOwner) OwnerInput(speed);

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
        if (dp > 0f && !DrunkPose)
        {
            if (IsOwner && Emote.Value != 0) Emote.Value = 0;
            body.SetLocalPositionAndRotation(new Vector3(0f, Mathf.Lerp(1f, 0.35f, dp), 0f),
                Quaternion.Euler(-88f * dp, 0f, 0f));
            SetArmL(Quaternion.Euler(0f, 0f, 55f * dp));
            SetArmR(Quaternion.Euler(0f, 0f, -55f * dp));
            legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
            downDirty = true;
            return;
        }
        if (downDirty) { downDirty = false; ResetPose(); } // wstał — kamera i Body do domu

        if (Emote.Value != 0 && !DrunkPose)
        {
            emoteT += Time.deltaTime;
            ApplyEmote(Emote.Value, emoteT);
            return;
        }
        // zwykły chód
        amp = Mathf.Lerp(amp, Mathf.Clamp01(speed / 4f), 10f * Time.deltaTime);
        phase += d.magnitude * stepPerMeter;
        float a = Mathf.Sin(phase) * swingDeg * amp;
        legL.Set(Quaternion.Euler(a, 0f, 0f));
        legR.Set(Quaternion.Euler(-a, 0f, 0f));
        SetArmL(Quaternion.Euler(-a * armSwingScale, 0f, 0f));
        SetArmR(Quaternion.Euler(a * armSwingScale, 0f, 0f));
    }

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
            case 1: // machanie prawą ręką nad głową
                SetArmR(Quaternion.Euler(-150f + Mathf.Sin(t * 8f) * 25f, 0f, -15f));
                break;
            case 2: // leżenie na plecach, ręce rozłożone
                body.SetLocalPositionAndRotation(new Vector3(0f, 0.35f, 0f),
                    Quaternion.Euler(-88f, 0f, 0f));
                SetArmL(Quaternion.Euler(0f, 0f, 55f));
                SetArmR(Quaternion.Euler(0f, 0f, -55f));
                break;
            case 3: // fikołek w tył z podskokiem
            {
                float k = Mathf.Clamp01(t / 1.6f);
                body.SetLocalPositionAndRotation(
                    new Vector3(0f, 1f + Mathf.Sin(k * Mathf.PI) * 0.6f, 0f),
                    Quaternion.Euler(-360f * k, 0f, 0f));
                break;
            }
            case 4: // salut: prawa ręka do czoła, na baczność
                SetArmR(Quaternion.Euler(-150f, 0f, -55f));
                SetArmL(Quaternion.identity);
                legL.Set(Quaternion.identity); legR.Set(Quaternion.identity);
                break;
            case 5: // taniec: ręce na zmianę w górę, podskok, biodra
            {
                float s = Mathf.Sin(t * 7f);
                body.SetLocalPositionAndRotation(
                    new Vector3(0f, 1f + Mathf.Abs(s) * 0.09f, 0f),
                    Quaternion.Euler(0f, s * 18f, 0f));
                SetArmL(Quaternion.Euler(-90f + s * 70f, 0f, 20f));
                SetArmR(Quaternion.Euler(-90f - s * 70f, 0f, -20f));
                legL.Set(Quaternion.Euler(s * 15f, 0f, 0f));
                legR.Set(Quaternion.Euler(-s * 15f, 0f, 0f));
                break;
            }
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
