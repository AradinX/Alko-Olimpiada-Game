using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Szatnia na hubie: podejdź, [E] otwiera listę ciuchów, klik zakłada/zdejmuje.
// UI czysto lokalne — sam strój replikuje PlayerOutfit (NetworkVariable).
public class WardrobeShop : MonoBehaviour
{
    public static bool Open; // PauseMenu chowa się, gdy szatnia otwarta (oba wiszą na odblokowanym kursorze)

    public float radius = 4f;
    bool open;

    PlayerOutfit Outfit()
    {
        var nm = NetworkManager.Singleton;
        var po = nm != null && nm.IsClient ? nm.LocalClient?.PlayerObject : null;
        return po != null ? po.GetComponent<PlayerOutfit>() : null;
    }

    bool Near(PlayerOutfit o) =>
        o != null && Vector3.Distance(o.transform.position, transform.position) <= radius;

    // zmiana sceny (głosowanie przeszło, gdy stałeś w szatni) nie może zostawić flagi
    void OnDestroy() { if (open) Open = false; }

    void Update()
    {
        var o = Outfit();
        if (open && (!Near(o) || Cursor.lockState == CursorLockMode.Locked))
        { open = false; Open = false; return; } // odszedł albo ESC zamknął kursor

        var kb = Keyboard.current;
        if (Near(o) && kb != null && kb.eKey.wasPressedThisFrame)
        {
            open = !open;
            Open = open;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
        }
    }

    void OnGUI()
    {
        var o = Outfit();
        if (!Near(o)) return;
        if (!open)
        {
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40),
                "[E] Szatnia — przebierz się", Ui.S(28));
            return;
        }
        float w = 280f, h = 60f + o.itemNames.Length * 34f + 40f;
        var r = new Rect(Screen.width / 2f - w / 2f, Screen.height / 2f - h / 2f, w, h);
        GUI.Box(r, "SZATNIA");
        for (int i = 0; i < o.itemNames.Length; i++)
        {
            bool worn = o.Worn(i);
            if (GUI.Button(new Rect(r.x + 20f, r.y + 30f + i * 34f, w - 40f, 28f),
                (worn ? "[x] " : "[  ] ") + o.itemNames[i]))
                o.Toggle(i);
        }
        if (GUI.Button(new Rect(r.x + 20f, r.y + h - 36f, w - 40f, 28f), "Zamknij [E]"))
        { open = false; Open = false; Cursor.lockState = CursorLockMode.Locked; }
    }
}
