// Scripts/Runtime/LevelGen/GridPathfinder.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A* over a grid with a cost per cell (8 directions, diagonals cost x1.414).
/// Reusable: arrays are allocated once per grid size and reset with a run stamp.
/// </summary>
public sealed class GridPathfinder
{
    const float Diagonal = 1.41421356f;

    readonly int width, height;
    readonly float[] g;
    readonly int[] cameFrom;
    readonly int[] stamp;
    readonly List<(float f, int i)> heap = new();
    int run;

    public GridPathfinder(int width, int height)
    {
        this.width = width;
        this.height = height;
        g = new float[width * height];
        cameFrom = new int[width * height];
        stamp = new int[width * height];
    }

    /// <summary> Finds a path from a to b, inclusive, or null if b can't be reached. </summary>
    /// <param name="cost">Cost of entering each cell; use a huge value for impassable cells.</param>
    /// <param name="heuristicWeight">Scales straight-line distance; below the cheapest cell cost keeps paths near-optimal.</param>
    /// <returns>Cells from a to b inclusive, or null if b can't be reached.</returns>
    public List<Vector2Int> Find(Vector2Int a, Vector2Int b, float[] cost, float heuristicWeight)
    {
        run++;
        heap.Clear();
        int start = a.y * width + a.x, goal = b.y * width + b.x;
        Visit(start, 0f, -1);
        Push(0f, start);

        while (heap.Count > 0)
        {
            int current = Pop();
            if (current == goal) return Trace(goal);

            int cx = current % width, cy = current / width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx, ny = cy + dy;
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue;

                    int next = ny * width + nx;
                    float step = cost[next] * (dx != 0 && dy != 0 ? Diagonal : 1f);
                    float ng = g[current] + step;
                    if (stamp[next] == run && ng >= g[next]) continue;

                    Visit(next, ng, current);
                    float hx = nx - b.x, hy = ny - b.y;
                    Push(ng + Mathf.Sqrt(hx * hx + hy * hy) * heuristicWeight, next);
                }
            }
        }
        return null;
    }

    void Visit(int i, float cost, int from)
    {
        stamp[i] = run;
        g[i] = cost;
        cameFrom[i] = from;
    }

    List<Vector2Int> Trace(int goal)
    {
        var path = new List<Vector2Int>();
        for (int i = goal; i >= 0; i = cameFrom[i])
            path.Add(new Vector2Int(i % width, i / width));
        path.Reverse();
        return path;
    }

    // ---------- binary min-heap on f ----------

    void Push(float f, int i)
    {
        heap.Add((f, i));
        int c = heap.Count - 1;
        while (c > 0)
        {
            int p = (c - 1) / 2;
            if (heap[p].f <= heap[c].f) break;
            (heap[p], heap[c]) = (heap[c], heap[p]);
            c = p;
        }
    }

    int Pop()
    {
        int top = heap[0].i;
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);

        int c = 0;
        while (true)
        {
            int l = c * 2 + 1, r = l + 1, m = c;
            if (l < heap.Count && heap[l].f < heap[m].f) m = l;
            if (r < heap.Count && heap[r].f < heap[m].f) m = r;
            if (m == c) break;
            (heap[m], heap[c]) = (heap[c], heap[m]);
            c = m;
        }
        return top;
    }
}
