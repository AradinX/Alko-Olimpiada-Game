using UnityEngine;

// Szum fal na hubie (GOAL stretch): zapętlony filtrowany szum generowany w kodzie,
// głośność faluje sinusem jak przybój. Czysto lokalne, 2D.
public class HubAmbience : MonoBehaviour
{
    AudioSource src;

    void Start()
    {
        const int sr = 22050, seconds = 6;
        var d = new float[sr * seconds];
        float lp = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            // tani low-pass na białym szumie = szum morza
            lp = Mathf.Lerp(lp, Random.value * 2f - 1f, 0.045f);
            float t = (float)i / sr;
            float surf = 0.6f + 0.4f * Mathf.Sin(t / seconds * 2f * Mathf.PI); // 1 fala na pętlę
            d[i] = lp * surf;
        }
        var clip = AudioClip.Create("waves", d.Length, 1, sr, false);
        clip.SetData(d, 0);
        src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = true;
        src.spatialBlend = 0f;
        src.Play();
    }

    void Update() => src.volume = 0.16f * GameSettings.SfxVol;
}
