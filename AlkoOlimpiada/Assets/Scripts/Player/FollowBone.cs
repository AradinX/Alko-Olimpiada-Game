using UnityEngine;

// Dokleja obiekt do kości szkieletu (pozycja+rotacja co klatkę, PO animacji).
// Zamiast parentowania pod kość — kości AccuRig mają nieuniform skalę, która
// rozpłaszczałaby dziecko. Offsety w przestrzeni kości ustawia bootstrap.
public class FollowBone : MonoBehaviour
{
    public Transform bone;
    public Vector3 posOffset;
    public Quaternion rotOffset = Quaternion.identity;

    void LateUpdate()
    {
        if (bone == null) return;
        transform.SetPositionAndRotation(
            bone.position + bone.rotation * posOffset,
            bone.rotation * rotOffset);
    }
}
