// Scripts/Runtime/LevelGen/LevelLayout.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public enum PlaceKind { Site, Farm, Church, Mill, Store, Wreck, Graveyard, Shack, Still }

public enum MapEdge { West, East, North, South }

/// <summary>What a stretch of road is like, read from the land it passes through.</summary>
[Flags]
public enum RoadCondition { None = 0, Steep = 1 }

public sealed class Place
{
    public PlaceKind Kind;
    public Vector2Int Cell;
    public bool IsVia;          // an alternate route was built to pass through here
    public float PadHeight;     // metres: the flat ground the place stands on (planned with the roads)
}

/// <summary>
/// Where the road comes into the level, on the map edge. The crew drives in here
/// and must leave the same way.
/// </summary>
public sealed class LevelEntry
{
    public readonly Vector2Int Cell;    // on the edge itself
    public readonly MapEdge Edge;

    public LevelEntry(Vector2Int cell, MapEdge edge)
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
/// A generated level on a grid (x right, y down; index = y * Width + x). Cells hold the
/// land's height and slope; the entry, places and roads sit on top. Plain data for previews,
/// the crew's road map, and the 3D build.
///
/// Road layout: Entry → Trunk → Fork → Routes → Site. Every route shares the trunk, so a
/// trip is the trunk, one route in, any route back, and the trunk out.
/// </summary>
public sealed class LevelLayout
{
    public readonly int Width, Height;
    public readonly float CellSize;
    public float Relief;                 // metres from 0 to 1 on the Elevation scale

    public readonly float[] Elevation;   // 0-1
    public readonly float[] Slope;       // rise over run
    public readonly bool[] Road, MainRoad;

    // Roads: the height each road cell is built at, decided here (grade-limited, smoothed).
    // NaN off roads. Cut is ground minus road: positive = a cutting, negative = an embankment.
    public readonly float[] RoadHeight;  // metres
    public readonly float[] RoadCut;     // metres

    // Pads: flat ground around each place, at Place.PadHeight.
    public float PadRadius;              // metres: flat out to here...
    public float PadBlend;               // ...then back to the ground over this

    public float[] RoadCost;             // cost of building road on each cell, from the land alone (what the pathfinder used)

    public LevelEntry Entry;
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

    public LevelLayout(int width, int height, float cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        int n = width * height;
        Elevation = new float[n];
        Slope = new float[n];
        Road = new bool[n];
        MainRoad = new bool[n];
        RoadHeight = Filled(n, float.NaN);
        RoadCut = Filled(n, float.NaN);
    }

    static float[] Filled(int n, float value)
    {
        var array = new float[n];
        Array.Fill(array, value);
        return array;
    }

    /// <summary>Plan height of a cell in metres.</summary>
    public float Metres(int i) => Elevation[i] * Relief;

    public int Index(Vector2Int c) => c.y * Width + c.x;
    public Vector2Int CellAt(int index) => new(index % Width, index / Width);
    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;

    public RoadCondition ConditionAt(int i, float steepSlope) =>
        Slope[i] > steepSlope ? RoadCondition.Steep : RoadCondition.None;
}
