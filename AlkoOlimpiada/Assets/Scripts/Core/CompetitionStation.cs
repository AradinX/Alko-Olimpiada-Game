using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Stanowisko konkurencji na hubie: [R] w pobliżu zgłasza gotowość,
// gdy wszyscy połączeni są gotowi — serwer ładuje scenę areny.
public class CompetitionStation : NetworkBehaviour
{
    public string title;
    public string sceneName;
    public string autoFlag;      // flaga CLI smoke-testu (np. -autosprint)
    public int uiSlot;           // wiersz na liście gotowości
    public bool showScoreboard;  // rysuje tabelę punktów (tylko jedno stanowisko)
    public float radius = 5f;

    public NetworkVariable<int> ReadyCount = new();
    public NetworkVariable<int> TotalCount = new();
    public NetworkVariable<bool> Launching = new();
    public NetworkVariable<FixedString512Bytes> ScoreboardText = new();

    readonly HashSet<ulong> ready = new(); // serwer
    bool autoSent;
    double autoAt;

    public override void OnNetworkSpawn()
    {
        autoAt = Time.timeAsDouble + 10; // czas na dołączenie drugiej instancji
        if (IsServer) ScoreboardText.Value = Olympics.Text();
    }

    [Rpc(SendTo.Server)]
    void ReadyRpc(RpcParams p = default)
    {
        if (Launching.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!ready.Remove(id)) ready.Add(id); // toggle
        ready.RemoveWhere(x => !NetworkManager.ConnectedClientsIds.Contains(x));
        ReadyCount.Value = ready.Count;
        TotalCount.Value = NetworkManager.ConnectedClientsIds.Count;
        if (ready.Count > 0 && ready.Count == TotalCount.Value)
        {
            Launching.Value = true;
            Debug.Log($"[Station] start {sceneName}");
            NetworkManager.SceneManager.LoadScene(sceneName,
                UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    void Update()
    {
        if (!IsSpawned || !NetworkManager.IsClient) return;
        var kb = Keyboard.current;
        if (Near() && kb != null && kb.rKey.wasPressedThisFrame) ReadyRpc();
        if (!autoSent && autoFlag != null && autoFlag.Length > 0
            && Time.timeAsDouble >= autoAt
            && Array.IndexOf(Environment.GetCommandLineArgs(), autoFlag) >= 0)
        { autoSent = true; ReadyRpc(); }
    }

    bool Near()
    {
        var po = NetworkManager.LocalClient?.PlayerObject;
        return po != null &&
            Vector3.Distance(po.transform.position, transform.position) <= radius;
    }

    void OnGUI()
    {
        if (!IsSpawned || !NetworkManager.IsClient) return;
        if (showScoreboard && ScoreboardText.Value.Length > 0)
            GUI.Label(new Rect(0, 8, Screen.width, 24), ScoreboardText.Value.ToString(), Ui.S(16));
        if (ReadyCount.Value > 0)
            GUI.Label(new Rect(0, 34 + uiSlot * 20, Screen.width, 20),
                $"{title} — gotowi {ReadyCount.Value}/{TotalCount.Value}", Ui.S(14));
        if (Near())
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40),
                $"[R] {title} — zgłoś gotowość", Ui.S(28));
    }
}
