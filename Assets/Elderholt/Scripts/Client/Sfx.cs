using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Sfx — procedural sound effects. No audio assets: every clip is synthesized
    //  from sines and noise on first play and cached, keeping the project's
    //  zero-setup philosophy (press Play, everything works). The settings volume
    //  slider drives AudioListener.volume above this.
    // ============================================================================
    public static class Sfx
    {
        const int Rate = 44100;
        static AudioSource source;
        static readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();
        static readonly System.Random noiseRng = new System.Random(1234);

        static AudioSource Source()
        {
            if (source == null)
            {
                GameObject go = new GameObject("Sfx");
                Object.DontDestroyOnLoad(go);
                source = go.AddComponent<AudioSource>();
                source.spatialBlend = 0f;
            }
            return source;
        }

        public static void Play(string name, float vol = 1f)
        {
            if (!Application.isPlaying) return;
            if (!cache.TryGetValue(name, out AudioClip clip))
            {
                clip = Build(name);
                cache[name] = clip;
            }
            if (clip != null) Source().PlayOneShot(clip, vol);
        }

        static AudioClip Build(string name)
        {
            switch (name)
            {
                // mining
                case "ore":      return Render(0.14f, t => Sine(660f + 440f * t, t) * Decay(t, 0.14f) * 0.5f);
                case "pristine": return Render(0.40f, t => (Sine(880f, t) + Sine(1320f, t) * 0.6f) * Decay(t, 0.40f) * 0.35f);
                case "fail":     return Render(0.18f, t => Noise() * Decay(t, 0.12f) * 0.25f);
                case "shore":    return Render(0.10f, t => Sine(210f, t) * Decay(t, 0.08f) * 0.7f);
                case "creak":    return Render(0.60f, t => Sine(110f + 30f * Sine(7f, t), t) * Decay(t, 0.55f) * 0.4f);
                case "cavein":   return Render(1.40f, t => Noise() * (1f - t / 1.4f) * (0.6f + 0.4f * Sine(19f, t)) * 0.6f);
                case "died":     return Render(0.90f, t => Sine(Mathf.Lerp(420f, 70f, t / 0.9f), t) * Decay(t, 0.90f) * 0.5f);
                case "descend":  return Render(0.25f, t => Sine(90f, t) * Decay(t, 0.20f) * 0.8f);
                // camp
                case "coin":     return Render(0.16f, t => (Sine(1250f, t) + Sine(1660f, t)) * Decay(t, 0.16f) * 0.3f);
                case "hammer":   return Render(0.12f, t => (Sine(1100f, t) * 0.7f + Noise() * 0.3f) * Decay(t, 0.06f) * 0.6f);
                case "quench":   return Render(0.50f, t => Noise() * Decay(t, 0.40f) * 0.3f);
                case "smelt":    return Render(0.35f, t => Noise() * (0.5f + 0.5f * Sine(3f, t)) * Decay(t, 0.35f) * 0.18f);
                // fanfare
                case "level":    return Render(0.45f, t => Sine(t < 0.2f ? 523f : 784f, t) * Decay(t % 0.2f, 0.20f) * 0.4f);
                case "victory":  return Render(1.60f, VictoryWave);
                default: return null;
            }
        }

        // A small rising arpeggio: C5 E5 G5 C6, the last note ringing out.
        static float VictoryWave(float t)
        {
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.50f };
            int step = Mathf.Min(3, (int)(t / 0.28f));
            float local = t - step * 0.28f;
            return Sine(notes[step], t) * Decay(local, step == 3 ? 0.7f : 0.26f) * 0.45f;
        }

        static AudioClip Render(float secs, System.Func<float, float> wave)
        {
            int n = Mathf.CeilToInt(secs * Rate);
            float[] data = new float[n];
            for (int i = 0; i < n; i++)
                data[i] = Mathf.Clamp(wave(i / (float)Rate), -1f, 1f);
            AudioClip clip = AudioClip.Create("sfx." + secs, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        static float Decay(float t, float len) => Mathf.Exp(-5f * t / Mathf.Max(0.01f, len));
        static float Noise() => (float)(noiseRng.NextDouble() * 2.0 - 1.0);
    }
}
