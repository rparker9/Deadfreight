// Scripts/Runtime/LevelGen/GridMath.cs
using UnityEngine;

/// <summary>Small maths helpers shared by level generation and building.</summary>
public static class GridMath
{
    /// <summary>0 at or below edge0, 1 at or above edge1, an S-curve between (like shader smoothstep).</summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}
