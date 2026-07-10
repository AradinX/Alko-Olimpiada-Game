using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Półmisek z kurczakiem (GDD: przekąski): [E] = jesz od razu, upojenie spada o snackRelief
// (nigdy poniżej podłogi). Ratunek szybszy niż rzyganie i bez ryzyka przyłapania — ale
// półmisków jest mało i długo respawnują.
public class SnackPickup : NetworkBehaviour
{
    public float respawnSeconds = 60f;
    public float snackRelief = 10f;

    public NetworkVariable<bool> Available = new(true);

    public override void OnNetworkSpawn()
    {
        Available.OnValueChanged += (_, v) => SetActive(v);
        SetActive(Available.Value);
    }

    void SetActive(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
        GetComponent<Collider>().enabled = v;
    }

    [Rpc(SendTo.Server)]
    public void RequestEatRpc(RpcParams p = default)
    {
        if (!Available.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var c)
            || c.PlayerObject == null) return;
        if (Vector3.Distance(c.PlayerObject.transform.position, transform.position) > 3f) return;

        var ds = c.PlayerObject.GetComponent<DrunkSystem>();
        if (ds.PassedOut.Value) return;
        float before = ds.Drunk.Value;
        ds.Drunk.Value = Mathf.Max(ds.Floor.Value, ds.Drunk.Value - snackRelief);
        ds.MsgOwner(ds.Drunk.Value < before
            ? $"Kurczaczek wchodzi. Trzeźwiejesz (-{before - ds.Drunk.Value:0})"
            : "Kurczaczek wchodzi, ale podłogi już nie przebijesz");
        EatFxRpc();
        Debug.Log($"[Snack] {Olympics.Nick(id)} zjadł kurczaka ({before:0} -> {ds.Drunk.Value:0})");
        Available.Value = false;
        StartCoroutine(Respawn());
    }

    [Rpc(SendTo.ClientsAndHost)]
    void EatFxRpc() => Sfx.Play("gulp", transform.position);

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        Available.Value = true;
    }
}
