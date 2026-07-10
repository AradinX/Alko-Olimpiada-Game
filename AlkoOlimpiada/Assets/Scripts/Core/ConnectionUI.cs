using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

// Menu połączeń: ONLINE przez Unity Relay (kod pokoju, zero konfiguracji routera)
// z fallbackiem na LAN po IP. ponytail: IMGUI zamiast Canvasa — na prototyp wystarczy.
public class ConnectionUI : MonoBehaviour
{
    public static string LocalNickname = "Gracz";
    public static GameObject SceneCamera;

    public GameObject beerPrefab; // rejestrowany w NGO — wyrzucane piwo spawnuje się w runtime

    string nickname;
    string ip = "127.0.0.1";
    string joinCode = "";
    string hostCode = "";
    string status = "";
    bool busy, loadingHub;

    void Awake()
    {
        SceneCamera = GameObject.FindWithTag("MainCamera");
        nickname = "Gracz" + UnityEngine.Random.Range(100, 1000);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void Start()
    {
        // przed startem sieci, na hoście i klientach — inaczej spawn nie zmapuje prefabu
        if (beerPrefab != null) NetworkManager.Singleton.AddNetworkPrefab(beerPrefab);

        NetworkManager.Singleton.OnClientConnectedCallback += id =>
            Debug.Log($"[Net] Polaczony klient {id}" + (NetworkManager.Singleton.IsServer
                ? $", graczy: {NetworkManager.Singleton.ConnectedClients.Count}" : ""));
        NetworkManager.Singleton.OnClientDisconnectCallback += id =>
            Debug.Log($"[Net] Rozlaczony klient {id}");

        // ponytail: flagi CLI do headless smoke-testów (LAN i Relay)
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("-autohost")) HostLan();
        else if (args.Contains("-autojoin")) JoinLan();
        else if (args.Contains("-autohostonline")) HostOnline();
        else if (args.Contains("-autojoincode"))
        {
            joinCode = args[Array.IndexOf(args, "-autojoincode") + 1];
            JoinOnline();
        }
    }

    void Update()
    {
        // rozłączenie poza hubem = brak kamery sceny; wróć lokalnie na Hub (menu)
        var nm = NetworkManager.Singleton;
        if (!loadingHub && nm != null && !nm.IsClient && !nm.IsServer
            && SceneManager.GetActiveScene().name != "Hub")
        {
            loadingHub = true;
            SceneManager.LoadScene("Hub");
        }
    }

    void OnSceneLoaded(Scene s, LoadSceneMode m)
    {
        if (s.name != "Hub") return;
        loadingHub = false;
        var nm = NetworkManager.Singleton;
        if (nm == null || (!nm.IsClient && !nm.IsServer))
        {
            SceneCamera = GameObject.FindWithTag("MainCamera");
            if (SceneCamera != null) SceneCamera.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
        }
    }

    void HostLan()
    {
        LocalNickname = nickname;
        hostCode = "";
        NetworkManager.Singleton.StartHost();
    }

    void JoinLan()
    {
        LocalNickname = nickname;
        var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        utp.SetConnectionData(ip, 7777);
        NetworkManager.Singleton.StartClient();
    }

    // Transport 2.x nie ma ctor RelayServerData(Allocation) — składamy z pól alokacji
    static RelayServerData ToServerData(Allocation a)
    {
        var ep = a.ServerEndpoints.First(e => e.ConnectionType == "dtls");
        return new RelayServerData(ep.Host, (ushort)ep.Port, a.AllocationIdBytes,
            a.ConnectionData, a.ConnectionData, a.Key, ep.Secure);
    }

    static RelayServerData ToServerData(JoinAllocation a)
    {
        var ep = a.ServerEndpoints.First(e => e.ConnectionType == "dtls");
        return new RelayServerData(ep.Host, (ushort)ep.Port, a.AllocationIdBytes,
            a.ConnectionData, a.HostConnectionData, a.Key, ep.Secure);
    }

    async void HostOnline()
    {
        busy = true;
        status = "Tworzenie pokoju...";
        try
        {
            await Ugs.EnsureSignedIn();
            var alloc = await RelayService.Instance.CreateAllocationAsync(9);
            hostCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
            var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            utp.SetRelayServerData(ToServerData(alloc));
            LocalNickname = nickname;
            NetworkManager.Singleton.StartHost();
            status = "";
            Debug.Log("[Net] Relay host, kod: " + hostCode);
        }
        catch (Exception e)
        {
            status = "Online niedostępne — podłącz projekt w Unity Cloud (Services)";
            Debug.LogWarning("[Net] Relay host: " + e.Message);
        }
        busy = false;
    }

    async void JoinOnline()
    {
        busy = true;
        status = "Łączenie kodem...";
        try
        {
            await Ugs.EnsureSignedIn();
            var alloc = await RelayService.Instance.JoinAllocationAsync(joinCode.Trim().ToUpper());
            var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            utp.SetRelayServerData(ToServerData(alloc));
            LocalNickname = nickname;
            NetworkManager.Singleton.StartClient();
            status = "";
        }
        catch (Exception e)
        {
            status = "Nie udało się dołączyć (zły kod? brak Unity Cloud?)";
            Debug.LogWarning("[Net] Relay join: " + e.Message);
        }
        busy = false;
    }

    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        GUILayout.BeginArea(new Rect(10, 10, 260, 330), GUI.skin.box);
        if (!nm.IsClient && !nm.IsServer)
        {
            GUILayout.Label("Nick:");
            nickname = GUILayout.TextField(nickname, 16);
            GUI.enabled = !busy;

            GUILayout.Space(6);
            GUILayout.Label("— ONLINE (kod pokoju) —");
            if (GUILayout.Button("STWÓRZ POKÓJ")) HostOnline();
            GUILayout.BeginHorizontal();
            joinCode = GUILayout.TextField(joinCode, 8);
            if (GUILayout.Button("DOŁĄCZ", GUILayout.Width(80))) JoinOnline();
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("— LAN —");
            if (GUILayout.Button("HOSTUJ LAN")) HostLan();
            GUILayout.Label("IP hosta:");
            ip = GUILayout.TextField(ip, 32);
            if (GUILayout.Button("DOŁĄCZ PO IP")) JoinLan();

            GUI.enabled = true;
            if (status.Length > 0) GUILayout.Label(status);
        }
        else
        {
            if (nm.IsHost)
                GUILayout.Label(hostCode.Length > 0
                    ? $"HOST — KOD POKOJU: {hostCode}"
                    : $"HOST — twoje IP: {LocalIP()}");
            else GUILayout.Label("KLIENT");
            if (GUILayout.Button("ROZŁĄCZ")) nm.Shutdown();
        }
        GUILayout.EndArea();
    }

    static string LocalIP()
    {
        try
        {
            var a = Dns.GetHostEntry(Dns.GetHostName()).AddressList
                .FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork);
            return a != null ? a.ToString() : "?";
        }
        catch { return "?"; }
    }
}
