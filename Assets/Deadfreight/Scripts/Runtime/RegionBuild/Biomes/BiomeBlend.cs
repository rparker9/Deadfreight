// Scripts/Runtime/RegionBuild/Biomes/BiomeBlend.cs
using UnityEngine;

/// <summary>
/// Which of the plan's biomes are at a world point, and how much of each. Each 50 m plan cell has
/// one biome (RegionMap.BiomeIndex); inside a cell it's that biome alone, and across a band
/// biomeBorder wide at each cell border it blends into the neighbour's. Borders wander with the
/// same warp as the terrain paint, so the grid doesn't show.
///
/// Used by both the fine ground noise (BiomeNoise) and what grows (BiomeMap), so the bumps and
/// the plants change at the same place.
/// </summary>
public sealed class BiomeBlend
{
    public const int MaxAtPoint = 4;    // the four cells around a point

    readonly RegionMap map;
    readonly RegionFrame frame;
    readonly float softness;            // half the border band, as a share of a cell
    readonly float warp;                // cells
    readonly Vector2 warpOffset;

    public BiomeBlend(RegionMap map, RegionFrame frame, RegionBuildSettings build, int seed)
    {
        this.map = map;
        this.frame = frame;
        softness = Mathf.Clamp(0.5f * build.biomeBorder / map.CellSize, 0.01f, 0.5f);
        warp = build.paintWarp;
        warpOffset = TerrainPaint.RandomOffset(new System.Random(seed ^ 0x0b1e));
    }

    public RegionMap Map => map;

    /// <summary>
    /// Fills <paramref name="biomes"/> (indices into map.Biomes) and <paramref name="weights"/>
    /// (summing to 1, less where a neighbour has no biome) for a world point. Both arrays need
    /// room for MaxAtPoint. Returns how many were filled.
    /// </summary>
    public int At(Vector2 world, int[] biomes, float[] weights)
    {
        var cell = frame.WorldToCell(world) + TerrainPaint.Warp(world, warpOffset) * warp;
        int x0 = Mathf.FloorToInt(cell.x), y0 = Mathf.FloorToInt(cell.y);
        float sx = Border(cell.x - x0), sy = Border(cell.y - y0);

        int count = 0;
        Add(x0, y0, (1f - sx) * (1f - sy), biomes, weights, ref count);
        Add(x0 + 1, y0, sx * (1f - sy), biomes, weights, ref count);
        Add(x0, y0 + 1, (1f - sx) * sy, biomes, weights, ref count);
        Add(x0 + 1, y0 + 1, sx * sy, biomes, weights, ref count);
        return count;
    }

    /// <summary>0 (this cell) to 1 (the next), switching over the border band in the middle.</summary>
    float Border(float t) => RegionMath.SmoothStep(0.5f - softness, 0.5f + softness, t);

    void Add(int x, int y, float weight, int[] biomes, float[] weights, ref int count)
    {
        if (weight <= 0f) return;
        int biome = map.BiomeIndex[Mathf.Clamp(y, 0, map.Height - 1) * map.Width + Mathf.Clamp(x, 0, map.Width - 1)];
        if (biome < 0) return;

        for (int k = 0; k < count; k++)
        {
            if (biomes[k] != biome) continue;
            weights[k] += weight;       // the same biome on two corners: one entry
            return;
        }
        biomes[count] = biome;
        weights[count] = weight;
        count++;
    }
}
