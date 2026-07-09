using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Punktacja olimpiady — czysty stan po stronie serwera (przeżywa zmiany scen).
public static class Olympics
{
    static readonly Dictionary<ulong, int> scores = new();
    static readonly int[] medals = { 5, 3, 1 };

    // serwer: przyznaje medale wg rankingu, zwraca tekst tabeli
    public static string Award(List<ulong> ranking, out string resultsText)
    {
        var lines = new List<string>();
        for (int i = 0; i < ranking.Count; i++)
        {
            int pts = i < 3 ? medals[i] : 0;
            scores[ranking[i]] = scores.GetValueOrDefault(ranking[i]) + pts;
            lines.Add($"#{i + 1}  {Nick(ranking[i])}  +{pts} pkt");
        }
        resultsText = string.Join("\n", lines);
        return Text();
    }

    public static string Text() =>
        scores.Count == 0 ? "" : "PUNKTY:  " + string.Join("   ",
            scores.OrderByDescending(kv => kv.Value)
                  .Select(kv => $"{Nick(kv.Key)}: {kv.Value}"));

    public static void Reset() => scores.Clear();

    public static string Champion() => scores.Count == 0 ? "?"
        : Nick(scores.OrderByDescending(kv => kv.Value).First().Key);

    public static string Nick(ulong id)
    {
        var nm = NetworkManager.Singleton;
        return nm != null && nm.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<PlayerNameTag>().Nickname.Value.ToString()
            : $"Gracz{id}";
    }
}

// Wspólne style IMGUI (wołać tylko z OnGUI)
public static class Ui
{
    public static GUIStyle S(int size) => new(GUI.skin.label)
    { fontSize = size, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
}
