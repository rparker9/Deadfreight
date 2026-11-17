// Scripts/Runtime/RegionBuild/TerrainPaint.cs
using UnityEngine;

/// <summary>
/// How much of each texture layer covers each spot of terrain, read from the plan's land layers
/// and the finished ground's slope. Layer order: grass, forest floor, mud, rock.
/// </summary>
public static class TerrainPaint
{
    public const int Grass = 0, ForestFloor = 1, Mud = 2, Rock = 3, LayerCount = 4;

    const float WarpScale = 90f;    // metres across one wobble of the texture borders

    /// <summary>Weights as [row, column, layer] for TerrainData.SetAlphamaps; each spot's weights add up to 1.</summary>
    public static float[,,] Weights(RegionMap map, RegionFrame frame, HeightField field, RegionBuildSettings settings, int seed)
    {
        int resolution = settings.alphamapResolution;
        var weights = new float[resolution, resolution, LayerCount];
        var rng = new System.Random(seed ^ 0x5eed);
        var warpOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));

        for (int row = 0; row < resolution; row++)
        for (int column = 0; column < resolution; column++)
        {
            var world = new Vector2(column / (resolution - 1f) * field.SizeX, row / (resolution - 1f) * field.SizeZ);
            var cell = frame.WorldToCell(world) + Warp(world, warpOffset) * settings.paintWarp;

            // Bilinear masks are soft blobs; the smoothstep firms up their edges.
            float forest = RegionMath.SmoothStep(0.3f, 0.7f, GridSampling.Bilinear(map.Forest, map.Width, map.Height, cell));
            float marsh = RegionMath.SmoothStep(0.3f, 0.7f, GridSampling.Bilinear(map.Marsh, map.Width, map.Height, cell));
            float rock = RegionMath.SmoothStep(settings.rockSlopeStart, settings.rockSlopeFull, field.SlopeAt(world));

            // Rock wins on steep ground, then mud, then forest floor; grass takes the rest.
            float mud = marsh * (1f - rock);
            float forestFloor = forest * (1f - rock) * (1f - mud);
            float grass = Mathf.Max(0f, 1f - rock - mud - forestFloor);

            float total = grass + forestFloor + mud + rock;
            weights[row, column, Grass] = grass / total;
            weights[row, column, ForestFloor] = forestFloor / total;
            weights[row, column, Mud] = mud / total;
            weights[row, column, Rock] = rock / total;
        }
        return weights;
    }

    /// <summary>A smooth random offset, about -1 to 1 cells in each direction.</summary>
    static Vector2 Warp(Vector2 world, Vector2 offset)
    {
        float x = Mathf.PerlinNoise(world.x / WarpScale + offset.x, world.y / WarpScale + offset.y);
        float y = Mathf.PerlinNoise(world.x / WarpScale + offset.y, world.y / WarpScale + offset.x);
        return new Vector2(x - 0.5f, y - 0.5f) * 2f;
    }
}
