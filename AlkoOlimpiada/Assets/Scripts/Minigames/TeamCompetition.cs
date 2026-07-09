using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

// Baza konkurencji drużynowych (Flanki, Beer Pong): podział na 2 drużyny (parzysty/
// nieparzysty index), naprzemienne tury rzutów i kółko timingowe z GDD —
// wskazówka kręci się tym szybciej, im bardziej pijany jest rzucający.
public abstract class TeamCompetition : Competition
{
    public float wheelBaseSpeed = 140f;  // stopnie/s
    public float wheelDrunkBonus = 1.2f; // mnożnik przy pełnym upojeniu
    public float arcHalf = 22f;          // połowa zielonego pola
    public float turnSeconds = 8f;       // limit czasu na rzut

    public NetworkVariable<ulong> TurnPlayer = new();
    public NetworkVariable<double> TurnStart = new();
    public NetworkVariable<float> WheelSpeed = new();
    public NetworkVariable<float> ArcCenter = new();

    protected int myTeam = -1; // klient (z TeamsRpc)
    int turnCounter;           // serwer

    protected int TeamOf(ulong id) => racers.IndexOf(id) % 2;
    protected List<ulong> Team(int t) => racers.Where(r => TeamOf(r) == t).ToList();

    // drużyny naprzeciwko siebie
    protected override void GetPose(int index, int count, out Vector3 pos, out float yaw)
    {
        int team = index % 2, slot = index / 2;
        float x = slot * 1.6f - ((count - 1) / 2) * 0.8f;
        pos = new Vector3(x, 0.1f, team == 0 ? -6f : 6f);
        yaw = team == 0 ? 0f : 180f;
    }

    [Rpc(SendTo.ClientsAndHost)]
    protected void TeamsRpc(ulong[] teamA) =>
        myTeam = teamA.Contains(NM.LocalClientId) ? 0 : 1;

    protected void NextTurn() // serwer
    {
        turnCounter++;
        int team = turnCounter % 2;
        var members = Team(team);
        if (members.Count == 0) members = Team(1 - team); // testy solo
        if (members.Count == 0) return;
        ulong p = members[(turnCounter / 2) % members.Count];
        TurnPlayer.Value = p;
        TurnStart.Value = Now;
        WheelSpeed.Value = wheelBaseSpeed * (1f + wheelDrunkBonus * GetDrunk01(p));
        ArcCenter.Value = Random.Range(0f, 360f);
    }

    protected float GetDrunk01(ulong id) =>
        NM.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<DrunkSystem>().Drunk.Value / 100f : 0f;

    protected DrunkSystem DrunkOf(ulong id) =>
        NM.ConnectedClients.TryGetValue(id, out var c) && c.PlayerObject != null
            ? c.PlayerObject.GetComponent<DrunkSystem>() : null;

    protected float WheelAngle() => (float)((Now - TurnStart.Value) * WheelSpeed.Value % 360.0);

    // serwer liczy kąt w momencie odbioru RPC — lag przesuwa wskazówkę, stąd szeroki łuk
    protected bool WheelHit() =>
        Mathf.Abs(Mathf.DeltaAngle(WheelAngle(), ArcCenter.Value)) <= arcHalf;

    protected void DrawWheel(Vector2 center, float radius)
    {
        for (float a = -arcHalf; a <= arcHalf; a += 5f)
            DrawSpoke(center, radius, ArcCenter.Value + a, 16f, new Color(0.2f, 1f, 0.3f, 0.9f), 7f);
        for (int i = 0; i < 36; i++)
            DrawSpoke(center, radius, i * 10f, 5f, new Color(1f, 1f, 1f, 0.25f), 2f);
        DrawSpoke(center, radius * 0.15f, WheelAngle(), radius * 0.8f, Color.white, 4f);
    }

    static void DrawSpoke(Vector2 c, float r, float angle, float len, Color col, float w)
    {
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, c);
        GUI.color = col;
        GUI.DrawTexture(new Rect(c.x - w / 2f, c.y - r - len, w, len), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.matrix = m;
    }
}
