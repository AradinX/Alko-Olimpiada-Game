using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Rzutki: każdy stoi przed swoją tarczą, 3 rzuty. Celujesz myszką, TRZYMASZ
// SPACJĘ — kółko na ekranie kurczy się i rośnie — puszczasz: im mniejsze kółko
// w tym momencie, tym mniejszy rozrzut lotki. Celownik pływa tym mocniej,
// im bardziej jesteś pijany. Serwer liczy trafienie raycastem.
public class Rzutki : Competition
{
    public int dartsPerPlayer = 3;
    public float boardRadius = 0.6f;
    public float aimWander = 0.12f;  // maks. offset celownika (viewport) przy pełnym upojeniu
    public float pulseSpeed = 1.5f;  // cykle kurczenia kółka na sekundę (ping-pong)
    public float maxSpread = 0.09f;  // rozrzut (viewport) przy największym kółku

    protected override string AutoFlag => "-autorzutki";

    public NetworkVariable<FixedString512Bytes> LiveText = new();

    // serwer
    readonly Dictionary<ulong, int> score = new(), thrown = new();
    // klient
    int myThrown;
    float nextAutoThrow;
    float holdT;      // czas trzymania spacji
    bool holdActive;
    static Texture2D ringTex; // okrąg celności (generowany raz)

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (NM.IsClient) BuildBoardRings();
    }

    // pkt 4: kolorowe pierścienie na tarczach — od razu widać gdzie celować (środek = 10 pkt).
    // Czysto wizualne, każdy klient buduje u siebie; brak collidera → nie psuje raycastu punktacji.
    void BuildBoardRings()
    {
        // od zewnątrz do środka; czerwony środek = bullseye
        var rings = new (float r, Color c)[]
        {
            (0.60f, new Color(0.12f, 0.12f, 0.15f)),
            (0.45f, new Color(0.20f, 0.45f, 0.90f)),
            (0.30f, new Color(0.95f, 0.85f, 0.20f)),
            (0.16f, new Color(0.95f, 0.50f, 0.15f)),
            (0.06f, new Color(0.90f, 0.15f, 0.15f)),
        };
        for (int i = 0; i < 8; i++)
        {
            var board = GameObject.Find("Board_" + i);
            if (board == null) continue;
            Vector3 bp = board.transform.position; // (x, 1.6, 6); przód tarczy od strony gracza (mniejsze z)
            for (int k = 0; k < rings.Length; k++)
            {
                var disk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(disk.GetComponent<Collider>());
                disk.transform.SetParent(transform, false); // kontroler Rzutki w origin → local == world
                disk.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // płaska ścianka ku graczowi
                disk.transform.localScale = new Vector3(rings[k].r * 2f, 0.02f, rings[k].r * 2f);
                // mniejszy pierścień = bliżej gracza, żeby był na wierzchu; odstęp 3 cm ≠ z-fighting
                disk.transform.localPosition = new Vector3(bp.x, bp.y, bp.z - 0.1f - k * 0.03f);
                // jawny materiał URP/Lit — runtime .material.color na prymitywie bywa zawodny (stąd 1 kolor)
                disk.GetComponent<Renderer>().sharedMaterial =
                    new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = rings[k].c };
            }
        }
    }

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
        // sanity: promień musi wychodzić z okolic głowy gracza (replikowana pozycja)
        if (!NM.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null
            || Vector3.Distance(origin,
                cl.PlayerObject.transform.position + Vector3.up * 1.7f) > 2.5f) return;
        thrown[id] = thrown.GetValueOrDefault(id) + 1;

        int pts = 0;
        if (Physics.Raycast(origin, dir.normalized, out var hit, 40f)
            && hit.transform.name == "Board_" + idx) // tylko własna tarcza
        {
            float d = Vector3.Distance(hit.point, hit.transform.position);
            pts = Mathf.Clamp(10 - (int)(d / (boardRadius / 10f)), 0, 10);
        }
        score[id] = score.GetValueOrDefault(id) + pts;
        FlashRpc(pts > 0, RpcTarget.Single(id, RpcTargetUse.Temp));
        Debug.Log($"[Rzutki] {Olympics.Nick(id)} rzut {thrown[id]}: {pts} pkt (suma {score[id]})");
        UpdateLive();
        if (racers.All(r => thrown.GetValueOrDefault(r) >= dartsPerPlayer)) Finish(Ranking());
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void FlashRpc(bool hit, RpcParams p = default) => Flash(hit);

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

    // 0 = kółko maksymalnie małe (celny rzut), 1 = największe
    float Circle01() => Mathf.PingPong(holdT * pulseSpeed * 2f, 1f);

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || myThrown >= dartsPerPlayer) return;
        var cam = OwnCamera();
        if (cam == null) return;
        var kb = Keyboard.current;

        bool released = false;
        if (kb != null && kb.spaceKey.isPressed) { holdActive = true; holdT += Time.deltaTime; }
        else if (holdActive) { released = true; holdActive = false; }

        bool auto = autoMode && Time.time >= nextAutoThrow;
        if (!released && !auto) return;
        if (auto) nextAutoThrow = Time.time + 1f;

        myThrown++;
        float c = auto ? 0f : Circle01();
        holdT = 0f;
        var o = AimOffset() + Random.insideUnitCircle * (maxSpread * c); // rozrzut z kółka
        var ray = cam.ViewportPointToRay(new Vector3(0.5f + o.x, 0.5f + o.y, 0f));
        ThrowRpc(ray.origin, ray.direction);
    }

    // biały okrąg (obwódka) — generowany raz, rysowany w skali kółka celności
    static Texture2D Ring()
    {
        if (ringTex != null) return ringTex;
        const int S = 128; const float mid = (S - 1) / 2f;
        ringTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        var px = new Color32[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float d = Mathf.Sqrt((x - mid) * (x - mid) + (y - mid) * (y - mid)) / mid;
                px[y * S + x] = d > 0.86f && d < 0.98f
                    ? new Color32(255, 255, 255, 230) : new Color32(0, 0, 0, 0);
            }
        ringTex.SetPixels32(px);
        ringTex.Apply();
        return ringTex;
    }

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, 34, Screen.width, 22), LiveText.Value.ToString(), Ui.S(16));
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 30),
            myThrown < dartsPerPlayer
                ? $"Rzut {myThrown + 1}/{dartsPerPlayer} — celuj myszką, TRZYMAJ SPACJĘ i puść przy MAŁYM kółku"
                : "Czekasz na resztę...", Ui.S(22));
        if (myThrown >= dartsPerPlayer) return;

        // pływający celownik
        var o = AimOffset();
        float px = (0.5f + o.x) * Screen.width;
        float py = (1f - (0.5f + o.y)) * Screen.height;
        GUI.Label(new Rect(px - 15f, py - 15f, 30f, 30f), "+", Ui.S(30));

        // kółko celności wokół celownika — kurczy się i rośnie póki trzymasz spację
        if (holdActive)
        {
            float r = Mathf.Lerp(20f, Screen.height * 0.14f, Circle01());
            GUI.DrawTexture(new Rect(px - r, py - r, r * 2f, r * 2f), Ring());
        }
    }
}
