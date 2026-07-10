using Unity.Netcode;
using UnityEngine;

// Klockowa postać (placeholder do art passu): proceduralny chód — nogi i ręce
// machają w przeciwfazie proporcjonalnie do obserwowanej prędkości. Działa też
// dla postaci zdalnych bez sieciowego animatora, bo NetworkTransform je przesuwa.
public class PlayerLimbs : NetworkBehaviour
{
    public float stepPerMeter = 5f; // fazy chodu na metr drogi
    public float swingDeg = 45f;    // maksymalny wymach kończyn

    Transform armL, armR, legL, legR;
    Vector3 lastPos;
    float phase, amp;

    public override void OnNetworkSpawn()
    {
        var b = transform.Find("Body");
        armL = b.Find("ArmL"); armR = b.Find("ArmR");
        legL = b.Find("LegL"); legR = b.Find("LegR");
        lastPos = transform.position;

        // kolor koszulki z id gracza — rozpoznawalność w greyboxie
        var torso = b.Find("Torso");
        if (torso != null)
            torso.GetComponent<Renderer>().material.color =
                Color.HSVToRGB(OwnerClientId * 0.618f % 1f, 0.65f, 0.9f);
    }

    void Update()
    {
        Vector3 d = transform.position - lastPos;
        lastPos = transform.position;
        d.y = 0f;
        float speed = d.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
        amp = Mathf.Lerp(amp, Mathf.Clamp01(speed / 4f), 10f * Time.deltaTime);
        phase += d.magnitude * stepPerMeter;
        float a = Mathf.Sin(phase) * swingDeg * amp;
        if (legL) legL.localRotation = Quaternion.Euler(a, 0f, 0f);
        if (legR) legR.localRotation = Quaternion.Euler(-a, 0f, 0f);
        if (armL) armL.localRotation = Quaternion.Euler(-a * 0.8f, 0f, 0f);
        if (armR) armR.localRotation = Quaternion.Euler(a * 0.8f, 0f, 0f);
    }
}
