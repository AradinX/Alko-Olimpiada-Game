using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// OCZKO (21) — hazard lite zamiast pokera (GDD 8.8 przyjdzie później).
// 3 rozdania przeciw bankowi. Przed rozdaniem obstawiasz W CIEMNO 1-3 szoty [1][2][3]
// i pijesz je od razu (upojenie na stałe!). Wygrana z bankiem płaci tyle żetonów,
// ile postawiłeś; przegrana tyle zabiera. Bank dobiera do 17. Ranking po żetonach.
// Pijacki twist: karty "mieszają się w oczach" — im bardziej pijany, tym częściej
// widzisz nie tę kartę, którą masz (prawdziwe wartości są stałe, jak w GDD pokerze).
public class Oczko : Competition
{
    public int rounds = 3;
    public float stakeSeconds = 8f;
    public float playSeconds = 20f;
    public float settleSeconds = 6f;
    public float shotDrunk = 5f; // upojenie za 1 zadeklarowany szot (na stałe)

    protected override string AutoFlag => "-autooczko";

    public enum Sub : byte { Stake, Play, Settle }

    public NetworkVariable<byte> SubPhase = new();
    public NetworkVariable<int> RoundNo = new();
    public NetworkVariable<double> SubEndsAt = new();
    public NetworkVariable<FixedString128Bytes> DealerLine = new();
    public NetworkVariable<int> DealerTotal = new(); // 0 = jeszcze zakryty
    public NetworkVariable<FixedString512Bytes> ChipsLine = new();

    // serwer
    readonly Dictionary<ulong, int> chips = new();
    readonly Dictionary<ulong, int> stake = new();
    readonly Dictionary<ulong, List<int>> hands = new();
    readonly HashSet<ulong> standing = new();
    readonly List<int> dealer = new();
    readonly List<int> deck = new();

    // klient
    string[] myCards = System.Array.Empty<string>();
    int myTotal, myStake;
    double nextAuto;

    // ---------- karty: int 0-51, ranga c%13 (0=A ... 12=K), kolor c/13 ----------

    static readonly string[] rankStr =
    { "A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "D", "K" };
    static readonly string[] suitStr = { "♠", "♥", "♦", "♣" };

    static string CardStr(int c) => rankStr[c % 13] + suitStr[c / 13];

    static int Total(List<int> h)
    {
        int sum = 0, aces = 0;
        foreach (var c in h)
        {
            int r = c % 13;
            if (r == 0) { aces++; sum += 11; }
            else sum += Mathf.Min(r + 1, 10);
        }
        while (sum > 21 && aces-- > 0) sum -= 10; // as 11 -> 1
        return sum;
    }

    int Draw()
    {
        // ponytail: przy 10 graczach mocno dobierających talia może się skończyć — dosypujemy świeżą
        if (deck.Count == 0)
            for (int i = 0; i < 52; i++) deck.Add(i);
        int k = Random.Range(0, deck.Count);
        int c = deck[k];
        deck.RemoveAt(k);
        return c;
    }

    DrunkSystem DrunkOf(ulong id) =>
        NM.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<DrunkSystem>() : null;

    // ---------- przebieg (serwer) ----------

    protected override void OnRaceStart()
    {
        chips.Clear();
        foreach (var r in racers) chips[r] = 0;
        UpdateChipsLine();
        StartRound(1);
    }

    void StartRound(int r)
    {
        RoundNo.Value = r;
        stake.Clear(); hands.Clear(); standing.Clear(); dealer.Clear();
        deck.Clear();
        for (int i = 0; i < 52; i++) deck.Add(i);
        DealerTotal.Value = 0;
        DealerLine.Value = "";
        SubPhase.Value = (byte)Sub.Stake;
        SubEndsAt.Value = Now + stakeSeconds;
        RoundResetRpc();
        Debug.Log($"[Oczko] rozdanie {r}");
    }

    [Rpc(SendTo.ClientsAndHost)]
    void RoundResetRpc()
    {
        myCards = System.Array.Empty<string>();
        myTotal = 0;
        myStake = 0;
    }

    [Rpc(SendTo.Server)]
    void StakeRpc(int s, RpcParams p = default)
    {
        if (State.Value != Phase.Running || SubPhase.Value != (byte)Sub.Stake) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || stake.ContainsKey(id)) return;
        stake[id] = Mathf.Clamp(s, 1, 3);
        Debug.Log($"[Oczko] {Olympics.Nick(id)} stawia {stake[id]}");
        if (racers.All(x => stake.ContainsKey(x))) StartPlay();
    }

    void StartPlay()
    {
        foreach (var r in racers)
        {
            if (!stake.ContainsKey(r)) stake[r] = 1; // AFK gra minimum
            DrunkOf(r)?.AddCompetitionDrink(stake[r] * shotDrunk); // szoty w ciemno — od razu
            var h = new List<int> { Draw(), Draw() };
            hands[r] = h;
            SendHand(r);
        }
        dealer.Add(Draw());
        dealer.Add(Draw());
        DealerLine.Value = $"BANK: {CardStr(dealer[0])} + zakryta";
        SubPhase.Value = (byte)Sub.Play;
        SubEndsAt.Value = Now + playSeconds;
    }

    void SendHand(ulong id)
    {
        var h = hands[id];
        HandRpc(string.Join(" ", h.Select(CardStr)), Total(h), stake[id],
            RpcTarget.Single(id, RpcTargetUse.Temp));
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void HandRpc(string cards, int total, int st, RpcParams p = default)
    {
        myCards = cards.Split(' ');
        myTotal = total;
        myStake = st;
    }

    [Rpc(SendTo.Server)]
    void HitRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running || SubPhase.Value != (byte)Sub.Play) return;
        ulong id = p.Receive.SenderClientId;
        if (!hands.TryGetValue(id, out var h) || standing.Contains(id)) return;
        h.Add(Draw());
        if (Total(h) >= 21) standing.Add(id); // fura albo dokładnie 21 = koniec dobierania
        SendHand(id);
        if (racers.All(r => standing.Contains(r) || !hands.ContainsKey(r))) Settle();
    }

    [Rpc(SendTo.Server)]
    void StandRpc(RpcParams p = default)
    {
        if (State.Value != Phase.Running || SubPhase.Value != (byte)Sub.Play) return;
        ulong id = p.Receive.SenderClientId;
        if (!hands.ContainsKey(id)) return;
        standing.Add(id);
        if (racers.All(r => standing.Contains(r) || !hands.ContainsKey(r))) Settle();
    }

    void Settle()
    {
        if (SubPhase.Value != (byte)Sub.Play) return;
        while (Total(dealer) < 17) dealer.Add(Draw());
        int dt = Total(dealer);
        DealerTotal.Value = dt;
        DealerLine.Value = $"BANK: {string.Join(" ", dealer.Select(CardStr))} = {dt}"
            + (dt > 21 ? " (FURA!)" : "");
        foreach (var r in racers)
        {
            if (!hands.TryGetValue(r, out var h)) continue;
            int t = Total(h);
            int win = t > 21 ? -1 : dt > 21 || t > dt ? 1 : t == dt ? 0 : -1;
            chips[r] = chips.GetValueOrDefault(r) + win * stake[r];
            Debug.Log($"[Oczko] {Olympics.Nick(r)}: {t} vs bank {dt} -> {win * stake[r]:+0;-0;0}");
        }
        UpdateChipsLine();
        SubPhase.Value = (byte)Sub.Settle;
        SubEndsAt.Value = Now + settleSeconds;
    }

    void UpdateChipsLine() => ChipsLine.Value = "ŻETONY:   " + string.Join("   ",
        chips.OrderByDescending(kv => kv.Value)
             .Select(kv => $"{Olympics.Nick(kv.Key)}: {kv.Value}"));

    protected override void RunningTick()
    {
        if (RoundNo.Value == 0 || Now < SubEndsAt.Value)
        {
            // wszyscy spasowali przed czasem? Settle woła się z RPC; tu tylko timeouty
            return;
        }
        switch ((Sub)SubPhase.Value)
        {
            case Sub.Stake: StartPlay(); break;
            case Sub.Play: Settle(); break;
            case Sub.Settle:
                if (RoundNo.Value >= rounds) Finish(Ranking());
                else StartRound(RoundNo.Value + 1);
                break;
        }
    }

    List<ulong> Ranking() => racers.OrderByDescending(r => chips.GetValueOrDefault(r)).ToList();
    protected override List<ulong> TimeoutRanking() => Ranking();

    // ---------- klient ----------

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running) return;
        var kb = Keyboard.current;
        var sub = (Sub)SubPhase.Value;

        if (sub == Sub.Stake && myStake == 0)
        {
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) { myStake = 1; StakeRpc(1); Sfx.Play("gulp"); }
                else if (kb.digit2Key.wasPressedThisFrame) { myStake = 2; StakeRpc(2); Sfx.Play("gulp"); }
                else if (kb.digit3Key.wasPressedThisFrame) { myStake = 3; StakeRpc(3); Sfx.Play("gulp"); }
            }
            if (autoMode && Time.timeAsDouble > nextAuto)
            {
                nextAuto = Time.timeAsDouble + 1;
                myStake = Random.Range(1, 4);
                StakeRpc(myStake);
            }
        }
        else if (sub == Sub.Play && myTotal is > 0 and < 21)
        {
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) HitRpc();
                else if (kb.sKey.wasPressedThisFrame) StandRpc();
            }
            if (autoMode && Time.timeAsDouble > nextAuto)
            {
                nextAuto = Time.timeAsDouble + 0.8;
                if (myTotal < 17) HitRpc(); else StandRpc();
            }
        }
    }

    // pijackie migotanie: czasem zamiast prawdziwej karty widzisz losową (tym częściej,
    // im bardziej pijany); podmianka zmienia się co pół sekundy, wartości realne są stałe
    string ShownCard(string real, int i)
    {
        float d = LocalDrunk01();
        if (d <= 0f) return real;
        var rnd = new System.Random(i * 131 + (int)(Time.time * 2f) * 7919);
        if (rnd.NextDouble() >= d * 0.9) return real;
        return rankStr[rnd.Next(13)] + suitStr[rnd.Next(4)];
    }

    protected override void DrawGame()
    {
        var sub = (Sub)SubPhase.Value;
        int left = Mathf.Max(0, Mathf.CeilToInt((float)(SubEndsAt.Value - Now)));
        GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 26),
            $"ROZDANIE {RoundNo.Value}/{rounds}   ({left}s)", Ui.S(20));
        if (ChipsLine.Value.Length > 0)
            GUI.Label(new Rect(0, Screen.height * 0.88f, Screen.width, 24),
                ChipsLine.Value.ToString(), Ui.S(16));

        var center = new Rect(0, Screen.height * 0.3f, Screen.width, 50);
        if (sub == Sub.Stake)
        {
            GUI.Label(center, myStake > 0
                ? $"Postawione: {myStake}. Czekamy na resztę..."
                : "OBSTAW W CIEMNO: [1] [2] [3] szoty — pijesz je OD RAZU, wygrana płaci tyle żetonów",
                Ui.S(24));
            return;
        }

        // karty banku i moje — z pijackim migotaniem
        if (DealerLine.Value.Length > 0)
            GUI.Label(new Rect(0, Screen.height * 0.22f, Screen.width, 30),
                DealerLine.Value.ToString(), Ui.S(24));
        float w = 84f, x0 = Screen.width / 2f - myCards.Length * w / 2f;
        for (int i = 0; i < myCards.Length; i++)
        {
            string shown = ShownCard(myCards[i], i);
            var st = Ui.S(40);
            st.normal.textColor = shown.Contains('♥') || shown.Contains('♦')
                ? new Color(1f, 0.35f, 0.35f) : Color.white;
            GUI.Label(new Rect(x0 + i * w, Screen.height * 0.42f, w, 60), shown, st);
        }
        GUI.Label(new Rect(0, Screen.height * 0.53f, Screen.width, 30),
            $"MASZ: {myTotal}" + (myTotal > 21 ? "  —  FURA!" : ""), Ui.S(26));

        if (sub == Sub.Play)
        {
            if (myTotal is > 0 and < 21)
                GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 30),
                    "[SPACJA] dobierz     [S] stój", Ui.S(22));
        }
        else if (sub == Sub.Settle && myTotal > 0 && DealerTotal.Value > 0)
        {
            int dt = DealerTotal.Value;
            string verdict = myTotal > 21 ? $"PRZEGRANA -{myStake}"
                : dt > 21 || myTotal > dt ? $"WYGRANA +{myStake}"
                : myTotal == dt ? "REMIS — stawka wraca" : $"PRZEGRANA -{myStake}";
            GUI.Label(new Rect(0, Screen.height * 0.62f, Screen.width, 36), verdict, Ui.S(30));
        }
    }
}
