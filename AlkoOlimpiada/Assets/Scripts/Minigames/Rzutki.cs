using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Rzutki: każdy stoi przed swoją tarczą, 3 rzuty (LPM). Celownik pływa tym
// mocniej, im bardziej jesteś pijany. Serwer liczy trafienie raycastem.
public class Rzutki : Competition
{
    public int dartsPerPlayer = 3;
    public float boardRadius = 0.6f;
    public float aimWander = 0.12f; // maks. offset celownika (viewport) przy pełnym upojeniu

    protected override string AutoFlag => "-autorzutki";

    public NetworkVariable<FixedString512Bytes> LiveText = new();

    // serwer
    readonly Dictionary<ulong, int> score = new(), thrown = new();
    // klient
    int myThrown;
    float nextAutoThrow;

    // linia graczy, tarcze 6 m przed nimi (Board_i w scenie)
    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        pos = new Vector3(index * 4f - 14f, 0.1f, 0f);
        yaw = 0f;
    }

    protected override void OnRaceStart()
    {
        score.Clear();
        thrown.Clear();
        UpdateLive();
    }

    [Rpc(SendTo.Server)]
    void ThrowRpc(Vector3 origin, Vector3 dir, RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        int idx = racers.IndexOf(id);
        if (idx < 0 || thrown.GetValueOrDefault(id) >= dartsPerPlayer) return;
        thrown[id] = thrown.GetValueOrDefault(id) + 1;

        int pts = 0;
        if (Physics.Raycast(origin, dir.normalized, out var hit, 40f)
            && hit.transform.name == "Board_" + idx) // tylko własna tarcza
        {
            float d = Vector3.Distance(hit.point, hit.transform.position);
            pts = Mathf.Clamp(10 - (int)(d / (boardRadius / 10f)), 0, 10);
        }
        score[id] = score.GetValueOrDefault(id) + pts;
        Debug.Log($"[Rzutki] {Olympics.Nick(id)} rzut {thrown[id]}: {pts} pkt (suma {score[id]})");
        UpdateLive();
        if (racers.All(r => thrown.GetValueOrDefault(r) >= dartsPerPlayer)) Finish(Ranking());
    }

    void UpdateLive() => LiveText.Value = string.Join("   ", racers.Select(r =>
        $"{Olympics.Nick(r)}: {score.GetValueOrDefault(r)} ({thrown.GetValueOrDefault(r)}/{dartsPerPlayer})"));

    List<ulong> Ranking() => racers.OrderByDescending(r => score.GetValueOrDefault(r)).ToList();
    protected override List<ulong> TimeoutRanking() => Ranking();

    Vector2 AimOffset()
    {
        float d = LocalDrunk01();
        float t = Time.time;
        return new Vector2(Mathf.PerlinNoise(t * 0.9f, 3f) - 0.5f,
                           Mathf.PerlinNoise(5f, t * 0.8f) - 0.5f) * (2f * aimWander * d);
    }

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || myThrown >= dartsPerPlayer) return;
        var cam = OwnCamera();
        if (cam == null) return;
        bool click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool auto = autoMode && Time.time >= nextAutoThrow;
        if (!click && !auto) return;
        if (auto) nextAutoThrow = Time.time + 1f;
        myThrown++;
        var o = AimOffset();
        var ray = cam.ViewportPointToRay(new Vector3(0.5f + o.x, 0.5f + o.y, 0f));
        ThrowRpc(ray.origin, ray.direction);
    }

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, 34, Screen.width, 22), LiveText.Value.ToString(), Ui.S(16));
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 30),
            myThrown < dartsPerPlayer ? $"Rzut {myThrown + 1}/{dartsPerPlayer} — celuj i klik LPM"
                                      : "Czekasz na resztę...", Ui.S(22));
        // pływający celownik
        var o = AimOffset();
        float px = (0.5f + o.x) * Screen.width;
        float py = (1f - (0.5f + o.y)) * Screen.height;
        GUI.Label(new Rect(px - 15f, py - 15f, 30f, 30f), "+", Ui.S(30));
    }
}
