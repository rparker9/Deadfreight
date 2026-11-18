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
    public float PadHeight;     // metres: the flat ground the place stands on (planned with the roads)
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

/// <summary>The shape of a region's terrace shelves, decided by the plan (RegionSettings → terraces).</summary>
public sealed class TerraceSpec
{
    public float Step;          // metres of height per shelf
    public float Riser;         // 0-1: share of each step taken by the rise to the next shelf
    public float Tilt;          // 0-1: share of the hill's own slope a shelf keeps
    public float Wobble;        // metres the shelf edges wander off the contour lines...
    public float WobbleScale;   // ...over this distance (metres)
    public Vector2 WobbleOffset;    // where in the wobble noise this region sits
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
    public float Relief;                 // metres from 0 to 1 on the Elevation scale
    public float LakeLevel;              // metres: the lakes' water surface

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
    public float FaceHalfWidth;          // cells: half the width of the face itself, as a cliff (not the routing band)
    public float PassRamp;               // cells: half the width of the climb at a pass
    public int Cuttings;                 // road cells cut through the face because no pass could be reached

    // Terraces: hillsides stepped into shelves. The plan decides where and how; the 3D build draws
    // the shelves from exactly this. Stepping averages out over each full step, so the 3D ground
    // keeps the plan's cell heights to within a fraction of a step.
    public readonly float[] Terrace;     // 0-1: how strongly each cell is terraced
    public TerraceSpec Terraces;         // the shelves' shape; null if this region has none

    // Rivers: the channel the 3D build cuts, decided here. NaN off rivers.
    public readonly float[] RiverWidth;  // metres
    public readonly float[] RiverDepth;  // metres below the water
    public readonly float[] RiverLevel;  // metres: the water surface, never rising downstream

    // Biomes: one per cell, from RegionSettings' rules (first match wins). The 3D build blends
    // them at cell borders and uses each one's trees, foliage and fine ground noise.
    public readonly int[] BiomeIndex;    // per cell: index into Biomes, or -1 where nothing grows (lakes, rivers)
    public readonly List<Biome> Biomes = new();     // each distinct biome used, in rule order

    // Roads: the height each road cell is built at, decided here (grade-limited, smoothed).
    // NaN off roads. Cut is ground minus road: positive = a cutting, negative = an embankment.
    public readonly float[] RoadHeight;  // metres
    public readonly float[] RoadCut;     // metres

    // Pads: flat ground around each place, at Place.PadHeight.
    public float PadRadius;              // metres: flat out to here...
    public float PadBlend;               // ...then back to the ground over this

    public float[] RoadCost;             // cost of building road on each cell, from the land alone (what the pathfinder used)

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
        Terrace = new float[n];
        RiverWidth = Filled(n, float.NaN);
        RiverDepth = Filled(n, float.NaN);
        RiverLevel = Filled(n, float.NaN);
        BiomeIndex = Filled(n, -1);
        RoadHeight = Filled(n, float.NaN);
        RoadCut = Filled(n, float.NaN);
    }

    static T[] Filled<T>(int n, T value)
    {
        var array = new T[n];
        System.Array.Fill(array, value);
        return array;
    }

    /// <summary>
    /// How far up the escarpment's rise the ground is at a point: 0 on the lowland, 1 on the
    /// upland, in metres times ScarpRise. A cliff FaceHalfWidth either side of the face, or a
    /// ramp PassRamp either side at a pass. The plan's heights are built with this, and the 3D
    /// build evaluates the same function between cell centres, so they agree.
    /// </summary>
    public float FaceLift(float scarp, float pass)
    {
        if (!HasEscarpment) return 0f;
        float halfWidth = Mathf.Lerp(FaceHalfWidth, PassRamp, Mathf.Clamp01(pass));
        return ScarpRise * RegionMath.SmoothStep(-halfWidth, halfWidth, scarp);
    }

    /// <summary>Plan height of a cell in metres.</summary>
    public float Metres(int i) => Elevation[i] * Relief;

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
