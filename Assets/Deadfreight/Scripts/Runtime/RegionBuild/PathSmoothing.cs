// Scripts/Runtime/RegionBuild/PathSmoothing.cs
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

/// <summary>
/// Turning a zigzag of 50 m plan cells into a smooth line, shared by roads and rivers:
/// drop corners that sit close to a straight line, run a spline through what's left, then take
/// evenly spaced points along it. Also a moving average for smoothing values along a line.
/// </summary>
public static class PathSmoothing
{
    /// <summary>
    /// Ramer-Douglas-Peucker: keeps the ends, then recursively keeps the cell furthest from the
    /// straight line between kept cells while it is more than <paramref name="tolerance"/> cells off.
    /// </summary>
    public static List<Vector2> Simplify(List<Vector2Int> cells, float tolerance)
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
    public static Spline SmoothSpline(List<Vector2> points)
    {
        var spline = new Spline();
        foreach (var p in points) spline.Add(new BezierKnot(new float3(p.x, 0f, p.y)), TangentMode.AutoSmooth);
        return spline;
    }

    /// <summary>
    /// Points every <paramref name="spacing"/> metres along the spline, plus its exact end.
    /// Spline t isn't proportional to distance, so this steps finely in t and measures as it goes.
    /// </summary>
    public static List<Vector2> SampleEvenly(Spline spline, float spacing)
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

    /// <summary>Each value becomes the mean of its neighbours within halfWindow (fewer at the ends).</summary>
    public static float[] MovingAverage(float[] values, int halfWindow)
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
}
