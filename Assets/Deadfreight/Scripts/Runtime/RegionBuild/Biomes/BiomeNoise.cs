// Scripts/Runtime/RegionBuild/Biomes/BiomeNoise.cs
using UnityEngine;

/// <summary>
/// Each biome's fine ground noise (bumps and roughness), blended where biomes meet. Kept from
/// changing the plan: for every biome, the noise's own average over each plan cell is worked out
/// once and subtracted (smoothly, between cell centres), so what's left is only detail smaller
/// than a cell and every cell keeps its planned height on average. It also fades out on cliffs
/// and lakes.
/// </summary>
public sealed class BiomeNoise
{
    const int MeanSamples = 4;      // per side of a cell, when working out the noise's average there

    readonly RegionMap map;
    readonly RegionFrame frame;
    readonly BiomeBlend blend;
    readonly Vector2[] bumpOffset, roughOffset;
    readonly float[][] cellMean;    // per biome, per cell: that biome's noise averaged over the cell (null = no noise)

    // Scratch for one lookup.
    readonly int[] biomes = new int[BiomeBlend.MaxAtPoint];
    readonly float[] weights = new float[BiomeBlend.MaxAtPoint];

    public BiomeNoise(RegionMap map, RegionFrame frame, BiomeBlend blend, int seed)
    {
        this.map = map;
        this.frame = frame;
        this.blend = blend;

        int count = map.Biomes.Count;
        bumpOffset = new Vector2[count];
        roughOffset = new Vector2[count];
        cellMean = new float[count][];

        var rng = new System.Random(seed ^ 0x401e);
        for (int b = 0; b < count; b++)
        {
            bumpOffset[b] = TerrainPaint.RandomOffset(rng);
            roughOffset[b] = TerrainPaint.RandomOffset(rng);
            if (map.Biomes[b] != null && map.Biomes[b].HasNoise) cellMean[b] = CellMeans(b);
        }
    }

    /// <summary>Metres to add to the ground at a world point (plan cell position given, to save working it out again).</summary>
    public float At(Vector2 world, Vector2 cell)
    {
        int count = blend.At(world, biomes, weights);
        float sum = 0f;
        for (int k = 0; k < count; k++)
        {
            int b = biomes[k];
            if (cellMean[b] == null) continue;
            float mean = GridSampling.Bilinear(cellMean[b], map.Width, map.Height, cell);
            sum += weights[k] * (Raw(b, world) - mean);
        }
        if (sum == 0f) return 0f;

        float cliff = GridSampling.Bilinear(map.Cliff, map.Width, map.Height, cell);
        float lake = GridSampling.Bilinear(map.Water, map.Width, map.Height, cell);
        return sum * (1f - cliff) * (1f - lake);
    }

    /// <summary>One biome's noise, before its cell averages are taken out.</summary>
    float Raw(int b, Vector2 world)
    {
        var biome = map.Biomes[b];
        return Noise(world, biome.bumpScale, bumpOffset[b]) * biome.bumpHeight
             + Noise(world, biome.roughScale, roughOffset[b]) * biome.roughHeight;
    }

    /// <summary>The biome's noise averaged over each cell, sampled on a small grid inside it.</summary>
    float[] CellMeans(int b)
    {
        var means = new float[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            float sum = 0f;
            for (int sy = 0; sy < MeanSamples; sy++)
            for (int sx = 0; sx < MeanSamples; sx++)
            {
                var inCell = new Vector2(x - 0.5f + (sx + 0.5f) / MeanSamples, y - 0.5f + (sy + 0.5f) / MeanSamples);
                sum += Raw(b, frame.CellToWorld(inCell));
            }
            means[y * map.Width + x] = sum / (MeanSamples * MeanSamples);
        }
        return means;
    }

    /// <summary>Perlin noise from -1 to 1 with features about <paramref name="scale"/> metres across.</summary>
    static float Noise(Vector2 world, float scale, Vector2 offset) =>
        (Mathf.PerlinNoise(world.x / scale + offset.x, world.y / scale + offset.y) - 0.5f) * 2f;
}
