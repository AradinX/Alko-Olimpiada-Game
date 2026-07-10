using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Beer Pong (GDD 8.3): celujesz myszką (kamerą), trzymana SPACJA ładuje siłę rzutu.
// Piłka leci parabolą i MUSI odbić się od blatu, zanim wpadnie do kubka przeciwników.
// Trafiony kubek znika, przeciwnicy piją (na stałe). Drużyny rzucają na zmianę.
// Wygrywa drużyna, która pierwsza zdejmie wszystkie kubki przeciwnika.
public class BeerPong : TeamCompetition
{
    public int cupsPerTeam = 15;    // piramida 5-4-3-2-1 od strony gracza
    public float drinkPerCup = 2.5f; // 15 kubków — mniejsza dawka na kubek niż przy 6
    public float minSpeed = 4.5f;   // rzut przy sile 0
    public float maxSpeed = 11f;    // rzut przy pełnej sile
    public float chargeTime = 1.1f; // sekundy trzymania SPACJI do pełnej siły

    protected override string AutoFlag => "-autopong";

    // bitmaski żywych kubków (bit i = Cup_t_i stoi na stole)
    public NetworkVariable<int> CupsA = new();
    public NetworkVariable<int> CupsB = new();

    readonly Dictionary<ulong, int> hits = new(); // serwer
    Transform[,] cups;   // Cup_t_i ze sceny — dekoracje sterowane bitmaską
    float[,] holdUntil;  // trafiony kubek znika dopiero, gdy piłka do niego wpadnie
    double lastAutoTurn; // klient
    float chargeStart = -1f;

    int Mask(int t) => t == 0 ? CupsA.Value : CupsB.Value;
    void SetMask(int t, int v) { if (t == 0) CupsA.Value = v; else CupsB.Value = v; }
    static int CupCount(int mask) { int n = 0; while (mask != 0) { n += mask & 1; mask >>= 1; } return n; }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        FindCups();
        if (IsServer) CupsA.Value = CupsB.Value = (1 << cupsPerTeam) - 1;
    }

    void FindCups()
    {
        cups = new Transform[2, cupsPerTeam];
        holdUntil = new float[2, cupsPerTeam];
        for (int t = 0; t < 2; t++)
            for (int i = 0; i < cupsPerTeam; i++)
                cups[t, i] = GameObject.Find($"Cup_{t}_{i}")?.transform;
    }

    protected override void OnRaceStart()
    {
        hits.Clear();
        CupsA.Value = CupsB.Value = (1 << cupsPerTeam) - 1;
        TeamsRpc(Team(0).ToArray());
        NextTurn();
    }

    // ponytail: parabola krokowa zamiast Rigidbody — ten sam deterministyczny kod
    // liczy trafienie na serwerze i rysuje lot piłki u klientów
    int Simulate(Vector3 pos, Vector3 vel, int oppTeam, int mask, List<Vector3> path)
    {
        const float dt = 0.02f, tableTop = 1f, rimY = 1.23f, catchR = 0.11f; // kubek r=0.10
        bool bounced = false;
        for (int step = 0; step < 200; step++)
        {
            vel.y -= 9.81f * dt;
            Vector3 next = pos + vel * dt;
            if (vel.y < 0f)
            {
                // odbicie od blatu (stół 2x8 m na środku, blat na y=1)
                if (!bounced && pos.y > tableTop && next.y <= tableTop
                    && Mathf.Abs(next.x) < 1f && Mathf.Abs(next.z) < 4f)
                {
                    bounced = true;
                    vel.y = -vel.y * 0.6f;
                    vel.x *= 0.85f; vel.z *= 0.85f;
                    next = pos + vel * dt;
                }
                // wpadnięcie przez wieniec kubka — liczy się TYLKO po odbiciu;
                // ranty się stykają, więc bierzemy NAJBLIŻSZY żywy kubek
                else if (bounced && pos.y > rimY && next.y <= rimY)
                {
                    int best = -1; float bestD = catchR;
                    for (int i = 0; i < cupsPerTeam; i++)
                        if ((mask & (1 << i)) != 0 && cups[oppTeam, i] != null)
                        {
                            Vector3 c = cups[oppTeam, i].position;
                            float d = new Vector2(next.x - c.x, next.z - c.z).magnitude;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                    if (best >= 0)
                    {
                        // animacja wpadania: od wieńca po łuku na dno kubka
                        if (path != null)
                        {
                            Vector3 c = cups[oppTeam, best].position;
                            Vector3 bottom = new(c.x, c.y - 0.06f, c.z);
                            for (int k = 1; k <= 10; k++)
                            {
                                float u = k / 10f;
                                path.Add(Vector3.Lerp(next, bottom, u * u)); // przyspiesza w dół
                            }
                        }
                        return best;
                    }
                }
            }
            pos = next;
            path?.Add(pos);
            if (pos.y < 0f) break;
        }
        return -1;
    }

    int FirstLive(int t) { for (int i = 0; i < cupsPerTeam; i++) if ((Mask(t) & (1 << i)) != 0) return i; return -1; }

    [Rpc(SendTo.Server)]
    void ThrowRpc(Vector3 origin, Vector3 dir, float power, RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (id != TurnPlayer.Value) return;
        // sanity: rzut musi wychodzić z okolic głowy gracza (replikowana pozycja)
        if (!NM.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null
            || Vector3.Distance(origin,
                cl.PlayerObject.transform.position + Vector3.up * 1.7f) > 2.5f) return;

        int opp = 1 - TeamOf(id);
        Vector3 v0 = dir.normalized * Mathf.Lerp(minSpeed, maxSpeed, Mathf.Clamp01(power));
        // smoke test nie celuje — losuje trafienie, żeby gra się skończyła
        int cup = autoMode ? (Random.value < 0.6f ? FirstLive(opp) : -1)
                           : Simulate(origin, v0, opp, Mask(opp), null);
        ThrowFxRpc(origin, v0, opp, cup, id);

        if (cup >= 0)
        {
            SetMask(opp, Mask(opp) & ~(1 << cup));
            hits[id] = hits.GetValueOrDefault(id) + 1;
            foreach (var m in Team(opp)) DrunkOf(m)?.AddCompetitionDrink(drinkPerCup);
            Debug.Log($"[Pong] {Olympics.Nick(id)} trafia Cup_{opp}_{cup}! Zostało: {CupCount(Mask(opp))}");
            if (Mask(opp) == 0) { Finish(Ranking(opp)); return; }
        }
        else Debug.Log($"[Pong] {Olympics.Nick(id)} pudło");
        NextTurn();
    }

    // wszyscy widzą lot piłki; maska = tylko trafiony kubek, więc tor wyjdzie ten sam
    [Rpc(SendTo.ClientsAndHost)]
    void ThrowFxRpc(Vector3 origin, Vector3 v0, int oppTeam, int cup, ulong thrower)
    {
        var path = new List<Vector3>();
        Simulate(origin, v0, oppTeam, cup >= 0 ? 1 << cup : 0, path);
        // kubek zniknie dopiero po wpadnięciu piłki (kroki toru ~50/s + zanik)
        if (cup >= 0) holdUntil[oppTeam, cup] = Time.time + path.Count / 50f + 0.3f;
        StartCoroutine(BallFx(path, thrower == NM.LocalClientId, cup >= 0));
    }

    IEnumerator BallFx(List<Vector3> path, bool mine, bool hit)
    {
        var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(ball.GetComponent<Collider>());
        ball.transform.localScale = Vector3.one * 0.09f;

        // cień piłki na blacie/podłodze — jedyny czytelny wskaźnik głębi (za stół czy przed?)
        var shadow = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(shadow.GetComponent<Collider>());
        shadow.transform.localScale = new Vector3(0.13f, 0.004f, 0.13f);
        shadow.GetComponent<Renderer>().sharedMaterial =
            new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.05f, 0.05f, 0.05f) };

        if (path.Count > 0) Sfx.Play("throw", path[0]);
        float prevY = float.MaxValue;
        bool falling = false, bounceHeard = false;
        foreach (var p in path)
        {
            // pierwszy dołek toru = odbicie od blatu
            if (!bounceHeard && falling && p.y > prevY)
            { bounceHeard = true; Sfx.Play("bounce", p); }
            falling = p.y < prevY;
            prevY = p.y;
            ball.transform.position = p;
            // cień pada na blat (stół 2x8 m, y=1) gdy piłka nad nim, inaczej na podłogę
            bool overTable = Mathf.Abs(p.x) < 1f && Mathf.Abs(p.z) < 4f;
            shadow.transform.position = new Vector3(p.x, overTable ? 1.001f : 0.02f, p.z);
            yield return null;
        }
        Destroy(shadow);
        if (hit) Sfx.Play("plop", ball.transform.position);
        if (mine) Flash(hit); // błysk w momencie lądowania
        // piłka znika w kubku (krótki zanik zamiast twardego Destroy)
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            ball.transform.localScale = Vector3.one * 0.09f * (1f - t / 0.25f);
            yield return null;
        }
        Destroy(ball);
    }

    // kubki w scenie odzwierciedlają bitmaskę (widzą wszyscy)
    void SyncCups()
    {
        for (int t = 0; t < 2; t++)
            for (int i = 0; i < cupsPerTeam; i++)
                if (cups[t, i] != null)
                {
                    bool on = (Mask(t) & (1 << i)) != 0 || Time.time < holdUntil[t, i];
                    if (cups[t, i].gameObject.activeSelf != on) cups[t, i].gameObject.SetActive(on);
                }
    }

    protected override void RunningTick()
    {
        if (Now - TurnStart.Value > turnSeconds)
        {
            Debug.Log($"[Pong] {Olympics.Nick(TurnPlayer.Value)} zaspał — pudło");
            NextTurn();
        }
    }

    List<ulong> Ranking(int losers) =>
        Team(1 - losers).OrderByDescending(m => hits.GetValueOrDefault(m))
            .Concat(Team(losers).OrderByDescending(m => hits.GetValueOrDefault(m))).ToList();

    protected override List<ulong> TimeoutRanking() =>
        Ranking(CupCount(Mask(0)) < CupCount(Mask(1)) ? 0 : 1);

    protected override void ClientTick()
    {
        SyncCups();
        if (State.Value != Phase.Running) return;
        bool myTurn = TurnPlayer.Value == NM.LocalClientId;
        var kb = Keyboard.current;

        if (!myTurn) chargeStart = -1f;
        else if (kb != null)
        {
            if (kb.spaceKey.wasPressedThisFrame) chargeStart = Time.time;
            if (kb.spaceKey.wasReleasedThisFrame && chargeStart >= 0f)
            {
                float power = Mathf.Clamp01((Time.time - chargeStart) / chargeTime);
                chargeStart = -1f;
                var cam = OwnCamera();
                if (cam != null)
                    ThrowRpc(cam.transform.position, cam.transform.forward, power);
            }
        }
        if (autoMode && myTurn && TurnStart.Value != lastAutoTurn)
        {
            lastAutoTurn = TurnStart.Value;
            var po = NM.LocalClient?.PlayerObject;
            if (po != null)
                ThrowRpc(po.transform.position + Vector3.up * 1.7f, Vector3.forward, 0.5f);
        }
    }

    protected override void DrawGame()
    {
        int my = myTeam < 0 ? 0 : myTeam;
        GUI.Label(new Rect(0, 34, Screen.width, 24),
            $"KUBKI — MY: {CupCount(Mask(my))}   ONI: {CupCount(Mask(1 - my))}", Ui.S(18));
        bool myTurn = TurnPlayer.Value == NM.LocalClientId;
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 40), myTurn
            ? "RZUCASZ! Celuj myszką, trzymaj SPACJĘ i puść — piłka musi odbić się od stołu"
            : $"Rzuca {Olympics.Nick(TurnPlayer.Value)}...", Ui.S(24));

        if (!myTurn) return;
        // celownik na środku (kamera = kierunek rzutu)
        GUI.Label(new Rect(Screen.width / 2f - 15f, Screen.height / 2f - 15f, 30f, 30f), "+", Ui.S(30));
        // pasek siły podczas ładowania
        if (chargeStart >= 0f)
        {
            float f = Mathf.Clamp01((Time.time - chargeStart) / chargeTime);
            var back = new Rect(Screen.width / 2f - 100f, Screen.height * 0.68f, 200f, 18f);
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(Color.green, Color.red, f);
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * f, back.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
    }
}
