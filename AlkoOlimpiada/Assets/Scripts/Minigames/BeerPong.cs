using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Beer Pong (GDD 8.3): drużyny naprzemiennie rzucają (kółko timingowe + SPACJA).
// Trafienie zdejmuje kubek przeciwników, a oni go wypijają (upojenie na stałe).
// Wygrywa drużyna, która pierwsza zdejmie wszystkie kubki przeciwnika.
public class BeerPong : TeamCompetition
{
    public int cupsPerTeam = 6;
    public float drinkPerCup = 6f;

    protected override string AutoFlag => "-autopong";

    public NetworkVariable<int> CupsA = new();
    public NetworkVariable<int> CupsB = new();

    readonly Dictionary<ulong, int> hits = new(); // serwer
    double lastAutoTurn; // klient

    int Cups(int t) => t == 0 ? CupsA.Value : CupsB.Value;
    void SetCups(int t, int v) { if (t == 0) CupsA.Value = v; else CupsB.Value = v; }

    protected override void OnRaceStart()
    {
        hits.Clear();
        CupsA.Value = CupsB.Value = cupsPerTeam;
        TeamsRpc(Team(0).ToArray());
        NextTurn();
    }

    [Rpc(SendTo.Server)]
    void ThrowRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (id != TurnPlayer.Value) return;

        if (WheelHit())
        {
            int opp = 1 - TeamOf(id);
            SetCups(opp, Cups(opp) - 1);
            hits[id] = hits.GetValueOrDefault(id) + 1;
            foreach (var m in Team(opp)) DrunkOf(m)?.AddCompetitionDrink(drinkPerCup);
            Debug.Log($"[Pong] {Olympics.Nick(id)} trafia! Kubki drużyny {opp}: {Cups(opp)}");
            if (Cups(opp) <= 0) { Finish(Ranking(opp)); return; }
        }
        else Debug.Log($"[Pong] {Olympics.Nick(id)} pudło");
        NextTurn();
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
        Ranking(Cups(0) < Cups(1) ? 0 : 1);

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running) return;
        bool myTurn = TurnPlayer.Value == NM.LocalClientId;
        var kb = Keyboard.current;
        if (myTurn && kb != null && kb.spaceKey.wasPressedThisFrame) ThrowRpc();
        if (autoMode && myTurn && TurnStart.Value != lastAutoTurn && WheelHit())
        { lastAutoTurn = TurnStart.Value; ThrowRpc(); }
    }

    protected override void DrawGame()
    {
        int my = myTeam < 0 ? 0 : myTeam;
        GUI.Label(new Rect(0, 34, Screen.width, 24),
            $"KUBKI — MY: {Cups(my)}   ONI: {Cups(1 - my)}", Ui.S(18));
        bool myTurn = TurnPlayer.Value == NM.LocalClientId;
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 40), myTurn
            ? "RZUCASZ! SPACJA gdy wskazówka w zielonym polu"
            : $"Rzuca {Olympics.Nick(TurnPlayer.Value)}...", Ui.S(24));
        DrawWheel(new Vector2(Screen.width / 2f, Screen.height * 0.55f), 90f);
    }
}
