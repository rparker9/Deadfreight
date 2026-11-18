// Scripts/Runtime/RegionBuild/TreePlanting.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Plants trees on the terrain, using Unity's terrain tree system (instanced, culled by distance,
/// collisions from each prefab's collider).
///
///   Forest: points on a jittered grid treeSpacing apart; each kept with a chance that rises with
///           the plan's forest amount, so forests are dense in the middle and thin at the edges.
///   Open:   a much sparser grid of lone trees out where there is no forest.
///
/// Every tree keeps clear of roads, rivers, lakes, cliffs, steep ground and the pads around places.
/// If more trees qualify than maxTrees, a random selection is kept, so the cap thins the whole
/// region evenly instead of cutting off one side of it.
/// </summary>
public static class TreePlanting
{
    const float ForestStart = 0.25f;    // plan forest amount where trees start appearing...
    const float ForestFull = 0.85f;     // ...and where they're as dense as treeSpacing allows
    const float DeepForestFrom = 0.75f; // trees past this amount use DeepForest kinds, the rest ForestEdge
    const float OpenBelow = 0.2f;       // lone trees only where the forest amount is below this
    const float AboveLake = 0.5f;       // metres: trees need at least this much dry ground

    /// <returns>How many trees were planted.</returns>
    public static int Plant(TerrainData data, RegionMap map, RegionFrame frame, HeightField field,
        RegionBuildSettings settings, float lakeLevel, int seed)
    {
        var kinds = new List<TreeKind>();
        foreach (var kind in settings.trees)
            if (kind != null && kind.prefab != null && kind.weight > 0f) kinds.Add(kind);
        if (kinds.Count == 0) return 0;

        var prototypes = new TreePrototype[kinds.Count];
        for (int k = 0; k < kinds.Count; k++) prototypes[k] = new TreePrototype { prefab = kinds[k].prefab };
        data.treePrototypes = prototypes;

        var space = new Clearance(map, frame, field, settings, lakeLevel);
        var rng = new System.Random(seed ^ 0x7ae5);
        var trees = new List<TreeInstance>();

        // Forest trees.
        Scatter(field, settings.treeSpacing, rng, world =>
        {
            float forest = RegionMath.SmoothStep(ForestStart, ForestFull, ForestAt(map, frame, world));
            if (rng.NextDouble() >= forest || !space.Allows(world)) return;
            var habitat = forest >= DeepForestFrom ? TreeHabitat.DeepForest : TreeHabitat.ForestEdge;
            AddTree(trees, kinds, Habitat(habitat, space.IsWet(world), kinds), world, field, rng);
        });

        // Lone trees in the open.
        Scatter(field, settings.openTreeSpacing, rng, world =>
        {
            if (ForestAt(map, frame, world) >= OpenBelow) return;
            if (rng.NextDouble() >= settings.openTreeChance || !space.Allows(world)) return;
            AddTree(trees, kinds, Habitat(TreeHabitat.Open, space.IsWet(world), kinds), world, field, rng);
        });

        if (trees.Count > settings.maxTrees) KeepRandom(trees, settings.maxTrees, rng);
        data.SetTreeInstances(trees.ToArray(), true);
        return trees.Count;
    }

    // ---------- placement ----------

    /// <summary>Calls <paramref name="visit"/> once per cell of a grid <paramref name="spacing"/> metres apart, at a random spot in the cell.</summary>
    static void Scatter(HeightField field, float spacing, System.Random rng, System.Action<Vector2> visit)
    {
        int columns = Mathf.FloorToInt(field.SizeX / spacing), rows = Mathf.FloorToInt(field.SizeZ / spacing);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            var world = new Vector2((column + (float)rng.NextDouble()) * spacing, (row + (float)rng.NextDouble()) * spacing);
            visit(world);
        }
    }

    static float ForestAt(RegionMap map, RegionFrame frame, Vector2 world) =>
        GridSampling.Bilinear(map.Forest, map.Width, map.Height, frame.WorldToCell(world));

    /// <summary>Wet ground uses Wet kinds when the region lists any; otherwise the habitat as given.</summary>
    static TreeHabitat Habitat(TreeHabitat habitat, bool wet, List<TreeKind> kinds) =>
        wet && kinds.Exists(k => k.grows == TreeHabitat.Wet) ? TreeHabitat.Wet : habitat;

    static void AddTree(List<TreeInstance> trees, List<TreeKind> kinds, TreeHabitat habitat, Vector2 world, HeightField field, System.Random rng)
    {
        int k = PickKind(kinds, habitat, rng);
        if (k < 0) return;      // nothing grows in this habitat in this region

        float scale = Mathf.Lerp(kinds[k].scale.x, kinds[k].scale.y, (float)rng.NextDouble());
        trees.Add(new TreeInstance
        {
            position = new Vector3(world.x / field.SizeX, 0f, world.y / field.SizeZ),    // height snaps to the terrain
            widthScale = scale,
            heightScale = scale,
            rotation = (float)(rng.NextDouble() * Mathf.PI * 2.0),
            color = Color.white,
            lightmapColor = Color.white,
            prototypeIndex = k,
        });
    }

    /// <summary>A random kind that grows in <paramref name="habitat"/>, chosen by weight; -1 if none does.</summary>
    static int PickKind(List<TreeKind> kinds, TreeHabitat habitat, System.Random rng)
    {
        float total = 0f;
        foreach (var kind in kinds)
            if (kind.grows == habitat) total += kind.weight;
        if (total <= 0f) return -1;

        float pick = (float)rng.NextDouble() * total;
        for (int k = 0; k < kinds.Count; k++)
        {
            if (kinds[k].grows != habitat) continue;
            pick -= kinds[k].weight;
            if (pick <= 0f) return k;
        }
        return kinds.FindLastIndex(kind => kind.grows == habitat);
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

    // ---------- where trees may grow ----------

    /// <summary>
    /// Answers "may a tree stand here?" and "is it wet here?" quickly, from distance fields worked
    /// out once: metres to the nearest road surface and to the nearest river water.
    /// </summary>
    sealed class Clearance
    {
        readonly RegionMap map;
        readonly RegionFrame frame;
        readonly HeightField field;
        readonly RegionBuildSettings settings;
        readonly float lakeLevel;
        readonly float[] toRoad, toRiver;       // metres, per height sample
        readonly List<Vector2> places = new();

        public Clearance(RegionMap map, RegionFrame frame, HeightField field, RegionBuildSettings settings, float lakeLevel)
        {
            this.map = map;
            this.frame = frame;
            this.field = field;
            this.settings = settings;
            this.lakeLevel = lakeLevel;
            toRoad = MetresToMarked(field, field.IsRoadCore);
            toRiver = MetresToMarked(field, field.IsChannel);
            foreach (var place in map.Places) places.Add(frame.CellToWorld(place.Cell));
        }

        public bool Allows(Vector2 world)
        {
            if (field.HeightAt(world) < lakeLevel + AboveLake) return false;
            if (field.SlopeAt(world) > settings.treeMaxSlope) return false;
            if (GridSampling.Bilinear(map.Cliff, map.Width, map.Height, frame.WorldToCell(world)) > 0.3f) return false;

            int sample = SampleIndex(world);
            if (toRoad[sample] < settings.treeRoadClearance) return false;
            if (toRiver[sample] < settings.treeRiverClearance) return false;

            float padSqr = (settings.padRadius + settings.treeRoadClearance) * (settings.padRadius + settings.treeRoadClearance);
            foreach (var place in places)
                if ((place - world).sqrMagnitude < padSqr) return false;
            return true;
        }

        public bool IsWet(Vector2 world) =>
            toRiver[SampleIndex(world)] < settings.wetTreeReach
            || GridSampling.Bilinear(map.Marsh, map.Width, map.Height, frame.WorldToCell(world)) > 0.5f;

        int SampleIndex(Vector2 world)
        {
            int c = Mathf.Clamp(Mathf.RoundToInt(world.x / field.StepX), 0, field.Resolution - 1);
            int r = Mathf.Clamp(Mathf.RoundToInt(world.y / field.StepZ), 0, field.Resolution - 1);
            return r * field.Resolution + c;
        }

        static float[] MetresToMarked(HeightField field, System.Func<int, int, bool> isMarked)
        {
            int n = field.Resolution;
            var marked = new bool[n * n];
            for (int r = 0; r < n; r++)
            for (int c = 0; c < n; c++)
                marked[r * n + c] = isMarked(c, r);

            GridOps.NearestMarked(marked, n, n, out var samples, out _);
            float step = 0.5f * (field.StepX + field.StepZ);
            for (int i = 0; i < samples.Length; i++) samples[i] *= step;
            return samples;
        }
    }
}
