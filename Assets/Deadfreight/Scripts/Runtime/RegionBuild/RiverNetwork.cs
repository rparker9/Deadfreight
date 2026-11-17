// Scripts/Runtime/RegionBuild/RiverNetwork.cs
using System.Collections.Generic;
using UnityEngine;

public enum RiverEnd { MapEdge, Lake, JoinsRiver }

/// <summary>One river, as plan cells from its source downstream, and where it ends.</summary>
public sealed class RiverPath
{
    public readonly List<Vector2Int> Cells = new();
    public RiverEnd End;
}

/// <summary>
/// Traces the plan's rivers into lines. Every river starts at a source (a river cell nothing
/// upstream flows into) and follows each cell's downhill neighbour (RegionMap.FlowTo) until it
/// reaches a lake, runs off the map, or meets a river already traced. Where it meets one, the
/// meeting cell is its last cell, so the two join.
/// </summary>
public static class RiverNetwork
{
    public static List<RiverPath> Trace(RegionMap map, float riverArea)
    {
        int n = map.Width * map.Height;

        // Cells on a river's flow. Corner cells the plan added to close diagonal gaps drain too
        // little to count, so they don't start rivers of their own.
        var flowing = new bool[n];
        for (int i = 0; i < n; i++) flowing[i] = map.River[i] && !map.Water[i] && map.Drainage[i] >= riverArea;

        var fedFromUpstream = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int down = map.FlowTo[i];
            if (flowing[i] && down >= 0) fedFromUpstream[down] = true;
        }

        var traced = new bool[n];
        var rivers = new List<RiverPath>();
        for (int source = 0; source < n; source++)
        {
            if (!flowing[source] || fedFromUpstream[source]) continue;
            var river = Follow(map, source, flowing, traced);
            if (river.Cells.Count >= 2) rivers.Add(river);      // a single cell has no line to follow
        }
        return rivers;
    }

    static RiverPath Follow(RegionMap map, int source, bool[] flowing, bool[] traced)
    {
        var river = new RiverPath { End = RiverEnd.MapEdge };
        int current = source;
        river.Cells.Add(map.CellAt(current));
        traced[current] = true;

        while (true)
        {
            int next = map.FlowTo[current];
            if (next < 0) break;                            // off the map (or a pit): ends here

            river.Cells.Add(map.CellAt(next));
            if (map.Water[next]) { river.End = RiverEnd.Lake; break; }
            if (traced[next]) { river.End = RiverEnd.JoinsRiver; break; }
            if (!flowing[next]) break;                      // shouldn't happen: drainage only grows downstream

            traced[next] = true;
            current = next;
        }
        return river;
    }
}
