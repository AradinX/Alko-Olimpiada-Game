using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Sprint na 500: każdy ma przed sobą stolik z kuflem. [E] bierzesz kufel,
// SPACJA pijesz (NIE widzisz ile zostało — czujesz tylko po przechyle głowy),
// [E] odkładasz. Czas zatrzymuje się, gdy odłożysz PUSTY kufel — jak odłożysz
// z piwem w środku, musisz brać jeszcze raz. Blef i wyczucie.
public class Sprint500 : Competition
{
    public float mugSize = 100f;
    public float sipBase = 4f;            // łyk za kliknięcie na trzeźwo
    public float sipDrunkPenalty = 0.6f;  // przy 100 upojenia łyk maleje do 40%
    public float drunkPerSip = 0.6f;      // chlanie upija
    public float vomitSeconds = 4f;
    public float finishGrace = 20f;       // po pierwszym finiszu reszta ma tyle czasu

    protected override string AutoFlag => "-autosprint";

    public NetworkVariable<FixedString512Bytes> LiveText = new();

    // serwer
    readonly Dictionary<ulong, float> progress = new();
    readonly HashSet<ulong> holding = new();
    readonly List<ulong> finished = new();
    readonly Dictionary<ulong, double> vomitUntil = new();
    double firstFinish, nextBroadcast;

    // klient
    readonly Dictionary<ulong, float> shown = new(); // postęp wszystkich (do przechyłów)
    readonly Dictionary<ulong, float> disp = new();  // wygładzony do animacji
    float vomitEndLocal, nextAutoSip;
    double nextAutoCycle;
    bool myHolding, myFinished;

    // stałe 8 miejsc w kręgu (niezależnie od liczby graczy) — stoliki budowane
    // klientowo muszą trafić w te same pozycje
    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        float a = index * Mathf.PI * 2f / 8f;
        var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
        pos = transform.position + dir * spawnRadius + Vector3.up * 0.1f;
        yaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (NM.IsClient) BuildTables();
    }

    // stolik + kufel przed każdym miejscem — czysta scenografia (bez colliderów)
    void BuildTables()
    {
        for (int i = 0; i < 8; i++)
        {
            GetPose(i, 8, out var pos, out _);
            Vector3 toCenter = (transform.position - pos); toCenter.y = 0f; toCenter.Normalize();
            Vector3 at = pos + toCenter * 0.75f;

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(table.GetComponent<Collider>());
            table.transform.SetParent(transform, false);
            table.transform.position = new Vector3(at.x, 0.45f, at.z);
            table.transform.localScale = new Vector3(0.7f, 0.9f, 0.5f);
            table.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.45f, 0.3f, 0.15f) };

            var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(cup.GetComponent<Collider>());
            cup.transform.SetParent(transform, false);
            cup.transform.position = new Vector3(at.x, 0.99f, at.z);
            cup.transform.localScale = new Vector3(0.14f, 0.09f, 0.14f);
            cup.GetComponent<Renderer>().sharedMaterial =
                new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { color = new Color(0.95f, 0.72f, 0.15f) };
        }
    }

    protected override void OnRaceStart()
    {
        progress.Clear();
        holding.Clear();
        finished.Clear();
        vomitUntil.Clear();
        firstFinish = 0;
        UpdateLive();
    }

    [Rpc(SendTo.Server)]
    void HoldRpc(bool take, RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || finished.Contains(id)) return;

        if (take) { holding.Add(id); return; }
        holding.Remove(id);
        // odłożony PUSTY kufel = koniec biegu; z piwem = strata czasu
        if (progress.GetValueOrDefault(id) >= mugSize)
        {
            finished.Add(id);
            if (finished.Count == 1) firstFinish = Now;
            FinishedRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            UpdateLive();
            Debug.Log($"[Sprint] finisz {Olympics.Nick(id)} t={Now - raceStart:0.0}s (#{finished.Count})");
        }
    }

    [Rpc(SendTo.Server)]
    void SipRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || finished.Contains(id) || !holding.Contains(id)) return;
        if (progress.GetValueOrDefault(id) >= mugSize) return; // pijesz powietrze
        if (vomitUntil.TryGetValue(id, out var vu) && Now < vu) return;
        if (!NM.ConnectedClients.TryGetValue(id, out var c) || c.PlayerObject == null) return;

        var ds = c.PlayerObject.GetComponent<DrunkSystem>();

        // pasek pełny w konkurencji = wymioty zamiast Zgonu (GDD sekcja 6)
        if (ds.Drunk.Value + drunkPerSip >= 100f)
        {
            ds.Drunk.Value = Mathf.Max(ds.Floor.Value, 60f);
            vomitUntil[id] = Now + vomitSeconds;
            VomitRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
            Debug.Log($"[Sprint] {Olympics.Nick(id)} rzyga");
            return;
        }
        ds.AddPermanent(drunkPerSip); // chlanie w konkurencji zostaje na stałe

        float sip = sipBase * (1f - sipDrunkPenalty * ds.Handicap01()); // papieros łagodzi karę
        progress[id] = progress.GetValueOrDefault(id) + sip;
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

    void UpdateLive() => LiveText.Value = finished.Count == 0 ? "" :
        "Odłożyli pusty kufel: " + string.Join(", ", finished.Select(Olympics.Nick));

    List<ulong> Ranking() => finished.Concat(
        racers.Where(r => !finished.Contains(r))
              .OrderByDescending(r => progress.GetValueOrDefault(r))).ToList();

    protected override List<ulong> TimeoutRanking() => Ranking();

    // postęp leci do wszystkich TYLKO do przechyłów ciał/kamery — UI go nie pokazuje
    [Rpc(SendTo.ClientsAndHost)]
    void AllProgressRpc(ulong[] ids, float[] vals)
    { for (int i = 0; i < ids.Length; i++) shown[ids[i]] = vals[i]; }

    [Rpc(SendTo.SpecifiedInParams)]
    void VomitRpc(RpcParams p = default) => vomitEndLocal = Time.time + vomitSeconds;

    [Rpc(SendTo.SpecifiedInParams)]
    void FinishedRpc(RpcParams p = default) => myFinished = true;

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || myFinished || Time.time < vomitEndLocal) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.eKey.wasPressedThisFrame)
            {
                myHolding = !myHolding;
                Sfx.Play(myHolding ? "beep" : "clank");
                HoldRpc(myHolding);
            }
            if (myHolding && kb.spaceKey.wasPressedThisFrame) { Sfx.Play("gulp"); SipRpc(); }
        }
        if (autoMode)
        {
            if (!myHolding) { myHolding = true; HoldRpc(true); }
            if (Time.time >= nextAutoSip) { nextAutoSip = Time.time + 0.12f; SipRpc(); }
            if (Time.timeAsDouble >= nextAutoCycle) // cykl odłóż+weź — finisz łapie się na odłożeniu
            {
                nextAutoCycle = Time.timeAsDouble + 3.0;
                myHolding = false; HoldRpc(false);
            }
        }
    }

    // przechył głowy przy piciu (własna kamera) + odchylanie ciał przeciwników —
    // jedyny sposób, by ocenić ile kto wypił
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
                if (running && myHolding)
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
        GUI.Label(new Rect(0, 34, Screen.width, 22), LiveText.Value.ToString(), Ui.S(14));
        string msg = myFinished ? "PUSTY KUFEL ODŁOŻONY — czas STOP! Czekasz na resztę..."
            : Time.time < vomitEndLocal ? "RZYGASZ..."
            : myHolding ? "CHLEJ! SPACJA łyk — [E] odłóż kufel (pusty = koniec, z piwem = strata czasu)"
            : "[E] WEŹ KUFEL ze stolika!";
        GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 40), msg, Ui.S(26));
        GUI.Label(new Rect(0, Screen.height * 0.25f + 40f, Screen.width, 24),
            "Nie widzisz ile zostało — wyczuj po przechyle głowy", Ui.S(14));
    }
}
