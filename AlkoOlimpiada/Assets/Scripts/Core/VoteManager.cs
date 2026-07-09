using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Głosowanie na następną konkurencję (GDD sekcja 4): [R] przy stanowisku = głos.
// Gdy zagłosują wszyscy — losowanie ważone głosami (każdy głos to los w kapeluszu).
// Rozegrane konkurencje wypadają z puli; po wszystkich — podsumowanie i mistrz.
public class VoteManager : NetworkBehaviour
{
    public static VoteManager Instance;

    public string[] scenes;   // sceny aren (ustawia bootstrap)
    public string[] titles;   // nazwy wyświetlane, równolegle do scenes
    public float drawSeconds = 3f;
    public float launchDelay = 2.5f;

    public NetworkVariable<FixedString512Bytes> Scoreboard = new();
    public NetworkVariable<FixedString512Bytes> VotesLine = new();
    public NetworkVariable<FixedString512Bytes> StatusText = new();
    public NetworkVariable<FixedString512Bytes> PlayedRaw = new();
    public NetworkVariable<FixedString512Bytes> FinalText = new();

    static readonly HashSet<string> played = new(); // przeżywa zmiany scen

    readonly Dictionary<ulong, string> votes = new(); // serwer
    double drawAt = -1, launchAt = -1;
    string winner;

    bool Finished => played.Count >= scenes.Length;
    double Now => NetworkManager.ServerTime.Time;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (!IsServer) return;
        Scoreboard.Value = Olympics.Text();
        PlayedRaw.Value = string.Join(";", played);
        if (Finished)
        {
            FinalText.Value = "KONIEC OLIMPIADY!\nMISTRZ: " + Olympics.Champion()
                + "\n\n" + Olympics.Text() + "\n\n[R] Nowa olimpiada";
            Debug.Log("[Vote] KONIEC OLIMPIADY — mistrz: " + Olympics.Champion());
        }
        else UpdateLine();
    }

    public override void OnNetworkDespawn()
    { if (Instance == this) Instance = null; }

    // klient: czy stanowiska mogą przyjmować głosy
    public bool VotingOpen => FinalText.Value.Length == 0 && StatusText.Value.Length == 0;
    public bool IsPlayed(string scene) => PlayedRaw.Value.ToString().Contains(scene);

    [Rpc(SendTo.Server)]
    public void VoteRpc(FixedString64Bytes sceneFs, RpcParams p = default)
    {
        if (drawAt >= 0 || Finished) return;
        string scene = sceneFs.ToString();
        if (!scenes.Contains(scene) || played.Contains(scene)) return;
        ulong id = p.Receive.SenderClientId;
        if (votes.TryGetValue(id, out var cur) && cur == scene) votes.Remove(id); // toggle
        else votes[id] = scene;
        foreach (var dead in votes.Keys
                     .Where(k => !NetworkManager.ConnectedClientsIds.Contains(k)).ToList())
            votes.Remove(dead);
        UpdateLine();
        if (votes.Count > 0 && votes.Count == NetworkManager.ConnectedClientsIds.Count)
        {
            drawAt = Now + drawSeconds;
            StatusText.Value = "LOSOWANIE...";
        }
    }

    [Rpc(SendTo.Server)]
    public void NewOlympicsRpc()
    {
        if (!Finished) return;
        played.Clear();
        Olympics.Reset();
        PlayedRaw.Value = "";
        FinalText.Value = "";
        Scoreboard.Value = "";
        UpdateLine();
        Debug.Log("[Vote] nowa olimpiada");
    }

    void UpdateLine()
    {
        int total = NetworkManager.ConnectedClientsIds.Count;
        var parts = scenes.Select((s, i) => (s, i))
            .Where(x => !played.Contains(x.s))
            .Select(x => $"{titles[x.i]}: {votes.Values.Count(v => v == x.s)}");
        VotesLine.Value = $"GŁOSY ({votes.Count}/{total}):   " + string.Join("   ", parts);
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer)
        {
            if (drawAt >= 0 && launchAt < 0 && Now >= drawAt)
            {
                // losowanie ważone: każdy głos to jeden los
                var pool = votes.Values.ToList();
                winner = pool[UnityEngine.Random.Range(0, pool.Count)];
                StatusText.Value = "WYLOSOWANO: " + titles[Array.IndexOf(scenes, winner)] + "!";
                Debug.Log($"[Vote] wylosowano {winner} ({pool.Count(v => v == winner)}/{pool.Count} glosow)");
                launchAt = Now + launchDelay;
            }
            else if (launchAt >= 0 && launchAt < double.MaxValue && Now >= launchAt)
            {
                launchAt = double.MaxValue; // nie ładuj dwa razy
                played.Add(winner);
                NetworkManager.SceneManager.LoadScene(winner,
                    UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }
        // po podsumowaniu każdy może wystartować nową olimpiadę
        if (NetworkManager.IsClient && FinalText.Value.Length > 0
            && Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            NewOlympicsRpc();
    }

    void OnGUI()
    {
        if (!IsSpawned || !NetworkManager.IsClient) return;
        if (Scoreboard.Value.Length > 0)
            GUI.Label(new Rect(0, 8, Screen.width, 24), Scoreboard.Value.ToString(), Ui.S(16));
        if (FinalText.Value.Length > 0)
        {
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 400),
                FinalText.Value.ToString(), Ui.S(28));
            return;
        }
        if (VotesLine.Value.Length > 0)
            GUI.Label(new Rect(0, 34, Screen.width, 22), VotesLine.Value.ToString(), Ui.S(14));
        if (StatusText.Value.Length > 0)
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 60),
                StatusText.Value.ToString(), Ui.S(36));
    }
}
