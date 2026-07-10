using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Lucky Shot (Simon Says, drabinka — GDD 8.7): każda runda to sekwencja 6 strzałek.
// Po fazie zapamiętywania odliczanie 3-2-1-SHOT i dopiero wtedy wolno wpisywać.
// Pomyłka albo brak kompletu w czasie = odpadasz. Komplet = chwytasz kieliszek
// ze stołu i wypijasz (odchył głowy). Po alkoholu strzałki bujają się przy pokazie.
public class LuckyShot : Competition
{
    public int seqLen = 6;
    public int maxRounds = 6;
    public float showSeconds = 2.5f;
    public float goCountdown = 3f;  // 3-2-1-SHOT między pokazem a wpisywaniem
    public float answerSeconds = 8f;

    protected override string AutoFlag => "-autolucky";

    public NetworkVariable<FixedString64Bytes> Seq = new();
    public NetworkVariable<int> Round = new();
    public NetworkVariable<int> AliveCount = new();
    public NetworkVariable<double> HideAt = new();
    public NetworkVariable<double> AnswerAt = new();   // koniec odliczania = start wpisywania
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
    float drinkAnimT = 99f;   // czas od startu animacji picia (99 = brak)
    Transform myGlass;
    Vector3 glassHome;

    // linia przed stołem z kieliszkami (stół w scenie na z=-0.5)
    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        pos = new Vector3(index * 2f - (count - 1), 0.1f, -1.6f);
        yaw = 0f;
    }

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
        for (int i = 0; i < seqLen; i++)
            s += glyphs[Random.Range(0, glyphs.Length)];
        Seq.Value = s;
        HideAt.Value = Now + showSeconds;
        AnswerAt.Value = HideAt.Value + goCountdown;
        RoundEndsAt.Value = AnswerAt.Value + answerSeconds;
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
        if (State.Value != Phase.Running || Now < AnswerAt.Value || Now > RoundEndsAt.Value) return;
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
        else if (roundDone) StartDrinkAnim(); // komplet = shot!
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

    // ---- animacja: chwyć kieliszek ze stołu, wypij z odchyłem głowy, odstaw ----

    void StartDrinkAnim()
    {
        drinkAnimT = 0f;
        if (myGlass == null)
        {
            // własny kieliszek = najbliższy Shot_i (bootstrap stawia 8 na stole)
            var po = NM.LocalClient?.PlayerObject;
            if (po == null) return;
            myGlass = Enumerable.Range(0, 8)
                .Select(i => GameObject.Find("Shot_" + i)?.transform)
                .Where(t => t != null)
                .OrderBy(t => Vector3.Distance(t.position, po.transform.position))
                .FirstOrDefault();
            if (myGlass != null) glassHome = myGlass.position;
        }
    }

    void LateUpdate()
    {
        const float dur = 1.4f;
        if (drinkAnimT >= dur)
        {
            if (myGlass != null && drinkAnimT < 98f) // odstaw po animacji
            { myGlass.SetPositionAndRotation(glassHome, Quaternion.identity); drinkAnimT = 99f; }
            return;
        }
        drinkAnimT += Time.deltaTime;
        var cam = OwnCamera();
        if (cam == null) return;
        float k = Mathf.Clamp01(drinkAnimT / dur);
        float tilt = Mathf.Sin(k * Mathf.PI) * 35f; // odchył głowy i powrót
        cam.transform.localRotation *= Quaternion.Euler(-tilt, 0f, 0f);
        if (myGlass != null)
        {
            Vector3 mouth = cam.transform.position
                + cam.transform.forward * 0.35f - cam.transform.up * 0.12f;
            myGlass.position = Vector3.Lerp(glassHome, mouth, Mathf.Clamp01(k * 2f));
            myGlass.rotation = cam.transform.rotation * Quaternion.Euler(-tilt * 2.2f, 0f, 0f);
        }
    }

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || myOut || myRoundDone) return;
        if (Now < AnswerAt.Value || Now > RoundEndsAt.Value) return;
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
            DrawWobblySeq();
            return;
        }
        if (Now < AnswerAt.Value) // 3-2-1 zanim wolno wpisywać
        {
            GUI.Label(center, Mathf.CeilToInt((float)(AnswerAt.Value - Now)).ToString(), Ui.S(72));
            return;
        }
        if (Now - AnswerAt.Value < 0.8f && !myRoundDone)
            GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 40), "SHOT!", Ui.S(40));
        GUI.Label(center, myRoundDone
            ? "KOMPLET! Na zdrowie — czekasz na resztę..."
            : $"POWTÓRZ STRZAŁKAMI:  {myProg}/{Seq.Value.Length}", Ui.S(28));
    }

    // po alkoholu strzałki pływają — trudniej zapamiętać
    void DrawWobblySeq()
    {
        float d = LocalDrunk01();
        var s = Seq.Value.ToString();
        float w = 46f, x0 = Screen.width / 2f - s.Length * w / 2f, y = Screen.height * 0.3f;
        for (int i = 0; i < s.Length; i++)
        {
            float ox = (Mathf.PerlinNoise(Time.time * 1.3f, i * 7.3f) - 0.5f) * 70f * d;
            float oy = (Mathf.PerlinNoise(i * 7.3f, Time.time * 1.1f) - 0.5f) * 50f * d;
            GUI.Label(new Rect(x0 + i * w + ox, y + oy, w, 60),
                arrows[System.Array.IndexOf(glyphs, s[i])], Ui.S(48));
        }
    }
}
