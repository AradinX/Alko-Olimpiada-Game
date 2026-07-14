using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Lucky Shot = "MENEL MÓWI" (Simon Says): wszyscy naraz wokół stołu z kieliszkiem.
// Na ekranie lecą komendy, na reakcję masz ~2 s:
//  - "MENEL MÓWI: kliknij [K]"      -> musisz kliknąć, inaczej odpadasz
//  - "Kliknij [K]" (bez prefiksu!)  -> pułapka: klikniesz = odpadasz
//  - "MENEL MÓWI: NIE klikaj [P]"   -> pułapka: klikniesz = odpadasz
// Gra trwa do "MENEL MÓWI: SHOT! [F]" — kto pierwszy wciśnie F, wygrywa
// (żywi ranked po refleksie, odpadli od najpóźniej wyeliminowanego).
public class LuckyShot : Competition
{
    public float answerSeconds = 2f;   // okno reakcji na komendę
    public float gapSeconds = 1.1f;    // przerwa między komendami
    public float shotSeconds = 3f;     // okno na F po "SHOT!"
    public int shotAfterMin = 6, shotAfterMax = 9; // po ilu komendach pada SHOT

    protected override string AutoFlag => "-autolucky";

    public NetworkVariable<int> CmdId = new();
    public NetworkVariable<FixedString128Bytes> CmdText = new();
    public NetworkVariable<byte> CmdKey = new();     // indeks w pool
    public NetworkVariable<bool> CmdShot = new();
    public NetworkVariable<double> CmdEndsAt = new();
    public NetworkVariable<int> AliveCount = new();

    // klawisze nieużywane przez systemy gry (bez V/Q/E/F/R/G i WASD)
    static readonly (Key key, string name)[] pool =
    { (Key.K, "K"), (Key.P, "P"), (Key.M, "M"), (Key.O, "O"), (Key.L, "L"),
      (Key.B, "B"), (Key.N, "N"), (Key.Space, "SPACJA") };

    // serwer
    readonly List<ulong> alive = new();
    readonly List<ulong> eliminated = new(); // najwcześniej odpadli = początek listy
    readonly HashSet<ulong> pressedOk = new();
    readonly Dictionary<ulong, double> shotTime = new();
    int cmdCount, shotAfter;
    bool cmdMenel, cmdNegated, judged;

    // klient
    bool myOut, myPressed, autoPlanned;
    double autoPressAt;
    float drinkAnimT = 99f;   // czas od startu animacji picia (99 = brak)
    Transform myGlass;
    Vector3 glassHome;

    protected override void OnRaceStart()
    {
        alive.Clear();
        alive.AddRange(racers);
        eliminated.Clear();
        shotTime.Clear();
        cmdCount = 0;
        shotAfter = Random.Range(shotAfterMin, shotAfterMax + 1);
        AliveCount.Value = alive.Count;
        NextCommand();
    }

    void NextCommand() // serwer
    {
        cmdCount++;
        pressedOk.Clear();
        judged = false;
        CmdShot.Value = cmdCount > shotAfter;
        if (CmdShot.Value)
        {
            cmdMenel = true; cmdNegated = false;
            CmdText.Value = "MENEL MÓWI: SHOT!  wciśnij [F]";
            CmdEndsAt.Value = Now + shotSeconds;
        }
        else
        {
            int k = Random.Range(0, pool.Length);
            CmdKey.Value = (byte)k;
            float roll = Random.value;
            cmdMenel = roll < 0.75f;          // 25% pułapka bez prefiksu
            cmdNegated = cmdMenel && roll < 0.20f; // 20% "NIE klikaj"
            CmdText.Value = !cmdMenel ? $"Kliknij [{pool[k].name}]"
                : cmdNegated ? $"MENEL MÓWI: NIE klikaj [{pool[k].name}]"
                : $"MENEL MÓWI: kliknij [{pool[k].name}]";
            CmdEndsAt.Value = Now + answerSeconds;
        }
        CmdId.Value++;
        Debug.Log($"[Lucky] #{cmdCount}: {CmdText.Value} ({alive.Count} w grze)");
    }

    void Eliminate(ulong id, string why) // serwer
    {
        alive.Remove(id);
        eliminated.Add(id);
        AliveCount.Value = alive.Count;
        OutRpc(RpcTarget.Single(id, RpcTargetUse.Temp));
        Debug.Log($"[Lucky] {Olympics.Nick(id)} odpada ({why})");
    }

    [Rpc(SendTo.Server)]
    void PressRpc(int cmdId, RpcParams p = default)
    {
        if (State.Value != Phase.Running || cmdId != CmdId.Value || Now > CmdEndsAt.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!alive.Contains(id)) return;

        if (CmdShot.Value)
        { if (!shotTime.ContainsKey(id)) shotTime[id] = Now; return; }

        if (cmdMenel && !cmdNegated) pressedOk.Add(id);   // dobrze
        else if (!pressedOk.Contains(id))                  // pułapka
        { pressedOk.Add(id); Eliminate(id, "pułapka: " + CmdText.Value); }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void OutRpc(RpcParams p = default) => myOut = true;

    protected override void RunningTick()
    {
        if (cmdCount == 0 || Now < CmdEndsAt.Value)
        {
            if (alive.Count <= (racers.Count > 1 ? 1 : 0) && cmdCount > 0 && !CmdShot.Value)
                Finish(BuildRanking()); // (prawie) wszyscy odpadli przed SHOTEM
            return;
        }
        if (!judged)
        {
            judged = true;
            if (CmdShot.Value) { Finish(BuildRanking()); return; }
            if (cmdMenel && !cmdNegated) // kto nie kliknął — odpada
                foreach (var a in alive.Where(a => !pressedOk.Contains(a)).ToList())
                    Eliminate(a, "zaspał");
        }
        if (Now >= CmdEndsAt.Value + gapSeconds) NextCommand();
    }

    List<ulong> BuildRanking()
    {
        // żywi: najpierw wg refleksu przy SHOT, bez F na końcu żywych; potem odpadli od końca
        var top = alive.OrderBy(a => shotTime.GetValueOrDefault(a, double.MaxValue));
        return top.Concat(Enumerable.Reverse(eliminated)).ToList();
    }

    protected override List<ulong> TimeoutRanking() => BuildRanking();

    // ---- animacja: chwyć kieliszek ze stołu, wypij z odchyłem głowy, odstaw ----

    void StartDrinkAnim()
    {
        drinkAnimT = 0f;
        Sfx.Play("gulp");
        if (myGlass == null)
        {
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
            if (myGlass != null && drinkAnimT < 98f)
            { myGlass.SetPositionAndRotation(glassHome, Quaternion.identity); drinkAnimT = 99f; }
            return;
        }
        drinkAnimT += Time.deltaTime;
        var cam = OwnCamera();
        if (cam == null) return;
        float k = Mathf.Clamp01(drinkAnimT / dur);
        float tilt = Mathf.Sin(k * Mathf.PI) * 35f;
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
        if (State.Value != Phase.Running || myOut) return;
        int id = CmdId.Value;
        if (myLastCmd != id) { myLastCmd = id; myPressed = false; autoPlanned = false; }
        if (Now > CmdEndsAt.Value) return;

        var kb = Keyboard.current;
        if (kb != null && !myPressed)
        {
            if (CmdShot.Value && kb.fKey.wasPressedThisFrame)
            { myPressed = true; PressRpc(id); StartDrinkAnim(); }
            else if (!CmdShot.Value && kb[pool[CmdKey.Value].key].wasPressedThisFrame)
            { myPressed = true; PressRpc(id); }
        }

        if (autoMode)
        {
            string t = CmdText.Value.ToString();
            bool should = CmdShot.Value
                || (t.StartsWith("MENEL MÓWI") && !t.Contains("NIE klikaj"));
            if (Random.value < 0.0015f) should = !should; // rzadkie pomyłki — ktoś musi odpaść
            if (!autoPlanned) { autoPlanned = true; autoPressAt = Now + Random.Range(0.2f, 1.2f); }
            if (should && !myPressed && Now >= autoPressAt)
            { myPressed = true; PressRpc(id); if (CmdShot.Value) StartDrinkAnim(); }
        }
    }
    int myLastCmd;

    protected override void DrawGame()
    {
        GUI.Label(new Rect(0, Screen.height * 0.14f, Screen.width, 26),
            $"MENEL MÓWI  —  w grze: {AliveCount.Value}", Ui.S(20));
        if (myOut)
        {
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 60),
                "ODPADŁEŚ — oglądasz resztę", Ui.S(28));
            return;
        }
        if (Now > CmdEndsAt.Value) return; // przerwa między komendami

        // komenda pływa po alkoholu — trudniej czytać
        float d = LocalDrunk01();
        float ox = (Mathf.PerlinNoise(Time.time * 1.3f, 3f) - 0.5f) * 90f * d;
        float oy = (Mathf.PerlinNoise(7f, Time.time * 1.1f) - 0.5f) * 60f * d;
        GUI.Label(new Rect(ox, Screen.height * 0.28f + oy, Screen.width, 60),
            CmdText.Value.ToString(), Ui.S(CmdShot.Value ? 44 : 34));
        if (myPressed)
            GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, 30), "kliknięte!", Ui.S(18));

        // pasek czasu okna reakcji
        float left = Mathf.Clamp01((float)((CmdEndsAt.Value - Now)
            / (CmdShot.Value ? shotSeconds : answerSeconds)));
        var bar = new Rect(Screen.width * 0.3f, Screen.height * 0.52f, Screen.width * 0.4f, 10f);
        GUI.color = new Color(0f, 0f, 0f, 0.5f);
        GUI.DrawTexture(bar, Texture2D.whiteTexture);
        GUI.color = Color.Lerp(Color.red, Color.green, left);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * left, bar.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
