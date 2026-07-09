using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// System upojenia: poziom 0-100 replikowany z serwera, bujanie kamery u właściciela,
// Zgon przy 100 (utrata kontroli), cucenie przez innego gracza klawiszem E.
public class DrunkSystem : NetworkBehaviour
{
    public float decayPerSecond = 0.4f;
    public float swayStart = 15f;   // poniżej tego progu brak efektów
    public float reviveRange = 3f;
    public float reviveTo = 50f;

    public NetworkVariable<float> Drunk = new();       // 0-100, zapis: serwer
    public NetworkVariable<bool> PassedOut = new();

    Transform body;
    Camera cam;
    DrunkSystem reviveTarget; // pobliski leżący gracz (tylko u właściciela)

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

        // ponytail: FindObjectsByType co klatkę — graczy jest max 10, wystarczy
        reviveTarget = null;
        foreach (var d in FindObjectsByType<DrunkSystem>(FindObjectsSortMode.None))
        {
            if (d == this || !d.PassedOut.Value) continue;
            if (Vector3.Distance(d.transform.position, transform.position) <= reviveRange)
            { reviveTarget = d; break; }
        }
        if (reviveTarget != null && Keyboard.current != null
            && Keyboard.current.eKey.wasPressedThisFrame)
            reviveTarget.ReviveRpc();
    }

    // zbieranie piw: trigger łapie właściciel (CC.Move działa tylko u niego), serwer waliduje
    void OnTriggerEnter(Collider other)
    {
        if (!IsOwner || PassedOut.Value) return;
        var beer = other.GetComponentInParent<BeerPickup>();
        if (beer != null) beer.RequestPickupRpc();
    }

    void LateUpdate()
    {
        if (!IsOwner) return;
        if (PassedOut.Value)
        {
            cam.transform.localRotation = Quaternion.Euler(0f, 0f, 75f); // leżysz
            return;
        }
        float t = Mathf.InverseLerp(swayStart, 100f, Drunk.Value);
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
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label)
        { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var center = new Rect(0, Screen.height * 0.4f, Screen.width, 40);
        if (PassedOut.Value) GUI.Label(center, "ZGON — czekaj aż cię ocucą", style);
        else if (reviveTarget != null) GUI.Label(center, "[E] Ocuć kolegę", style);
    }
}
