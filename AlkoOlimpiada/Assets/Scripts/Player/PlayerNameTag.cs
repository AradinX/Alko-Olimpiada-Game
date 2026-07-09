using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class PlayerNameTag : NetworkBehaviour
{
    public TMP_Text label;

    public NetworkVariable<FixedString64Bytes> Nickname = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            Nickname.Value = ConnectionUI.LocalNickname;
            label.gameObject.SetActive(false); // własny nick nie zasłania widoku
            return;
        }
        Nickname.OnValueChanged += (_, v) => label.text = v.ToString();
        label.text = Nickname.Value.ToString();
    }

    void LateUpdate()
    {
        if (IsOwner) return;
        var cam = Camera.main;
        if (cam == null) return;
        // billboard w stronę lokalnej kamery
        label.transform.rotation =
            Quaternion.LookRotation(label.transform.position - cam.transform.position);
    }
}
