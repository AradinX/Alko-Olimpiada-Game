using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float jumpHeight = 1.2f;
    public float gravity = -20f;
    public float lookSensitivity = 0.08f;
    public float pushForce = 8f;
    public float pushRange = 2.2f;
    public float pushCooldown = 1f;
    public Camera playerCamera;

    CharacterController cc;
    DrunkSystem drunk;
    [HideInInspector] public float pitch; // PlayerLimbs dodaje go do widoku leżąc
    float yVelocity;
    float nextPush;
    Vector3 knock;               // odrzut po popchnięciu
    PlayerController aimTarget;  // gracz na celowniku (hub)

    void Awake()
    {
        // GroundRef to edytorski poziomownik w prefabie — tag EditorOnly nie
        // wycina go z prefabów spawnowanych w runtime, więc sprzątamy tu
        var gr = transform.Find("GroundRef");
        if (gr != null) Destroy(gr.gameObject);
    }

    public override void OnNetworkSpawn()
    {
        cc = GetComponent<CharacterController>();
        drunk = GetComponent<DrunkSystem>();
        if (!IsOwner) return;

        Place();
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (ConnectionUI.SceneCamera != null) ConnectionUI.SceneCamera.SetActive(false);
        playerCamera.gameObject.SetActive(true);
        Cursor.lockState = CursorLockMode.Locked;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (ConnectionUI.SceneCamera != null) ConnectionUI.SceneCamera.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
    }

    // rozstaw spawnów na hubie, żeby gracze nie stali w sobie
    void Place()
    {
        cc.enabled = false;
        transform.position = new Vector3((OwnerClientId % 8) * 2f - 4f, 0.1f, -5f);
        cc.enabled = true;
    }

    // konkurencje ustawiają graczy tym RPC (właściciel ma autorytet nad transformem)
    [Rpc(SendTo.Owner)]
    public void TeleportRpc(Vector3 pos, float yaw) => TeleportLocal(pos, yaw);

    public void TeleportLocal(Vector3 pos, float yaw)
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        pitch = 0f;
        yVelocity = 0f;
        knock = Vector3.zero;
        cc.enabled = true;
    }

    void OnSceneLoaded(Scene s, LoadSceneMode mode)
    {
        // wyłącz kamery sceny (menu huba, areny) — po połączeniu liczy się kamera gracza
        foreach (var c in Camera.allCameras)
            if (c != playerCamera && c.GetComponentInParent<PlayerController>() == null)
            {
                if (s.name == "Hub") ConnectionUI.SceneCamera = c.gameObject; // wraca po rozłączeniu
                c.gameObject.SetActive(false);
            }
        if (s.name == "Hub") Place();
    }

    // znany bug Input System: po zmianie okna (alt-tab) stan klawiszy bywa niespójny
    // i WASD przestaje reagować — twardy reset klawiatury przy powrocie focusa
    void OnApplicationFocus(bool focused)
    {
        if (focused && IsOwner && Keyboard.current != null)
            InputSystem.ResetDevice(Keyboard.current);
    }

    void Update()
    {
        if (!IsOwner) return;
        // ponytail: bezpośredni odczyt urządzeń z Input System zamiast assetu .inputactions;
        // asset dodamy, gdy dojdzie rebinding/gamepad
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        if (kb.escapeKey.wasPressedThisFrame)
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked
                ? CursorLockMode.None : CursorLockMode.Locked;

        if (drunk != null && (drunk.PassedOut.Value || drunk.Vomiting.Value)) return; // Zgon/rzyganie

        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * (lookSensitivity * GameSettings.MouseSens);
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        }
        // reset co klatkę — DrunkSystem/konkurencje dokładają efekty w LateUpdate
        playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);

        if (Competition.InputLocked) return; // konkurencja: patrzysz, ale nie chodzisz

        // powalony pchnięciem: patrzysz, ale nie ruszasz się (odrzut i grawitacja działają)
        bool downed = drunk != null && drunk.Downed;

        // agresja (GDD sekcja 6): od etapu "Lekko chycony" można popychać, tylko na hubie
        aimTarget = null;
        if (!downed && Competition.Current == null && Cursor.lockState == CursorLockMode.Locked
            && Physics.Raycast(playerCamera.transform.position, playerCamera.transform.forward,
                out var hit, pushRange)
            && hit.collider.GetComponentInParent<PlayerController>() is { } victim
            && victim != this)
            aimTarget = victim;

        if (aimTarget != null && mouse != null && mouse.leftButton.wasPressedThisFrame
            && Time.time >= nextPush && drunk.Stage >= 2)
        {
            nextPush = Time.time + pushCooldown;
            PushRpc(aimTarget.OwnerClientId);
        }

        // mapowanie klawiszy ruchu zależy od etapu upojenia (DrunkSystem.ApplyControls)
        float x = (kb[drunk.keyD].isPressed ? 1f : 0f) - (kb[drunk.keyA].isPressed ? 1f : 0f);
        float z = (kb[drunk.keyW].isPressed ? 1f : 0f) - (kb[drunk.keyS].isPressed ? 1f : 0f);
        if (drunk.CurseActive(64)) { x = -x; z = -z; } // klątwa: odwrócone sterowanie
        float speed = kb.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * x + transform.forward * z).normalized * speed;
        move = Quaternion.Euler(0f, drunk.VeerAngle(), 0f) * move; // pijacki zygzak (SoT)
        if (downed) move = Vector3.zero;

        if (cc.isGrounded)
        {
            yVelocity = -2f;
            if (kb.spaceKey.wasPressedThisFrame && !downed)
                yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        yVelocity += gravity * Time.deltaTime;
        move += knock;
        knock = Vector3.MoveTowards(knock, Vector3.zero, 12f * Time.deltaTime);
        move.y = yVelocity + knock.y;
        cc.Move(move * Time.deltaTime);

        // hub to wyspa: wpadłeś do wody — wracasz na plażę
        if (Competition.Current == null && transform.position.y < -1.5f) Place();
    }

    [Rpc(SendTo.Server)]
    void PushRpc(ulong targetId, RpcParams p = default)
    {
        // walidacja: etap agresji napastnika + realny zasięg
        if (drunk == null || drunk.Drunk.Value < DrunkSystem.Stages[1].min) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(targetId, out var c)
            || c.PlayerObject == null) return;
        var victim = c.PlayerObject.GetComponent<PlayerController>();
        if (Vector3.Distance(victim.transform.position, transform.position) > pushRange + 1.5f) return;
        Vector3 dir = (victim.transform.position - transform.position).normalized + Vector3.up * 0.35f;
        victim.KnockRpc(dir * pushForce);
        victim.GetComponent<DrunkSystem>().KnockDown(); // popchnięty się przewraca
        Debug.Log($"[Push] {Olympics.Nick(OwnerClientId)} popchnął {Olympics.Nick(targetId)}");

        // mocno wstawiony: pchnięcie potrafi przewrócić i napastnika — im bardziej pijany,
        // tym częściej (0% na progu "Lekko chycony" → 100% przy Zgonie)
        if (Random.value < Mathf.InverseLerp(DrunkSystem.Stages[1].min, 100f, drunk.Drunk.Value))
        {
            KnockRpc((-dir + Vector3.up * 0.7f) * (pushForce * 0.5f));
            drunk.KnockDown();
            drunk.MsgOwner("Popchnąłeś się razem z nim...");
            Debug.Log($"[Push] {Olympics.Nick(OwnerClientId)} przewrócił się od własnego pchnięcia");
        }
    }

    [Rpc(SendTo.Owner)]
    public void KnockRpc(Vector3 impulse) => knock = impulse;

    void OnGUI()
    {
        if (!IsOwner || !IsSpawned || Cursor.lockState != CursorLockMode.Locked) return;
        if (Competition.Current != null) return; // areny mają własne UI/celowniki

        // kropka-celownik na hubie
        GUI.color = new Color(1f, 1f, 1f, 0.7f);
        GUI.DrawTexture(new Rect(Screen.width / 2f - 2f, Screen.height / 2f - 2f, 4f, 4f),
            Texture2D.whiteTexture);
        GUI.color = Color.white;

        if (aimTarget != null)
            GUI.Label(new Rect(0, Screen.height / 2f + 20f, Screen.width, 24f),
                drunk != null && drunk.Stage >= 2
                    ? "[LPM] Popchnij"
                    : "Za trzeźwy, żeby się bić (od etapu: Lekko chycony)",
                new GUIStyle(GUI.skin.label)
                { fontSize = 18, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
    }
}
