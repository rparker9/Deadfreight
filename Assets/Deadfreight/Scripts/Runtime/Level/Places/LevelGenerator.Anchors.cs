// Scripts/Runtime/LevelGen/LevelGenerator.Anchors.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 3: anchors, the places that cause roads. The playable area inside the border; where the
// road comes in, and the start place just inside; the site; settlements. Roads will grow from
// travel between them. Stops (the places players decide about) come after the roads.
public sealed partial class LevelGenerator
{
    const int PlaceTries = 5000;        // random cells tried for each kind of place
    const int SiteMargin = 6;           // cells the site keeps from the playable area's edge
    const int SettlementMargin = 2;     // ...and settlements
    const float EntryNoise = 0.02f;     // a little randomness, so the flattest spot on an edge isn't always the one

    void PlaceAnchors()
    {
        SetPlayArea();
        if (!PlaceEntry())
            return;
        if (!PlaceSite())
            return;
        PlaceSettlements();
    }

    /// <summary>The map minus the border on every side.</summary>
    void SetPlayArea()
    {
        int border = Mathf.Clamp(Mathf.RoundToInt(settings.borderWidth / settings.cellSize), 0, Mathf.Min(cellsX, cellsY) / 2 - 4);
        plan.PlayArea = new RectInt(border, border, cellsX - 2 * border, cellsY - 2 * border);
    }

    // ---------------- entry and start ----------------

    /// <summary>
    /// Picks a random edge and the best spot along its middle for the road in: dry all the way
    /// in, a buildable start place, and the gentlest ground. If that edge has none, tries the others.
    /// </summary>
    bool PlaceEntry()
    {
        var edges = new List<MapEdge> { MapEdge.West, MapEdge.East, MapEdge.South, MapEdge.North };
        Shuffle(edges);

        foreach (var edge in edges)
        {
            var entry = BestEntryOn(edge);
            if (entry == null)
                continue;

            plan.Entry = entry;
            plan.Start = AddPlace(PlaceKind.Start, StartCell(entry));
            return true;
        }

        plan.Failure = "No map edge has a dry road in and buildable ground for the start place.";
        return false;
    }

    LevelEntry BestEntryOn(MapEdge edge)
    {
        int edgeLength = edge is MapEdge.West or MapEdge.East ? cellsY : cellsX;
        int from = (int)(edgeLength * (1f - settings.entrySpan) / 2f);
        int to = (int)(edgeLength * (1f + settings.entrySpan) / 2f);

        LevelEntry best = null;
        float bestScore = float.MaxValue;
        for (int t = from; t < to; t++)
        {
            var entry = new LevelEntry(edge, EdgeCell(edge, t));
            var start = StartCell(entry);
            if (!Buildable(start) || !RoadInIsDry(entry, start))
                continue;

            float score = RoadInSlope(entry, start) + (float)rng.NextDouble() * EntryNoise;
            if (score < bestScore)
            {
                bestScore = score;
                best = entry;
            }
        }
        return best;
    }

    /// <summary>The cell <paramref name="t"/> along an edge (west to east, or south to north).</summary>
    Vector2Int EdgeCell(MapEdge edge, int t) => edge switch
    {
        MapEdge.West => new Vector2Int(0, t),
        MapEdge.East => new Vector2Int(cellsX - 1, t),
        MapEdge.South => new Vector2Int(t, 0),
        _ => new Vector2Int(t, cellsY - 1),
    };

    /// <summary>Straight inland from the entry, across the border, then Start Inset into the playable area.</summary>
    Vector2Int StartCell(LevelEntry entry)
    {
        int border = entry.Edge is MapEdge.West or MapEdge.East ? plan.PlayArea.xMin : plan.PlayArea.yMin;
        return entry.Inland(border + Mathf.RoundToInt(settings.startInset / settings.cellSize));
    }

    bool RoadInIsDry(LevelEntry entry, Vector2Int start)
    {
        for (var c = entry.Cell; c != start; c += entry.Inward)
            if (plan.IsWater(plan.Index(c)))
                return false;
        return true;
    }

    /// <summary>Average slope along the road in, with the start place's own slope counted double.</summary>
    float RoadInSlope(LevelEntry entry, Vector2Int start)
    {
        float sum = 0f;
        int count = 0;
        for (var c = entry.Cell; c != start; c += entry.Inward)
        {
            sum += plan.Slope[plan.Index(c)];
            count++;
        }
        return sum / Mathf.Max(1, count) + 2f * plan.Slope[plan.Index(start)];
    }

    // ---------------- site and settlements ----------------

    /// <summary>The heist: buildable ground in the playable area, at the set straight-line distance from the start.</summary>
    bool PlaceSite()
    {
        for (int t = 0; t < PlaceTries; t++)
        {
            var c = RandomCellInPlayArea(SiteMargin);
            float metres = Vector2Int.Distance(c, plan.Start.Cell) * settings.cellSize;
            if (metres < settings.siteMinDistance || metres > settings.siteMaxDistance || !Buildable(c))
                continue;

            plan.Site = AddPlace(PlaceKind.Site, c);
            return true;
        }

        plan.Failure = "No dry, buildable ground for the site at the set distance from the start.";
        return false;
    }

    /// <summary>Farmsteads scattered through the playable area, apart from each other and every other place.</summary>
    void PlaceSettlements()
    {
        int target = Mathf.RoundToInt(settings.settlements * region.settlementDensity);
        int placed = 0;
        for (int t = 0; t < PlaceTries && placed < target; t++)
        {
            var c = RandomCellInPlayArea(SettlementMargin);
            if (!Buildable(c) || MetresToNearestPlace(c) < settings.placeSpacing)
                continue;

            AddPlace(PlaceKind.Farmstead, c);
            placed++;
        }
    }

    // ---------------- helpers ----------------

    Place AddPlace(PlaceKind kind, Vector2Int cell)
    {
        var place = new Place { Kind = kind, Cell = cell };
        plan.Places.Add(place);
        return place;
    }

    /// <summary>In the playable area, flat enough, and dry with dry ground all round.</summary>
    bool Buildable(Vector2Int c)
    {
        if (!plan.InPlayArea(c) || plan.Slope[plan.Index(c)] >= settings.maxPlaceSlope)
            return false;

        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            var n = new Vector2Int(c.x + dx, c.y + dy);
            if (plan.InBounds(n) && plan.IsWater(plan.Index(n)))
                return false;
        }
        return true;
    }

    Vector2Int RandomCellInPlayArea(int margin)
    {
        var area = plan.PlayArea;
        return new Vector2Int(rng.Next(area.xMin + margin, area.xMax - margin), rng.Next(area.yMin + margin, area.yMax - margin));
    }

    float MetresToNearestPlace(Vector2Int c)
    {
        float best = float.MaxValue;
        foreach (var p in plan.Places)
            best = Mathf.Min(best, Vector2Int.Distance(c, p.Cell) * settings.cellSize);
        return best;
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
