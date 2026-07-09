using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Lucky Shot (Simon Says, drabinka — GDD 8.7): rundy z coraz dłuższą sekwencją strzałek.
// Pomyłka albo brak kompletu w czasie = odpadasz. Ostatni na nogach wygrywa.
public class LuckyShot : Competition
{
    public int startLen = 3;
    public int maxRounds = 6;
    public float showSeconds = 2.5f;
    public float answerSeconds = 8f;

    protected override string AutoFlag => "-autolucky";

    public NetworkVariable<FixedString64Bytes> Seq = new();
    public NetworkVariable<int> Round = new();
    public NetworkVariable<int> AliveCount = new();
    public NetworkVariable<double> HideAt = new();
    public NetworkVariable<double> RoundEndsAt = new();

    static readonly char[] glyphs = { 'U', 'D', 'L', 'R' };
    static readonly string[] arrows = { "^", "v", "<", ">" };

    // serwer
    readonly List<ulong> alive = new();
    readonly List<ulong> eliminated = new(); // najwcześniej odpadli = najgorsi (początek listy)
    readonly Dictionary<ulong, int> progress = new();
    readonly Dictionary<ulong, double> okTime = new();
    readonly HashSet<ulong> failed = new(), doneOk = new();

    // klient
    int myProg;
    bool myOut, myRoundDone;
    float nextAutoKey;

    protected override void OnRaceStart()
    {
        alive.Clear();
        alive.AddRange(racers);
        eliminated.Clear();
        StartRound(1);
    }

    void StartRound(int r)
    {
        Round.Value = r;
        AliveCount.Value = alive.Count;
        progress.Clear(); okTime.Clear(); failed.Clear(); doneOk.Clear();
        var s = "";
        for (int i = 0; i < startLen + r - 1; i++)
            s += glyphs[Random.Range(0, glyphs.Length)];
        Seq.Value = s;
        HideAt.Value = Now + showSeconds;
        RoundEndsAt.Value = HideAt.Value + answerSeconds;
        RoundResetRpc(r);
        Debug.Log($"[Lucky] runda {r}: {s} ({alive.Count} graczy)");
    }

    [Rpc(SendTo.ClientsAndHost)]
    void RoundResetRpc(int r)
    {
        myProg = 0;
        myRoundDone = false;
        if (r == 1) myOut = false;
    }

    [Rpc(SendTo.Server)]
    void KeyRpc(byte g, RpcParams p = default)
    {
        if (State.Value != Phase.Running || Now < HideAt.Value || Now > RoundEndsAt.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!alive.Contains(id) || failed.Contains(id) || doneOk.Contains(id)) return;

        int prog = progress.GetValueOrDefault(id);
        if (Seq.Value[prog] == (byte)glyphs[g])
        {
            progress[id] = ++prog;
            if (prog >= Seq.Value.Length) { doneOk.Add(id); okTime[id] = Now; }
        }
        else failed.Add(id);
        StateRpc(progress.GetValueOrDefault(id),
            doneOk.Contains(id) || failed.Contains(id), failed.Contains(id),
            RpcTarget.Single(id, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void StateRpc(int prog, bool roundDone, bool outNow, RpcParams p = default)
    {
        myProg = prog;
        myRoundDone = roundDone;
        if (outNow) myOut = true;
    }

    protected override void RunningTick()
    {
        if (Round.Value == 0) return;
        bool allDone = alive.All(a => doneOk.Contains(a) || failed.Contains(a));
        if (!allDone && Now < RoundEndsAt.Value) return;

        // rozliczenie: bez kompletu = odpadasz; mniejszy postęp = niższe miejsce
        var outNow = alive.Where(a => !doneOk.Contains(a))
            .OrderBy(a => progress.GetValueOrDefault(a)).ToList();
        foreach (var a in outNow)
        {
            alive.Remove(a);
            eliminated.Add(a);
            OutRpc(RpcTarget.Single(a, RpcTargetUse.Temp));
        }
        if (outNow.Count > 0)
            Debug.Log("[Lucky] odpadli: " + string.Join(", ", outNow.Select(Olympics.Nick)));

        if (alive.Count <= 1 || Round.Value >= maxRounds) Finish(BuildRanking());
        else StartRound(Round.Value + 1);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void OutRpc(RpcParams p = default) => myOut = true;

    List<ulong> BuildRanking()
    {
        // żywi na górze (szybszy komplet ostatniej rundy wyżej), potem odpadli od końca
        var top = alive.OrderBy(a => okTime.GetValueOrDefault(a, double.MaxValue));
        return top.Concat(Enumerable.Reverse(eliminated)).ToList();
    }

    protected override List<ulong> TimeoutRanking() => BuildRanking();

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || myOut || myRoundDone) return;
        if (Now < HideAt.Value || Now > RoundEndsAt.Value) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.upArrowKey.wasPressedThisFrame) KeyRpc(0);
            else if (kb.downArrowKey.wasPressedThisFrame) KeyRpc(1);
            else if (kb.leftArrowKey.wasPressedThisFrame) KeyRpc(2);
            else if (kb.rightArrowKey.wasPressedThisFrame) KeyRpc(3);
        }
        if (autoMode && Time.time >= nextAutoKey && myProg < Seq.Value.Length)
        {
            nextAutoKey = Time.time + 0.3f;
            int g = System.Array.IndexOf(glyphs, (char)Seq.Value[myProg]);
            // ponytail: auto myli się w 10% — inaczej drabinka w smoke tescie nie ma końca
            if (Random.value < 0.1f) g = (g + 1) % 4;
            KeyRpc((byte)g);
        }
    }

    string Pretty(string seq) => string.Join(" ",
        seq.Select(ch => arrows[System.Array.IndexOf(glyphs, ch)]));

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, Screen.height * 0.14f, Screen.width, 26),
            $"RUNDA {Round.Value}  —  w grze: {AliveCount.Value}", Ui.S(20));
        var center = new Rect(0, Screen.height * 0.3f, Screen.width, 60);
        if (myOut)
        {
            GUI.Label(center, "ODPADŁEŚ — oglądasz resztę", Ui.S(28));
            return;
        }
        if (Now < HideAt.Value)
        {
            GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 30),
                "ZAPAMIĘTAJ SEKWENCJĘ:", Ui.S(22));
            GUI.Label(center, Pretty(Seq.Value.ToString()), Ui.S(48));
            return;
        }
        GUI.Label(center, myRoundDone
            ? "KOMPLET! Czekasz na resztę..."
            : $"POWTÓRZ STRZAŁKAMI:  {myProg}/{Seq.Value.Length}", Ui.S(28));
    }
}
