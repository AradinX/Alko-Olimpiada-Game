using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

// Stanowisko konkurencji na hubie: [R] w pobliżu = głos na tę konkurencję (VoteManager).
public class CompetitionStation : NetworkBehaviour
{
    public string title;
    public string sceneName;
    public string autoFlag; // flaga CLI smoke-testu (np. -autosprint)
    public float radius = 5f;

    bool autoSent;
    double autoAt;

    public override void OnNetworkSpawn() => autoAt = Time.timeAsDouble + 10;

    void Update()
    {
        var vm = VoteManager.Instance;
        if (!IsSpawned || !NetworkManager.IsClient || vm == null) return;
        if (!vm.VotingOpen || vm.IsPlayed(sceneName)) return;

        var kb = Keyboard.current;
        if (Near() && kb != null && kb.rKey.wasPressedThisFrame)
            vm.VoteRpc(sceneName);
        if (!autoSent && !string.IsNullOrEmpty(autoFlag)
            && Time.timeAsDouble >= autoAt
            && Array.IndexOf(Environment.GetCommandLineArgs(), autoFlag) >= 0)
        { autoSent = true; vm.VoteRpc(sceneName); }
    }

    bool Near()
    {
        var po = NetworkManager.LocalClient?.PlayerObject;
        return po != null &&
            Vector3.Distance(po.transform.position, transform.position) <= radius;
    }

    void OnGUI()
    {
        var vm = VoteManager.Instance;
        if (!IsSpawned || !NetworkManager.IsClient || vm == null || !Near()) return;
        if (vm.IsPlayed(sceneName))
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40),
                $"{title} — ROZEGRANA", Ui.S(28));
        else if (vm.VotingOpen)
            GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 40),
                $"[R] Głosuj: {title}", Ui.S(28));
    }
}
