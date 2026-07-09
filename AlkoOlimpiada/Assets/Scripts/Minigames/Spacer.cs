using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Spacer do monopolowego: tor przeszkód — wąskie belki nad ziemią, spadniesz = wracasz
// na start. Jedyna konkurencja z wolnym ruchem: bujanie i popsute sterowanie robią robotę.
public class Spacer : Competition
{
    public float finishZ = 25.5f;
    public float fallY = 1.2f;
    static readonly Vector3 startPos = new(0f, 3.4f, -3f);

    protected override string AutoFlag => "-autospacer";
    protected override bool LockMovement => false;

    // serwer
    readonly List<ulong> finished = new();
    readonly Dictionary<ulong, float> bestZ = new();

    // klient
    bool sentFinish;

    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        pos = new Vector3(index * 1.2f - (count - 1) * 0.6f, 3.4f, -3f); // linia na starcie
        yaw = 0f;
    }

    protected override void OnRaceStart() { finished.Clear(); bestZ.Clear(); }

    [Rpc(SendTo.Server)]
    void FinishRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || finished.Contains(id)) return;
        if (!NM.ConnectedClients.TryGetValue(id, out var c) || c.PlayerObject == null) return;
        // walidacja po replikowanej pozycji
        if (c.PlayerObject.transform.position.z < finishZ - 2f) return;

        finished.Add(id);
        Debug.Log($"[Spacer] meta {Olympics.Nick(id)} t={Now - raceStart:0.0}s (#{finished.Count})");
        if (finished.Count == racers.Count) Finish(Ranking());
    }

    protected override void RunningTick()
    {
        foreach (var r in racers)
            if (NM.ConnectedClients.TryGetValue(r, out var c) && c.PlayerObject != null)
                bestZ[r] = Mathf.Max(bestZ.GetValueOrDefault(r, -99f),
                    c.PlayerObject.transform.position.z);
    }

    List<ulong> Ranking() => finished.Concat(
        racers.Where(r => !finished.Contains(r))
              .OrderByDescending(r => bestZ.GetValueOrDefault(r, -99f))).ToList();

    protected override List<ulong> TimeoutRanking() => Ranking();

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running) return;
        var po = NM.LocalClient?.PlayerObject;
        if (po == null) return;
        var pc = po.GetComponent<PlayerController>();

        if (po.transform.position.y < fallY) // spadłeś z belki
            pc.TeleportLocal(startPos, 0f);

        if (!sentFinish && po.transform.position.z > finishZ)
        { sentFinish = true; FinishRpc(); }

        // ponytail: auto sunie prosto nad torem (headless nie ma grawitacji z inputu)
        if (autoMode) po.transform.position += Vector3.forward * 3f * Time.deltaTime;
    }

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 30),
            sentFinish ? "W MONOPOLOWYM! Czekasz na resztę..."
                       : "DO MONOPOLOWEGO! Nie spadnij z belki (spadniesz = od nowa)", Ui.S(22));
    }
}
