using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Pigułka na hubie: [E] podnosi do ekwipunku; [Q] przy butelce dosypuje (BeerPickup).
public class PillPickup : NetworkBehaviour
{
    public float respawnSeconds = 30f;

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
    public void RequestPickupRpc(RpcParams p = default)
    {
        if (!Available.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var c)
            || c.PlayerObject == null) return;
        if (Vector3.Distance(c.PlayerObject.transform.position, transform.position) > 3f) return;

        c.PlayerObject.GetComponent<DrunkSystem>().Pills.Value++;
        Debug.Log($"[Pill] {Olympics.Nick(id)} podniósł pigułkę");
        Available.Value = false;
        StartCoroutine(Respawn());
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        Available.Value = true;
    }
}
