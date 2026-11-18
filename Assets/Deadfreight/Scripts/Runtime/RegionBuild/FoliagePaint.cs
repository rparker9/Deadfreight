// Scripts/Runtime/RegionBuild/FoliagePaint.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scatters each biome's foliage as terrain details (instanced meshes). Each distinct model is
/// one detail layer, however many biomes list it, and at each point it covers
///
///     the sum over biomes of:  its weight there (BiomeMap)  ×  its amount of the model  ×  patches
///
/// so a grass used by two biomes carries on across their border, thinning where
/// one biome uses less of it. Patches come from noise per model (foliagePatchScale), scaled by
/// each biome's patchiness. Nothing grows on roads, in river water, under lakes, on bare rock
/// faces (the paint's rock layer) or under placed rocks.
/// </summary>
public static class FoliagePaint
{
    const int DetailCellsPerBatch = 32;     // detail cells Unity draws as one batch
    const float PatchEdgeLow = 0.35f;       // patch noise below this: none...
    const float PatchEdgeHigh = 0.65f;      // ...above this: full cover
    const int SeedSalt = 0x6c0e;

    /// <summary>One detail layer: a model, its look, and how much of it each biome (slot) grows.</summary>
    sealed class Layer
    {
        public BiomeFoliage Look;           // from the first biome listing the model
        public readonly float[] Amount;     // per slot
        public readonly float[] Patchiness; // per slot

        public Layer(BiomeFoliage look, int slots)
        {
            Look = look;
            Amount = new float[slots];
            Patchiness = new float[slots];
        }
    }

    /// <param name="paint">TerrainPaint weights [row, column, layer] at the alphamap resolution.</param>
    public static void Apply(TerrainData data, float[,,] paint, GroundSurvey survey, BiomeMap biomes, Footprints rocks, int seed)
    {
        var settings = survey.Settings;
        var layers = Layers(biomes.Slots);
        int slots = biomes.Slots.Count;
        if (layers.Count == 0) return;

        int resolution = settings.detailResolution;
        data.SetDetailResolution(resolution, DetailCellsPerBatch);
        data.SetDetailScatterMode(DetailScatterMode.CoverageMode);
        data.detailPrototypes = Prototypes(layers);

        var bare = BareGround(survey, rocks, resolution);
        int paintResolution = paint.GetLength(0);
        var weights = new float[slots];
        var cover = new int[resolution, resolution];
        var rng = new System.Random(seed ^ SeedSalt);

        for (int k = 0; k < layers.Count; k++)
        {
            var layer = layers[k];
            var offset = TerrainPaint.RandomOffset(rng);

            for (int row = 0; row < resolution; row++)
            for (int column = 0; column < resolution; column++)
            {
                cover[row, column] = 0;
                if (bare[row, column]) continue;

                var world = CellCentre(survey.Field, resolution, column, row);
                biomes.Weights(world, weights);
                float noise = Mathf.PerlinNoise(world.x / settings.foliagePatchScale + offset.x, world.y / settings.foliagePatchScale + offset.y);
                float patch = RegionMath.SmoothStep(PatchEdgeLow, PatchEdgeHigh, noise);

                float amount = 0f;
                for (int slot = 0; slot < slots; slot++)
                    if (weights[slot] > 0f && layer.Amount[slot] > 0f)
                        amount += weights[slot] * layer.Amount[slot] * Mathf.Lerp(1f, patch, layer.Patchiness[slot]);

                int pr = Mathf.Min(paintResolution - 1, row * paintResolution / resolution);
                int pc = Mathf.Min(paintResolution - 1, column * paintResolution / resolution);
                amount *= 1f - paint[pr, pc, TerrainPaint.Rock];

                cover[row, column] = Mathf.RoundToInt(Mathf.Clamp01(amount) * 255f);
            }
            data.SetDetailLayer(0, 0, k, cover);
        }
    }

    // ---------- layers ----------

    /// <summary>Gathers every biome's foliage into one layer per distinct model.</summary>
    static List<Layer> Layers(IReadOnlyList<Biome> biomes)
    {
        var layers = new List<Layer>();
        var byModel = new Dictionary<GameObject, Layer>();
        for (int slot = 0; slot < biomes.Count; slot++)
        {
            var biome = biomes[slot];
            if (biome == null) continue;
            foreach (var foliage in biome.foliage)
            {
                if (foliage == null || foliage.prototype == null) continue;
                if (!byModel.TryGetValue(foliage.prototype, out var layer))
                {
                    layer = new Layer(foliage, biomes.Count);
                    byModel[foliage.prototype] = layer;
                    layers.Add(layer);
                }
                else if (!SameLook(layer.Look, foliage))
                {
                    Debug.LogWarning($"Foliage '{foliage.prototype.name}' has a different scale, density or tilt in biome '{biome.name}'. " +
                                     "A model is one detail layer, so the first biome's look is used everywhere.", biome);
                }
                layer.Amount[slot] += foliage.amount;
                layer.Patchiness[slot] = biome.patchiness;
            }
        }
        return layers;
    }

    static bool SameLook(BiomeFoliage a, BiomeFoliage b) =>
        a.scale == b.scale && Mathf.Approximately(a.density, b.density) && Mathf.Approximately(a.alignToGround, b.alignToGround);

    static DetailPrototype[] Prototypes(List<Layer> layers)
    {
        var prototypes = new DetailPrototype[layers.Count];
        for (int k = 0; k < layers.Count; k++)
        {
            var look = layers[k].Look;
            prototypes[k] = new DetailPrototype
            {
                prototype = look.prototype,
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = look.scale.x,
                maxWidth = look.scale.y,
                minHeight = look.scale.x,
                maxHeight = look.scale.y,
                density = look.density,
                alignToGround = look.alignToGround,
                healthyColor = Color.white,
                dryColor = Color.white,
            };
        }
        return prototypes;
    }

    // ---------- where nothing grows ----------

    /// <summary>Detail cells where nothing may grow: roads, river water, lakes, and under rocks.</summary>
    static bool[,] BareGround(GroundSurvey survey, Footprints rocks, int resolution)
    {
        var field = survey.Field;
        var bare = new bool[resolution, resolution];
        for (int row = 0; row < resolution; row++)
        for (int column = 0; column < resolution; column++)
        {
            var world = CellCentre(field, resolution, column, row);
            bare[row, column] = survey.OnRoad(world) || survey.InRiver(world) || survey.BelowLake(world);
        }

        // Rocks: mark every cell whose centre is inside a rock's footprint.
        float cellX = field.SizeX / resolution, cellZ = field.SizeZ / resolution;
        foreach (var (centre, radius) in rocks.Circles)
        {
            int c0 = Mathf.Max(0, Mathf.FloorToInt((centre.x - radius) / cellX));
            int c1 = Mathf.Min(resolution - 1, Mathf.FloorToInt((centre.x + radius) / cellX));
            int r0 = Mathf.Max(0, Mathf.FloorToInt((centre.y - radius) / cellZ));
            int r1 = Mathf.Min(resolution - 1, Mathf.FloorToInt((centre.y + radius) / cellZ));
            for (int row = r0; row <= r1; row++)
            for (int column = c0; column <= c1; column++)
                if ((CellCentre(field, resolution, column, row) - centre).sqrMagnitude < radius * radius)
                    bare[row, column] = true;
        }
        return bare;
    }

    /// <summary>World (x, z) at the centre of a detail cell.</summary>
    static Vector2 CellCentre(HeightField field, int resolution, int column, int row) =>
        new((column + 0.5f) / resolution * field.SizeX, (row + 0.5f) / resolution * field.SizeZ);
}
