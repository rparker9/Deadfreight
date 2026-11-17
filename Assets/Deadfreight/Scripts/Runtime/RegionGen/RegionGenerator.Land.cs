// Scripts/Runtime/RegionGen/RegionGenerator.Land.cs
using UnityEngine;

// Stage 1: the land everything else is built on.
public sealed partial class RegionGenerator
{
    const int RiverMaxSteps = 800;
    const float PitWear = 0.002f;           // how much a river lowers a pit each step it is stuck
    const int MarshReach = 4;               // cells from water that can be marsh
    const int HollowBlurPasses = 8;
    const int HollowBlurStep = 3;           // cells between blur samples

    void BuildLand()
    {
        BuildHeightAndForest();
        BuildRivers();
        BuildSlope();
        BuildMarsh();
        BuildHollows();
    }

    /// <summary>Noise heightmap (0-1, flattened lowland), open water below sea level, forest patches.</summary>
    void BuildHeightAndForest()
    {
        var e = map.Elevation;
        var raw = RegionNoise.Fbm(width, height, settings.hillScale, 5, rng);
        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in raw)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        for (int i = 0; i < cellCount; i++)
            e[i] = Mathf.Pow((raw[i] - min) / Mathf.Max(1e-6f, max - min), settings.flatness);

        var forestNoise = RegionNoise.Fbm(width, height, settings.forestScale, 4, rng);
        for (int i = 0; i < cellCount; i++)
        {
            map.Forest[i] = forestNoise[i] > settings.forestThreshold;
            map.Water[i] = e[i] < settings.seaLevel;
        }
    }

    /// <summary>Rivers start at springs on high ground and step to the lowest neighbour until they reach water.</summary>
    void BuildRivers()
    {
        var e = map.Elevation;
        for (int r = 0; r < settings.rivers; r++)
        {
            int x = rng.Next(width), y = rng.Next(height);
            if (e[y * width + x] < settings.riverSourceHeight) continue;

            for (int step = 0; step < RiverMaxSteps; step++)
            {
                int i = y * width + x;
                map.River[i] = true;
                if (map.Water[i]) break;

                int bx = x, by = y;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                    if (e[ny * width + nx] < e[by * width + bx]) { bx = nx; by = ny; }
                }

                if (bx == x && by == y)     // a pit: wear it down and keep flowing
                {
                    e[i] -= PitWear;
                    continue;
                }
                x = bx;
                y = by;
            }
        }
    }

    /// <summary>Slope as metres of rise per metre, from the final heights.</summary>
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

    /// <summary>Marsh: low ground within a few cells of water or a river. Nothing grows on open water.</summary>
    void BuildMarsh()
    {
        var wet = new bool[cellCount];
        for (int i = 0; i < cellCount; i++) wet[i] = map.Water[i] || map.River[i];
        var nearWet = Dilate(wet, MarshReach);

        for (int i = 0; i < cellCount; i++)
        {
            map.Marsh[i] = nearWet[i] && !wet[i] && map.Elevation[i] < settings.seaLevel + settings.marshBand;
            if (wet[i]) map.Forest[i] = false;
        }
    }

    /// <summary>Hollows: ground noticeably lower than its smoothed surroundings. Fog collects here.</summary>
    void BuildHollows()
    {
        var e = map.Elevation;
        var blur = (float[])e.Clone();
        var tmp = new float[cellCount];
        int k = HollowBlurStep;

        for (int pass = 0; pass < HollowBlurPasses; pass++)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = blur[y * width + x]
                    + blur[Mathf.Clamp(y - k, 0, height - 1) * width + x] + blur[Mathf.Clamp(y + k, 0, height - 1) * width + x]
                    + blur[y * width + Mathf.Clamp(x - k, 0, width - 1)] + blur[y * width + Mathf.Clamp(x + k, 0, width - 1)];
                tmp[y * width + x] = sum / 5f;
            }
            (blur, tmp) = (tmp, blur);
        }

        for (int i = 0; i < cellCount; i++)
            map.Hollow[i] = blur[i] - e[i] > settings.hollowDepth && !map.Water[i] && !map.River[i];
    }
}
