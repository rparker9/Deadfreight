// Scripts/Runtime/RegionGen/RegionGenerator.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a region the crew drives into from one edge and leaves the same way.
/// The work is split by stage across partial files:
///
///   1. Land    (RegionGenerator.Land.cs)    heightmap, water, rivers, marsh, forest, fog hollows.
///   2. Places  (RegionGenerator.Places.cs)  the entry on a random edge, the heist site across
///                                           the map, stops scattered between.
///   3. Roads   (RegionGenerator.Roads.cs)   the main road from entry to site, whose first stretch
///                                           is the shared trunk ending at the fork; alternates from
///                                           the fork, each bowing out through a via point and kept
///                                           inside a time window; lanes from the stops to the road.
///   4. Travel  (RegionGenerator.Travel.cs)  speed per cell; each route's miles, minutes, conditions.
///
/// Same seed + settings = same region. If the land leaves no room for an entry, a site or a
/// road between them, map.Failure says why and the map has no routes.
/// </summary>
public sealed partial class RegionGenerator
{
    // Shared by every stage.
    readonly RegionMap map;
    readonly RegionSettings settings;
    readonly System.Random rng;
    readonly int width, height, cellCount;

    // Set up by the roads stage.
    GridPathfinder finder;
    float[] baseCost;       // cost of building road on each cell, from the land alone
    float trunkSeconds;     // driving time of the trunk, added to every route's time

    public static RegionMap Generate(int seed, RegionSettings settings)
    {
        var generator = new RegionGenerator(seed, settings);
        generator.Run();
        return generator.map;
    }

    RegionGenerator(int seed, RegionSettings settings)
    {
        this.settings = settings;
        rng = new System.Random(seed);
        map = new RegionMap(settings.width, settings.height, settings.cellSize);
        width = map.Width;
        height = map.Height;
        cellCount = width * height;
    }

    void Run()
    {
        BuildLand();

        if (!PlaceEntry()) return;
        if (!PlaceSite()) return;
        PlaceStops();

        if (!BuildMainRoad()) return;
        BuildAlternates();
        BuildLanes();

        MeasureRoutes();
    }

    // ---------------- helpers used by several stages ----------------

    /// <summary>Dry, not too steep, inside the map: fine for a place or the entry road.</summary>
    bool Buildable(Vector2Int c)
    {
        if (!map.InBounds(c)) return false;
        int i = map.Index(c);
        return !map.Water[i] && !map.River[i] && map.Slope[i] < settings.maxPlaceSlope;
    }

    static float Distance(Vector2Int a, Vector2Int b) => Vector2Int.Distance(a, b);

    /// <summary>Grows a mask outward by <paramref name="radius"/> cells (4-neighbour steps).</summary>
    bool[] Dilate(bool[] mask, int radius)
    {
        var current = (bool[])mask.Clone();
        var next = new bool[mask.Length];
        for (int r = 0; r < radius; r++)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = y * width + x;
                next[i] = current[i]
                    || (x > 0 && current[i - 1]) || (x < width - 1 && current[i + 1])
                    || (y > 0 && current[i - width]) || (y < height - 1 && current[i + width]);
            }
            (current, next) = (next, current);
        }
        return current;
    }

    void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
