using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Flanki (GDD 8.2): rzucasz butelką w puszkę — CELUJESZ myszką (bez znacznika,
// na oko po środku ekranu) i ŁADUJESZ SIŁĘ trzymając SPACJĘ (pasek pływa w górę
// i w dół) — puszczasz, gdy siła pasuje do odległości od puszki.
// Trafienie = drużyna rzucająca chleje (SPACJA), aż przeciwnik postawi puszkę
// (naprzemienne A-D-A-D). Wygrywa drużyna, która pierwsza dopije wszystkie kufle.
public class Flanki : TeamCompetition
{
    public enum FPhase : byte { Throwing, Drinking }

    public float mugSize = 100f;
    public float sipBase = 4f;
    public float sipDrunkPenalty = 0.6f;
    public float drunkPerSip = 0.5f;
    public int resetNeeded = 14;      // naprzemiennych A-D do postawienia puszki
    public float canAimRadius = 0.4f; // jak blisko puszki musi przejść promień z kamery
    public float maxThrow = 14f;      // zasięg rzutu przy pełnej sile (m)
    public float powerTolerance = 1.3f; // dopuszczalny błąd siły w metrach
    public float chargeSpeed = 0.85f; // pełne naładowanie w ~1.2 s (ping-pong)

    protected override string AutoFlag => "-autoflanki";

    public NetworkVariable<FPhase> FState = new();
    public NetworkVariable<int> DrinkingTeam = new();
    public NetworkVariable<ulong> ResetterId = new();
    public NetworkVariable<int> ResetCount = new();
    public NetworkVariable<FixedString512Bytes> LiveText = new();

    readonly Dictionary<ulong, float> mug = new(); // serwer
    double drinkStart;                              // serwer (fallback bez resettera)

    // klient
    double lastAutoTurn;
    float nextAutoTap;
    bool autoTapA;
    float myMug;    // wypite 0..1 (MugRpc)
    Transform can;  // "Puszka" ze sceny
    float heldT;    // czas trzymania spacji (ładowanie siły)
    bool chargingThrow;

    Transform Can()
    {
        if (can == null) can = GameObject.Find("Puszka")?.transform;
        return can;
    }

    protected override void OnRaceStart()
    {
        mug.Clear();
        FState.Value = FPhase.Throwing;
        TeamsRpc(Team(0).ToArray());
        NextTurn();
        UpdateLive();
    }

    // trafienie wymaga OBU: timing kółka + promień z kamery przechodzi blisko puszki
    bool AimOk(Vector3 origin, Vector3 dir)
    {
        var c = Can();
        if (c == null) return true; // brak puszki w scenie — nie blokuj gry
        Vector3 to = c.position - origin;
        float along = Vector3.Dot(to, dir.normalized);
        if (along < 0f) return false;
        return Vector3.Distance(origin + dir.normalized * along, c.position) <= canAimRadius;
    }

    [Rpc(SendTo.Server)]
    void ThrowRpc(Vector3 origin, Vector3 dir, float power, RpcParams p = default)
    {
        if (State.Value != Phase.Running || FState.Value != FPhase.Throwing) return;
        ulong id = p.Receive.SenderClientId;
        if (id != TurnPlayer.Value) return;
        // sanity: rzut z okolic głowy gracza
        if (!NM.ConnectedClients.TryGetValue(id, out var cl) || cl.PlayerObject == null
            || Vector3.Distance(origin,
                cl.PlayerObject.transform.position + Vector3.up * 1.7f) > 2.5f) return;

        // trafienie = celowanie kamerą w puszkę + siła dopasowana do odległości
        var cn = Can();
        float dist = cn != null ? Vector3.Distance(origin, cn.position) : power * maxThrow;
        float over = power * maxThrow - dist; // przerzut(+)/niedorzut(-) do FX
        bool hit = Mathf.Abs(over) <= powerTolerance && (autoMode || AimOk(origin, dir));
        ThrowFxRpc(origin, hit, id, Mathf.Clamp(over, -3f, 3f));
        if (hit)
        {
            DrinkingTeam.Value = TeamOf(id);
            var opp = Team(1 - TeamOf(id));
            ResetterId.Value = opp.Count > 0 ? opp[Random.Range(0, opp.Count)] : ulong.MaxValue;
            ResetCount.Value = 0;
            drinkStart = Now;
            FState.Value = FPhase.Drinking;
            Debug.Log($"[Flanki] {Olympics.Nick(id)} TRAFIŁ — pije drużyna {TeamOf(id)}, "
                + $"puszkę stawia {(ResetterId.Value == ulong.MaxValue ? "nikt" : Olympics.Nick(ResetterId.Value))}");
        }
        else
        {
            Debug.Log($"[Flanki] {Olympics.Nick(id)} pudło");
            NextTurn();
        }
    }

    [Rpc(SendTo.Server)]
    void SipRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running || FState.Value != FPhase.Drinking) return;
        ulong id = p.Receive.SenderClientId;
        if (TeamOf(id) != DrinkingTeam.Value || mug.GetValueOrDefault(id) >= mugSize) return;
        var ds = DrunkOf(id);
        if (ds == null) return;

        float sip = sipBase * (1f - sipDrunkPenalty * ds.Handicap01()); // papieros łagodzi karę
        ds.AddCompetitionDrink(drunkPerSip);
        mug[id] = mug.GetValueOrDefault(id) + sip;
        MugRpc(mug[id] / mugSize, RpcTarget.Single(id, RpcTargetUse.Temp));
        UpdateLive();

        if (Team(DrinkingTeam.Value).All(m => mug.GetValueOrDefault(m) >= mugSize))
        {
            Debug.Log($"[Flanki] drużyna {DrinkingTeam.Value} dopiła — koniec");
            Finish(Ranking());
        }
    }

    [Rpc(SendTo.Server)]
    void ResetTapRpc(bool isA, RpcParams p = default)
    {
        if (State.Value != Phase.Running || FState.Value != FPhase.Drinking) return;
        if (p.Receive.SenderClientId != ResetterId.Value) return;
        if ((ResetCount.Value % 2 == 0) != isA) return; // musi być naprzemiennie A-D
        ResetCount.Value++;
        if (ResetCount.Value >= resetNeeded) EndDrinking();
    }

    void EndDrinking()
    {
        Debug.Log("[Flanki] puszka postawiona — koniec picia");
        FState.Value = FPhase.Throwing;
        NextTurn();
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void MugRpc(float v, RpcParams p = default) => myMug = v;

    // wszyscy widzą lot butelki w stronę puszki
    [Rpc(SendTo.ClientsAndHost)]
    void ThrowFxRpc(Vector3 origin, bool hit, ulong thrower, float over) =>
        StartCoroutine(BottleFx(origin, hit, thrower == NM.LocalClientId, over));

    IEnumerator BottleFx(Vector3 origin, bool hit, bool mine, float over)
    {
        var c = Can();
        Vector3 target = c != null ? c.position : Vector3.zero;
        if (!hit) // pudło: butelka leci za krótko/za daleko wzdłuż rzutu + lekki rozrzut
        {
            Vector3 dirH = (target - origin); dirH.y = 0f; dirH.Normalize();
            target += dirH * (Mathf.Abs(over) > 0.3f ? over : 1.2f)
                    + new Vector3(Random.Range(-0.5f, 0.5f), 0f, 0f);
        }
        var bottle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(bottle.GetComponent<Collider>());
        bottle.transform.localScale = new Vector3(0.1f, 0.16f, 0.1f);
        Sfx.Play("throw", origin);
        for (float t = 0f; t < 1f; t += Time.deltaTime / 0.45f)
        {
            bottle.transform.position = Vector3.Lerp(origin, target, t)
                + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 1.2f); // lekki łuk
            bottle.transform.Rotate(400f * Time.deltaTime, 0f, 0f);
            yield return null;
        }
        if (hit) Sfx.Play("clank", target);
        if (mine) Flash(hit); // błysk gdy butelka doleci
        Destroy(bottle);
    }

    // puszka leży w fazie picia, stoi w fazie rzucania — widzą wszyscy
    void SyncCan()
    {
        var c = Can();
        if (c == null) return;
        bool down = FState.Value == FPhase.Drinking;
        c.localRotation = down ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        c.localPosition = new Vector3(0f, down ? 0.13f : 0.25f, 0f);
    }

    protected override void RunningTick()
    {
        switch (FState.Value)
        {
            case FPhase.Throwing when Now - TurnStart.Value > turnSeconds:
                Debug.Log($"[Flanki] {Olympics.Nick(TurnPlayer.Value)} zaspał — pudło");
                NextTurn();
                break;
            case FPhase.Drinking when ResetterId.Value == ulong.MaxValue && Now - drinkStart > 4:
                EndDrinking(); // testy solo: nie ma komu stawiać puszki
                break;
        }
    }

    void UpdateLive() => LiveText.Value = string.Join("   ", racers.Select(r =>
        $"{Olympics.Nick(r)}: {mug.GetValueOrDefault(r) / mugSize:P0}"));

    List<ulong> Ranking()
    {
        int w = DrinkingTeam.Value; // wygrała drużyna, która dopiła
        return Team(w).OrderByDescending(m => mug.GetValueOrDefault(m))
            .Concat(Team(1 - w).OrderByDescending(m => mug.GetValueOrDefault(m))).ToList();
    }

    protected override List<ulong> TimeoutRanking() =>
        racers.OrderByDescending(r => mug.GetValueOrDefault(r)).ToList();

    protected override void ClientTick()
    {
        SyncCan();
        if (State.Value != Phase.Running) return;
        var kb = Keyboard.current;
        bool myTurn = TurnPlayer.Value == NM.LocalClientId;

        if (FState.Value == FPhase.Throwing)
        {
            if (myTurn && kb != null)
            {
                // ładowanie siły: trzymaj SPACJĘ (pasek ping-pong), puść = rzut
                if (kb.spaceKey.isPressed) { chargingThrow = true; heldT += Time.deltaTime; }
                else if (chargingThrow)
                {
                    chargingThrow = false;
                    float power = Mathf.PingPong(heldT * chargeSpeed, 1f);
                    heldT = 0f;
                    var cam = OwnCamera();
                    if (cam != null) ThrowRpc(cam.transform.position, cam.transform.forward, power);
                }
            }
            else { chargingThrow = false; heldT = 0f; }
            if (autoMode && myTurn && TurnStart.Value != lastAutoTurn)
            {
                lastAutoTurn = TurnStart.Value;
                var po = NM.LocalClient?.PlayerObject;
                if (po != null)
                {
                    Vector3 o = po.transform.position + Vector3.up * 1.7f;
                    var cn = Can(); // idealna siła z dystansu — test pipeline'u siły
                    float power = cn != null
                        ? Mathf.Clamp01(Vector3.Distance(o, cn.position) / maxThrow) : 0.5f;
                    ThrowRpc(o, Vector3.forward, power);
                }
            }
            return;
        }
        // Drinking
        bool drinking = myTeam == DrinkingTeam.Value;
        bool resetter = ResetterId.Value == NM.LocalClientId;
        if (kb != null)
        {
            if (drinking && kb.spaceKey.wasPressedThisFrame) { Sfx.Play("gulp"); SipRpc(); }
            if (resetter && kb.aKey.wasPressedThisFrame) ResetTapRpc(true);
            if (resetter && kb.dKey.wasPressedThisFrame) ResetTapRpc(false);
        }
        if (autoMode && Time.time >= nextAutoTap)
        {
            nextAutoTap = Time.time + 0.13f;
            if (drinking) SipRpc();
            else if (resetter) { ResetTapRpc(autoTapA); autoTapA = !autoTapA; }
        }
    }

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, 34, Screen.width, 22), LiveText.Value.ToString(), Ui.S(14));
        var center = new Rect(0, Screen.height * 0.2f, Screen.width, 40);
        if (FState.Value == FPhase.Throwing)
        {
            bool myTurn = TurnPlayer.Value == NM.LocalClientId;
            GUI.Label(center, myTurn
                ? "RZUCASZ! Celuj myszką w puszkę (bez celownika), trzymaj SPACJĘ i puść z DOBRĄ SIŁĄ"
                : $"Rzuca {Olympics.Nick(TurnPlayer.Value)}...", Ui.S(24));
            if (myTurn) DrawPowerBar();
            return;
        }
        if (myTeam == DrinkingTeam.Value)
        {
            GUI.Label(center, "PUSZKA LEŻY — CHLEJ! (SPACJA)", Ui.S(28));
            DrawMug();
        }
        else if (ResetterId.Value == NM.LocalClientId)
            GUI.Label(center, $"PODNIEŚ PUSZKĘ: wciskaj A-D-A-D!  ({ResetCount.Value}/{resetNeeded})", Ui.S(28));
        else
            GUI.Label(center, "Twoja drużyna stawia puszkę...", Ui.S(24));
    }

    // pionowy pasek siły po prawej — pływa w górę i w dół, puść w dobrym momencie
    void DrawPowerBar()
    {
        float h = Screen.height * 0.32f;
        var back = new Rect(Screen.width - 74f, (Screen.height - h) / 2f, 30f, h);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        float power = chargingThrow ? Mathf.PingPong(heldT * chargeSpeed, 1f) : 0f;
        GUI.color = Color.Lerp(new Color(1f, 0.85f, 0.2f), new Color(0.95f, 0.25f, 0.1f), power);
        GUI.DrawTexture(new Rect(back.x, back.yMax - h * power, back.width, h * power),
            Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(back.x - 30f, back.yMax + 6f, 100f, 22f), "SIŁA", Ui.S(14));
    }

    // pionowy kufel po lewej: ile piwa jeszcze zostało
    void DrawMug()
    {
        float h = Screen.height * 0.32f;
        var back = new Rect(28f, (Screen.height - h) / 2f, 26f, h);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(back, Texture2D.whiteTexture);
        float left = Mathf.Clamp01(1f - myMug); // myMug = wypite
        GUI.color = new Color(0.95f, 0.72f, 0.15f, 0.95f);
        GUI.DrawTexture(new Rect(back.x, back.yMax - h * left, back.width, h * left),
            Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(back.x - 30f, back.yMax + 6f, 90f, 22f),
            $"KUFEL {left:P0}", Ui.S(14));
    }
}
