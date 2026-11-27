// Scripts/Runtime/Level/Grid/PlanGrid.cs
using UnityEngine;

namespace Deadfreight.Level
{
    /// <summary>
    /// Operations on the plan's per-cell arrays (index = y × cellsX + x). Lookups at fractional
    /// cells read smoothly between cells, so the 50 m grid can be sampled anywhere without its
    /// squares showing; cells outside the grid read the nearest edge cell.
    /// </summary>
    public static class PlanGrid
    {
        /// <summary>Stretches values to exactly 0-1, in place.</summary>
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

        /// <summary>Straight-line blend of the four cells around a fractional cell: cheaper than Bicubic, for searches.</summary>
        public static float Bilinear(float[] values, int cellsX, int cellsY, Vector2 cell)
        {
            int x0 = Mathf.Clamp(Mathf.FloorToInt(cell.x), 0, cellsX - 2), y0 = Mathf.Clamp(Mathf.FloorToInt(cell.y), 0, cellsY - 2);
            float tx = Mathf.Clamp01(cell.x - x0), ty = Mathf.Clamp01(cell.y - y0);
            int i = y0 * cellsX + x0;

            float south = Mathf.Lerp(values[i], values[i + 1], tx);
            float north = Mathf.Lerp(values[i + cellsX], values[i + cellsX + 1], tx);
            return Mathf.Lerp(south, north, ty);
        }

        /// <summary>Catmull-Rom bicubic at a fractional cell: smooth, and exactly each cell's value at the cell.</summary>
        public static float Bicubic(float[] values, int cellsX, int cellsY, Vector2 cell)
        {
            int x0 = Mathf.FloorToInt(cell.x), y0 = Mathf.FloorToInt(cell.y);
            float tx = cell.x - x0, ty = cell.y - y0;

            float At(int x, int y) => values[Mathf.Clamp(y, 0, cellsY - 1) * cellsX + Mathf.Clamp(x, 0, cellsX - 1)];
            float Row(int y) => CatmullRom(At(x0 - 1, y), At(x0, y), At(x0 + 1, y), At(x0 + 2, y), tx);

            return CatmullRom(Row(y0 - 1), Row(y0), Row(y0 + 1), Row(y0 + 2), ty);
        }

        static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3);
        }
    }
}
