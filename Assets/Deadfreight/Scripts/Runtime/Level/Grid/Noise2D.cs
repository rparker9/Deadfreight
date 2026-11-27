// Scripts/Runtime/Level/Grid/Noise2D.cs
using UnityEngine;

namespace Deadfreight.Level
{
    /// <summary>
    /// Seeded gradient (Perlin-style) noise that can be read at any point, so it can be warped
    /// (read at moved coordinates) and layered. Seeded from a System.Random, so the same seed always
    /// gives the same noise, independent of UnityEngine.Random. Coordinates are in cells.
    /// </summary>
    public sealed class Noise2D
    {
        readonly int[] perm = new int[512];         // a shuffled 0-255, twice, so lookups never wrap
        readonly float[] gradX = new float[256];
        readonly float[] gradY = new float[256];

        public Noise2D(System.Random rng)
        {
            var order = new int[256];
            for (int i = 0; i < 256; i++) order[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (order[i], order[j]) = (order[j], order[i]);
            }
            for (int i = 0; i < 512; i++) perm[i] = order[i & 255];

            for (int i = 0; i < 256; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                gradX[i] = Mathf.Cos(angle);
                gradY[i] = Mathf.Sin(angle);
            }
        }

        /// <summary>One layer, with features about one unit apart. Roughly -1 to 1.</summary>
        public float Value(float x, float y)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;

            float n00 = Dot(x0, y0, fx, fy);
            float n10 = Dot(x0 + 1, y0, fx - 1f, fy);
            float n01 = Dot(x0, y0 + 1, fx, fy - 1f);
            float n11 = Dot(x0 + 1, y0 + 1, fx - 1f, fy - 1f);
            float u = Fade(fx), v = Fade(fy);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v) * 1.4f;
        }

        /// <summary>
        /// Layered noise: the first layer's features are <paramref name="scale"/> cells apart, each
        /// further layer half the size and half the strength. Roughly -1 to 1.
        /// </summary>
        public float Fbm(float x, float y, float scale, int octaves)
        {
            float total = 0f, amplitude = 1f, sum = 0f;
            for (int o = 0; o < octaves; o++)
            {
                total += Value(x / scale + o * 17.3f, y / scale + o * 9.1f) * amplitude;   // offsets keep layers unaligned
                sum += amplitude;
                amplitude *= 0.5f;
                scale *= 0.5f;
            }
            return total / sum;
        }

        /// <summary>
        /// Ridged noise: sharp crests where the plain noise crosses zero, with smaller ridges only on
        /// the crests of bigger ones. Roughly 0 to 1.
        /// </summary>
        public float Ridged(float x, float y, float scale, int octaves)
        {
            float total = 0f, amplitude = 1f, sum = 0f, weight = 1f;
            for (int o = 0; o < octaves; o++)
            {
                float crest = 1f - Mathf.Abs(Value(x / scale + o * 31.7f, y / scale + o * 5.3f));
                crest *= crest * weight;
                weight = Mathf.Clamp01(crest * 1.5f);
                total += crest * amplitude;
                sum += amplitude;
                amplitude *= 0.5f;
                scale *= 0.5f;
            }
            return total / sum;
        }

        float Dot(int ix, int iy, float dx, float dy)
        {
            int g = perm[perm[ix & 255] + (iy & 255)];
            return gradX[g] * dx + gradY[g] * dy;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
