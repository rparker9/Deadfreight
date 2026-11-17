// Scripts/Runtime/RegionGen/RegionMap.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlaceKind { Site, Farm, Church, Mill, Store, Wreck, Graveyard, Shack, Still }

public enum MapEdge { West, East, North, South }

/// <summary>What a stretch of road is like, read from the land it passes through.</summary>
[Flags]
public enum RoadCondition { None = 0, Mud = 1, Ford = 2, Steep = 4, Forest = 8, Fog = 16 }

public sealed class Place
{
    public PlaceKind Kind;
    public Vector2Int Cell;
    public bool IsVia;          // an alternate route was built to pass through here
}

/// <summary>
/// Where the road comes into the region, on the map edge. The crew drives in here
/// and must leave the same way.
/// </summary>
public sealed class RegionEntry
{
    public readonly Vector2Int Cell;    // on the edge itself
    public readonly MapEdge Edge;

    public RegionEntry(Vector2Int cell, MapEdge edge)
    {
        Cell = cell;
        Edge = edge;
    }

    /// <summary>One cell step pointing into the map (y runs down).</summary>
    public Vector2Int Inward => Edge switch
    {
        MapEdge.West => new Vector2Int(1, 0),
        MapEdge.East => new Vector2Int(-1, 0),
        MapEdge.North => new Vector2Int(0, 1),
        _ => new Vector2Int(0, -1),
    };

    /// <summary>The cell <paramref name="steps"/> straight inland from the entry.</summary>
    public Vector2Int StubCell(int steps) => Cell + Inward * steps;
}

/// <summary>
/// One stretch of road with its length, driving time and conditions.
/// For the main road and alternates, Cells run fork first and end at the site;
/// the One Way figures add the trunk, so they cover entry to site.
/// </summary>
public sealed class Route
{
    public string Name;
    public Place Via;                   // alternates only
    public readonly List<Vector2Int> Cells = new();

    public float LegMiles;              // this route's own cells
    public float LegMinutes;            // driving only
    public float OneWayMiles;           // entry to site: trunk + leg
    public float OneWayMinutes;
    public float RoundTripMinutes => OneWayMinutes * 2f;   // same route both ways

    public readonly Dictionary<RoadCondition, float> Share = new();   // 0-1 of the leg's cells
}

/// <summary>
/// A generated region on a grid (x right, y down; index = y * Width + x). Cells hold the
/// land layers; the entry, places and roads sit on top. Plain data for previews, the
/// crew's road map, and later the 3D build.
///
/// Road layout: Entry → Trunk → Fork → Routes → Site. Every route shares the trunk, so a
/// trip is the trunk, one route in, any route back, and the trunk out.
/// </summary>
public sealed class RegionMap
{
    public readonly int Width, Height;
    public readonly float CellSize;

    public readonly float[] Elevation;   // 0-1
    public readonly float[] Slope;       // rise over run
    public readonly float[] Drainage;    // cells draining through each cell; rivers are where it's large
    public readonly int[] FlowTo;        // the cell each cell drains into, or -1 (lakes, and cells draining off the map)
    public readonly bool[] Water, River, Marsh, Forest, Hollow, Road, MainRoad;

    // The escarpment, if this region has one: a raised upland with a steep face along a line.
    public bool HasEscarpment;
    public readonly float[] Scarp;       // signed cells from the face: positive on the upland side
    public readonly float[] ScarpPass;   // 0 = sheer face, 1 = the middle of a pass (a long ramp)
    public readonly bool[] Cliff;        // face cells roads can't climb: everything but the passes
    public float ScarpRise;              // metres the upland stands above the lowland
    public int Cuttings;                 // road cells cut through the face because no pass could be reached

    public RegionEntry Entry;
    public Place Site;
    public Vector2Int Fork;              // end of the trunk, where the routes split
    public Route Trunk;                  // entry to fork, shared by every route
    public readonly List<Place> Places = new();   // the site, stops and vias
    public readonly List<Route> Routes = new();   // Routes[0] is the main road
    public readonly List<List<Vector2Int>> Lanes = new();   // each runs from a stop to the road

    /// <summary>Why generation stopped early, or null if it finished. Without it there are no routes.</summary>
    public string Failure;

    /// <summary>Alternates thrown away for falling outside the time window (for tuning).</summary>
    public int RejectedAlternates;

    public RegionMap(int width, int height, float cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        int n = width * height;
        Elevation = new float[n];
        Slope = new float[n];
        Drainage = new float[n];
        FlowTo = new int[n];
        Water = new bool[n];
        River = new bool[n];
        Marsh = new bool[n];
        Forest = new bool[n];
        Hollow = new bool[n];
        Road = new bool[n];
        MainRoad = new bool[n];
        Scarp = new float[n];
        ScarpPass = new float[n];
        Cliff = new bool[n];
    }

    public int Index(Vector2Int c) => c.y * Width + c.x;
    public Vector2Int CellAt(int index) => new(index % Width, index / Width);
    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

    public RoadCondition ConditionAt(int i, float steepSlope)
    {
        var c = RoadCondition.None;
        if (Marsh[i]) c |= RoadCondition.Mud;
        if (River[i]) c |= RoadCondition.Ford;
        if (Slope[i] > steepSlope) c |= RoadCondition.Steep;
        if (Forest[i]) c |= RoadCondition.Forest;
        if (Hollow[i]) c |= RoadCondition.Fog;
        return c;
    }
}
