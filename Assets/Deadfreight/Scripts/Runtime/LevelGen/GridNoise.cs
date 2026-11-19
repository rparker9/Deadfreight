// Scripts/Runtime/LevelGen/GridNoise.cs
using UnityEngine;

/// <summary>
/// Seeded gradient (Perlin-style) noise on a grid. Uses its own System.Random so the same
/// seed always gives the same land, independent of UnityEngine.Random.
/// </summary>
public static class GridNoise
{
    /// <summary>Layered noise: each octave half the size and half the strength. Roughly -1..1.</summary>
    public static float[] Fbm(int w, int h, float scale, int octaves, System.Random rng)
    {
        var total = new float[w * h];
        float amp = 1f, norm = 0f;
        for (int o = 0; o < octaves; o++)
        {
            var layer = Perlin(w, h, scale, rng);
            for (int i = 0; i < total.Length; i++) total[i] += layer[i] * amp;
            norm += amp;
            amp *= 0.5f;
            scale *= 0.5f;
        }
        for (int i = 0; i < total.Length; i++) total[i] /= norm;
        return total;
    }

    /// <summary>One layer of gradient noise; `scale` is cells per noise period.</summary>
    public static float[] Perlin(int w, int h, float scale, System.Random rng)
    {
        scale = Mathf.Max(1f, scale);
        int gw = (int)(w / scale) + 2, gh = (int)(h / scale) + 2;
        var gx = new float[gw * gh];
        var gy = new float[gw * gh];
        for (int i = 0; i < gx.Length; i++)
        {
            float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            gx[i] = Mathf.Cos(a);
            gy[i] = Mathf.Sin(a);
        }

        var result = new float[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float sx = x / scale, sy = y / scale;
                int x0 = (int)sx, y0 = (int)sy;
                float fx = sx - x0, fy = sy - y0;

                float Dot(int ix, int iy, float dx, float dy)
                {
                    int g = iy * gw + ix;
                    return gx[g] * dx + gy[g] * dy;
                }

                float n00 = Dot(x0, y0, fx, fy);
                float n10 = Dot(x0 + 1, y0, fx - 1f, fy);
                float n01 = Dot(x0, y0 + 1, fx, fy - 1f);
                float n11 = Dot(x0 + 1, y0 + 1, fx - 1f, fy - 1f);
                float u = Fade(fx), v = Fade(fy);
                result[y * w + x] = Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
            }
        }
        return result;
    }

    static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
}
