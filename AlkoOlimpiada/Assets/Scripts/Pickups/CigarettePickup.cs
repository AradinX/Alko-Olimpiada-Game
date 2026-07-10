using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Papieros na hubie (GDD): [E] = zapalasz od razu, bez ekwipunku.
// Cena: upojenie rośnie NA STAŁE (podłoga paska). Bonus: "pewna ręka" —
// utrudnienia z alkoholu w NASTĘPNEJ konkurencji o połowę mniejsze (DrunkSystem.Handicap01).
public class CigarettePickup : NetworkBehaviour
{
    public float respawnSeconds = 45f;
    public float drunkCost = 8f;

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
    public void RequestSmokeRpc(RpcParams p = default)
    {
        if (!Available.Value) return;
        ulong id = p.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var c)
            || c.PlayerObject == null) return;
        if (Vector3.Distance(c.PlayerObject.transform.position, transform.position) > 3f) return;

        var ds = c.PlayerObject.GetComponent<DrunkSystem>();
        if (ds.Steady.Value) { ds.MsgOwner("Już kopcisz — najpierw zużyj pewną rękę"); return; }
        ds.Steady.Value = true;
        ds.AddPermanent(drunkCost); // papieros dokopuje na stałe (GDD: potęguje upojenie)
        ds.MsgOwner("Zapalone. Pewna ręka w następnej konkurencji (utrudnienia o połowę)");
        SmokeFxRpc();
        Debug.Log($"[Cig] {Olympics.Nick(id)} zapalił papierosa");
        Available.Value = false;
        StartCoroutine(Respawn());
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SmokeFxRpc() => Sfx.Play("puff", transform.position);

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        Available.Value = true;
    }
}
