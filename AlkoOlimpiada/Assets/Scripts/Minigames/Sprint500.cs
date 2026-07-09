using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Konkurencja: Sprint na 500 — kto najszybciej wychleje kufel klikając SPACJĘ.
// Pełna pętla w scenie Hub (stan replikowany, bez ładowania scen):
// [R] gotowość przy stanowisku -> odliczanie -> chlanie -> medale 5/3/1 -> powrót.
public class Sprint500 : NetworkBehaviour
{
    public enum Phase : byte { Idle, Countdown, Running, Results }

    public float stationRadius = 5f;
    public float mugSize = 100f;
    public float sipBase = 4f;            // łyk za kliknięcie na trzeźwo
    public float sipDrunkPenalty = 0.6f;  // przy 100 upojenia łyk maleje do 40%
    public float drunkPerSip = 0.6f;      // chlanie w wyścigu też upija (GDD: naturalny przyrost)
    public float vomitSeconds = 4f;
    public float raceTimeout = 60f;
    public float finishGrace = 20f;       // po pierwszym finiszu reszta ma tyle czasu

    public static bool InputLocked;       // PlayerController nie rusza się w trakcie

    public NetworkVariable<Phase> State = new();
    public NetworkVariable<int> ReadyCount = new();
    public NetworkVariable<int> TotalCount = new();
    public NetworkVariable<double> PhaseEndsAt = new();
    public NetworkVariable<FixedString512Bytes> Scoreboard = new();
    public NetworkVariable<FixedString512Bytes> ResultsText = new();

    // stan serwera
    readonly HashSet<ulong> ready = new();
    readonly List<ulong> racers = new();
    readonly Dictionary<ulong, float> progress = new();
    readonly List<ulong> finished = new();
    readonly Dictionary<ulong, int> scores = new(); // punkty przez całą olimpiadę
    readonly Dictionary<ulong, double> vomitUntil = new();
    double raceStart, firstFinish;

    // stan lokalny klienta
    float shownProgress;
    float vomitEndLocal;
    bool autoMode, autoReadySent;
    double autoReadyAt;
    float nextAutoSip;

    double Now => NetworkManager.ServerTime.Time;

    public override void OnNetworkSpawn()
    {
        // ponytail: headless smoke test pętli konkurencji (flaga -autosprint)
        autoMode = Array.IndexOf(Environment.GetCommandLineArgs(), "-autosprint") >= 0;
        autoReadyAt = Time.timeAsDouble + 10; // czas, żeby druga instancja zdążyła dołączyć
        State.OnValueChanged += (_, s) => ApplyPhase(s);
        ApplyPhase(State.Value); // late joiner nie dostaje OnValueChanged
    }

    public override void OnNetworkDespawn() => InputLocked = false;

    void ApplyPhase(Phase s)
    {
        InputLocked = s == Phase.Countdown || s == Phase.Running;
        if (s == Phase.Running) shownProgress = 0;
    }

    // ---------- serwer ----------

    [Rpc(SendTo.Server)]
    void ReadyRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Idle) return;
        ulong id = p.Receive.SenderClientId;
        if (!ready.Remove(id)) ready.Add(id); // toggle
        ready.RemoveWhere(x => !NetworkManager.ConnectedClientsIds.Contains(x));
        ReadyCount.Value = ready.Count;
        // ponytail: TotalCount liczony przy zgłoszeniu — gracz, który dołączy po tym,
        // jak wszyscy byli gotowi, nie zablokuje startu; wystarczy na prototyp
        TotalCount.Value = NetworkManager.ConnectedClientsIds.Count;
        if (ready.Count > 0 && ready.Count == TotalCount.Value)
        {
            State.Value = Phase.Countdown;
            PhaseEndsAt.Value = Now + 3;
        }
    }

    [Rpc(SendTo.Server)]
    void SipRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || finished.Contains(id)) return;
        if (vomitUntil.TryGetValue(id, out var vu) && Now < vu) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var c)
            || c.PlayerObject == null) return;

        var drunkSys = c.PlayerObject.GetComponent<DrunkSystem>();
        float drunk = drunkSys.Drunk.Value;

        // pasek pełny w konkurencji = wymioty zamiast Zgonu (GDD sekcja 6)
        if (drunk + drunkPerSip >= 100f)
        {
            drunkSys.Drunk.Value = 60f;
            vomitUntil[id] = Now + vomitSeconds;
            VomitRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            Debug.Log($"[Sprint] {Nick(id)} rzyga");
            return;
        }
        drunkSys.Drunk.Value = drunk + drunkPerSip;

        float sip = sipBase * (1f - sipDrunkPenalty * drunk / 100f);
        progress[id] = progress.GetValueOrDefault(id) + sip;
        ProgressRpc(progress[id] / mugSize, RpcTarget.Single(id, RpcTargetUse.Temp));

        if (progress[id] >= mugSize)
        {
            finished.Add(id);
            if (finished.Count == 1) firstFinish = Now;
            Debug.Log($"[Sprint] finisz {Nick(id)} t={Now - raceStart:0.0}s (#{finished.Count})");
            if (finished.Count == racers.Count) EndRace();
        }
    }

    void ServerTick()
    {
        switch (State.Value)
        {
            case Phase.Countdown when Now >= PhaseEndsAt.Value:
                racers.Clear();
                racers.AddRange(NetworkManager.ConnectedClientsIds);
                progress.Clear();
                finished.Clear();
                vomitUntil.Clear();
                raceStart = Now;
                firstFinish = 0;
                State.Value = Phase.Running;
                Debug.Log($"[Sprint] START, {racers.Count} graczy");
                break;
            case Phase.Running when Now - raceStart >= raceTimeout
                                    || (finished.Count > 0 && Now - firstFinish >= finishGrace):
                EndRace();
                break;
            case Phase.Results when Now >= PhaseEndsAt.Value:
                ready.Clear();
                ReadyCount.Value = 0;
                ResultsText.Value = "";
                State.Value = Phase.Idle;
                break;
        }
    }

    void EndRace()
    {
        // ranking: finiszerzy wg kolejności, reszta (DNF) wg postępu
        var ranking = finished.Concat(
            racers.Where(r => !finished.Contains(r))
                  .OrderByDescending(r => progress.GetValueOrDefault(r))).ToList();
        int[] medals = { 5, 3, 1 };
        var lines = new List<string>();
        for (int i = 0; i < ranking.Count; i++)
        {
            int pts = i < 3 ? medals[i] : 0;
            scores[ranking[i]] = scores.GetValueOrDefault(ranking[i]) + pts;
            lines.Add($"#{i + 1}  {Nick(ranking[i])}  +{pts} pkt");
        }
        ResultsText.Value = string.Join("\n", lines);
        Scoreboard.Value = "PUNKTY:  " + string.Join("   ",
            scores.OrderByDescending(kv => kv.Value)
                  .Select(kv => $"{Nick(kv.Key)}: {kv.Value}"));
        State.Value = Phase.Results;
        PhaseEndsAt.Value = Now + 8;
        Debug.Log("[Sprint] WYNIKI: " + string.Join(" | ", lines));
    }

    string Nick(ulong id) =>
        NetworkManager.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<PlayerNameTag>().Nickname.Value.ToString()
            : $"Gracz{id}";

    // ---------- klient ----------

    [Rpc(SendTo.SpecifiedInParams)]
    void ProgressRpc(float t, RpcParams p = default) => shownProgress = t;

    [Rpc(SendTo.SpecifiedInParams)]
    void VomitRpc(RpcParams p = default) => vomitEndLocal = Time.time + vomitSeconds;

    void Update()
    {
        if (!IsSpawned) return;
        if (IsServer) ServerTick();
        if (!NetworkManager.IsClient) return;

        var kb = Keyboard.current;
        switch (State.Value)
        {
            case Phase.Idle:
                if (NearStation() && kb != null && kb.rKey.wasPressedThisFrame) ReadyRpc();
                if (autoMode && !autoReadySent && Time.timeAsDouble >= autoReadyAt)
                { autoReadySent = true; ReadyRpc(); }
                break;
            case Phase.Running:
                if (Time.time < vomitEndLocal) return;
                if (kb != null && kb.spaceKey.wasPressedThisFrame) SipRpc();
                if (autoMode && Time.time >= nextAutoSip)
                { nextAutoSip = Time.time + 0.12f; SipRpc(); }
                break;
        }
    }

    bool NearStation()
    {
        var po = NetworkManager.LocalClient?.PlayerObject;
        return po != null &&
            Vector3.Distance(po.transform.position, transform.position) <= stationRadius;
    }

    void OnGUI()
    {
        if (!IsSpawned || !NetworkManager.IsClient) return;
        var small = new GUIStyle(GUI.skin.label)
        { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        var big = new GUIStyle(GUI.skin.label)
        { fontSize = 28, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        var huge = new GUIStyle(big) { fontSize = 72 };

        if (Scoreboard.Value.Length > 0)
            GUI.Label(new Rect(0, 8, Screen.width, 24), Scoreboard.Value.ToString(), small);

        var center = new Rect(0, Screen.height * 0.3f, Screen.width, 60);
        switch (State.Value)
        {
            case Phase.Idle:
                if (ReadyCount.Value > 0)
                    GUI.Label(new Rect(0, 34, Screen.width, 24),
                        $"Gotowi: {ReadyCount.Value}/{TotalCount.Value}", small);
                if (NearStation())
                    GUI.Label(center, "[R] SPRINT NA 500 — zgłoś gotowość", big);
                break;
            case Phase.Countdown:
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 100),
                    Mathf.CeilToInt((float)(PhaseEndsAt.Value - Now)).ToString(), huge);
                break;
            case Phase.Running:
                GUI.Label(center, Time.time < vomitEndLocal ? "RZYGASZ..." : "SPACJA! CHLEJ!", big);
                var bar = new Rect(Screen.width * 0.25f, Screen.height - 60f, Screen.width * 0.5f, 24f);
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
                GUI.color = new Color(1f, 0.8f, 0.2f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(shownProgress), bar.height),
                    Texture2D.whiteTexture);
                GUI.color = Color.white;
                break;
            case Phase.Results:
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 300),
                    ResultsText.Value.ToString(), big);
                break;
        }
    }
}
