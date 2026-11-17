// Scripts/Runtime/RegionBuild/GroundCoverPaint.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters ground cover as terrain details. Each kind grows where TerrainPaint painted its ground
/// (grass on grass, ferns on forest floor...), broken into patches by noise so it doesn't carpet
/// everything evenly. Nothing grows on roads or under water.
/// </summary>
public static class GroundCoverPaint
{
    const int DetailCellsPerBatch = 32;      // detail cells Unity draws as one batch
    const float PatchEdgeLow = 0.35f;       // patch noise below this: none...
    const float PatchEdgeHigh = 0.65f;      // ...above this: full cover

    /// <param name="paint">TerrainPaint weights [row, column, layer] at the alphamap resolution.</param>
    /// <param name="waterHeight">World height of lake water; nothing grows below it.</param>
    public static void Apply(TerrainData data, float[,,] paint, HeightField field, float waterHeight,
        RegionBuildSettings settings, int seed)
    {
        var covers = new List<GroundCover>();
        foreach (var cover in settings.groundCover)
            if (cover != null && cover.prototype != null) covers.Add(cover);
        if (covers.Count == 0) return;

        int resolution = settings.detailResolution;
        data.SetDetailResolution(resolution, DetailCellsPerBatch);
        data.SetDetailScatterMode(DetailScatterMode.CoverageMode);
        data.detailPrototypes = Prototypes(covers);

        var bare = BareGround(field, waterHeight, resolution);
        var layer = new int[resolution, resolution];
        int paintResolution = paint.GetLength(0);
        var rng = new System.Random(seed ^ 0x6c0e);

        for (int k = 0; k < covers.Count; k++)
        {
            var cover = covers[k];
            var offset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
            int ground = (int)cover.grows;

            for (int row = 0; row < resolution; row++)
            for (int column = 0; column < resolution; column++)
            {
                if (bare[row, column])
                {
                    layer[row, column] = 0;
                    continue;
                }

                var world = CellCentre(field, resolution, column, row);
                int pr = Mathf.Min(paintResolution - 1, row * paintResolution / resolution);
                int pc = Mathf.Min(paintResolution - 1, column * paintResolution / resolution);
                float onGround = paint[pr, pc, ground];

                float noise = Mathf.PerlinNoise(world.x / settings.coverPatchScale + offset.x, world.y / settings.coverPatchScale + offset.y);
                float patch = Mathf.Lerp(1f, RegionMath.SmoothStep(PatchEdgeLow, PatchEdgeHigh, noise), settings.coverPatchiness);

                layer[row, column] = Mathf.RoundToInt(Mathf.Clamp01(onGround * cover.amount * patch) * 255f);
            }
            data.SetDetailLayer(0, 0, k, layer);
        }
    }

    static DetailPrototype[] Prototypes(List<GroundCover> covers)
    {
        var prototypes = new DetailPrototype[covers.Count];
        for (int k = 0; k < covers.Count; k++)
        {
            var cover = covers[k];
            prototypes[k] = new DetailPrototype
            {
                prototype = cover.prototype,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = cover.scale.x,
                maxWidth = cover.scale.y,
                minHeight = cover.scale.x,
                maxHeight = cover.scale.y,
                density = cover.density,
                alignToGround = cover.alignToGround,
                healthyColor = Color.white,
                dryColor = Color.white,
            };
        }
        return prototypes;
    }

    /// <summary>Detail cells where nothing may grow: road surfaces and lake water.</summary>
    static bool[,] BareGround(HeightField field, float waterHeight, int resolution)
    {
        var bare = new bool[resolution, resolution];
        for (int row = 0; row < resolution; row++)
        for (int column = 0; column < resolution; column++)
        {
            var world = CellCentre(field, resolution, column, row);
            int c = Mathf.Clamp(Mathf.RoundToInt(world.x / field.StepX), 0, field.Resolution - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt(world.y / field.StepZ), 0, field.Resolution - 1);
            bare[row, column] = field.IsRoadCore(c, r) || field.HeightAt(world) < waterHeight;
        }
        return bare;
    }

    /// <summary>World (x, z) at the centre of a detail cell.</summary>
    static Vector2 CellCentre(HeightField field, int resolution, int column, int row) =>
        new((column + 0.5f) / resolution * field.SizeX, (row + 0.5f) / resolution * field.SizeZ);
}
