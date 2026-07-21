using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Dwa losowe sloty papierosów są aktywne podczas każdej przerwy w hubie.
public class CigarettePickup : NetworkBehaviour
{
    public int availablePerBreak = 2;
    public float drunkCost = 8f;
    public NetworkVariable<bool> Available = new(true);

    static readonly List<CigarettePickup> hubSlots = new();
    static ulong hubSceneHandle = ulong.MaxValue;
    static bool rollScheduled;

    public override void OnNetworkSpawn()
    {
        Available.OnValueChanged += (_, value) => SetActive(value);
        if (IsServer) RegisterHubSlot();
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
        Available.Value = false;
        if (!rollScheduled)
        {
            rollScheduled = true;
            StartCoroutine(ActivateSlots());
        }
    }

    IEnumerator ActivateSlots()
    {
        yield return null;
        Shuffle(hubSlots);
        for (int i = 0; i < hubSlots.Count; i++)
            if (hubSlots[i] != null) hubSlots[i].Available.Value = i < availablePerBreak;
        Debug.Log($"[Cig] Hub: {Mathf.Min(availablePerBreak, hubSlots.Count)} aktywne papierosy");
    }

    static void Shuffle(List<CigarettePickup> list)
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
    public void RequestSmokeRpc(RpcParams rpcParams = default)
    {
        if (!Available.Value) return;
        ulong id = rpcParams.Receive.SenderClientId;
        if (!NetworkManager.ConnectedClients.TryGetValue(id, out var client)
            || client.PlayerObject == null) return;
        if (Vector3.Distance(client.PlayerObject.transform.position, transform.position) > 3f) return;

        var drunk = client.PlayerObject.GetComponent<DrunkSystem>();
        if (drunk.Steady.Value)
        {
            drunk.MsgOwner("Już kopcisz — najpierw zużyj pewną rękę");
            return;
        }

        drunk.Steady.Value = true;
        drunk.AddPermanent(drunkCost);
        drunk.MsgOwner("Zapalone. Pewna ręka w następnej konkurencji (utrudnienia o połowę)");
        SmokeFxRpc();
        Debug.Log($"[Cig] {Olympics.Nick(id)} zapalił papierosa");
        Available.Value = false;
    }

    [Rpc(SendTo.ClientsAndHost)]
    void SmokeFxRpc() => Sfx.Play("puff", transform.position);
}
