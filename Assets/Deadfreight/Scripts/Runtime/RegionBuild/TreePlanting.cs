// Scripts/Runtime/RegionBuild/TreePlanting.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plants trees on the terrain, using Unity's terrain tree system (instanced, culled by distance,
/// collisions from each prefab's collider).
///
/// Each biome role with a Biome in its slot plants its own trees: points on a jittered grid the
/// biome's treeSpacing apart, each kept with a chance of
///
///     the biome's treeFill  ×  that role's weight there (BiomeMap)  ×  the thinning away from roads
///
/// so where biomes blend their trees mix, and forests are thick along the roads the players see
/// and thinner deep in the woods they don't (that's most of the trees, so it's the big saving).
///
/// Every tree keeps clear of roads, rivers, lakes, cliffs, steep ground, place pads, rocks, and
/// other trees (treeGap). If more than maxTrees qualify, a random selection is kept, thinning
/// the whole region evenly instead of cutting off one side of it.
/// </summary>
public static class TreePlanting
{
    const float AboveLake = 0.5f;       // metres: trees need at least this much dry ground
    const float CliffAbove = 0.3f;      // plan cliff amount past which nothing grows
    const int SeedSalt = 0x7ae5;

    /// <returns>How many trees were planted.</returns>
    public static int Plant(TerrainData data, GroundSurvey survey, BiomeMap biomes, Footprints rocks, int seed)
    {
        var settings = survey.Settings;
        var prototypeIndex = Prototypes(settings.biomes, out var prototypes);
        data.treePrototypes = prototypes;
        if (prototypes.Length == 0) return 0;

        var rng = new System.Random(seed ^ SeedSalt);
        var trees = new List<TreeInstance>();
        var standing = new Footprints();
        float gap = 0.5f * settings.treeGap;   // each tree's footprint radius: two never closer than treeGap

        for (int role = 0; role < BiomeSet.RoleCount; role++)
        {
            var biome = settings.biomes[role];
            if (biome == null || biome.trees.Length == 0) continue;

            Scatter(survey.Field, biome.treeSpacing, rng, world =>
            {
                float chance = biome.treeFill * biomes.Weight(world, (BiomeRole)role) * RoadThinning(survey.FromRoad(world), settings);
                if (rng.NextDouble() >= chance || !Allowed(world, survey)) return;
                if (rocks.Overlaps(world, gap) || standing.Overlaps(world, gap)) return;

                var tree = Pick(biome.trees, rng);
                if (tree == null) return;
                standing.Add(world, gap);
                trees.Add(Instance(tree, prototypeIndex[tree.prefab], world, survey.Field, rng));
            });
        }

        if (trees.Count > settings.maxTrees) KeepRandom(trees, settings.maxTrees, rng);
        data.SetTreeInstances(trees.ToArray(), true);
        return trees.Count;
    }

    // ---------- where ----------

    /// <summary>1 near roads, falling to treeFarDensity once far from them.</summary>
    static float RoadThinning(float fromRoad, RegionBuildSettings settings) =>
        Mathf.Lerp(1f, settings.treeFarDensity, RegionMath.SmoothStep(settings.treeThinFrom, settings.treeThinTo, fromRoad));

    static bool Allowed(Vector2 world, GroundSurvey survey)
    {
        var settings = survey.Settings;
        if (survey.BelowLake(world, AboveLake)) return false;
        if (survey.Slope(world) > settings.treeMaxSlope) return false;
        if (survey.Cliff(survey.Cell(world)) > CliffAbove) return false;
        if (survey.FromRoad(world) < settings.treeRoadClearance) return false;
        if (survey.FromRiver(world) < settings.treeRiverClearance) return false;
        return !survey.NearPad(world, settings.treeRoadClearance);
    }

    /// <summary>Calls <paramref name="visit"/> once per cell of a grid <paramref name="spacing"/> metres apart, at a random spot in the cell.</summary>
    static void Scatter(HeightField field, float spacing, System.Random rng, System.Action<Vector2> visit)
    {
        int columns = Mathf.FloorToInt(field.SizeX / spacing), rows = Mathf.FloorToInt(field.SizeZ / spacing);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
            visit(new Vector2((column + (float)rng.NextDouble()) * spacing, (row + (float)rng.NextDouble()) * spacing));
    }

    // ---------- which ----------

    /// <summary>
    /// One terrain tree prototype per distinct prefab across all biomes (a prefab in two biomes is
    /// still one prototype). Returns each prefab's prototype index.
    /// </summary>
    static Dictionary<GameObject, int> Prototypes(BiomeSet biomes, out TreePrototype[] prototypes)
    {
        var index = new Dictionary<GameObject, int>();
        var list = new List<TreePrototype>();
        for (int role = 0; role < BiomeSet.RoleCount; role++)
        {
            var biome = biomes[role];
            if (biome == null) continue;
            foreach (var tree in biome.trees)
            {
                if (tree == null || tree.prefab == null || index.ContainsKey(tree.prefab)) continue;
                index[tree.prefab] = list.Count;
                list.Add(new TreePrototype { prefab = tree.prefab });
            }
        }
        prototypes = list.ToArray();
        return index;
    }

    /// <summary>A random tree, chosen by weight, from those with a prefab; null if none.</summary>
    static BiomeTree Pick(BiomeTree[] trees, System.Random rng)
    {
        float total = 0f;
        foreach (var tree in trees)
            if (tree != null && tree.prefab != null && tree.weight > 0f) total += tree.weight;
        if (total <= 0f) return null;

        float pick = (float)rng.NextDouble() * total;
        BiomeTree last = null;
        foreach (var tree in trees)
        {
            if (tree == null || tree.prefab == null || tree.weight <= 0f) continue;
            last = tree;
            pick -= tree.weight;
            if (pick <= 0f) return tree;
        }
        return last;    // only reached through rounding
    }

    static TreeInstance Instance(BiomeTree tree, int prototype, Vector2 world, HeightField field, System.Random rng)
    {
        float scale = Mathf.Lerp(tree.scale.x, tree.scale.y, (float)rng.NextDouble());
        return new TreeInstance
        {
            position = new Vector3(world.x / field.SizeX, 0f, world.y / field.SizeZ),    // height snaps to the terrain
            widthScale = scale,
            heightScale = scale,
            rotation = (float)(rng.NextDouble() * Mathf.PI * 2.0),
            color = Color.white,
            lightmapColor = Color.white,
            prototypeIndex = prototype,
        };
    }

    /// <summary>Keeps a random <paramref name="count"/> of the trees (a partial shuffle).</summary>
    static void KeepRandom(List<TreeInstance> trees, int count, System.Random rng)
    {
        for (int i = 0; i < count; i++)
        {
            int j = rng.Next(i, trees.Count);
            (trees[i], trees[j]) = (trees[j], trees[i]);
        }
        trees.RemoveRange(count, trees.Count - count);
    }
}
