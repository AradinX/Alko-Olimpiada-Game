using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Butelka piwa na hubie: [E] podnosi do ekwipunku. Część butelek jest SPECJALNA
// (złota, ~25%): pijesz od razu — x2 punkty w następnej konkurencji + losowa klątwa.
// [Q] z pigułką dosypuje — stan niereplikowany, ofiara nic nie widzi (pkt 5).
public class BeerPickup : NetworkBehaviour
{
    public float respawnSeconds = 20f;
    public float specialChance = 0.25f;
    public bool respawns = true; // false = wyrzucona butelka, po podniesieniu znika na dobre

    public NetworkVariable<bool> Available = new(true);
    public NetworkVariable<bool> Special = new();

    bool spiked; // tylko serwer — celowo bez replikacji

    public void SetSpiked(bool v) => spiked = v; // wyrzucone piwo zachowuje pigułkę

    public override void OnNetworkSpawn()
    {
        Available.OnValueChanged += (_, v) => SetActive(v);
        if (IsServer) Special.Value = Random.value < specialChance;
        SetActive(Available.Value);
    }

    // specjalne i zatrute wyglądają jak zwykłe — specjalne zdradza dopiero hint przy butelce
    void SetActive(bool v)
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
        GetComponent<Collider>().enabled = v;
    }

    [Rpc(SendTo.Server)]
    public void RequestPickupRpc(RpcParams rpcParams = default)
    {
        if (!Available.Value) return;
        ulong id = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client)) return;
        var player = client.PlayerObject;
        if (player == null ||
            Vector3.Distance(player.transform.position, transform.position) > 3f) return;

        var ds = player.GetComponent<DrunkSystem>();
        if (ds.Beers.Value >= ds.maxBeers)
        {
            ds.MsgOwner("Masz już piwo w ręce — wypij [F] albo wyrzuć [G]");
            return; // butelka zostaje
        }
        ds.PickUpBeer(spiked, Special.Value);
        Debug.Log($"[Beer] {Olympics.Nick(id)} podniósł " +
            (Special.Value ? "piwo SPECJALNE" : "piwo"));

        spiked = false;
        Available.Value = false;
        if (respawns) StartCoroutine(Respawn());
        else NetworkObject.Despawn(); // niszczy też u klientów
    }

    [Rpc(SendTo.Server)]
    public void SpikeRpc(RpcParams p = default)
    {
        if (!Available.Value || spiked) return;
        ulong id = p.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var c)
            || c.PlayerObject == null) return;
        var ds = c.PlayerObject.GetComponent<DrunkSystem>();
        if (ds.Pills.Value <= 0) return;
        if (Vector3.Distance(c.PlayerObject.transform.position, transform.position) > 3f) return;

        ds.Pills.Value--;
        spiked = true;
        ds.MsgOwner("Dosypane. Nikt nic nie widział...");
        Debug.Log($"[Beer] {Olympics.Nick(id)} dosypał pigułkę do piwa");
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        Special.Value = Random.value < specialChance; // nowy rzut przy respawnie
        Available.Value = true;
    }
}
