using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

// ponytail: menu na IMGUI (OnGUI) zamiast Canvasa — na prototyp wystarczy,
// uGUI dojdzie razem z HUD-em upojenia w Prototypie 2
public class ConnectionUI : MonoBehaviour
{
    public static string LocalNickname = "Gracz";
    public static GameObject SceneCamera;

    string nickname;
    string ip = "127.0.0.1";

    void Awake()
    {
        SceneCamera = GameObject.FindWithTag("MainCamera");
        nickname = "Gracz" + UnityEngine.Random.Range(100, 1000);
    }

    void Start()
    {
        NetworkManager.Singleton.OnClientConnectedCallback += id =>
            Debug.Log($"[Net] Polaczony klient {id}" + (NetworkManager.Singleton.IsServer
                ? $", graczy: {NetworkManager.Singleton.ConnectedClients.Count}" : ""));
        NetworkManager.Singleton.OnClientDisconnectCallback += id =>
            Debug.Log($"[Net] Rozlaczony klient {id}");

        // ponytail: flagi CLI do headless smoke-testu host+klient
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("-autohost")) Host();
        else if (args.Contains("-autojoin")) Join();
    }

    void Host()
    {
        LocalNickname = nickname;
        NetworkManager.Singleton.StartHost();
    }

    void Join()
    {
        LocalNickname = nickname;
        var utp = (UnityTransport)NetworkManager.Singleton.NetworkConfig.NetworkTransport;
        utp.SetConnectionData(ip, 7777);
        NetworkManager.Singleton.StartClient();
    }

    void OnGUI()
    {
        var nm = NetworkManager.Singleton;
        GUILayout.BeginArea(new Rect(10, 10, 240, 220), GUI.skin.box);
        if (!nm.IsClient && !nm.IsServer)
        {
            GUILayout.Label("Nick:");
            nickname = GUILayout.TextField(nickname, 16);
            if (GUILayout.Button("HOSTUJ")) Host();
            GUILayout.Space(8);
            GUILayout.Label("IP hosta:");
            ip = GUILayout.TextField(ip, 32);
            if (GUILayout.Button("DOLACZ")) Join();
        }
        else
        {
            GUILayout.Label(nm.IsHost ? $"HOST — twoje IP: {LocalIP()}" : "KLIENT");
            if (GUILayout.Button("ROZLACZ")) nm.Shutdown();
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
