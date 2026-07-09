using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Flanki (GDD 8.2): drużyny naprzemiennie rzucają w puszkę (kółko timingowe + SPACJA).
// Trafienie = drużyna rzucająca chleje (SPACJA), aż przeciwnik postawi puszkę
// (naprzemienne A-D-A-D). Wygrywa drużyna, która pierwsza dopije wszystkie kufle.
public class Flanki : TeamCompetition
{
    public enum FPhase : byte { Throwing, Drinking }

    public float mugSize = 100f;
    public float sipBase = 4f;
    public float sipDrunkPenalty = 0.6f;
    public float drunkPerSip = 0.5f;
    public int resetNeeded = 14; // naprzemiennych A-D do postawienia puszki

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

    protected override void OnRaceStart()
    {
        mug.Clear();
        FState.Value = FPhase.Throwing;
        TeamsRpc(Team(0).ToArray());
        NextTurn();
        UpdateLive();
    }

    [Rpc(SendTo.Server)]
    void ThrowRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running || FState.Value != FPhase.Throwing) return;
        ulong id = p.Receive.SenderClientId;
        if (id != TurnPlayer.Value) return;

        if (WheelHit())
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

        float sip = sipBase * (1f - sipDrunkPenalty * ds.Drunk.Value / 100f);
        ds.AddCompetitionDrink(drunkPerSip);
        mug[id] = mug.GetValueOrDefault(id) + sip;
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
        if (State.Value != Phase.Running) return;
        var kb = Keyboard.current;
        bool myTurn = TurnPlayer.Value == NM.LocalClientId;

        if (FState.Value == FPhase.Throwing)
        {
            if (myTurn && kb != null && kb.spaceKey.wasPressedThisFrame) ThrowRpc();
            if (autoMode && myTurn && TurnStart.Value != lastAutoTurn && WheelHit())
            { lastAutoTurn = TurnStart.Value; ThrowRpc(); }
            return;
        }
        // Drinking
        bool drinking = myTeam == DrinkingTeam.Value;
        bool resetter = ResetterId.Value == NM.LocalClientId;
        if (kb != null)
        {
            if (drinking && kb.spaceKey.wasPressedThisFrame) SipRpc();
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
                ? "RZUCASZ! SPACJA gdy wskazówka w zielonym polu"
                : $"Rzuca {Olympics.Nick(TurnPlayer.Value)}...", Ui.S(24));
            DrawWheel(new Vector2(Screen.width / 2f, Screen.height * 0.55f), 90f);
            return;
        }
        if (myTeam == DrinkingTeam.Value)
            GUI.Label(center, "PUSZKA LEŻY — CHLEJ! (SPACJA)", Ui.S(28));
        else if (ResetterId.Value == NM.LocalClientId)
            GUI.Label(center, $"PODNIEŚ PUSZKĘ: wciskaj A-D-A-D!  ({ResetCount.Value}/{resetNeeded})", Ui.S(28));
        else
            GUI.Label(center, "Twoja drużyna stawia puszkę...", Ui.S(24));
    }
}
