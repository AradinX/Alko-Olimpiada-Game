using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sprint na 500: kto najszybciej wychleje kufel klikając SPACJĘ.
// Gracze stoją w kręgu wokół beczki; im mniej piwa w kuflu, tym bardziej
// zadzierasz głowę (kamera), a przeciwnicy widzą, jak się odchylasz.
public class Sprint500 : Competition
{
    public float mugSize = 100f;
    public float sipBase = 4f;            // łyk za kliknięcie na trzeźwo
    public float sipDrunkPenalty = 0.6f;  // przy 100 upojenia łyk maleje do 40%
    public float drunkPerSip = 0.6f;      // chlanie upija
    public float vomitSeconds = 4f;
    public float finishGrace = 20f;       // po pierwszym finiszu reszta ma tyle czasu

    protected override string AutoFlag => "-autosprint";

    // serwer
    readonly Dictionary<ulong, float> progress = new();
    readonly List<ulong> finished = new();
    readonly Dictionary<ulong, double> vomitUntil = new();
    double firstFinish, nextBroadcast;

    // klient
    readonly Dictionary<ulong, float> shown = new(); // postęp wszystkich (broadcast 4 Hz)
    readonly Dictionary<ulong, float> disp = new();  // wygładzony do animacji
    float vomitEndLocal, nextAutoSip;

    protected override void OnRaceStart()
    {
        progress.Clear();
        finished.Clear();
        vomitUntil.Clear();
        firstFinish = 0;
    }

    [Rpc(SendTo.Server)]
    void SipRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || finished.Contains(id)) return;
        if (vomitUntil.TryGetValue(id, out var vu) && Now < vu) return;
        if (!NM.ConnectedClients.TryGetValue(id, out var c) || c.PlayerObject == null) return;

        var ds = c.PlayerObject.GetComponent<DrunkSystem>();
        float drunk = ds.Drunk.Value;

        // pasek pełny w konkurencji = wymioty zamiast Zgonu (GDD sekcja 6)
        if (drunk + drunkPerSip >= 100f)
        {
            ds.Drunk.Value = 60f;
            vomitUntil[id] = Now + vomitSeconds;
            VomitRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            Debug.Log($"[Sprint] {Olympics.Nick(id)} rzyga");
            return;
        }
        ds.Drunk.Value = drunk + drunkPerSip;

        float sip = sipBase * (1f - sipDrunkPenalty * drunk / 100f);
        progress[id] = progress.GetValueOrDefault(id) + sip;
        if (progress[id] >= mugSize)
        {
            finished.Add(id);
            if (finished.Count == 1) firstFinish = Now;
            Debug.Log($"[Sprint] finisz {Olympics.Nick(id)} t={Now - raceStart:0.0}s (#{finished.Count})");
        }
    }

    protected override void RunningTick()
    {
        if (racers.Count > 0 && finished.Count == racers.Count) { Finish(Ranking()); return; }
        if (finished.Count > 0 && Now - firstFinish >= finishGrace) { Finish(Ranking()); return; }
        if (Now >= nextBroadcast)
        {
            nextBroadcast = Now + 0.25;
            var ids = racers.ToArray();
            AllProgressRpc(ids, ids.Select(i => progress.GetValueOrDefault(i) / mugSize).ToArray());
        }
    }

    List<ulong> Ranking() => finished.Concat(
        racers.Where(r => !finished.Contains(r))
              .OrderByDescending(r => progress.GetValueOrDefault(r))).ToList();

    protected override List<ulong> TimeoutRanking() => Ranking();

    [Rpc(SendTo.ClientsAndHost)]
    void AllProgressRpc(ulong[] ids, float[] vals)
    { for (int i = 0; i < ids.Length; i++) shown[ids[i]] = vals[i]; }

    [Rpc(SendTo.SpecifiedInParams)]
    void VomitRpc(RpcParams p = default) => vomitEndLocal = Time.time + vomitSeconds;

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || Time.time < vomitEndLocal) return;
        var kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame) SipRpc();
        if (autoMode && Time.time >= nextAutoSip)
        { nextAutoSip = Time.time + 0.12f; SipRpc(); }
    }

    // przechył głowy przy piciu (własna kamera) + odchylanie ciał przeciwników
    void LateUpdate()
    {
        if (!IsSpawned || !NM.IsClient) return;
        bool running = State.Value == Phase.Running;
        foreach (var pc in FindObjectsByType<PlayerController>(FindObjectsSortMode.None))
        {
            float target = running ? shown.GetValueOrDefault(pc.OwnerClientId) : 0f;
            float t = disp[pc.OwnerClientId] = Mathf.Lerp(
                disp.GetValueOrDefault(pc.OwnerClientId), target, 8f * Time.deltaTime);
            if (pc.IsOwner)
            {
                if (running)
                    pc.playerCamera.transform.localRotation *= Quaternion.Euler(-70f * t, 0f, 0f);
            }
            else
            {
                var body = pc.transform.Find("Body");
                if (body != null) body.localRotation = Quaternion.Euler(-35f * t, 0f, 0f);
            }
        }
    }

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 40),
            Time.time < vomitEndLocal ? "RZYGASZ..." : "SPACJA! CHLEJ!", Ui.S(28));

        // paski wszystkich graczy — własny na dole, największy
        var tags = FindObjectsByType<PlayerNameTag>(FindObjectsSortMode.None)
            .OrderByDescending(t => t.OwnerClientId == NM.LocalClientId).ToArray();
        for (int i = 0; i < tags.Length; i++)
        {
            bool own = tags[i].OwnerClientId == NM.LocalClientId;
            float t = disp.GetValueOrDefault(tags[i].OwnerClientId);
            float h = own ? 24f : 14f;
            var bar = new Rect(Screen.width * 0.25f, Screen.height - 50f - i * 34f, Screen.width * 0.5f, h);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = own ? new Color(1f, 0.8f, 0.2f) : new Color(0.6f, 0.6f, 0.9f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(t), h), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(bar.x - 160f, bar.y - 4f, 150f, 24f),
                tags[i].Nickname.Value.ToString(),
                new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight });
        }
    }
}
