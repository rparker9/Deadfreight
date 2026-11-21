// Scripts/Runtime/Level/Roads/Road.cs
using System.Collections.Generic;
using UnityEngine;

public enum RoadKind
{
    Main,       // from the map edge, through the start, to the site
    Branch,     // from a settlement to the nearest road
}

/// <summary>
/// A road in the plan: the cells it passes through, in order. Neighbouring points can be up to
/// a few cells apart, joined by a straight line; the builder smooths the line into a curve that
/// passes through every point.
///
/// A branch runs from its settlement to a point on an earlier road, so the roads join up.
/// </summary>
public sealed class Road
{
    public readonly RoadKind Kind;
    public readonly List<Vector2Int> Points;

    public Road(RoadKind kind, List<Vector2Int> points)
    {
        Kind = kind;
        Points = points;
    }

    /// <summary>Metres along the straight lines between the points (the curve is a little shorter).</summary>
    public float Length(float cellSize)
    {
        float cells = 0f;
        for (int i = 1; i < Points.Count; i++)
            cells += Vector2Int.Distance(Points[i - 1], Points[i]);
        return cells * cellSize;
    }
}
