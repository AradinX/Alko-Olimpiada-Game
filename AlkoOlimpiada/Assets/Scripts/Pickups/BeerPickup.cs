using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Butelka piwa na hubie: [E] w pobliżu podnosi do ekwipunku (DrunkSystem.Beers),
// serwer waliduje dystans i dostępność, po chwili butelka wraca.
public class BeerPickup : NetworkBehaviour
{
    public float respawnSeconds = 20f;

    public NetworkVariable<bool> Available = new(true);

    public override void OnNetworkSpawn()
    {
        Available.OnValueChanged += (_, v) => SetActive(v);
        SetActive(Available.Value);
    }

    void SetActive(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = v;
        GetComponent<Collider>().enabled = v;
    }

    [Rpc(SendTo.Server)]
    public void RequestPickupRpc(RpcParams rpcParams = default)
    {
        if (!Available.Value) return;
        if (!NetworkManager.ConnectedClients.TryGetValue(
                rpcParams.Receive.SenderClientId, out var client)) return;
        var player = client.PlayerObject;
        if (player == null ||
            Vector3.Distance(player.transform.position, transform.position) > 3f) return;

        player.GetComponent<DrunkSystem>().Beers.Value++;
        Debug.Log($"[Beer] {Olympics.Nick(rpcParams.Receive.SenderClientId)} podniósł piwo");
        Available.Value = false;
        StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        Available.Value = true;
    }
}
