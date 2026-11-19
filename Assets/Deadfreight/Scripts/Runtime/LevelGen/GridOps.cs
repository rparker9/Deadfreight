// Scripts/Runtime/LevelGen/GridOps.cs
using UnityEngine;

/// <summary>
/// Whole-grid operations on the level's layers (index = y * width + x).
/// </summary>
public static class GridOps
{
    /// <summary>
    /// Stretches values to exactly 0-1, in place.
    /// </summary>
    public static void Normalize(float[] values)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in values)
        {
            min = Mathf.Min(min, v);
            max = Mathf.Max(max, v);
        }
        float range = Mathf.Max(1e-6f, max - min);
        for (int i = 0; i < values.Length; i++) values[i] = (values[i] - min) / range;
    }
}
