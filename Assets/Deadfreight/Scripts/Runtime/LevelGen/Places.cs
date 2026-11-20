// Scripts/Runtime/LevelGen/Places.cs
using UnityEngine;

/// <summary>What a place is. Stops (the places players decide about) join this list later.</summary>
public enum PlaceKind
{
    Start,          // where the run begins and ends: just inside the playable area, on the road in
    Site,           // the heist
    Farmstead,      // an unmarked home: no business there, but travel to and from it shapes the roads
    Hamlet,         // a few buildings, added when the roads offer too few alternate routes
}

public enum MapEdge { West, East, South, North }

/// <summary>A place in the plan: what it is and which cell it stands on.</summary>
public sealed class Place
{
    public PlaceKind Kind;
    public Vector2Int Cell;
}

/// <summary>
/// Where the road comes into the level, on the map edge. From here it runs straight inland,
/// across the border, to the start place.
/// </summary>
public sealed class LevelEntry
{
    public readonly MapEdge Edge;
    public readonly Vector2Int Cell;        // on the edge itself

    public LevelEntry(MapEdge edge, Vector2Int cell)
    {
        Edge = edge;
        Cell = cell;
    }

    /// <summary>One cell step pointing into the map (y runs north).</summary>
    public Vector2Int Inward => Edge switch
    {
        MapEdge.West => new Vector2Int(1, 0),
        MapEdge.East => new Vector2Int(-1, 0),
        MapEdge.South => new Vector2Int(0, 1),
        _ => new Vector2Int(0, -1),
    };

    /// <summary>The cell <paramref name="steps"/> straight inland from the entry.</summary>
    public Vector2Int Inland(int steps) => Cell + Inward * steps;
}
