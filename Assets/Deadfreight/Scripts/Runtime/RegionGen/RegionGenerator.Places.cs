// Scripts/Runtime/RegionGen/RegionGenerator.Places.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 2: where the road comes in, where it is going, and what is along the way.
public sealed partial class RegionGenerator
{
    const int SiteMargin = 10;              // cells the site keeps from the map edge
    const int SiteTries = 5000;
    const int StopMargin = 5;
    const int StopTries = 5000;
    const float StopEntryClearance = 1.5f;  // stops keep this many times the place spacing from the entry

    // Entry scoring: prefer flat ground, then low ground; a little noise so it isn't always the same spot.
    const float EntryLowGroundWeight = 0.3f;
    const float EntryNoise = 0.15f;

    static readonly PlaceKind[] StopKinds =
    {
        PlaceKind.Farm, PlaceKind.Farm, PlaceKind.Farm, PlaceKind.Church, PlaceKind.Mill, PlaceKind.Store,
        PlaceKind.Wreck, PlaceKind.Graveyard, PlaceKind.Shack, PlaceKind.Farm, PlaceKind.Still, PlaceKind.Farm,
    };

    // ---------------- entry ----------------

    /// <summary>
    /// Picks a random edge and the best spot along its middle where a short straight road can
    /// lead inland. If that edge has none (all water or cliffs), tries the others.
    /// </summary>
    bool PlaceEntry()
    {
        var edges = new List<MapEdge> { MapEdge.West, MapEdge.East, MapEdge.North, MapEdge.South };
        Shuffle(edges);

        foreach (var edge in edges)
        {
            var entry = BestEntryOn(edge);
            if (entry == null) continue;
            map.Entry = entry;
            return true;
        }

        map.Failure = "No edge has buildable ground for the road to come in.";
        return false;
    }

    /// <summary>The best entry along the middle of one edge, or null if none can take a road.</summary>
    RegionEntry BestEntryOn(MapEdge edge)
    {
        int edgeLength = edge is MapEdge.West or MapEdge.East ? height : width;
        int from = (int)(edgeLength * (1f - settings.entrySpan) / 2f);
        int to = (int)(edgeLength * (1f + settings.entrySpan) / 2f);

        RegionEntry best = null;
        float bestScore = float.MaxValue;
        for (int t = from; t < to; t++)
        {
            var candidate = new RegionEntry(EdgeCell(edge, t), edge);
            if (!StubBuildable(candidate)) continue;

            float score = StubSlope(candidate)
                + map.Elevation[map.Index(candidate.Cell)] * EntryLowGroundWeight
                + (float)rng.NextDouble() * EntryNoise;
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>The cell <paramref name="t"/> along an edge (top to bottom, or left to right).</summary>
    Vector2Int EdgeCell(MapEdge edge, int t) => edge switch
    {
        MapEdge.West => new Vector2Int(0, t),
        MapEdge.East => new Vector2Int(width - 1, t),
        MapEdge.North => new Vector2Int(t, 0),
        _ => new Vector2Int(t, height - 1),
    };

    // The stub is the straight road in from the edge: cells 0..entryStub inclusive.
    bool StubBuildable(RegionEntry entry)
    {
        for (int k = 0; k <= settings.entryStub; k++)
            if (!Buildable(entry.StubCell(k))) return false;
        return true;
    }

    float StubSlope(RegionEntry entry)
    {
        float sum = 0f;
        for (int k = 0; k <= settings.entryStub; k++) sum += map.Slope[map.Index(entry.StubCell(k))];
        return sum;
    }

    // ---------------- site ----------------

    /// <summary>
    /// The heist site: buildable ground inland, at a set straight-line distance from the entry.
    /// That distance is what sets the main road's length, whichever edge the entry is on.
    /// </summary>
    bool PlaceSite()
    {
        for (int t = 0; t < SiteTries; t++)
        {
            var c = new Vector2Int(rng.Next(SiteMargin, width - SiteMargin), rng.Next(SiteMargin, height - SiteMargin));
            float d = Distance(map.Entry.Cell, c);
            if (!Buildable(c) || d < settings.siteMinDistance || d > settings.siteMaxDistance) continue;

            map.Site = new Place { Kind = PlaceKind.Site, Cell = c };
            map.Places.Add(map.Site);
            return true;
        }

        map.Failure = "No buildable ground for the site at the set distance from the entry.";
        return false;
    }

    // ---------------- stops ----------------

    /// <summary>Farms, churches and the like, spread apart and kept clear of the entry.</summary>
    void PlaceStops()
    {
        int target = map.Places.Count + settings.stops;
        for (int t = 0; t < StopTries && map.Places.Count < target; t++)
        {
            var c = new Vector2Int(rng.Next(StopMargin, width - StopMargin), rng.Next(StopMargin, height - StopMargin));
            if (!Buildable(c)) continue;
            if (Distance(c, map.Entry.Cell) < settings.placeSpacing * StopEntryClearance) continue;
            if (DistanceToNearestPlace(c) < settings.placeSpacing) continue;

            var kind = StopKinds[map.Places.Count % StopKinds.Length];
            map.Places.Add(new Place { Kind = kind, Cell = c });
        }
    }

    float DistanceToNearestPlace(Vector2Int c)
    {
        float best = float.MaxValue;
        foreach (var p in map.Places) best = Mathf.Min(best, Distance(c, p.Cell));
        return best;
    }
}
