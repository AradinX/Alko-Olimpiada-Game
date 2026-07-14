using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Spacer do monopolowego: tor przeszkód w klimacie Fall Guys — wąskie belki nad
// ziemią, spadające puszki i wirujące młoty spychają cię z trasy. Spadniesz =
// wracasz na start. Jedyna konkurencja z wolnym ruchem: bujanie i popsute
// sterowanie robią robotę.
// Przeszkody budują się klientowo i chodzą deterministycznie po ServerTime
// (u wszystkich wyglądają tak samo); spychają TYLKO lokalnego gracza.
public class Spacer : Competition
{
    public float finishZ = 25.5f;
    public float fallY = 1.2f;
    public float canPeriod = 3.2f;   // cykl spadania puszki (s)
    public float hammerDegPerSec = 110f;
    static readonly Vector3 startPos = new(0f, 3.4f, -3f);

    protected override string AutoFlag => "-autospacer";
    protected override bool LockMovement => false;

    // serwer
    readonly List<ulong> finished = new();
    readonly Dictionary<ulong, float> bestZ = new();

    // klient
    bool sentFinish;
    Transform[] cans; float[] canZ = { 2.5f, 7f, 12.5f, 17f };
    Transform[] hammers; float[] hammerZ = { 5f, 14.5f, 21f };

    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        pos = new Vector3(index * 1.2f - (count - 1) * 0.6f, 3.4f, -3f); // linia na starcie
        yaw = 0f;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (NM.IsClient) BuildObstacles();
    }

    void BuildObstacles()
    {
        var canMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.85f, 0.15f, 0.1f) };
        cans = new Transform[canZ.Length];
        for (int i = 0; i < canZ.Length; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(g.GetComponent<Collider>()); // spychanie liczymy sami
            g.transform.SetParent(transform, false);
            g.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f);
            g.GetComponent<Renderer>().sharedMaterial = canMat;
            cans[i] = g.transform;
        }
        var hamMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
        { color = new Color(0.7f, 0.7f, 0.75f) };
        hammers = new Transform[hammerZ.Length];
        for (int i = 0; i < hammerZ.Length; i++)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false);
            g.transform.position = new Vector3(0f, 3.6f, hammerZ[i]);
            g.transform.localScale = new Vector3(4f, 0.3f, 0.3f); // ramię zamiata belkę
            g.GetComponent<Renderer>().sharedMaterial = hamMat;
            hammers[i] = g.transform;
        }
    }

    // wysokość puszki w cyklu: szybki zrzut, chwila na belce, powrót do góry
    float CanY(double t, int i)
    {
        float p = (float)((t / canPeriod + i * 0.37) % 1.0);
        if (p < 0.35f) return Mathf.Lerp(8f, 3.4f, p / 0.35f);   // spada
        if (p < 0.55f) return 3.4f;                              // leży na trasie
        return Mathf.Lerp(3.4f, 8f, (p - 0.55f) / 0.45f);        // wraca
    }

    void AnimateObstacles()
    {
        double t = Now;
        for (int i = 0; i < cans.Length; i++)
            cans[i].position = new Vector3(0f, CanY(t, i), canZ[i]);
        for (int i = 0; i < hammers.Length; i++)
            hammers[i].rotation = Quaternion.Euler(0f, (float)(t * hammerDegPerSec) + i * 120f, 0f);
    }

    // spychanie lokalnego gracza — CharacterController.Move, bez fizyki i sieci
    void PushLocal()
    {
        var po = NM.LocalClient?.PlayerObject;
        if (po == null) return;
        var cc = po.GetComponent<CharacterController>();
        Vector3 pos = po.transform.position;

        for (int i = 0; i < cans.Length; i++)
        {
            Vector3 cp = cans[i].position;
            if (cp.y > pos.y + 2.2f) continue; // jeszcze wysoko
            Vector3 d = pos - cp; d.y = 0f;
            if (d.magnitude < 0.8f && Mathf.Abs(pos.y + 1f - cp.y) < 1.8f)
                cc.Move((d.sqrMagnitude > 0.001f ? d.normalized : Vector3.right)
                        * 5f * Time.deltaTime);
        }
        foreach (var h in hammers)
        {
            Vector3 rel = pos + Vector3.up * 1f - h.position;
            if (Mathf.Abs(rel.y) > 1.4f) continue;
            rel.y = 0f;
            Vector3 arm = h.right; arm.y = 0f; arm.Normalize();
            float along = Vector3.Dot(rel, arm);
            if (Mathf.Abs(along) > 2.1f) continue;             // poza ramieniem
            Vector3 perp = rel - arm * along;
            if (perp.magnitude > 0.55f) continue;              // nie dotyka
            // spycha w kierunku obrotu ramienia (styczna) + lekko od osi
            Vector3 tangent = Vector3.Cross(Vector3.up, arm) * Mathf.Sign(along);
            cc.Move((tangent * 6f + perp.normalized * 2f) * Time.deltaTime);
        }
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
        AnimateObstacles();
        if (State.Value != Phase.Running) return;
        var po = NM.LocalClient?.PlayerObject;
        if (po == null) return;
        var pc = po.GetComponent<PlayerController>();

        if (!autoMode) PushLocal(); // auto sunie po prostej — przeszkody by go zapętliły

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
                       : "DO MONOPOLOWEGO! Omijaj puszki i młoty, nie spadnij z belki!", Ui.S(22));
    }
}
