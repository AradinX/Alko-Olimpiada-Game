using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Beer Pong 1v1 (drabinka jak Lucky Shot): pojedynek na jednym stole, reszta patrzy.
// Celujesz kamerą, trzymana SPACJA ładuje siłę, piłka MUSI odbić się od blatu.
// Trafiony kubek znika, rywal pije. Kto pierwszy zdejmie 10 kubków rywala (albo więcej,
// gdy skończą się piłki) wygrywa mecz. Zwycięzcy awansują; osobno mecz o 3. miejsce i finał.
public class BeerPong : Competition
{
    public int cupsPerSide = 10;    // piramida 4-3-2-1
    public int ballsPerPlayer = 20; // limit rzutów na gracza w meczu
    public float drinkPerCup = 3.5f;
    public float minSpeed = 4.5f;   // rzut przy sile 0
    public float maxSpeed = 11f;    // rzut przy pełnej sile
    public float chargeTime = 1.1f; // sekundy trzymania SPACJI do pełnej siły
    public float turnSeconds = 12f; // limit na rzut (zaspał = stracona piłka)

    protected override string AutoFlag => "-autopong";

    // stan aktualnego meczu (replikowany)
    public NetworkVariable<int> CupsA = new();   // maska żywych kubków strony 0 (Cup_0_i)
    public NetworkVariable<int> CupsB = new();   // maska żywych kubków strony 1 (Cup_1_i)
    public NetworkVariable<ulong> PlayerA = new();
    public NetworkVariable<ulong> PlayerB = new();
    public NetworkVariable<int> BallsA = new();
    public NetworkVariable<int> BallsB = new();
    public NetworkVariable<ulong> TurnPlayer = new();
    public NetworkVariable<double> TurnStart = new();
    public NetworkVariable<FixedString64Bytes> MatchLabel = new();

    // scena
    Transform[,] cups;
    float[,] holdUntil;
    double lastAutoTurn;
    float chargeStart = -1f;

    // ---- drabinka (serwer) ----
    readonly List<ulong> waiting = new();   // do rozegrania w tej rundzie
    readonly List<ulong> winners = new();   // awansujący do następnej rundy
    readonly List<ulong> roundLosers = new(); // przegrani bieżącej rundy (do wykrycia półfinału)
    readonly Dictionary<ulong, int> outRound = new(); // runda odpadnięcia (ranking)
    int roundNo, roundStartCount, matchKind;  // kind: 0 zwykły, 1 o 3. miejsce, 2 finał
    ulong finalistA, finalistB, champion, runnerUp, third, fourth, loneThird;
    bool thirdDone, loneThirdSet, hasRunnerUp; // clientId 0 == default, więc flagi zamiast != default

    int Mask(int t) => t == 0 ? CupsA.Value : CupsB.Value;
    void SetMask(int t, int v) { if (t == 0) CupsA.Value = v; else CupsB.Value = v; }
    static int CupCount(int mask) { int n = 0; while (mask != 0) { n += mask & 1; mask >>= 1; } return n; }
    int SideOf(ulong id) => id == PlayerA.Value ? 0 : 1;
    int Balls(ulong id) => id == PlayerA.Value ? BallsA.Value : BallsB.Value;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        timeoutSeconds = 900f; // drabinka bywa długa — nie ubijaj jej globalnym limitem
        BuildCups();
    }

    // Buduję 10 kubków (4-3-2-1) w runtime — pozycje deterministyczne, więc serwer (trafienia)
    // i klienci (wizual) się zgadzają. Niezależne od tego ile kubków ma zapisana scena.
    void BuildCups()
    {
        cups = new Transform[2, cupsPerSide];
        holdUntil = new float[2, cupsPerSide];
        var red = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.85f, 0.2f, 0.15f) };
        for (int t = 0; t < 2; t++)
        {
            for (int i = 0; ; i++) { var old = GameObject.Find($"Cup_{t}_{i}"); if (old == null) break; Destroy(old); }
            int idx = 0;
            for (int row = 0; row < 4; row++)
                for (int k = 0; k < 4 - row; k++, idx++)
                {
                    var cup = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(cup.GetComponent<Collider>());
                    cup.name = $"Cup_{t}_{idx}";
                    float x = (k - (4 - row - 1) / 2f) * 0.2f;
                    float z = 3.5f - row * 0.174f;
                    cup.transform.position = new Vector3(x, 1.12f, (t == 0 ? -1f : 1f) * z);
                    cup.transform.localScale = new Vector3(0.2f, 0.11f, 0.2f);
                    cup.GetComponent<Renderer>().sharedMaterial = red;
                    cups[t, idx] = cup.transform;
                }
        }
    }

    // na odliczaniu wszyscy w linii za stroną 0 (twarzą do stołu); BeginMatch ściąga 2 do gry
    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        pos = new Vector3(index * 1.5f - (count - 1) * 0.75f, 0.1f, -9f);
        yaw = 0f;
    }

    // ---------- drabinka ----------

    protected override void OnRaceStart()
    {
        waiting.Clear(); winners.Clear(); roundLosers.Clear(); outRound.Clear();
        thirdDone = loneThirdSet = hasRunnerUp = false;
        waiting.AddRange(racers);
        roundNo = 1; roundStartCount = waiting.Count;

        if (waiting.Count <= 1) { champion = waiting.FirstOrDefault(); FinishBracket(); return; }
        if (waiting.Count == 2)
        {
            finalistA = waiting[0]; finalistB = waiting[1]; waiting.Clear();
            BeginMatch(finalistA, finalistB, "FINAŁ", 2);
            return;
        }
        StartNextMatch();
    }

    ulong Pop(List<ulong> l) { var x = l[0]; l.RemoveAt(0); return x; }

    void StartNextMatch()
    {
        if (waiting.Count >= 2)
        {
            ulong a = Pop(waiting), b = Pop(waiting);
            string label = roundStartCount <= 4 ? "PÓŁFINAŁ" : $"RUNDA {roundNo}";
            BeginMatch(a, b, label, 0);
            return;
        }
        if (waiting.Count == 1) winners.Add(Pop(waiting)); // wolny los

        // runda się skończyła
        if (winners.Count <= 1) { champion = winners.FirstOrDefault(); FinishBracket(); return; }

        if (winners.Count == 2) // następny mecz to finał
        {
            finalistA = winners[0]; finalistB = winners[1];
            if (!thirdDone && roundLosers.Count == 2) // dwaj przegrani półfinału → mecz o 3.
            {
                ulong sa = roundLosers[0], sb = roundLosers[1];
                winners.Clear(); roundLosers.Clear();
                BeginMatch(sa, sb, "MECZ O 3. MIEJSCE", 1);
                return;
            }
            if (roundLosers.Count == 1) { loneThird = roundLosers[0]; loneThirdSet = true; }
            winners.Clear(); roundLosers.Clear();
            BeginMatch(finalistA, finalistB, "FINAŁ", 2);
            return;
        }

        // zwykłe przejście do następnej rundy
        roundLosers.Clear();
        waiting.AddRange(winners); winners.Clear();
        roundNo++; roundStartCount = waiting.Count;
        StartNextMatch();
    }

    void BeginMatch(ulong a, ulong b, string label, int kind)
    {
        matchKind = kind;
        PlayerA.Value = a; PlayerB.Value = b;
        MatchLabel.Value = label;
        CupsA.Value = CupsB.Value = (1 << cupsPerSide) - 1;
        BallsA.Value = BallsB.Value = ballsPerPlayer;
        for (int t = 0; t < 2; t++) for (int i = 0; i < cupsPerSide; i++) holdUntil[t, i] = 0f;

        Teleport(a, new Vector3(0f, 0.1f, -6f), 0f);
        Teleport(b, new Vector3(0f, 0.1f, 6f), 180f);
        int slot = 0;
        foreach (var r in racers)
            if (r != a && r != b) Teleport(r, new Vector3(slot++ * 1.5f - 3f, 0.1f, -9f), 0f);

        SetTurn(a);
        Debug.Log($"[Pong] {label}: {Olympics.Nick(a)} vs {Olympics.Nick(b)}");
    }

    void Teleport(ulong id, Vector3 pos, float yaw)
    {
        if (NM.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null)
            c.PlayerObject.GetComponent<PlayerController>().TeleportRpc(pos, yaw);
    }

    void SetTurn(ulong id) { TurnPlayer.Value = id; TurnStart.Value = Now; }

    void ConsumeBall(ulong id) { if (id == PlayerA.Value) BallsA.Value--; else BallsB.Value--; }

    // po rzucie: tura rywala, jeśli ma piłki; inaczej dorzuca ten sam; inaczej koniec z kubków
    void AfterThrow()
    {
        ulong cur = TurnPlayer.Value;
        ulong other = cur == PlayerA.Value ? PlayerB.Value : PlayerA.Value;
        if (Balls(other) > 0) SetTurn(other);
        else if (Balls(cur) > 0) SetTurn(cur);
        else EndMatchByCups();
    }

    void EndMatchByCups()
    {
        int bLeft = CupCount(CupsB.Value), aLeft = CupCount(CupsA.Value);
        // A zbija kubki B; mniej zostało B = A wygrał. Remis → gospodarz A.
        if (bLeft <= aLeft) EndMatch(PlayerA.Value, PlayerB.Value);
        else EndMatch(PlayerB.Value, PlayerA.Value);
    }

    void EndMatch(ulong winner, ulong loser)
    {
        Debug.Log($"[Pong] mecz: wygrywa {Olympics.Nick(winner)} (przegrywa {Olympics.Nick(loser)})");
        if (matchKind == 2) { champion = winner; runnerUp = loser; hasRunnerUp = true; FinishBracket(); return; }
        if (matchKind == 1)
        {
            third = winner; fourth = loser; thirdDone = true;
            BeginMatch(finalistA, finalistB, "FINAŁ", 2);
            return;
        }
        // zwykły mecz
        winners.Add(winner);
        roundLosers.Add(loser);
        outRound[loser] = roundNo;
        StartNextMatch();
    }

    void FinishBracket()
    {
        var rank = new List<ulong> { champion };
        if (hasRunnerUp) rank.Add(runnerUp);
        if (thirdDone) { rank.Add(third); rank.Add(fourth); }
        else if (loneThirdSet) rank.Add(loneThird);
        rank.AddRange(racers.Where(r => !rank.Contains(r))
            .OrderByDescending(r => outRound.GetValueOrDefault(r, 0)));
        Finish(rank);
    }

    protected override List<ulong> TimeoutRanking() =>
        racers.OrderByDescending(r => outRound.GetValueOrDefault(r, 99)).ToList();

    // ---------- rzut i symulacja ----------

    // parabola krokowa (jak wcześniej): ten sam deterministyczny kod liczy trafienie
    // na serwerze i rysuje lot u klientów
    int Simulate(Vector3 pos, Vector3 vel, int oppSide, int mask, List<Vector3> path)
    {
        const float dt = 0.02f, tableTop = 1f, rimY = 1.23f, catchR = 0.11f;
        bool bounced = false;
        for (int step = 0; step < 200; step++)
        {
            vel.y -= 9.81f * dt;
            Vector3 next = pos + vel * dt;
            if (vel.y < 0f)
            {
                if (!bounced && pos.y > tableTop && next.y <= tableTop
                    && Mathf.Abs(next.x) < 1f && Mathf.Abs(next.z) < 4f)
                {
                    bounced = true;
                    vel.y = -vel.y * 0.6f;
                    vel.x *= 0.85f; vel.z *= 0.85f;
                    next = pos + vel * dt;
                }
                else if (bounced && pos.y > rimY && next.y <= rimY)
                {
                    int best = -1; float bestD = catchR;
                    for (int i = 0; i < cupsPerSide; i++)
                        if ((mask & (1 << i)) != 0 && cups[oppSide, i] != null)
                        {
                            Vector3 c = cups[oppSide, i].position;
                            float d = new Vector2(next.x - c.x, next.z - c.z).magnitude;
                            if (d < bestD) { bestD = d; best = i; }
                        }
                    if (best >= 0)
                    {
                        if (path != null)
                        {
                            Vector3 c = cups[oppSide, best].position;
                            Vector3 bottom = new(c.x, c.y - 0.06f, c.z);
                            for (int k = 1; k <= 10; k++)
                                path.Add(Vector3.Lerp(next, bottom, (k / 10f) * (k / 10f)));
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

    int FirstLive(int t) { for (int i = 0; i < cupsPerSide; i++) if ((Mask(t) & (1 << i)) != 0) return i; return -1; }

    [Rpc(SendTo.Server)]
    void ThrowRpc(Vector3 origin, Vector3 dir, float power, RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (id != TurnPlayer.Value || Balls(id) <= 0) return;
        if (!NM.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null
            || Vector3.Distance(origin,
                cl.PlayerObject.transform.position + Vector3.up * 1.7f) > 2.5f) return;

        int side = SideOf(id), opp = 1 - side;
        ConsumeBall(id);
        Vector3 v0 = dir.normalized * Mathf.Lerp(minSpeed, maxSpeed, Mathf.Clamp01(power));
        int cup = autoMode ? (Random.value < 0.55f ? FirstLive(opp) : -1)
                           : Simulate(origin, v0, opp, Mask(opp), null);
        ThrowFxRpc(origin, v0, opp, cup, id);

        if (cup >= 0)
        {
            SetMask(opp, Mask(opp) & ~(1 << cup));
            DrunkOf(id == PlayerA.Value ? PlayerB.Value : PlayerA.Value)?.AddCompetitionDrink(drinkPerCup);
            Debug.Log($"[Pong] {Olympics.Nick(id)} trafia! Rywalowi zostało {CupCount(Mask(opp))}");
            if (Mask(opp) == 0) { EndMatch(id, id == PlayerA.Value ? PlayerB.Value : PlayerA.Value); return; }
        }
        AfterThrow();
    }

    DrunkSystem DrunkOf(ulong id) =>
        NM.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<DrunkSystem>() : null;

    [Rpc(SendTo.ClientsAndHost)]
    void ThrowFxRpc(Vector3 origin, Vector3 v0, int oppSide, int cup, ulong thrower)
    {
        var path = new List<Vector3>();
        Simulate(origin, v0, oppSide, cup >= 0 ? 1 << cup : 0, path);
        if (cup >= 0) holdUntil[oppSide, cup] = Time.time + path.Count / 50f + 0.3f;
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
            if (!bounceHeard && falling && p.y > prevY) { bounceHeard = true; Sfx.Play("bounce", p); }
            falling = p.y < prevY;
            prevY = p.y;
            ball.transform.position = p;
            bool overTable = Mathf.Abs(p.x) < 1f && Mathf.Abs(p.z) < 4f;
            shadow.transform.position = new Vector3(p.x, overTable ? 1.001f : 0.02f, p.z);
            yield return null;
        }
        Destroy(shadow);
        if (hit) Sfx.Play("plop", ball.transform.position);
        if (mine) Flash(hit);
        for (float t = 0f; t < 0.25f; t += Time.deltaTime)
        {
            ball.transform.localScale = Vector3.one * 0.09f * (1f - t / 0.25f);
            yield return null;
        }
        Destroy(ball);
    }

    void SyncCups()
    {
        if (cups == null) return;
        for (int t = 0; t < 2; t++)
            for (int i = 0; i < cupsPerSide; i++)
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
            Debug.Log($"[Pong] {Olympics.Nick(TurnPlayer.Value)} zaspał — stracona piłka");
            ConsumeBall(TurnPlayer.Value);
            AfterThrow();
        }
    }

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
                if (cam != null) ThrowRpc(cam.transform.position, cam.transform.forward, power);
            }
        }
        if (autoMode && myTurn && TurnStart.Value != lastAutoTurn)
        {
            lastAutoTurn = TurnStart.Value;
            var po = NM.LocalClient?.PlayerObject;
            if (po != null) ThrowRpc(po.transform.position + Vector3.up * 1.7f, po.transform.forward, 0.6f);
        }
    }

    int MySide()
    {
        ulong me = NM.LocalClientId;
        return me == PlayerA.Value ? 0 : me == PlayerB.Value ? 1 : -1;
    }

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, 34, Screen.width, 26),
            $"{MatchLabel.Value}:  {Olympics.Nick(PlayerA.Value)}  vs  {Olympics.Nick(PlayerB.Value)}", Ui.S(20));

        int side = MySide();
        if (side < 0) // widz
        {
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 30),
                $"Oglądasz mecz. Kubki: {CupCount(CupsA.Value)} — {CupCount(CupsB.Value)}", Ui.S(22));
            return;
        }

        int myCups = CupCount(Mask(side)), oppCups = CupCount(Mask(1 - side));
        int myBalls = side == 0 ? BallsA.Value : BallsB.Value;
        GUI.Label(new Rect(0, 60, Screen.width, 24),
            $"KUBKI RYWALA (zdejmij je): {oppCups}    Twoje: {myCups}    Piłki: {myBalls}", Ui.S(18));

        bool myTurn = TurnPlayer.Value == NM.LocalClientId;
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 40), myTurn
            ? "RZUCASZ! Celuj myszką, trzymaj SPACJĘ i puść — piłka musi odbić się od stołu"
            : $"Rzuca {Olympics.Nick(TurnPlayer.Value)}...", Ui.S(24));

        if (!myTurn) return;
        GUI.Label(new Rect(Screen.width / 2f - 15f, Screen.height / 2f - 15f, 30f, 30f), "+", Ui.S(30));
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
