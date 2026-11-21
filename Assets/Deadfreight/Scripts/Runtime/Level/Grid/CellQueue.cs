// Scripts/Runtime/Level/Grid/CellQueue.cs
using System.Collections.Generic;

/// <summary>
/// Cell indices waiting to be visited, cheapest first (a binary min-heap), for path searches.
/// A cell can be added again with a lower cost; the search skips the stale, costlier copy when
/// it comes out, which is simpler than updating the copy already in the queue.
/// </summary>
public sealed class CellQueue
{
    readonly List<(int cell, float cost)> heap = new();

    public int Count => heap.Count;

    public void Clear() => heap.Clear();

    public void Push(int cell, float cost)
    {
        heap.Add((cell, cost));
        int child = heap.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (heap[parent].cost <= heap[child].cost)
                break;
            (heap[parent], heap[child]) = (heap[child], heap[parent]);
            child = parent;
        }
    }

    /// <summary>Removes and returns the cheapest cell, with the cost it was added at.</summary>
    public (int cell, float cost) Pop()
    {
        var top = heap[0];
        int last = heap.Count - 1;
        heap[0] = heap[last];
        heap.RemoveAt(last);

        int parent = 0;
        while (true)
        {
            int left = parent * 2 + 1, right = left + 1, smallest = parent;
            if (left < heap.Count && heap[left].cost < heap[smallest].cost)
                smallest = left;
            if (right < heap.Count && heap[right].cost < heap[smallest].cost)
                smallest = right;
            if (smallest == parent)
                break;
            (heap[parent], heap[smallest]) = (heap[smallest], heap[parent]);
            parent = smallest;
        }
        return top;
    }
}
