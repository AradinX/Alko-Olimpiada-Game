using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Punktacja olimpiady i jednorazowe efekty przeżywają zmiany scen po stronie serwera.
public static class Olympics
{
    public const ulong NoBet = ulong.MaxValue;

    static readonly Dictionary<ulong, int> scores = new();
    static readonly Dictionary<ulong, SpecialBeer> bonuses = new();
    static readonly Dictionary<ulong, ulong> bets = new();
    static readonly int[] medals = { 5, 3, 1 };

    static Olympics()
    {
        Debug.Assert(BonusPoints(SpecialBeer.DoublePoints, 0, 5, 0) == 10);
        Debug.Assert(BonusPoints(SpecialBeer.Spartan, 0, 5, 0) == 15);
        Debug.Assert(BonusPoints(SpecialBeer.Spartan, 1, 3, 0) == 0);
        Debug.Assert(BonusPoints(SpecialBeer.Nike, 2, 1, 0) == 3);
        Debug.Assert(BonusPoints(SpecialBeer.Tyche, 3, 0, 4) == 4);
    }

    public static void SetBeerBonus(ulong id, SpecialBeer bonus)
    {
        if (bonus == SpecialBeer.None || bonus == SpecialBeer.Shield) bonuses.Remove(id);
        else bonuses[id] = bonus; // nowe piwo zastępuje poprzedni bonus
    }

    public static int PointsOf(ulong id) => scores.GetValueOrDefault(id);

    public static void AddPoints(ulong id, int points) =>
        scores[id] = Mathf.Max(0, scores.GetValueOrDefault(id) + points);

    public static bool TrySetBet(ulong bettor, ulong target, out string message)
    {
        if (target == NoBet)
        {
            if (bets.Remove(bettor))
            {
                AddPoints(bettor, 1);
                message = "Zakład anulowany — 1 pkt wraca";
            }
            else message = "Brak aktywnego zakładu";
            return true;
        }

        if (!bets.ContainsKey(bettor))
        {
            if (PointsOf(bettor) < 1)
            {
                message = "Potrzebujesz 1 punktu, żeby obstawić";
                return false;
            }
            AddPoints(bettor, -1);
        }

        bets[bettor] = target;
        message = $"Zakład: {Nick(target)} wygra (+2 pkt)";
        return true;
    }

    public static string BetsText() => bets.Count == 0
        ? "ZAKŁADY: brak   [TAB] wybierz gracza"
        : "ZAKŁADY: " + string.Join("   ", bets.Select(b => $"{Nick(b.Key)}→{Nick(b.Value)}"))
            + "   [TAB] zmień/anuluj";

    // Serwer: przyznaje medale, rozlicza bonusy, Nemezis i zakłady.
    public static string Award(List<ulong> ranking, out string resultsText)
    {
        var lines = new List<string>();
        var position = ranking.Select((id, index) => (id, index))
            .ToDictionary(x => x.id, x => x.index);

        int leaderScore = scores.Count == 0 ? 0 : scores.Values.Max();
        var leaders = scores.Where(pair => pair.Value == leaderScore).Select(pair => pair.Key).ToList();

        for (int i = 0; i < ranking.Count; i++)
        {
            ulong id = ranking[i];
            int basePoints = i < medals.Length ? medals[i] : 0;
            var bonus = bonuses.GetValueOrDefault(id);
            int tycheRoll = bonus == SpecialBeer.Tyche ? Random.Range(-1, 5) : 0;
            int requested = BonusPoints(bonus, i, basePoints, tycheRoll);
            int before = PointsOf(id);
            AddPoints(id, requested);
            int gained = PointsOf(id) - before;

            string tag = bonus switch
            {
                SpecialBeer.DoublePoints when basePoints > 0 => " [x2]",
                SpecialBeer.Spartan when i == 0 => " [SPARTA x3]",
                SpecialBeer.Spartan => " [SPARTA: 0]",
                SpecialBeer.Nike when i < 3 => " [NIKE +2]",
                SpecialBeer.Tyche => $" [TYCHE {(tycheRoll >= 0 ? "+" : "")}{tycheRoll}]",
                SpecialBeer.Nemesis => " [NEMEZIS]",
                _ => ""
            };
            string delta = gained >= 0 ? $"+{gained}" : gained.ToString();
            lines.Add($"#{i + 1}  {Nick(id)}  {delta} pkt{tag}");
        }

        foreach (var pair in bonuses.Where(pair => pair.Value == SpecialBeer.Nemesis).ToList())
        {
            ulong owner = pair.Key;
            if (!position.TryGetValue(owner, out int ownerPlace)) continue;
            ulong? target = leaders
                .Where(leader => leader != owner
                    && position.TryGetValue(leader, out int leaderPlace)
                    && ownerPlace < leaderPlace)
                .Cast<ulong?>()
                .FirstOrDefault();
            if (!target.HasValue) continue;

            int stolen = Mathf.Min(2, PointsOf(target.Value));
            if (stolen <= 0) continue;
            AddPoints(target.Value, -stolen);
            AddPoints(owner, stolen);
            lines.Add($"NEMEZIS: {Nick(owner)} kradnie {stolen} pkt graczowi {Nick(target.Value)}");
        }
        bonuses.Clear();

        if (ranking.Count > 0)
        {
            ulong winner = ranking[0];
            foreach (var bet in bets)
            {
                if (bet.Value == winner)
                {
                    AddPoints(bet.Key, 2);
                    lines.Add($"ZAKŁAD WYGRANY: {Nick(bet.Key)} +2 pkt");
                }
                else lines.Add($"Zakład przegrany: {Nick(bet.Key)}");
            }
        }
        bets.Clear();

        resultsText = string.Join("\n", lines);
        return Text();
    }

    static int BonusPoints(SpecialBeer bonus, int place, int basePoints, int tycheRoll) => bonus switch
    {
        SpecialBeer.DoublePoints => basePoints * 2,
        SpecialBeer.Spartan => place == 0 ? basePoints * 3 : 0,
        SpecialBeer.Nike => basePoints + (place < 3 ? 2 : 0),
        SpecialBeer.Tyche => basePoints + tycheRoll,
        _ => basePoints
    };

    public static string Text() =>
        scores.Count == 0 ? "" : "PUNKTY:  " + string.Join("   ",
            scores.OrderByDescending(pair => pair.Value)
                  .Select(pair => $"{Nick(pair.Key)}: {pair.Value}"));

    public static void Reset()
    {
        scores.Clear();
        bonuses.Clear();
        bets.Clear();
    }

    public static string Champion() => scores.Count == 0 ? "?"
        : Nick(scores.OrderByDescending(pair => pair.Value).First().Key);

    public static string Nick(ulong id)
    {
        var manager = NetworkManager.Singleton;
        return manager != null
            && manager.ConnectedClients.TryGetValue(id, out var client)
            && client.PlayerObject != null
            ? client.PlayerObject.GetComponent<PlayerNameTag>().Nickname.Value.ToString()
            : $"Gracz{id}";
    }
}

public static class Ui
{
    public static GUIStyle S(int size) => new(GUI.skin.label)
    {
        fontSize = size,
        alignment = TextAnchor.MiddleCenter,
        fontStyle = FontStyle.Bold
    };
}
