using System.Collections.Generic;
using UnityEngine;

// Syntetyzowane one-shoty (AudioClip.Create) — zero plików audio w projekcie.
// ponytail: sinus/szum + obwiednia wystarczą na prototyp; nagrania dojdą z art passem
public static class Sfx
{
    const int SR = 22050;
    static readonly Dictionary<string, AudioClip> cache = new();

    // 3D w świecie (słyszą gracze w pobliżu)
    public static void Play(string name, Vector3 pos, float vol = 1f)
    {
        var c = Get(name);
        if (c != null) AudioSource.PlayClipAtPoint(c, pos, vol);
    }

    // "2D" — przy uchu lokalnego gracza (UI: beepy, fanfary, własne akcje)
    public static void Play(string name, float vol = 1f)
    {
        var cam = Camera.main;
        if (cam != null) Play(name, cam.transform.position, vol);
    }

    static AudioClip Get(string name)
    {
        if (cache.TryGetValue(name, out var c)) return c;
        float[] d = name switch
        {
            "throw"   => Noise(0.14f, 6f),                                   // świst rzutu
            "bounce"  => Tone(170f, 0.09f, 40f),                             // stuk o blat
            "plop"    => Sweep(420f, 140f, 0.14f),                           // piłka w kubku
            "clank"   => Mix(Tone(1250f, 0.16f, 25f), Tone(1870f, 0.16f, 30f)), // brzdęk puszki
            "gulp"    => Sweep(320f, 110f, 0.12f),                           // łyk
            "vomit"   => Noise(0.55f, 4f),                                   // rzyganie
            "zgon"    => Tone(75f, 0.35f, 8f),                               // upadek
            "slap"    => Noise(0.05f, 60f),                                  // cucenie
            "beep"    => Tone(880f, 0.09f, 20f),                             // odliczanie
            "go"      => Tone(1320f, 0.25f, 8f),                             // start / SHOT
            "fanfare" => Seq(Tone(523f, 0.14f, 6f), Tone(659f, 0.14f, 6f),
                             Tone(784f, 0.3f, 5f)),                          // medale
            _ => null
        };
        if (d == null) return null;
        c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        cache[name] = c;
        return c;
    }

    static float[] Tone(float hz, float dur, float decay)
    {
        var d = new float[(int)(SR * dur)];
        for (int i = 0; i < d.Length; i++)
        {
            float t = (float)i / SR;
            d[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * Mathf.Exp(-decay * t) * 0.5f;
        }
        return d;
    }

    static float[] Sweep(float f0, float f1, float dur)
    {
        var d = new float[(int)(SR * dur)];
        float phase = 0f;
        for (int i = 0; i < d.Length; i++)
        {
            float u = (float)i / d.Length;
            phase += 2f * Mathf.PI * Mathf.Lerp(f0, f1, u) / SR;
            d[i] = Mathf.Sin(phase) * (1f - u) * 0.5f;
        }
        return d;
    }

    static float[] Noise(float dur, float decay)
    {
        var d = new float[(int)(SR * dur)];
        for (int i = 0; i < d.Length; i++)
            d[i] = (Random.value * 2f - 1f) * Mathf.Exp(-decay * (float)i / SR) * 0.4f;
        return d;
    }

    static float[] Mix(float[] a, float[] b)
    {
        var d = new float[Mathf.Max(a.Length, b.Length)];
        for (int i = 0; i < d.Length; i++)
            d[i] = ((i < a.Length ? a[i] : 0f) + (i < b.Length ? b[i] : 0f)) * 0.7f;
        return d;
    }

    static float[] Seq(params float[][] parts)
    {
        int len = 0;
        foreach (var p in parts) len += p.Length;
        var d = new float[len];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(d, o); o += p.Length; }
        return d;
    }
}
