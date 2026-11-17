// Scripts/Runtime/RegionGen/MinHeap.cs
using System.Collections.Generic;

/// <summary>
/// A binary min-heap of (key, item) pairs: Pop always returns the item with the smallest key.
/// Used to walk the land from lowest to highest when working out where water drains.
/// </summary>
public sealed class MinHeap
{
    readonly List<(float key, int item)> entries = new();

    public int Count => entries.Count;

    public void Push(float key, int item)
    {
        entries.Add((key, item));
        int child = entries.Count - 1;
        while (child > 0)
        {
            int parent = (child - 1) / 2;
            if (entries[parent].key <= entries[child].key) break;
            (entries[parent], entries[child]) = (entries[child], entries[parent]);
            child = parent;
        }
    }

    public (float key, int item) Pop()
    {
        var top = entries[0];
        int last = entries.Count - 1;
        entries[0] = entries[last];
        entries.RemoveAt(last);

        int current = 0;
        while (true)
        {
            int left = current * 2 + 1, right = left + 1, smallest = current;
            if (left < entries.Count && entries[left].key < entries[smallest].key) smallest = left;
            if (right < entries.Count && entries[right].key < entries[smallest].key) smallest = right;
            if (smallest == current) break;
            (entries[smallest], entries[current]) = (entries[current], entries[smallest]);
            current = smallest;
        }
        return top;
    }
}
