using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Lucky Shot (Simon Says): serwer pokazuje sekwencję strzałek na 3 s, potem
// powtarzasz ją strzałkami. Pomyłka = koniec. Ranking: ile trafiłeś, potem czas.
// ponytail: jedna runda pamięciowa zamiast drabinki z GDD 8.7 — drabinka po playteście
public class LuckyShot : Competition
{
    public int seqLen = 6;
    public float showSeconds = 3f;

    protected override string AutoFlag => "-autolucky";

    public NetworkVariable<FixedString64Bytes> Seq = new(); // np. "ULDR"
    public NetworkVariable<double> HideAt = new();

    static readonly char[] glyphs = { 'U', 'D', 'L', 'R' };
    static readonly string[] arrows = { "^", "v", "<", ">" };

    // serwer
    readonly Dictionary<ulong, int> progress = new();
    readonly Dictionary<ulong, double> doneTime = new();
    readonly HashSet<ulong> done = new();

    // klient
    int myProg; bool myDone, myFailed;
    float nextAutoKey;

    protected override void OnRaceStart()
    {
        progress.Clear(); doneTime.Clear(); done.Clear();
        var s = "";
        for (int i = 0; i < seqLen; i++) s += glyphs[Random.Range(0, glyphs.Length)];
        Seq.Value = s;
        HideAt.Value = Now + showSeconds;
        Debug.Log("[Lucky] sekwencja " + s);
    }

    [Rpc(SendTo.Server)]
    void KeyRpc(byte g, RpcParams p = default)
    {
        if (State.Value != Phase.Running || Now < HideAt.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || done.Contains(id)) return;

        int prog = progress.GetValueOrDefault(id);
        bool ok = Seq.Value[prog] == (byte)glyphs[g];
        if (ok)
        {
            progress[id] = ++prog;
            if (prog >= seqLen)
            {
                done.Add(id); doneTime[id] = Now;
                Debug.Log($"[Lucky] {Olympics.Nick(id)} komplet w {Now - raceStart:0.0}s");
            }
        }
        else
        {
            done.Add(id); doneTime[id] = Now;
            Debug.Log($"[Lucky] {Olympics.Nick(id)} pomyłka na {prog + 1}");
        }
        StateRpc(progress.GetValueOrDefault(id), done.Contains(id), !ok,
            RpcTarget.Single(id, RpcTargetUse.Temp));
        if (done.Count == racers.Count) Finish(Ranking());
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void StateRpc(int prog, bool finished, bool failed, RpcParams p = default)
    { myProg = prog; myDone = finished; myFailed = failed; }

    List<ulong> Ranking() => racers
        .OrderByDescending(r => progress.GetValueOrDefault(r))
        .ThenBy(r => doneTime.GetValueOrDefault(r, double.MaxValue)).ToList();

    protected override List<ulong> TimeoutRanking() => Ranking();

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || myDone || Now < HideAt.Value) return;
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
            nextAutoKey = Time.time + 0.33f;
            byte g = (byte)System.Array.IndexOf(glyphs, (char)Seq.Value[myProg]);
            KeyRpc(g);
        }
    }

    string Pretty(string seq) => string.Join(" ",
        seq.Select(ch => arrows[System.Array.IndexOf(glyphs, ch)]));

    protected override void DrawGame()
    {
        var center = new Rect(0, Screen.height * 0.3f, Screen.width, 60);
        if (Now < HideAt.Value)
        {
            GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 30),
                "ZAPAMIĘTAJ SEKWENCJĘ:", Ui.S(22));
            GUI.Label(center, Pretty(Seq.Value.ToString()), Ui.S(48));
            return;
        }
        if (myDone)
            GUI.Label(center, myFailed ? $"POMYŁKA! Zaliczone: {myProg}/{seqLen}"
                                       : "KOMPLET! Czekasz na resztę...", Ui.S(28));
        else
            GUI.Label(center, $"POWTÓRZ STRZAŁKAMI:  {myProg}/{seqLen}", Ui.S(28));
    }
}
