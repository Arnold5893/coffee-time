using UnityEngine;

namespace CoffeeShop.Audio
{
    /// <summary>
    /// 程序化占位音效：不依赖任何音频资产，运行时合成短音。
    /// 12 票接正式音效时把这些调用换成 AudioClip 资产即可，调用点不用动。
    /// </summary>
    public static class ProceduralSfx
    {
        const int Rate = 44100;

        /// <summary>店门被推开时的门铃："叮—咚"两声，E6 → A5，指数衰减。</summary>
        public static AudioClip DoorBell()
        {
            var freqs = new[] { 1318.5f, 880.0f };
            var starts = new[] { 0.0f, 0.34f };
            const float dur = 1.30f;
            int n = Mathf.CeilToInt(dur * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    float tt = t - starts[k];
                    if (tt < 0f) continue;
                    float env = Mathf.Exp(-tt * 7.0f);
                    float f = freqs[k];
                    s += env * (0.60f * Mathf.Sin(2f * Mathf.PI * f * tt)
                              + 0.24f * Mathf.Sin(2f * Mathf.PI * f * 2.01f * tt)
                              + 0.10f * Mathf.Sin(2f * Mathf.PI * f * 3.02f * tt));
                }
                // 首尾各淡入淡出 8ms，避免爆音
                float fade = Mathf.Clamp01(t / 0.008f) * Mathf.Clamp01((dur - t) / 0.008f);
                data[i] = Mathf.Clamp(s * 0.32f * fade, -1f, 1f);
            }
            var clip = AudioClip.Create("DoorBell", n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
