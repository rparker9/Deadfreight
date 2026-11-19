// Scripts/Runtime/LevelBuild/GridSampling.cs
using UnityEngine;

/// <summary>
/// Smooth lookups into the level's per-cell layers at fractional cell positions, so the
/// 50 m grid can be read at any point without its squares showing. Positions outside the
/// grid read the nearest edge cell. Bilinear lookups went with the land features; add one
/// back here when a feature needs it.
/// </summary>
public static class GridSampling
{
    /// <summary>Catmull-Rom bicubic: smooth, and passes exactly through each cell's value.</summary>
    public static float Bicubic(float[] values, int width, int height, Vector2 cell)
    {
        int x0 = Mathf.FloorToInt(cell.x), y0 = Mathf.FloorToInt(cell.y);
        float tx = cell.x - x0, ty = cell.y - y0;

        float At(int x, int y) => values[Mathf.Clamp(y, 0, height - 1) * width + Mathf.Clamp(x, 0, width - 1)];
        float Row(int y) => CatmullRom(At(x0 - 1, y), At(x0, y), At(x0 + 1, y), At(x0 + 2, y), tx);

        return CatmullRom(Row(y0 - 1), Row(y0), Row(y0 + 1), Row(y0 + 2), ty);
    }

    static float CatmullRom(float p0, float p1, float p2, float p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
    }
}
