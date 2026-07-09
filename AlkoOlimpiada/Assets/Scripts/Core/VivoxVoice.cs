using System;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;

public class VivoxVoice : MonoBehaviour
{
    const string ChannelName = "AlkoOlimpiada";
    bool ready;
    bool inChannel;

    async void Start()
    {
        try
        {
            await Ugs.EnsureSignedIn();
            await VivoxService.Instance.InitializeAsync();
            ready = true;
        }
        catch (Exception e)
        {
            // bez podpiętego projektu Unity Cloud (Project Settings > Services) głos nie ruszy
            Debug.LogWarning($"[Vivox] Voice chat wylaczony: {e.Message}");
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnDisconnected;
    }

    async void OnConnected(ulong id)
    {
        if (!ready || inChannel || id != NetworkManager.Singleton.LocalClientId) return;
        try
        {
            await VivoxService.Instance.LoginAsync(new LoginOptions
            {
                DisplayName = ConnectionUI.LocalNickname
            });
            await VivoxService.Instance.JoinGroupChannelAsync(ChannelName, ChatCapability.AudioOnly);
            inChannel = true;
            Debug.Log("[Vivox] Dolaczono do kanalu glosowego");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vivox] Nie udalo sie dolaczyc do kanalu: {e.Message}");
        }
    }

    async void OnDisconnected(ulong id)
    {
        if (!inChannel || id != NetworkManager.Singleton.LocalClientId) return;
        inChannel = false;
        try
        {
            await VivoxService.Instance.LeaveAllChannelsAsync();
            await VivoxService.Instance.LogoutAsync();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Vivox] Blad przy wylogowaniu: {e.Message}");
        }
    }
}
