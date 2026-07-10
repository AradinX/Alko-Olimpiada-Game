using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Baza konkurencji: kontroler leży w scenie areny (in-scene NetworkObject).
// Przebieg: wszyscy klienci wczytali arenę -> teleport na pozycje (naprzeciwko siebie)
// -> odliczanie -> gra (implementuje klasa pochodna) -> medale -> powrót na Hub.
public abstract class Competition : NetworkBehaviour
{
    public enum Phase : byte { Waiting, Countdown, Running, Results }

    public float countdownSeconds = 3f;
    public float timeoutSeconds = 60f;
    public float resultsSeconds = 8f;
    public float spawnRadius = 3f;
    public float naturalDrunkGain = 0f; // GDD: każda konkurencja upija (0 gdy gra sama poi)

    public static Competition Current;    // aktywna konkurencja (null na hubie)

    // blokada WASD/skoku (patrzenie działa); Spacer pozwala chodzić w Running.
    // Liczona z Current zamiast flagi: OnNetworkDespawn przy zmianie sceny bywał
    // pomijany na hoście i flaga zostawała true — zniszczony obiekt == null i po sprawie
    public static bool InputLocked =>
        Current != null && (Current.State.Value != Phase.Running || Current.LockMovement);

    protected virtual bool LockMovement => true; // Spacer nadpisuje na false

    public NetworkVariable<Phase> State = new();
    public NetworkVariable<double> PhaseEndsAt = new();
    public NetworkVariable<FixedString512Bytes> ResultsText = new();
    public NetworkVariable<FixedString512Bytes> ScoreboardText = new();

    protected readonly List<ulong> racers = new(); // serwer
    protected double raceStart;
    protected bool autoMode;

    protected abstract string AutoFlag { get; } // flaga CLI smoke-testu, np. -autosprint

    protected NetworkManager NM => NetworkManager;
    protected double Now => NM.ServerTime.Time;

    public override void OnNetworkSpawn()
    {
        Current = this;
        autoMode = Array.IndexOf(Environment.GetCommandLineArgs(), AutoFlag) >= 0;
        if (IsServer)
        {
            ScoreboardText.Value = Olympics.Text();
            NM.SceneManager.OnLoadEventCompleted += OnAllLoaded;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Current == this) Current = null;
        if (IsServer && NM != null && NM.SceneManager != null)
            NM.SceneManager.OnLoadEventCompleted -= OnAllLoaded;
    }

    void OnAllLoaded(string sceneName, UnityEngine.SceneManagement.LoadSceneMode mode,
                     List<ulong> completed, List<ulong> timedOut)
    {
        if (sceneName != gameObject.scene.name || State.Value != Phase.Waiting) return;
        racers.Clear();
        racers.AddRange(NM.ConnectedClientsIds);
        for (int i = 0; i < racers.Count; i++)
        {
            GetPose(i, racers.Count, out var pos, out float yaw);
            var po = NM.ConnectedClients[racers[i]].PlayerObject;
            if (po != null) po.GetComponent<PlayerController>().TeleportRpc(pos, yaw);
        }
        State.Value = Phase.Countdown;
        PhaseEndsAt.Value = Now + countdownSeconds;
    }

    // domyślnie krąg wokół kontrolera, twarzą do środka (2 graczy = naprzeciwko siebie)
    protected virtual void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        float a = index * Mathf.PI * 2f / count;
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        pos = transform.position + dir * spawnRadius + Vector3.up * 0.1f;
        yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerTick();
        if (NM.IsClient) ClientTick();
    }

    void ServerTick()
    {
        switch (State.Value)
        {
            case Phase.Countdown when Now >= PhaseEndsAt.Value:
                raceStart = Now;
                State.Value = Phase.Running;
                OnRaceStart();
                Debug.Log($"[{GetType().Name}] START, {racers.Count} graczy");
                break;
            case Phase.Running:
                if (Now - raceStart >= timeoutSeconds) Finish(TimeoutRanking());
                else RunningTick();
                break;
            case Phase.Results when Now >= PhaseEndsAt.Value:
                NM.SceneManager.LoadScene("Hub", UnityEngine.SceneManagement.LoadSceneMode.Single);
                State.Value = Phase.Waiting; // nie ładuj huba dwa razy
                break;
        }
    }

    // serwer: koniec gry, medale, po chwili powrót na Hub
    protected void Finish(List<ulong> ranking)
    {
        if (State.Value != Phase.Running) return;
        foreach (var r in ranking)
            if (NM.ConnectedClients.TryGetValue(r, out var c) && c.PlayerObject != null)
            {
                var d = c.PlayerObject.GetComponent<DrunkSystem>();
                // alkohol z konkurencji zostaje na stałe (podłoga paska)
                if (naturalDrunkGain > 0) d.AddPermanent(naturalDrunkGain);
                d.Curse.Value = 0; // klątwa z piwa specjalnego zużyta
            }
        ScoreboardText.Value = Olympics.Award(ranking, out var results);
        ResultsText.Value = results;
        State.Value = Phase.Results;
        PhaseEndsAt.Value = Now + resultsSeconds;
        Debug.Log($"[{GetType().Name}] WYNIKI: " + results.Replace("\n", " | "));
    }

    protected virtual void OnRaceStart() { }
    protected virtual void RunningTick() { }          // serwer, co klatkę w Running
    protected virtual void ClientTick() { }           // klient, co klatkę
    protected virtual void DrawGame() { }             // klient, OnGUI w Running
    protected abstract List<ulong> TimeoutRanking();  // ranking przy timeoucie

    protected float LocalDrunk01()
    {
        var po = NM.LocalClient?.PlayerObject;
        return po != null ? po.GetComponent<DrunkSystem>().Drunk.Value / 100f : 0f;
    }

    protected Camera OwnCamera()
    {
        var po = NM.LocalClient?.PlayerObject;
        return po != null ? po.GetComponent<PlayerController>().playerCamera : null;
    }

    void OnGUI()
    {
        if (!IsSpawned || !NM.IsClient) return;
        if (ScoreboardText.Value.Length > 0)
            GUI.Label(new Rect(0, 8, Screen.width, 24), ScoreboardText.Value.ToString(), Ui.S(16));

        switch (State.Value)
        {
            case Phase.Waiting:
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40),
                    "Czekanie na graczy...", Ui.S(24));
                break;
            case Phase.Countdown:
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 100),
                    Mathf.CeilToInt((float)(PhaseEndsAt.Value - Now)).ToString(), Ui.S(72));
                break;
            case Phase.Running:
                DrawGame();
                break;
            case Phase.Results:
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 300),
                    ResultsText.Value.ToString(), Ui.S(28));
                break;
        }
    }
}
