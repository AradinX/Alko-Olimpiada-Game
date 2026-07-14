using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Na pół (Split the G): trzymaj SPACJĘ, żeby pić — puść dokładnie na połowie.
// TRZY RUNDY: piwo, wino, wódka — każdy trunek leje się szybciej. Im bardziej
// pijany, tym nierówniej. Wygrywa najmniejsza suma odchyleń od połowy.
public class NaPol : Competition
{
    public float target = 0.5f;
    public float baseSpeed = 0.25f;      // spadek poziomu na sekundę (piwo)
    public float drunkSpeedBonus = 1.5f; // mnożnik przy pełnym upojeniu
    public float roundSeconds = 15f;     // limit rundy — kto nie spróbował, dostaje max karę

    // kolejne rundy: nazwa, mnożnik prędkości, kolor cieczy
    static readonly (string name, float speed, Color col)[] Rounds =
    {
        ("PIWO",  1.0f, new Color(0.95f, 0.72f, 0.15f)),
        ("WINO",  1.5f, new Color(0.55f, 0.08f, 0.18f)),
        ("WÓDKA", 2.2f, new Color(0.85f, 0.9f, 0.95f)),
    };

    protected override string AutoFlag => "-autonapol";

    public NetworkVariable<int> Round = new();

    // serwer
    readonly Dictionary<ulong, float> totalErr = new();
    readonly HashSet<ulong> sentThisRound = new();
    double roundStart;

    // klient
    float level = 1f;
    bool drinking, sent;
    float sentLevel; // poziom w chwili puszczenia — do pokazania wyniku %
    double autoAt = -1;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // nowa runda: świeża szklanka u każdego klienta
        Round.OnValueChanged += (_, _) =>
        { level = 1f; drinking = false; sent = false; sentLevel = 0f; autoAt = -1; };
    }

    protected override void OnRaceStart()
    {
        totalErr.Clear();
        sentThisRound.Clear();
        Round.Value = 0;
        roundStart = Now;
    }

    void NextRoundOrFinish() // serwer
    {
        // kto nie wysłał — kara jak pełna szklanka
        foreach (var r in racers.Where(r => !sentThisRound.Contains(r)))
            totalErr[r] = totalErr.GetValueOrDefault(r) + Mathf.Abs(1f - target);
        if (Round.Value + 1 < Rounds.Length)
        {
            sentThisRound.Clear();
            roundStart = Now;
            Round.Value++;
            Debug.Log($"[NaPol] runda {Round.Value + 1}: {Rounds[Round.Value].name}");
        }
        else Finish(Ranking());
    }

    [Rpc(SendTo.Server)]
    void ResultRpc(float lvl, RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || sentThisRound.Contains(id)) return;
        sentThisRound.Add(id);
        totalErr[id] = totalErr.GetValueOrDefault(id) + Mathf.Abs(Mathf.Clamp01(lvl) - target);
        Debug.Log($"[NaPol] {Olympics.Nick(id)} ({Rounds[Round.Value].name}): {lvl:P0} (cel {target:P0})");
        if (racers.All(r => sentThisRound.Contains(r))) NextRoundOrFinish();
    }

    protected override void RunningTick()
    {
        if (Now - roundStart > roundSeconds) NextRoundOrFinish(); // maruderzy
    }

    List<ulong> Ranking() => racers
        .OrderBy(r => totalErr.GetValueOrDefault(r, 99f)).ToList();
    protected override List<ulong> TimeoutRanking() => Ranking();

    protected override void ClientTick()
    {
        if (State.Value != Phase.Running || sent) return;
        var kb = Keyboard.current;
        bool hold = kb != null && kb.spaceKey.isPressed;
        if (autoMode)
        {
            if (autoAt < 0) autoAt = Time.timeAsDouble + 1;
            hold = Time.timeAsDouble > autoAt && level > 0.47f;
        }
        if (hold)
        {
            drinking = true;
            float d = LocalDrunk01();
            float jitter = 1f + (Mathf.PerlinNoise(Time.time * 3f, 1f) - 0.5f) * d; // chlupanie
            level -= baseSpeed * Rounds[Round.Value].speed
                     * (1f + drunkSpeedBonus * d) * jitter * Time.deltaTime;
        }
        else if (drinking) { sentLevel = level; sent = true; ResultRpc(level); }
        if (level <= 0f) { level = 0f; sentLevel = 0f; sent = true; ResultRpc(0f); }
    }

    // przechył głowy proporcjonalny do wypitego
    void LateUpdate()
    {
        if (!IsSpawned || !NM.IsClient || State.Value != Phase.Running) return;
        var cam = OwnCamera();
        if (cam != null && drinking && !sent)
            cam.transform.localRotation *= Quaternion.Euler(-50f * (1f - level), 0f, 0f);
    }

    protected override void DrawGame()
    {
        var rd = Rounds[Mathf.Clamp(Round.Value, 0, Rounds.Length - 1)];
        // celność: 100% = puszczone idealnie na pół, 0% = na samym skraju
        float acc = Mathf.Clamp01(1f - Mathf.Abs(sentLevel - target) * 2f);
        GUI.Label(new Rect(0, Screen.height * 0.16f, Screen.width, 30),
            $"RUNDA {Round.Value + 1}/{Rounds.Length}: {rd.name}"
            + (rd.speed > 1f ? $"  (leci {rd.speed:0.0}x szybciej!)" : ""), Ui.S(24));
        GUI.Label(new Rect(0, Screen.height * 0.16f + 34f, Screen.width, 30),
            sent ? $"Puściłeś na {sentLevel:P0}  —  celność {acc:P0}. Czekaj na resztę..."
                 : "Trzymaj SPACJĘ — puść NA PÓŁ (na oko, bez znacznika)!", Ui.S(22));

        // szklanka: pionowy pasek BEZ linii celu — trzeba trafić na oko
        float h = Screen.height * 0.45f;
        var glass = new Rect(Screen.width * 0.5f - 30f, (Screen.height - h) / 2f, 60f, h);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(glass, Texture2D.whiteTexture);
        GUI.color = rd.col;
        float fill = h * Mathf.Clamp01(level);
        GUI.DrawTexture(new Rect(glass.x, glass.yMax - fill, glass.width, fill), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
