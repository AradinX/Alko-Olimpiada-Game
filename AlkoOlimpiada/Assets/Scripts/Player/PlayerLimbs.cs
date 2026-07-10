using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Klockowa postać (placeholder do art passu): proceduralny chód + emotki [1-6].
// Chód: nogi i ręce machają w przeciwfazie proporcjonalnie do obserwowanej
// prędkości — działa też dla postaci zdalnych, bo NetworkTransform je przesuwa.
// Emotki: choreografia na pivotach kończyn i Body, replikowana NetworkVariable;
// ruch przerywa emotkę, pozy pijackie (Zgon/rzyganie) mają priorytet.
public class PlayerLimbs : NetworkBehaviour
{
    public float stepPerMeter = 5f; // fazy chodu na metr drogi
    public float swingDeg = 45f;    // maksymalny wymach kończyn

    // 0 = brak; 1 machanie, 2 leżenie, 3 fikołek, 4 salut, 5 taniec, 6 wskazanie
    public NetworkVariable<byte> Emote = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    static readonly string[] emoteNames =
    { "", "machanie", "leżenie", "fikołek", "salut", "taniec", "wskazanie" };

    Transform body, armL, armR, legL, legR, head, cam;
    Vector3 camHome; // domyślna pozycja kamery (przy oku)
    DrunkSystem drunk;
    Vector3 lastPos;
    float phase, amp, emoteT;

    public override void OnNetworkSpawn()
    {
        body = transform.Find("Body");
        armL = body.Find("ArmL"); armR = body.Find("ArmR");
        legL = body.Find("LegL"); legR = body.Find("LegR");
        head = body.Find("Head");
        drunk = GetComponent<DrunkSystem>();
        lastPos = transform.position;
        if (IsOwner)
        {
            cam = GetComponent<PlayerController>().playerCamera.transform;
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

    void ResetPose()
    {
        if (cam != null) cam.localPosition = camHome; // kamera wraca do oka
        if (DrunkPose) return; // Zgon/rzyganie ustawia Body po swojemu — nie ruszaj
        body.SetLocalPositionAndRotation(new Vector3(0f, 1f, 0f), Quaternion.identity);
        armL.localRotation = armR.localRotation = Quaternion.identity;
        legL.localRotation = legR.localRotation = Quaternion.identity;
    }

    // kamera przypięta do głowy przy emotkach ruszających Body (leżenie/fikołek/taniec):
    // czysty obrót ciała BEZ pitchu myszki (inaczej leżąc patrzysz przez własny brzuch),
    // pozycja tuż nad głową — poza klockami postaci. Rozglądanie w poziomie działa (yaw
    // siedzi na korpusie). Po emotce PlayerController/ResetPose przywracają widok.
    void LateUpdate()
    {
        if (!IsOwner || cam == null || DrunkPose || Emote.Value is not (2 or 3 or 5)) return;
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
        legL.localRotation = Quaternion.Euler(a, 0f, 0f);
        legR.localRotation = Quaternion.Euler(-a, 0f, 0f);
        armL.localRotation = Quaternion.Euler(-a * 0.8f, 0f, 0f);
        armR.localRotation = Quaternion.Euler(a * 0.8f, 0f, 0f);
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
                armR.localRotation = Quaternion.Euler(-150f + Mathf.Sin(t * 8f) * 25f, 0f, -15f);
                break;
            case 2: // leżenie na plecach, ręce rozłożone
                body.SetLocalPositionAndRotation(new Vector3(0f, 0.35f, 0f),
                    Quaternion.Euler(-88f, 0f, 0f));
                armL.localRotation = Quaternion.Euler(0f, 0f, 70f);
                armR.localRotation = Quaternion.Euler(0f, 0f, -70f);
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
                armR.localRotation = Quaternion.Euler(-150f, 0f, -55f);
                armL.localRotation = Quaternion.identity;
                legL.localRotation = legR.localRotation = Quaternion.identity;
                break;
            case 5: // taniec: ręce na zmianę w górę, podskok, biodra
            {
                float s = Mathf.Sin(t * 7f);
                body.SetLocalPositionAndRotation(
                    new Vector3(0f, 1f + Mathf.Abs(s) * 0.09f, 0f),
                    Quaternion.Euler(0f, s * 18f, 0f));
                armL.localRotation = Quaternion.Euler(-90f + s * 70f, 0f, 20f);
                armR.localRotation = Quaternion.Euler(-90f - s * 70f, 0f, -20f);
                legL.localRotation = Quaternion.Euler(s * 15f, 0f, 0f);
                legR.localRotation = Quaternion.Euler(-s * 15f, 0f, 0f);
                break;
            }
            case 6: // wskazanie palcem przed siebie
                armR.localRotation = Quaternion.Euler(-90f, 0f, 0f);
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
