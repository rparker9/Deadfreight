// Scripts/Runtime/RegionBuild/RiverShaper.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>One river ready to build: points along its line with the water surface, half-width and depth at each.</summary>
public sealed class RiverChannel
{
    public RiverEnd End;
    public readonly List<Vector3> Points = new();       // y = water surface height
    public readonly List<float> HalfWidths = new();     // channel cut at least this wide (metres either side)
    public readonly List<float> Depths = new();
}

/// <summary>
/// Turns traced rivers into channels with water. The plan decides each river cell's width,
/// depth and water level (RegionGenerator.Channels); this draws them at full resolution:
///
///   1. Smooth: plan cells → simplified → spline → points every riverSpacing metres (PathSmoothing).
///      A river that joins another ends on that river's line.
///   2. Size and surface: the plan's values, read along the line between its cells. The surface
///      is held so it never rises downstream and ends level with the lake or the river it joins.
///   3. Press: cut a rounded bed under the surface, and raise any bank lower than the water so it
///      can't spill.
///
/// Rivers are shaped and pressed one at a time, in order, before roads: tributaries then meet the
/// river they join at its level, and roads cross the finished channels.
/// </summary>
public sealed class RiverShaper
{
    readonly HeightField field;
    readonly RegionFrame frame;
    readonly RegionMap map;
    readonly RegionBuildSettings settings;
    readonly float lakeLevel;
    readonly List<RiverChannel> built = new();

    // Scratch for pressing one river: for each sample it touches, the nearest point's distance and index.
    readonly float[,] nearestDistance;
    readonly int[,] nearestPoint;
    readonly List<Vector2Int> touched = new();

    public RiverShaper(HeightField field, RegionFrame frame, RegionMap map, RegionBuildSettings settings, float lakeLevel)
    {
        this.field = field;
        this.frame = frame;
        this.map = map;
        this.settings = settings;
        this.lakeLevel = lakeLevel;
        nearestDistance = new float[field.Resolution, field.Resolution];
        nearestPoint = new int[field.Resolution, field.Resolution];
        for (int r = 0; r < field.Resolution; r++)
        for (int c = 0; c < field.Resolution; c++)
            nearestDistance[r, c] = float.MaxValue;
    }

    public IReadOnlyList<RiverChannel> Built => built;

    // ---------- shape ----------

    public RiverChannel Shape(RiverPath path)
    {
        // 1. Smooth, joining the river it flows into where that river actually is.
        var corners = PathSmoothing.Simplify(path.Cells, settings.riverSimplifyCells);
        var points = new List<Vector2>(corners.Count);
        foreach (var corner in corners) points.Add(frame.CellToWorld(corner));

        float? endSurface = null;
        if (path.End == RiverEnd.Lake)
        {
            endSurface = lakeLevel;
        }
        else if (path.End == RiverEnd.JoinsRiver && NearestBuiltPoint(points[^1], out var join))
        {
            points[^1] = new Vector2(join.x, join.z);
            endSurface = join.y;
        }
        var line = PathSmoothing.SampleEvenly(PathSmoothing.SmoothSpline(points), settings.riverSpacing);

        // 2. Size and surface, from the plan.
        var surface = new float[line.Count];
        var widths = new float[line.Count];
        var depths = new float[line.Count];
        for (int i = 0; i < line.Count; i++)
        {
            float along = i / (float)Mathf.Max(1, line.Count - 1);
            surface[i] = AlongPath(map.RiverLevel, path, along);
            widths[i] = AlongPath(map.RiverWidth, path, along);
            depths[i] = AlongPath(map.RiverDepth, path, along);
        }
        KeepDownhill(surface, endSurface);

        // Cut at least half a height sample wider than the water, so narrow streams still get a channel.
        float sampleMargin = 0.5f * Mathf.Max(field.StepX, field.StepZ);
        var channel = new RiverChannel { End = path.End };
        for (int i = 0; i < line.Count; i++)
        {
            channel.Points.Add(new Vector3(line[i].x, surface[i], line[i].y));
            channel.HalfWidths.Add(widths[i] * 0.5f + sampleMargin);
            channel.Depths.Add(depths[i]);
        }

        built.Add(channel);
        return channel;
    }

    /// <summary>
    /// A plan value (per cell, NaN off rivers) part of the way along a river: between the two
    /// cells either side, skipping cells without a value (the lake a river ends in).
    /// </summary>
    float AlongPath(float[] values, RiverPath path, float along)
    {
        float at = along * (path.Cells.Count - 1);
        int before = Mathf.FloorToInt(at), after = Mathf.Min(before + 1, path.Cells.Count - 1);
        float a = ValueNear(values, path, before), b = ValueNear(values, path, after);
        return Mathf.Lerp(a, b, at - before);
    }

    /// <summary>The value at a path cell, or the nearest cell before it that has one.</summary>
    float ValueNear(float[] values, RiverPath path, int k)
    {
        for (int j = k; j >= 0; j--)
        {
            float value = values[map.Index(path.Cells[j])];
            if (!float.IsNaN(value)) return value;
        }
        return values[map.Index(path.Cells[0])];    // the source always has one
    }

    /// <summary>
    /// The water never rises downstream, never sits below where the river ends (the lake, or the
    /// river it joins), and meets that end exactly.
    /// </summary>
    void KeepDownhill(float[] surface, float? endSurface)
    {
        int n = surface.Length;
        float lowest = endSurface ?? lakeLevel;
        surface[0] = Mathf.Max(surface[0], lowest);
        for (int i = 1; i < n; i++) surface[i] = Mathf.Clamp(surface[i], lowest, surface[i - 1]);
        if (endSurface.HasValue) surface[n - 1] = endSurface.Value;
    }

    // ---------- press ----------

    /// <summary>
    /// Cuts the channel: a rounded bed, deepest in the middle, under the water. Beside it, ground
    /// lower than just above the water is raised, so the water can't spill; higher ground is left
    /// alone. Marks the channel so nothing grows in it.
    /// </summary>
    public void Press(RiverChannel channel)
    {
        // Find, for each height sample near the river, the closest point on its line.
        touched.Clear();
        for (int k = 0; k < channel.Points.Count; k++)
        {
            var at = new Vector2(channel.Points[k].x, channel.Points[k].z);
            float reach = channel.HalfWidths[k] + settings.riverBankWidth;
            field.SamplesAround(at, reach, out int c0, out int c1, out int r0, out int r1);
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                float d = Vector2.Distance(field.SampleToWorld(c, r), at);
                if (d > reach || d >= nearestDistance[r, c]) continue;
                if (nearestDistance[r, c] == float.MaxValue) touched.Add(new Vector2Int(c, r));
                nearestDistance[r, c] = d;
                nearestPoint[r, c] = k;
            }
        }

        foreach (var sample in touched)
        {
            int c = sample.x, r = sample.y, k = nearestPoint[r, c];
            float d = nearestDistance[r, c];
            float surface = channel.Points[k].y, halfWidth = channel.HalfWidths[k];
            float h = field.Heights[r, c];

            if (d <= halfWidth)
            {
                float across = d / halfWidth;
                float bed = surface - channel.Depths[k] * (1f - across * across);
                field.Heights[r, c] = Mathf.Min(h, bed);
                field.MarkChannel(c, r);
            }
            else
            {
                float lip = surface + settings.riverBankLift;
                if (h < lip)
                {
                    float back = RegionMath.SmoothStep(halfWidth, halfWidth + settings.riverBankWidth, d);
                    field.Heights[r, c] = Mathf.Lerp(lip, h, back);
                }
            }
            nearestDistance[r, c] = float.MaxValue;     // reset the scratch for the next river
        }
    }

    // ---------- lookups ----------

    /// <summary>The closest point (with its water height) of any river built so far.</summary>
    bool NearestBuiltPoint(Vector2 at, out Vector3 point)
    {
        point = default;
        float best = float.MaxValue;
        foreach (var river in built)
        foreach (var p in river.Points)
        {
            float d = (new Vector2(p.x, p.z) - at).sqrMagnitude;
            if (d < best)
            {
                best = d;
                point = p;
            }
        }
        return best < float.MaxValue;
    }
}
