// Scripts/Runtime/RegionBuild/Rocks/RockPlacing.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lays a RockSet's rocks over the region as scene objects, landform by landform:
///
///   Outcrops      cliffs, steep ground (the paint's rock slopes) and ridge tops
///   Scree         out from the foot of the escarpment's cliffs
///   Riverbeds     in river water and up to riverReach from it
///   Forest floor  wherever the biome map has forest (deep or edge)
///   Open ground   wherever the biome map has field
///
/// Biggest landforms first, so later rocks fit around earlier ones. Rocks only overlap a little
/// (Nestle), enough for a cluster to sit together. Each rock is sunk into the ground a little
/// and tilted with it, and given a mesh collider if its prefab has none. Nothing goes on roads,
/// pads or under lakes. If more than maxRocks qualify, a random selection is kept, thinning the
/// whole region evenly.
///
/// The kept rocks' footprints are handed back so trees and foliage can keep clear of them.
/// </summary>
public static class RockPlacing
{
    const int SeedSalt = 0x40c5;
    const float Nestle = 0.75f;     // rocks may overlap by a quarter of their footprint, so clusters sit against each other

    /// <summary>One rock decided on, before it's made into a scene object.</summary>
    struct Placement
    {
        public GameObject Prefab;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Scale;
        public Vector2 Ground;  // where it stands, for its footprint
        public float Radius;
    }

    /// <summary>A prefab's size, measured once: how far it reaches out from its pivot and how tall it is.</summary>
    struct Size
    {
        public float Radius;    // metres, on the ground, at scale 1
        public float Bottom;    // metres from the pivot down to the lowest point (negative if below)
        public float Height;
    }

    /// <returns>The placed rocks' footprints (Count = how many were placed).</returns>
    public static Footprints Place(Transform parent, GroundSurvey survey, BiomeMap biomes, int seed)
    {
        var set = survey.Settings.rocks;
        if (set == null) return new Footprints();

        var rng = new System.Random(seed ^ SeedSalt);
        var sizes = new Dictionary<GameObject, Size>();
        var placements = new List<Placement>();
        var settings = survey.Settings;
        var taken = new Footprints();     // everything decided on, so later rocks fit around earlier ones

        void Scatter(RockScatter scatter, System.Func<Vector2, float> strength)
        {
            if (scatter == null || scatter.IsEmpty) return;
            ScatterOne(scatter, strength, survey, taken, sizes, placements, rng);
        }

        Scatter(set.outcrops, world =>
        {
            var cell = survey.Cell(world);
            float steep = RegionMath.SmoothStep(settings.rockSlopeStart, settings.rockSlopeFull, survey.Slope(world));
            return Mathf.Max(survey.Cliff(cell), steep, survey.Ridge(cell) * settings.ridgeStony);
        });
        Scatter(set.scree, world => survey.Scree(survey.Cell(world)));
        Scatter(set.riverbeds, world => TerrainPaint.Within(survey.FromRiver(world), set.riverReach));
        Scatter(set.forestFloor, world => biomes.Weight(world, BiomeRole.DeepForest) + biomes.Weight(world, BiomeRole.ForestEdge));
        Scatter(set.openGround, world => biomes.Weight(world, BiomeRole.Field));

        if (placements.Count > settings.maxRocks) KeepRandom(placements, settings.maxRocks, rng);

        var holder = new GameObject("Rocks").transform;
        holder.SetParent(parent, false);
        var kept = new Footprints();
        foreach (var placement in placements)
        {
            Make(placement, holder);
            kept.Add(placement.Ground, placement.Radius);
        }
        return kept;
    }

    // ---------- deciding where ----------

    static void ScatterOne(RockScatter scatter, System.Func<Vector2, float> strength, GroundSurvey survey, Footprints taken,
        Dictionary<GameObject, Size> sizes, List<Placement> placements, System.Random rng)
    {
        float spacing = Mathf.Max(1f, scatter.spacing);
        int columns = Mathf.FloorToInt(survey.Field.SizeX / spacing), rows = Mathf.FloorToInt(survey.Field.SizeZ / spacing);

        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            var centre = new Vector2((column + (float)rng.NextDouble()) * spacing, (row + (float)rng.NextDouble()) * spacing);
            if (rng.NextDouble() >= scatter.chance * Mathf.Clamp01(strength(centre))) continue;

            int count = rng.Next(Mathf.Max(1, scatter.clusterSize.x), Mathf.Max(1, scatter.clusterSize.y) + 1);
            float firstScale = 0f;
            for (int i = 0; i < count; i++)
            {
                var piece = Pick(scatter.pieces, rng);
                var size = SizeOf(piece.prefab, sizes);

                float scale = Mathf.Lerp(piece.scale.x, piece.scale.y, (float)rng.NextDouble());
                if (i == 0) firstScale = scale;
                else scale = firstScale * scatter.followerScale * Mathf.Lerp(0.7f, 1.3f, (float)rng.NextDouble());

                var world = i == 0 ? centre : centre + RandomInCircle(rng) * scatter.clusterRadius;
                float radius = size.Radius * scale;
                if (!Allowed(world, radius, survey) || taken.Overlaps(world, radius * Nestle)) continue;

                taken.Add(world, radius);
                placements.Add(Decide(piece.prefab, size, world, scale, radius, scatter, survey, rng));
            }
        }
    }

    /// <summary>Rocks stay off roads (with a margin), place pads and lakes, and inside the region.</summary>
    static bool Allowed(Vector2 world, float radius, GroundSurvey survey)
    {
        if (world.x < 0f || world.y < 0f || world.x > survey.Field.SizeX || world.y > survey.Field.SizeZ) return false;
        float margin = radius + survey.Settings.rockRoadClearance;
        if (survey.FromRoad(world) < margin) return false;
        if (survey.NearPad(world, margin)) return false;
        return !survey.BelowLake(world);
    }

    static Placement Decide(GameObject prefab, Size size, Vector2 world, float scale, float radius, RockScatter scatter,
        GroundSurvey survey, System.Random rng)
    {
        var up = Vector3.Slerp(Vector3.up, survey.Normal(world), scatter.alignToGround);
        var tilt = Quaternion.FromToRotation(Vector3.up, up);
        var yaw = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);

        // Lift the pivot so the lowest point meets the ground, then sink by a share of the height.
        float lift = -(size.Bottom + scatter.sink * size.Height) * scale;
        var position = new Vector3(world.x, survey.Height(world), world.y) + up * lift;

        return new Placement { Prefab = prefab, Position = position, Rotation = tilt * yaw, Scale = scale, Ground = world, Radius = radius };
    }

    // ---------- making the scene objects ----------

    static void Make(Placement placement, Transform holder)
    {
        var rock = Object.Instantiate(placement.Prefab, holder);
        rock.name = placement.Prefab.name;
        rock.transform.SetLocalPositionAndRotation(placement.Position, placement.Rotation);
        rock.transform.localScale = Vector3.one * placement.Scale;

        if (rock.GetComponentInChildren<Collider>() != null) return;
        foreach (var filter in rock.GetComponentsInChildren<MeshFilter>())
            if (filter.sharedMesh != null) filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
    }

    /// <summary>Measures a prefab once (by making a copy and reading its renderers' bounds), then remembers it.</summary>
    static Size SizeOf(GameObject prefab, Dictionary<GameObject, Size> sizes)
    {
        if (sizes.TryGetValue(prefab, out var known)) return known;

        var copy = Object.Instantiate(prefab);
        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        copy.transform.localScale = Vector3.one;
        var renderers = copy.GetComponentsInChildren<Renderer>();

        var size = new Size { Radius = 0.5f, Bottom = 0f, Height = 1f };
        if (renderers.Length > 0)
        {
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            size.Radius = Mathf.Max(Mathf.Abs(bounds.center.x) + bounds.extents.x, Mathf.Abs(bounds.center.z) + bounds.extents.z);
            size.Bottom = bounds.min.y;
            size.Height = bounds.size.y;
        }

        if (Application.isPlaying) Object.Destroy(copy);
        else Object.DestroyImmediate(copy);
        sizes[prefab] = size;
        return size;
    }

    // ---------- helpers ----------

    /// <summary>A random piece, chosen by weight, from those with a prefab.</summary>
    static RockPiece Pick(RockPiece[] pieces, System.Random rng)
    {
        float total = 0f;
        foreach (var piece in pieces)
            if (piece != null && piece.prefab != null) total += piece.weight;

        float pick = (float)rng.NextDouble() * total;
        RockPiece last = null;
        foreach (var piece in pieces)
        {
            if (piece == null || piece.prefab == null || piece.weight <= 0f) continue;
            last = piece;
            pick -= piece.weight;
            if (pick <= 0f) return piece;
        }
        return last;    // only reached through rounding; IsEmpty guarantees there is one
    }

    static Vector2 RandomInCircle(System.Random rng)
    {
        float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float distance = Mathf.Sqrt((float)rng.NextDouble());    // square root: spread evenly, not bunched in the middle
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    /// <summary>Keeps a random <paramref name="count"/> of the placements (a partial shuffle).</summary>
    static void KeepRandom(List<Placement> placements, int count, System.Random rng)
    {
        for (int i = 0; i < count; i++)
        {
            int j = rng.Next(i, placements.Count);
            (placements[i], placements[j]) = (placements[j], placements[i]);
        }
        placements.RemoveRange(count, placements.Count - count);
    }
}
