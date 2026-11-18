// Scripts/Runtime/RegionBuild/Biomes/BiomeMap.cs
using UnityEngine;

/// <summary>
/// How much of each biome role is at every point of the region, worked out once from the
/// finished ground (GroundSurvey). Each point gets a weight per role, and the weights add up to
/// 1, so where two biomes meet their trees and foliage mix rather than stopping at a line.
/// Roads, river water and lakes get no weight at all: nothing grows there.
///
/// Roles claim the ground in this order, each taking its share of what's left:
///
///   Verge      within biomeVergeWidth of a road
///   Riverbank  within riverbankWidth of river water
///   Wetland    the plan's marsh
///   Hollow     the plan's hollows
///   Upland     steep ground (uplandSlope) or high ground (uplandHeight)
///   Forest     the plan's forest, split into DeepForest past deepForestDepth in from its edge
///              and ForestEdge before that
///   Field      whatever is left
///
/// Borders wander with noise (edgeNoise, edgeNoiseScale), the same as the terrain paint's.
/// </summary>
public sealed class BiomeMap
{
    const int Roles = BiomeSet.RoleCount;

    public readonly int Resolution;
    public readonly float SizeX, SizeZ;

    readonly byte[] weights;    // [(row * Resolution + column) * Roles + role], 0-255

    BiomeMap(int resolution, float sizeX, float sizeZ)
    {
        Resolution = resolution;
        SizeX = sizeX;
        SizeZ = sizeZ;
        weights = new byte[resolution * resolution * Roles];
    }

    public static BiomeMap Build(GroundSurvey survey, int seed)
    {
        var settings = survey.Settings;
        var map = new BiomeMap(settings.biomeResolution, survey.Field.SizeX, survey.Field.SizeZ);

        var rng = new System.Random(seed ^ 0x0b10);
        var warpOffset = TerrainPaint.RandomOffset(rng);
        var forestNoise = TerrainPaint.RandomOffset(rng);
        var otherNoise = TerrainPaint.RandomOffset(rng);
        var one = new float[Roles];

        for (int row = 0; row < map.Resolution; row++)
        for (int column = 0; column < map.Resolution; column++)
        {
            var world = map.PointToWorld(column, row);
            var cell = survey.Cell(world) + TerrainPaint.Warp(world, warpOffset) * settings.paintWarp;
            Weigh(survey, world, cell, forestNoise, otherNoise, one);

            int at = (row * map.Resolution + column) * Roles;
            for (int role = 0; role < Roles; role++) map.weights[at + role] = (byte)Mathf.RoundToInt(one[role] * 255f);
        }
        return map;
    }

    /// <summary>The weights at one point, by the rules in the class summary.</summary>
    static void Weigh(GroundSurvey survey, Vector2 world, Vector2 cell, Vector2 forestNoise, Vector2 otherNoise, float[] into)
    {
        System.Array.Clear(into, 0, Roles);
        if (survey.OnRoad(world) || survey.InRiver(world) || survey.BelowLake(world)) return;

        var settings = survey.Settings;
        float noise = TerrainPaint.EdgeNoise(world, otherNoise, settings);
        float Wander(float amount) => TerrainPaint.Wander(amount, noise, settings.edgeNoise);

        float verge = TerrainPaint.Within(survey.FromRoad(world), settings.biomeVergeWidth);
        float riverbank = TerrainPaint.Within(survey.FromRiver(world), settings.riverbankWidth);
        float wetland = Wander(survey.Marsh(cell));
        float hollow = Wander(survey.Hollow(cell));
        float steep = RegionMath.SmoothStep(settings.uplandSlope.x, settings.uplandSlope.y, survey.Slope(world));
        float high = RegionMath.SmoothStep(settings.uplandHeight.x, settings.uplandHeight.y, survey.Elevation(cell));
        float upland = Wander(Mathf.Max(steep, high));
        float forest = TerrainPaint.Wander(survey.Forest(cell), TerrainPaint.EdgeNoise(world, forestNoise, settings), settings.edgeNoise);
        float deep = RegionMath.SmoothStep(settings.deepForestDepth * 0.5f, settings.deepForestDepth, survey.ForestDepth(cell));

        float left = 1f;
        float Claim(float strength)
        {
            float share = strength * left;
            left -= share;
            return share;
        }

        into[(int)BiomeRole.Verge] = Claim(verge);
        into[(int)BiomeRole.Riverbank] = Claim(riverbank);
        into[(int)BiomeRole.Wetland] = Claim(wetland);
        into[(int)BiomeRole.Hollow] = Claim(hollow);
        into[(int)BiomeRole.Upland] = Claim(upland);
        float forestShare = Claim(forest);
        into[(int)BiomeRole.DeepForest] = forestShare * deep;
        into[(int)BiomeRole.ForestEdge] = forestShare * (1f - deep);
        into[(int)BiomeRole.Field] = left;
    }

    // ---------- reading ----------

    /// <summary>Weight 0-1 of one role at a world point (from the nearest map point).</summary>
    public float Weight(Vector2 world, BiomeRole role) => weights[Index(world) + (int)role] / 255f;

    /// <summary>All roles' weights at a world point, 0-1 each.</summary>
    public void Weights(Vector2 world, float[] into)
    {
        int at = Index(world);
        for (int role = 0; role < Roles; role++) into[role] = weights[at + role] / 255f;
    }

    /// <summary>The role with the most weight at a map point, or -1 where nothing grows.</summary>
    public int Strongest(int column, int row)
    {
        int at = (row * Resolution + column) * Roles, best = -1, bestWeight = 0;
        for (int role = 0; role < Roles; role++)
            if (weights[at + role] > bestWeight) (best, bestWeight) = (role, weights[at + role]);
        return best;
    }

    public Vector2 PointToWorld(int column, int row) =>
        new(column / (Resolution - 1f) * SizeX, row / (Resolution - 1f) * SizeZ);

    int Index(Vector2 world)
    {
        int column = Mathf.Clamp(Mathf.RoundToInt(world.x / SizeX * (Resolution - 1)), 0, Resolution - 1);
        int row = Mathf.Clamp(Mathf.RoundToInt(world.y / SizeZ * (Resolution - 1)), 0, Resolution - 1);
        return (row * Resolution + column) * Roles;
    }
}
