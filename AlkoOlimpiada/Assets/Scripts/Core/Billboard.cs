using UnityEngine;

// Obraca obiekt (etykiety stanowisk) przodem do aktywnej kamery.
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null)
            transform.rotation =
                Quaternion.LookRotation(transform.position - cam.transform.position);
    }
}
