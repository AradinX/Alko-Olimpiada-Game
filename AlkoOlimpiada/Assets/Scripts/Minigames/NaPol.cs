using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Na pół (Split the G): trzymaj SPACJĘ, żeby pić — puść dokładnie na połowie szklanki.
// Im bardziej pijany, tym szybciej i nierówniej leci poziom. Jedna próba.
public class NaPol : Competition
{
    public float target = 0.5f;
    public float baseSpeed = 0.25f;      // spadek poziomu na sekundę
    public float drunkSpeedBonus = 1.5f; // mnożnik przy pełnym upojeniu

    protected override string AutoFlag => "-autonapol";

    readonly Dictionary<ulong, float> results = new(); // serwer

    float level = 1f;
    bool drinking, sent;
    double autoAt = -1;

    protected override void OnRaceStart() => results.Clear();

    [Rpc(SendTo.Server)]
    void ResultRpc(float lvl, RpcParams p = default)
    {
        if (State.Value != Phase.Running) return;
        ulong id = p.Receive.SenderClientId;
        if (!racers.Contains(id) || results.ContainsKey(id)) return;
        results[id] = Mathf.Clamp01(lvl); // sanity na dane od klienta
        Debug.Log($"[NaPol] {Olympics.Nick(id)}: {lvl:P0} (cel {target:P0})");
        if (racers.All(r => results.ContainsKey(r))) Finish(Ranking());
    }

    // brak próby liczy się jak pełna szklanka (najgorzej)
    List<ulong> Ranking() => racers
        .OrderBy(r => Mathf.Abs(results.GetValueOrDefault(r, 1f) - target)).ToList();
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
            level -= baseSpeed * (1f + drunkSpeedBonus * d) * jitter * Time.deltaTime;
        }
        else if (drinking) { sent = true; ResultRpc(level); }
        if (level <= 0f) { level = 0f; sent = true; ResultRpc(0f); }
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
        GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 30),
            sent ? "Czekasz na resztę..." : "Trzymaj SPACJĘ — puść dokładnie NA PÓŁ!", Ui.S(22));

        // szklanka: pionowy pasek z linią celu
        float h = Screen.height * 0.45f;
        var glass = new Rect(Screen.width * 0.5f - 30f, (Screen.height - h) / 2f, 60f, h);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(glass, Texture2D.whiteTexture);
        GUI.color = new Color(0.35f, 0.2f, 0.05f); // guinness
        float fill = h * Mathf.Clamp01(level);
        GUI.DrawTexture(new Rect(glass.x, glass.yMax - fill, glass.width, fill), Texture2D.whiteTexture);
        GUI.color = Color.white;
        float ty = glass.yMax - h * target;
        GUI.DrawTexture(new Rect(glass.x - 10f, ty - 1.5f, glass.width + 20f, 3f), Texture2D.whiteTexture);
        GUI.Label(new Rect(glass.xMax + 14f, ty - 12f, 60f, 24f), "G",
            new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold });
    }
}
