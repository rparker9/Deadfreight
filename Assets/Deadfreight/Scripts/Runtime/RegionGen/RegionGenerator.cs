// Scripts/Runtime/RegionGen/RegionGenerator.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a region map with land, roads, and stops. 
/// </summary>
public sealed partial class RegionGenerator
{
    readonly RegionMap map;
    readonly RegionSettings settings;
    readonly System.Random rng;
    readonly int width, height, cellCount;

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
        map = new RegionMap(settings.width, settings.height, settings.cellSize)
        {
            Relief = settings.relief,
            PadRadius = settings.padRadius,
            PadBlend = settings.padBlend,
        };
        width = map.Width;
        height = map.Height;
        cellCount = width * height;
    }

    void Run()
    {
        BuildLand();

        if (!PlaceEntry()) 
            return;
        if (!PlaceSite()) 
            return;

        PlaceStops();

        if (!BuildMainRoad()) 
            return;

        BuildAlternates();
        BuildLanes();
        PlanRoadHeights();
        PlanPads();

        MeasureRoutes();
    }

    /// <summary>Inside the map and not too steep: fine for a place or the entry road.</summary>
    bool Buildable(Vector2Int c) => map.InBounds(c) && map.Slope[map.Index(c)] < settings.maxPlaceSlope;

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
