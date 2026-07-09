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
    public Camera playerCamera;

    CharacterController cc;
    DrunkSystem drunk;
    float pitch;
    float yVelocity;

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
    public void TeleportRpc(Vector3 pos, float yaw)
    {
        cc.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
        pitch = 0f;
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

        if (drunk != null && drunk.PassedOut.Value) return; // Zgon = brak kontroli

        if (Cursor.lockState == CursorLockMode.Locked && mouse != null)
        {
            Vector2 look = mouse.delta.ReadValue() * lookSensitivity;
            transform.Rotate(0f, look.x, 0f);
            pitch = Mathf.Clamp(pitch - look.y, -85f, 85f);
        }
        // reset co klatkę — DrunkSystem/konkurencje dokładają efekty w LateUpdate
        playerCamera.transform.localEulerAngles = new Vector3(pitch, 0f, 0f);

        if (Competition.InputLocked) return; // konkurencja: patrzysz, ale nie chodzisz

        float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        float speed = kb.leftShiftKey.isPressed ? sprintSpeed : walkSpeed;
        Vector3 move = (transform.right * x + transform.forward * z).normalized * speed;

        if (cc.isGrounded)
        {
            yVelocity = -2f;
            if (kb.spaceKey.wasPressedThisFrame)
                yVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }
        yVelocity += gravity * Time.deltaTime;
        move.y = yVelocity;
        cc.Move(move * Time.deltaTime);
    }
}
