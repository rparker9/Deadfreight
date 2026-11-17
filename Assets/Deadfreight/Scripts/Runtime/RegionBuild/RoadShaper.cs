// Scripts/Runtime/RegionBuild/RoadShaper.cs
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
///   4. Profile: heights from the ground, averaged so the road cuts through bumps and fills dips,
///      then held to maxGrade. Ends that join an earlier road take its height.
///   5. Press: flatten the ground to the road across its width, blending back over the shoulder.
///
/// Segments must be shaped and pressed in order, one at a time: each reads the ground the
/// earlier ones left, which is how roads meet at the same height.
/// </summary>
public sealed class RoadShaper
{
    readonly HeightField field;
    readonly RegionFrame frame;
    readonly RegionBuildSettings settings;
    readonly List<RoadPath> built = new();

    // Scratch for pressing one road: for each sample it touches, the nearest centre point's distance and height.
    readonly float[,] nearestDistance;
    readonly float[,] nearestHeight;
    readonly List<Vector2Int> touched = new();

    public RoadShaper(HeightField field, RegionFrame frame, RegionBuildSettings settings)
    {
        this.field = field;
        this.frame = frame;
        this.settings = settings;
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
        var corners = Simplify(segment.Cells, settings.simplifyCells);
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
        var spline = SmoothSpline(points);

        // 3. Sample, 4. profile.
        var line = SampleEvenly(spline, settings.sampleSpacing);
        var heights = Profile(line, startHeight, endHeight);

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
    /// Ramer-Douglas-Peucker: keeps the ends, then recursively keeps the cell furthest from the
    /// straight line between kept cells while it is more than <paramref name="tolerance"/> cells off.
    /// </summary>
    static List<Vector2> Simplify(List<Vector2Int> cells, float tolerance)
    {
        var keep = new bool[cells.Count];
        keep[0] = keep[^1] = true;
        SimplifyBetween(cells, 0, cells.Count - 1, tolerance, keep);

        var result = new List<Vector2>();
        for (int i = 0; i < cells.Count; i++)
            if (keep[i]) result.Add(cells[i]);
        return result;
    }

    static void SimplifyBetween(List<Vector2Int> cells, int first, int last, float tolerance, bool[] keep)
    {
        float worst = 0f;
        int worstIndex = -1;
        for (int i = first + 1; i < last; i++)
        {
            float d = DistanceToLine(cells[i], cells[first], cells[last]);
            if (d > worst)
            {
                worst = d;
                worstIndex = i;
            }
        }
        if (worstIndex < 0 || worst <= tolerance) return;

        keep[worstIndex] = true;
        SimplifyBetween(cells, first, worstIndex, tolerance, keep);
        SimplifyBetween(cells, worstIndex, last, tolerance, keep);
    }

    static float DistanceToLine(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float lengthSqr = ab.sqrMagnitude;
        if (lengthSqr < 1e-6f) return Vector2.Distance(p, a);
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>A flat (y = 0) spline through the points with automatic smooth tangents.</summary>
    static Spline SmoothSpline(List<Vector2> points)
    {
        var spline = new Spline();
        foreach (var p in points) spline.Add(new BezierKnot(new float3(p.x, 0f, p.y)), TangentMode.AutoSmooth);
        return spline;
    }

    /// <summary>
    /// Points every <paramref name="spacing"/> metres along the spline, plus its exact end.
    /// Spline t isn't proportional to distance, so this steps finely in t and measures as it goes.
    /// </summary>
    static List<Vector2> SampleEvenly(Spline spline, float spacing)
    {
        float length = spline.GetLength();
        int steps = Mathf.Max(8, Mathf.CeilToInt(length / spacing * 4f));

        var result = new List<Vector2>();
        Vector3 previous = (Vector3)spline.EvaluatePosition(0f);
        result.Add(new Vector2(previous.x, previous.z));

        float travelled = 0f, nextAt = spacing;
        for (int s = 1; s <= steps; s++)
        {
            Vector3 current = (Vector3)spline.EvaluatePosition(s / (float)steps);
            float step = Vector3.Distance(previous, current);

            // Emit every point that falls inside this little step.
            while (travelled + step >= nextAt)
            {
                var p = Vector3.Lerp(previous, current, (nextAt - travelled) / step);
                result.Add(new Vector2(p.x, p.z));
                nextAt += spacing;
            }
            travelled += step;
            previous = current;
        }

        // Finish exactly on the end, replacing a point that landed just short of it.
        var end = new Vector2(previous.x, previous.z);
        if (Vector2.Distance(result[^1], end) < spacing * 0.5f && result.Count > 2) result[^1] = end;
        else result.Add(end);
        return result;
    }

    /// <summary>
    /// The road's height along its length: ground height, averaged over profileSmoothing metres,
    /// then held to maxGrade (a forward and a backward pass). Pinned ends keep the height of
    /// the road they join.
    /// </summary>
    float[] Profile(List<Vector2> line, float? startHeight, float? endHeight)
    {
        int n = line.Count;
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = field.HeightAt(line[i]);
        Pin(heights, startHeight, endHeight);

        int halfWindow = Mathf.Max(1, Mathf.RoundToInt(settings.profileSmoothing / settings.sampleSpacing * 0.5f));
        for (int pass = 0; pass < 3; pass++)
        {
            heights = MovingAverage(heights, halfWindow);
            Pin(heights, startHeight, endHeight);
        }

        float maxRise = settings.maxGrade * settings.sampleSpacing;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 1; i < n; i++)
                heights[i] = Mathf.Clamp(heights[i], heights[i - 1] - maxRise, heights[i - 1] + maxRise);
            for (int i = n - 2; i >= 0; i--)
                heights[i] = Mathf.Clamp(heights[i], heights[i + 1] - maxRise, heights[i + 1] + maxRise);
            Pin(heights, startHeight, endHeight);
        }
        return heights;
    }

    static void Pin(float[] heights, float? start, float? end)
    {
        if (start.HasValue) heights[0] = start.Value;
        if (end.HasValue) heights[^1] = end.Value;
    }

    /// <summary>Each value becomes the mean of its neighbours within halfWindow (fewer at the ends).</summary>
    static float[] MovingAverage(float[] values, int halfWindow)
    {
        int n = values.Length;
        var prefix = new float[n + 1];
        for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + values[i];

        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            int from = Mathf.Max(0, i - halfWindow), to = Mathf.Min(n - 1, i + halfWindow);
            result[i] = (prefix[to + 1] - prefix[from]) / (to - from + 1);
        }
        return result;
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
                float weight = 1f - RegionMath.SmoothStep(core, reach, d);
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
