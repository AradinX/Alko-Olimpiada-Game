using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public enum SpecialBeer : byte
{
    None,
    DoublePoints,
    Spartan,
    Nike,
    Nemesis,
    Shield,
    Tyche
}

// Butelka piwa na hubie. Przy każdym powrocie dokładnie dwa niezatrute sloty
// dostają losowy, unikalny typ specjalny. Zatruty slot zachowuje stan między scenami.
public class BeerPickup : NetworkBehaviour
{
    public float respawnSeconds = 20f;
    public int specialsPerBreak = 2;
    public bool respawns = true;

    public NetworkVariable<bool> Available = new(true);
    public NetworkVariable<SpecialBeer> Special = new();

    static readonly List<BeerPickup> hubSlots = new();
    static readonly Dictionary<string, SpecialBeer> savedSpiked = new();
    static ulong hubSceneHandle = ulong.MaxValue;
    static bool rollScheduled;

    bool spiked;

    string SlotKey => $"{Mathf.RoundToInt(transform.position.x * 100)}:"
        + $"{Mathf.RoundToInt(transform.position.y * 100)}:"
        + Mathf.RoundToInt(transform.position.z * 100);

    public static string SpecialName(SpecialBeer type) => type switch
    {
        SpecialBeer.DoublePoints => "ZŁOTE x2",
        SpecialBeer.Spartan => "SPARTAŃSKIE",
        SpecialBeer.Nike => "NIKE",
        SpecialBeer.Nemesis => "NEMEZIS",
        SpecialBeer.Shield => "TARCZA ATENY",
        SpecialBeer.Tyche => "TYCHE",
        _ => "ZWYKŁE"
    };

    public void SetSpiked(bool value)
    {
        spiked = value;
        if (!respawns) return;
        if (value) savedSpiked[SlotKey] = Special.Value;
        else savedSpiked.Remove(SlotKey);
    }

    public override void OnNetworkSpawn()
    {
        Available.OnValueChanged += (_, value) => SetActive(value);
        if (IsServer && respawns && gameObject.scene.name == "Hub") RegisterHubSlot();
        SetActive(Available.Value);
    }

    void RegisterHubSlot()
    {
        ulong sceneHandle = gameObject.scene.handle.GetRawData();
        if (hubSceneHandle != sceneHandle)
        {
            hubSceneHandle = sceneHandle;
            hubSlots.Clear();
            rollScheduled = false;
        }

        hubSlots.Add(this);
        Available.Value = true;
        if (savedSpiked.TryGetValue(SlotKey, out var saved))
        {
            spiked = true;
            Special.Value = saved;
        }
        else Special.Value = SpecialBeer.None;

        if (!rollScheduled)
        {
            rollScheduled = true;
            StartCoroutine(RollHubSpecials());
        }
    }

    IEnumerator RollHubSpecials()
    {
        yield return null;

        var candidates = new List<BeerPickup>();
        int alreadySpecial = 0;
        foreach (var slot in hubSlots)
        {
            if (slot == null) continue;
            if (savedSpiked.ContainsKey(slot.SlotKey))
            {
                if (slot.Special.Value != SpecialBeer.None) alreadySpecial++;
            }
            else
            {
                slot.Special.Value = SpecialBeer.None;
                candidates.Add(slot);
            }
        }

        Shuffle(candidates);
        var types = new List<SpecialBeer>
        {
            SpecialBeer.DoublePoints, SpecialBeer.Spartan, SpecialBeer.Nike,
            SpecialBeer.Nemesis, SpecialBeer.Shield, SpecialBeer.Tyche
        };
        Shuffle(types);

        int count = Mathf.Min(Mathf.Max(0, specialsPerBreak - alreadySpecial), candidates.Count, types.Count);
        for (int i = 0; i < count; i++) candidates[i].Special.Value = types[i];
        Debug.Log($"[Beer] Hub: {count + alreadySpecial} piwa specjalne, {savedSpiked.Count} zatrute sloty");
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    void SetActive(bool value)
    {
        foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = value;
        GetComponent<Collider>().enabled = value;
    }

    [Rpc(SendTo.Server)]
    public void RequestPickupRpc(RpcParams rpcParams = default)
    {
        if (!Available.Value) return;
        ulong id = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client)) return;
        var player = client.PlayerObject;
        if (player == null || Vector3.Distance(player.transform.position, transform.position) > 3f) return;

        var drunk = player.GetComponent<DrunkSystem>();
        if (drunk.Beers.Value >= drunk.maxBeers)
        {
            drunk.MsgOwner("Masz już piwo w ręce — wypij [F] albo wyrzuć [G]");
            return;
        }

        drunk.PickUpBeer(spiked, Special.Value);
        Debug.Log($"[Beer] {Olympics.Nick(id)} podniósł {SpecialName(Special.Value)}"
            + (spiked ? " z pigułką" : ""));

        SetSpiked(false);
        Special.Value = SpecialBeer.None;
        Available.Value = false;
        if (respawns) StartCoroutine(Respawn());
        else NetworkObject.Despawn();
    }

    [Rpc(SendTo.Server)]
    public void SpikeRpc(RpcParams rpcParams = default)
    {
        if (!Available.Value || spiked) return;
        ulong id = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client)
            || client.PlayerObject == null) return;
        var drunk = client.PlayerObject.GetComponent<DrunkSystem>();
        if (drunk.Pills.Value <= 0) return;
        if (Vector3.Distance(client.PlayerObject.transform.position, transform.position) > 3f) return;

        drunk.Pills.Value--;
        SetSpiked(true);
        drunk.MsgOwner("Dosypane. Nikt nic nie widział...");
        Debug.Log($"[Beer] {Olympics.Nick(id)} dosypał pigułkę do piwa");
    }

    IEnumerator Respawn()
    {
        yield return new WaitForSeconds(respawnSeconds);
        Special.Value = SpecialBeer.None;
        Available.Value = true;
    }
}
