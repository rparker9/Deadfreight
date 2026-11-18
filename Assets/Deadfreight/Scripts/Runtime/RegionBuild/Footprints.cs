// Scripts/Runtime/RegionBuild/Footprints.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ground already taken by placed things (rocks, trees), as circles, for asking "is this
/// spot free?" quickly. Circles are filed into square buckets, so a question only looks at the
/// few buckets around the spot instead of at everything placed so far.
/// </summary>
public sealed class Footprints
{
    const float BucketSize = 16f;   // metres; comfortably bigger than most footprints

    readonly Dictionary<Vector2Int, List<Vector3>> buckets = new();    // x, z = centre, y = radius
    float largest;                  // the biggest radius added, so lookups know how far to reach

    public int Count { get; private set; }

    public void Add(Vector2 centre, float radius)
    {
        var key = Bucket(centre);
        if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<Vector3>();
        list.Add(new Vector3(centre.x, radius, centre.y));
        largest = Mathf.Max(largest, radius);
        Count++;
    }

    /// <summary>Whether a circle of <paramref name="radius"/> at <paramref name="centre"/> overlaps anything added.</summary>
    public bool Overlaps(Vector2 centre, float radius)
    {
        int reach = Mathf.CeilToInt((radius + largest) / BucketSize);
        var middle = Bucket(centre);
        for (int dz = -reach; dz <= reach; dz++)
        for (int dx = -reach; dx <= reach; dx++)
        {
            if (!buckets.TryGetValue(new Vector2Int(middle.x + dx, middle.y + dz), out var list)) continue;
            foreach (var circle in list)
            {
                float apart = radius + circle.y;
                float x = circle.x - centre.x, z = circle.z - centre.y;
                if (x * x + z * z < apart * apart) return true;
            }
        }
        return false;
    }

    /// <summary>Every circle added, in no particular order.</summary>
    public IEnumerable<(Vector2 centre, float radius)> Circles
    {
        get
        {
            foreach (var list in buckets.Values)
                foreach (var circle in list)
                    yield return (new Vector2(circle.x, circle.z), circle.y);
        }
    }

    static Vector2Int Bucket(Vector2 point) =>
        new(Mathf.FloorToInt(point.x / BucketSize), Mathf.FloorToInt(point.y / BucketSize));
}
