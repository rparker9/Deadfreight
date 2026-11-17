// Scripts/Runtime/RegionGen/GridOps.cs
using System;
using UnityEngine;

/// <summary>
/// Whole-grid operations on the region's layers (index = y * width + x). Edges are clamped:
/// cells past the border read as the nearest border cell.
/// </summary>
public static class GridOps
{
    /// <summary>Gaussian blur with spread <paramref name="sigma"/> cells, done as a horizontal then a vertical pass.</summary>
    public static float[] Blur(float[] values, int width, int height, float sigma)
    {
        int radius = Mathf.CeilToInt(sigma * 3f);
        var kernel = new float[radius * 2 + 1];
        float sum = 0f;
        for (int k = -radius; k <= radius; k++)
        {
            kernel[k + radius] = Mathf.Exp(-k * k / (2f * sigma * sigma));
            sum += kernel[k + radius];
        }
        for (int k = 0; k < kernel.Length; k++) kernel[k] /= sum;

        var across = new float[values.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float v = 0f;
            for (int k = -radius; k <= radius; k++)
                v += values[y * width + Mathf.Clamp(x + k, 0, width - 1)] * kernel[k + radius];
            across[y * width + x] = v;
        }

        var result = new float[values.Length];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float v = 0f;
            for (int k = -radius; k <= radius; k++)
                v += across[Mathf.Clamp(y + k, 0, height - 1) * width + x] * kernel[k + radius];
            result[y * width + x] = v;
        }
        return result;
    }

    /// <summary>
    /// For every cell: the distance (in cells) to the nearest marked cell, and which cell that is
    /// (-1 if nothing is marked). Two sweeps hand each cell's nearest mark on to its neighbours;
    /// the result is very close to exact.
    /// </summary>
    public static void NearestMarked(bool[] marked, int width, int height, out float[] distance, out int[] nearest)
    {
        int n = width * height;
        var dist = new float[n];
        var near = new int[n];
        for (int i = 0; i < n; i++)
        {
            near[i] = marked[i] ? i : -1;
            dist[i] = marked[i] ? 0f : float.MaxValue;
        }

        // The forward sweep looks at neighbours already visited (left and above); the backward sweep mirrors it.
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            for (int k = 0; k < 4; k++) TakeNearer(dist, near, width, height, x, y, x + BackX[k], y + BackY[k]);

        for (int y = height - 1; y >= 0; y--)
        for (int x = width - 1; x >= 0; x--)
            for (int k = 0; k < 4; k++) TakeNearer(dist, near, width, height, x, y, x - BackX[k], y - BackY[k]);

        distance = dist;
        nearest = near;
    }

    static readonly int[] BackX = { -1, -1, 0, 1 };
    static readonly int[] BackY = { 0, -1, -1, -1 };

    /// <summary>If the neighbour's nearest mark is closer to this cell than this cell's own, adopt it.</summary>
    static void TakeNearer(float[] dist, int[] near, int width, int height, int x, int y, int nx, int ny)
    {
        if (nx < 0 || ny < 0 || nx >= width || ny >= height) return;
        int mark = near[ny * width + nx];
        if (mark < 0) return;

        float dx = mark % width - x, dy = mark / width - y;
        float d = Mathf.Sqrt(dx * dx + dy * dy);
        int i = y * width + x;
        if (d < dist[i])
        {
            dist[i] = d;
            near[i] = mark;
        }
    }

    /// <summary>Stretches values to exactly 0-1, in place.</summary>
    public static void Normalize(float[] values)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in values)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        float range = Mathf.Max(1e-6f, max - min);
        for (int i = 0; i < values.Length; i++) values[i] = (values[i] - min) / range;
    }

    /// <summary>The share (0-1) of values below <paramref name="threshold"/>.</summary>
    public static float ShareBelow(float[] values, float threshold)
    {
        int below = 0;
        foreach (float v in values)
            if (v < threshold) below++;
        return below / (float)values.Length;
    }

    /// <summary>
    /// Rescales heights to 0-1 so that <paramref name="share"/> of the cells still sit below
    /// <paramref name="seaLevel"/>: reshaping the land changes where water is, not how much.
    /// Returns how much height differences above the shoreline were stretched (below 1 = squashed).
    /// </summary>
    public static float KeepWaterShare(float[] heights, float seaLevel, float share)
    {
        var sorted = (float[])heights.Clone();
        Array.Sort(sorted);
        float low = sorted[0], high = sorted[^1];
        float shoreline = share > 0f ? sorted[Mathf.Clamp((int)(share * sorted.Length), 0, sorted.Length - 1)] : low;

        for (int i = 0; i < heights.Length; i++)
        {
            float h = heights[i];
            heights[i] = h < shoreline
                ? seaLevel * (h - low) / Mathf.Max(shoreline - low, 1e-6f)
                : seaLevel + (1f - seaLevel) * (h - shoreline) / Mathf.Max(high - shoreline, 1e-6f);
        }
        return (1f - seaLevel) / Mathf.Max(high - shoreline, 1e-6f);
    }
}
