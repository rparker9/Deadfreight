// Scripts/Runtime/RegionBuild/TerrainPaint.cs
using System;
using UnityEngine;

/// <summary>
/// How much of each texture layer covers each spot of terrain. Four roles, in this order:
///
///   Grass   open ground: whatever the others leave
///   Dirt    forest floor, marsh, damp ground near rivers, hollows
///   Stony   road verges, gravel at the water's edge, scree below cliffs, ridges and crests
///   Rock    steep ground, read from the finished heights
///
/// Each role starts as a soft 0-1 amount, then noise moves where its border falls so edges wander
/// instead of following the 50 m plan grid. Rock wins over stony, stony over dirt, and grass takes
/// what's left, so each spot's weights add up to 1.
/// </summary>
public static class TerrainPaint
{
    public const int Grass = 0, Dirt = 1, Stony = 2, Rock = 3, LayerCount = 4;

    const float WarpScale = 90f;            // metres across one wobble of the sampling position
    const float EdgeSoftness = 0.25f;       // how gradual a border is once moved by noise
    const float FineNoiseShare = 0.35f;     // share of the edge noise from a finer, second layer
    const float FineNoiseScale = 0.3f;      // its size, relative to edgeNoiseScale
    const float RidgeBlur = 3f;             // cells: ridges and hollows are judged against this wide a neighbourhood
    const float HollowDirt = 0.5f;          // hollows get half as much dirt as forest floor

    /// <summary>Weights as [row, column, layer] for TerrainData.SetAlphamaps.</summary>
    public static float[,,] Weights(RegionMap map, RegionFrame frame, HeightField field, RegionBuildSettings settings, int seed)
    {
        int resolution = settings.alphamapResolution;
        float pixelSize = 0.5f * (field.SizeX + field.SizeZ) / (resolution - 1);   // metres per paint pixel

        // From the finished build: how far each paint pixel is from a road and from river water.
        var toRoad = PixelsToMarked(field, resolution, field.IsRoadCore);
        var toRiver = PixelsToMarked(field, resolution, field.IsChannel);

        // From the plan: how far each cell is from a cliff, and how ridged or hollow the land is.
        GridOps.NearestMarked(map.Cliff, map.Width, map.Height, out var cellsToCliff, out _);
        var shape = RidgesAndHollows(map);

        var rng = new System.Random(seed ^ 0x5eed);
        var warpOffset = RandomOffset(rng);
        var rockNoise = RandomOffset(rng);
        var stonyNoise = RandomOffset(rng);
        var dirtNoise = RandomOffset(rng);

        var weights = new float[resolution, resolution, LayerCount];
        for (int row = 0; row < resolution; row++)
        for (int column = 0; column < resolution; column++)
        {
            var world = new Vector2(column / (resolution - 1f) * field.SizeX, row / (resolution - 1f) * field.SizeZ);
            var cell = frame.WorldToCell(world) + Warp(world, warpOffset) * settings.paintWarp;
            int pixel = row * resolution + column;

            // Plan layers, read softly and firmed up so they don't look like blurred squares.
            float forest = RegionMath.SmoothStep(0.3f, 0.7f, GridSampling.Bilinear(map.Forest, map.Width, map.Height, cell));
            float marsh = RegionMath.SmoothStep(0.3f, 0.7f, GridSampling.Bilinear(map.Marsh, map.Width, map.Height, cell));
            float relief = GridSampling.Bilinear(shape, map.Width, map.Height, cell);
            float ridge = RegionMath.SmoothStep(0f, settings.ridgeDepth, relief);
            float hollow = RegionMath.SmoothStep(0f, settings.ridgeDepth, -relief);

            // Bands measured in metres from roads, river water and cliffs.
            float fromRoad = toRoad[pixel] * pixelSize;
            float fromRiver = toRiver[pixel] * pixelSize;
            float verge = Within(fromRoad, settings.vergeWidth);
            float gravelBar = Within(fromRiver, settings.riverGravel);
            float damp = Within(fromRiver, settings.riverDamp);
            float scree = Scree(map, cell, cellsToCliff, settings);

            float rock = RegionMath.SmoothStep(settings.rockSlopeStart, settings.rockSlopeFull, field.SlopeAt(world));
            float stony = Max(verge, gravelBar, scree, ridge * settings.ridgeStony);
            float dirt = Max(forest, marsh, damp, hollow * HollowDirt);

            rock = Wander(rock, EdgeNoise(world, rockNoise, settings), settings.edgeNoise);
            stony = Wander(stony, EdgeNoise(world, stonyNoise, settings), settings.edgeNoise);
            dirt = Wander(dirt, EdgeNoise(world, dirtNoise, settings), settings.edgeNoise);

            // Rock first, then stony, then dirt; grass fills the rest. These always sum to 1.
            float stonyShare = stony * (1f - rock);
            float dirtShare = dirt * (1f - rock) * (1f - stony);
            weights[row, column, Rock] = rock;
            weights[row, column, Stony] = stonyShare;
            weights[row, column, Dirt] = dirtShare;
            weights[row, column, Grass] = Mathf.Max(0f, 1f - rock - stonyShare - dirtShare);
        }
        return weights;
    }

    // ---------- bands ----------

    /// <summary>1 close in, fading to 0 by <paramref name="width"/>.</summary>
    internal static float Within(float distance, float width) => 1f - RegionMath.SmoothStep(width * 0.5f, width, distance);

    /// <summary>Loose rock at the foot of the escarpment: on the lowland side, fading out with distance.</summary>
    internal static float Scree(RegionMap map, Vector2 cell, float[] cellsToCliff, RegionBuildSettings settings)
    {
        if (!map.HasEscarpment) return 0f;
        float scarp = GridSampling.Bilinear(map.Scarp, map.Width, map.Height, cell);
        float lowland = 1f - RegionMath.SmoothStep(-0.5f, 0.5f, scarp);       // Scarp is positive on the upland side
        float metres = GridSampling.Bilinear(cellsToCliff, map.Width, map.Height, cell) * map.CellSize;
        return lowland * (1f - RegionMath.SmoothStep(settings.screeReach * 0.3f, settings.screeReach, metres));
    }

    /// <summary>Plan height minus its blurred surroundings: positive on ridges and crests, negative in hollows.</summary>
    internal static float[] RidgesAndHollows(RegionMap map)
    {
        var surroundings = GridOps.Blur(map.Elevation, map.Width, map.Height, RidgeBlur);
        var shape = new float[map.Elevation.Length];
        for (int i = 0; i < shape.Length; i++) shape[i] = map.Elevation[i] - surroundings[i];
        return shape;
    }

    /// <summary>
    /// For every paint pixel, the distance in pixels to the nearest one whose height sample is
    /// marked (a road or river). Huge everywhere if nothing is marked.
    /// </summary>
    static float[] PixelsToMarked(HeightField field, int resolution, Func<int, int, bool> isMarked)
    {
        var marked = new bool[resolution * resolution];
        for (int row = 0; row < resolution; row++)
        for (int column = 0; column < resolution; column++)
        {
            int c = Mathf.Clamp(Mathf.RoundToInt(column / (resolution - 1f) * (field.Resolution - 1)), 0, field.Resolution - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt(row / (resolution - 1f) * (field.Resolution - 1)), 0, field.Resolution - 1);
            marked[row * resolution + column] = isMarked(c, r);
        }
        GridOps.NearestMarked(marked, resolution, resolution, out var distance, out _);
        return distance;
    }

    // ---------- noise ----------

    /// <summary>
    /// Moves where a soft amount's border falls: noise raises or lowers the level that counts as
    /// "half covered", then the amount is firmed around it. Solid areas stay solid.
    /// </summary>
    internal static float Wander(float amount, float noise, float strength)
    {
        float border = Mathf.Clamp(0.5f + (noise - 0.5f) * 2f * strength, EdgeSoftness, 1f - EdgeSoftness);
        return RegionMath.SmoothStep(border - EdgeSoftness, border + EdgeSoftness, amount);
    }

    /// <summary>Two layers of Perlin noise, 0-1: borders wander at two sizes.</summary>
    internal static float EdgeNoise(Vector2 world, Vector2 offset, RegionBuildSettings settings)
    {
        float scale = settings.edgeNoiseScale;
        float coarse = Mathf.PerlinNoise(world.x / scale + offset.x, world.y / scale + offset.y);
        float fine = Mathf.PerlinNoise(world.x / (scale * FineNoiseScale) + offset.y, world.y / (scale * FineNoiseScale) + offset.x);
        return Mathf.Lerp(coarse, fine, FineNoiseShare);
    }

    /// <summary>A smooth random offset, about -1 to 1 cells in each direction.</summary>
    internal static Vector2 Warp(Vector2 world, Vector2 offset)
    {
        float x = Mathf.PerlinNoise(world.x / WarpScale + offset.x, world.y / WarpScale + offset.y);
        float y = Mathf.PerlinNoise(world.x / WarpScale + offset.y, world.y / WarpScale + offset.x);
        return new Vector2(x - 0.5f, y - 0.5f) * 2f;
    }

    internal static Vector2 RandomOffset(System.Random rng) => new(rng.Next(0, 10000), rng.Next(0, 10000));

    static float Max(float a, float b, float c, float d) => Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
}
