// Scripts/Runtime/LevelGen/LevelGenerator.Land.cs
using UnityEngine;

public sealed partial class LevelGenerator
{
    const int HillOctaves = 5;

    void BuildLand()
    {
        BuildBaseHeight();
        BuildSlope();
    }

    /// <summary>
    /// Rolling noise heights, 0-1, with the lowland flattened by the flatness power.</summary>
    void BuildBaseHeight()
    {
        var e = map.Elevation;
        var raw = GridNoise.Fbm(width, height, settings.hillScale, HillOctaves, rng);
        GridOps.Normalize(raw);
        for (int i = 0; i < cellCount; i++) e[i] = Mathf.Pow(raw[i], settings.flatness);
    }

    /// <summary>
    /// Rise over run at each cell, from the heights either side.</summary>
    void BuildSlope()
    {
        var e = map.Elevation;
        float span = 2f * settings.cellSize;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = (e[y * width + Mathf.Min(x + 1, width - 1)] - e[y * width + Mathf.Max(x - 1, 0)]) * settings.relief;
            float dy = (e[Mathf.Min(y + 1, height - 1) * width + x] - e[Mathf.Max(y - 1, 0) * width + x]) * settings.relief;
            map.Slope[y * width + x] = Mathf.Sqrt(dx * dx + dy * dy) / span;
        }
    }
}
