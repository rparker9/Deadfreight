// Scripts/Runtime/RegionBuild/RoadNetwork.cs
using System.Collections.Generic;
using UnityEngine;

public enum RoadClass { Main, Back, Lane }

/// <summary>
/// A stretch of road that shares no cells with any other, except where it joins one.
/// If it starts or ends on an earlier road, that end cell belongs to the earlier road: it is
/// where the two meet.
/// </summary>
public sealed class RoadSegment
{
    public RoadClass Class;
    public readonly List<Vector2Int> Cells = new();
    public bool StartsOnRoad;
    public bool EndsOnRoad;
}

/// <summary>
/// Turns the plan's routes and lanes into segments that don't overlap. Routes share road near
/// the fork and site and lanes run along roads, so building every path in full would stack
/// road meshes on top of each other. Paths are taken in priority order (main road, alternates,
/// lanes); each keeps only the cells no earlier path has claimed.
/// </summary>
public static class RoadNetwork
{
    public static List<RoadSegment> Split(RegionMap map)
    {
        var claimed = new HashSet<Vector2Int>();
        var segments = new List<RoadSegment>();

        // The main road is the trunk followed by the main route; they share the fork cell.
        var mainCells = new List<Vector2Int>(map.Trunk.Cells);
        var mainRoute = map.Routes[0].Cells;
        mainCells.AddRange(mainRoute.GetRange(1, mainRoute.Count - 1));
        AddPath(mainCells, RoadClass.Main, claimed, segments);

        for (int k = 1; k < map.Routes.Count; k++)
            AddPath(map.Routes[k].Cells, RoadClass.Back, claimed, segments);

        foreach (var lane in map.Lanes)
            AddPath(lane, RoadClass.Lane, claimed, segments);

        return segments;
    }

    /// <summary>
    /// Each run of unclaimed cells along the path becomes a segment. Where a run meets claimed
    /// road, the claimed cell is added as its end so the segment joins that road. An alternate's
    /// two halves (fork→via, via→site) can retrace the same cells; claiming as we go turns the
    /// retraced stretch into a junction instead of a second road on top of the first.
    /// </summary>
    static void AddPath(List<Vector2Int> path, RoadClass roadClass, HashSet<Vector2Int> claimed, List<RoadSegment> segments)
    {
        RoadSegment current = null;
        for (int i = 0; i < path.Count; i++)
        {
            var cell = path[i];
            bool onRoad = claimed.Contains(cell);

            if (!onRoad && current == null)
            {
                // A new run. Unless the path starts here, the cell before it is claimed road: a junction.
                current = new RoadSegment { Class = roadClass };
                if (i > 0)
                {
                    current.Cells.Add(path[i - 1]);
                    current.StartsOnRoad = true;
                }
            }

            if (!onRoad)
            {
                current.Cells.Add(cell);
                claimed.Add(cell);      // claimed at once, so a path that doubles back joins itself
            }
            else if (current != null)
            {
                // The run has reached claimed road: end it at that junction.
                current.Cells.Add(cell);
                current.EndsOnRoad = true;
                Keep(current, segments);
                current = null;
            }
        }
        if (current != null) Keep(current, segments);
    }

    static void Keep(RoadSegment segment, List<RoadSegment> segments)
    {
        if (segment.Cells.Count >= 2) segments.Add(segment);
    }
}
