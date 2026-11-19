// Scripts/Runtime/LevelBuild/RoadShaper.cs
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>One road ready to build: its centre line as evenly spaced world points with final heights.</summary>
public sealed class RoadPath
{
    public RoadClass Class;
    public float HalfWidth;
    public readonly List<Vector3> Points = new();
    public Spline Spline;       // the smoothed centre line, knots lifted to road height; for the scene and later tools
}

/// <summary>
/// Turns road segments into drivable roads and sets them into the ground. For each segment:
///
///   1. Simplify: drop cell corners that sit close to a straight line (the plan zigzags in 50 m steps).
///   2. Smooth: a spline through what's left. Ends that join an earlier road snap onto it.
///   3. Sample: points every sampleSpacing metres along the spline.
///   4. Profile: the plan's road heights (LevelGenerator.RoadHeights), read along the line
///      between its cells, lightly smoothed so the 50 m steps don't show, and held to the plan's
///      maxGrade. Ends that join an earlier road take its height.
///   5. Press: flatten the ground to the road across its width, blending back over the shoulder.
///
/// Segments must be shaped and pressed in order, one at a time: each reads the ground the
/// earlier ones left, which is how roads meet at the same height.
/// </summary>
public sealed class RoadShaper
{
    readonly HeightField field;
    readonly LevelFrame frame;
    readonly LevelBuildSettings settings;
    readonly LevelLayout map;
    readonly float maxGrade;            // the plan's
    readonly List<RoadPath> built = new();

    // Scratch for pressing one road: for each sample it touches, the nearest centre point's distance and height.
    readonly float[,] nearestDistance;
    readonly float[,] nearestHeight;
    readonly List<Vector2Int> touched = new();

    const float SmoothingLength = 20f;  // metres: just enough to round off the plan's cell-to-cell corners

    public RoadShaper(HeightField field, LevelFrame frame, LevelLayout map, LevelBuildSettings settings, float maxGrade)
    {
        this.field = field;
        this.frame = frame;
        this.map = map;
        this.settings = settings;
        this.maxGrade = maxGrade;
        nearestDistance = new float[field.Resolution, field.Resolution];
        nearestHeight = new float[field.Resolution, field.Resolution];
        for (int r = 0; r < field.Resolution; r++)
        for (int c = 0; c < field.Resolution; c++)
            nearestDistance[r, c] = float.MaxValue;
    }

    public IReadOnlyList<RoadPath> Built => built;

    // ---------- shape ----------

    public RoadPath Shape(RoadSegment segment)
    {
        // 1. Simplify, then move to world space.
        var corners = PathSmoothing.Simplify(segment.Cells, settings.simplifyCells);
        var points = new List<Vector2>(corners.Count);
        foreach (var corner in corners) points.Add(frame.CellToWorld(corner));

        // 2. Join earlier roads where their smoothed centre line actually is, not at the cell centre.
        float? startHeight = null, endHeight = null;
        if (segment.StartsOnRoad && NearestBuiltPoint(points[0], out var start))
        {
            points[0] = new Vector2(start.x, start.z);
            startHeight = start.y;
        }
        if (segment.EndsOnRoad && NearestBuiltPoint(points[^1], out var end))
        {
            points[^1] = new Vector2(end.x, end.z);
            endHeight = end.y;
        }
        var spline = PathSmoothing.SmoothSpline(points);

        // 3. Sample, 4. profile.
        var line = PathSmoothing.SampleEvenly(spline, settings.sampleSpacing);
        var heights = Profile(segment, line.Count, startHeight, endHeight);

        var road = new RoadPath
        {
            Class = segment.Class,
            HalfWidth = settings.RoadWidth(segment.Class) * 0.5f,
            Spline = spline,
        };
        for (int i = 0; i < line.Count; i++) road.Points.Add(new Vector3(line[i].x, heights[i], line[i].y));
        LiftKnots(road);

        built.Add(road);
        return road;
    }

    /// <summary>
    /// The road's height along its length: the plan's height for each cell, read between cells,
    /// averaged over SmoothingLength, then held to maxGrade (a forward and a backward pass).
    /// Pinned ends keep the height of the road they join.
    /// </summary>
    float[] Profile(RoadSegment segment, int count, float? startHeight, float? endHeight)
    {
        var heights = new float[count];
        for (int i = 0; i < count; i++) heights[i] = PlanHeightAlong(segment, i / (float)Mathf.Max(1, count - 1));
        Pin(heights, startHeight, endHeight);

        int halfWindow = Mathf.Max(1, Mathf.RoundToInt(SmoothingLength / settings.sampleSpacing * 0.5f));
        for (int pass = 0; pass < 2; pass++)
        {
            heights = PathSmoothing.MovingAverage(heights, halfWindow);
            Pin(heights, startHeight, endHeight);
        }

        float maxRise = maxGrade * settings.sampleSpacing;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < count; i++)
                heights[i] = Mathf.Clamp(heights[i], heights[i - 1] - maxRise, heights[i - 1] + maxRise);
            for (int i = count - 2; i >= 0; i--)
                heights[i] = Mathf.Clamp(heights[i], heights[i + 1] - maxRise, heights[i + 1] + maxRise);
            Pin(heights, startHeight, endHeight);
        }
        return heights;
    }

    /// <summary>The plan's road height part of the way (0-1) along a segment, between the cells either side.</summary>
    float PlanHeightAlong(RoadSegment segment, float along)
    {
        var cells = segment.Cells;
        float at = along * (cells.Count - 1);
        int before = Mathf.FloorToInt(at), after = Mathf.Min(before + 1, cells.Count - 1);
        return Mathf.Lerp(PlanHeight(cells[before]), PlanHeight(cells[after]), at - before);
    }

    /// <summary>A road cell's planned height (the ground's, should a cell somehow have none).</summary>
    float PlanHeight(Vector2Int cell)
    {
        int i = map.Index(cell);
        return float.IsNaN(map.RoadHeight[i]) ? map.Metres(i) : map.RoadHeight[i];
    }

    static void Pin(float[] heights, float? start, float? end)
    {
        if (start.HasValue) heights[0] = start.Value;
        if (end.HasValue) heights[^1] = end.Value;
    }

    /// <summary>Raises each spline knot to the road height at that spot, so the scene spline matches the road.</summary>
    static void LiftKnots(RoadPath road)
    {
        for (int k = 0; k < road.Spline.Count; k++)
        {
            var knot = road.Spline[k];
            var at = new Vector2(knot.Position.x, knot.Position.z);
            float y = NearestPoint(road.Points, at).y;
            knot.Position = new float3(knot.Position.x, y, knot.Position.z);
            road.Spline[k] = knot;
        }
    }

    // ---------- press ----------

    /// <summary>
    /// Flattens the ground to the road: level with the road across its width (plus one sample,
    /// so the terrain never pokes through the mesh edge), then blending back to natural ground
    /// over the shoulder. Ground under an earlier road is left as it is.
    /// </summary>
    public void Press(RoadPath road)
    {
        float core = road.HalfWidth + Mathf.Max(field.StepX, field.StepZ);
        float reach = core + settings.shoulder;

        // Find, for each sample near the road, the closest centre point.
        touched.Clear();
        foreach (var point in road.Points)
        {
            var at = new Vector2(point.x, point.z);
            field.SamplesAround(at, reach, out int c0, out int c1, out int r0, out int r1);
            for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                float d = Vector2.Distance(field.SampleToWorld(c, r), at);
                if (d > reach || d >= nearestDistance[r, c]) continue;
                if (nearestDistance[r, c] == float.MaxValue) touched.Add(new Vector2Int(c, r));
                nearestDistance[r, c] = d;
                nearestHeight[r, c] = point.y;
            }
        }

        // Move those samples toward the road height, fully inside the core.
        foreach (var sample in touched)
        {
            int c = sample.x, r = sample.y;
            float d = nearestDistance[r, c];
            if (!field.IsRoadCore(c, r))
            {
                float weight = 1f - GridMath.SmoothStep(core, reach, d);
                field.Heights[r, c] = Mathf.Lerp(field.Heights[r, c], nearestHeight[r, c], weight);
                if (d <= core) field.MarkRoadCore(c, r);
            }
            nearestDistance[r, c] = float.MaxValue;     // reset the scratch for the next road
        }
    }

    // ---------- lookups ----------

    /// <summary>The closest centre point of any road built so far, by horizontal distance.</summary>
    public bool NearestBuiltPoint(Vector2 at, out Vector3 point)
    {
        point = default;
        float best = float.MaxValue;
        foreach (var road in built)
        foreach (var p in road.Points)
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

    /// <summary>The closest road to a spot: centre point, direction of travel there, and that road's half width.</summary>
    public bool NearestRoad(Vector2 at, out Vector3 point, out Vector3 direction, out float halfWidth)
    {
        point = default;
        direction = Vector3.forward;
        halfWidth = 0f;
        float best = float.MaxValue;
        foreach (var road in built)
        {
            var points = road.Points;
            for (int i = 0; i < points.Count; i++)
            {
                float d = (new Vector2(points[i].x, points[i].z) - at).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                point = points[i];
                halfWidth = road.HalfWidth;
                var ahead = points[Mathf.Min(i + 1, points.Count - 1)] - points[Mathf.Max(i - 1, 0)];
                ahead.y = 0f;
                if (ahead.sqrMagnitude > 1e-6f) direction = ahead.normalized;
            }
        }
        return best < float.MaxValue;
    }

    static Vector3 NearestPoint(List<Vector3> points, Vector2 at)
    {
        var best = points[0];
        float bestSqr = float.MaxValue;
        foreach (var p in points)
        {
            float d = (new Vector2(p.x, p.z) - at).sqrMagnitude;
            if (d < bestSqr)
            {
                bestSqr = d;
                best = p;
            }
        }
        return best;
    }
}
