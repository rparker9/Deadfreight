// Scripts/Runtime/Level/Land/Landforms/Basin.cs
using UnityEngine;

namespace Deadfreight.Level
{
    /// <summary>
    /// The level sits in a bowl: the middle keeps its hills, and the ground rises toward the map
    /// edges into a rim, so the level's edge reads as high ground rather than a cut-off.
    /// </summary>
    [CreateAssetMenu(fileName = "Basin", menuName = "Level/Landforms/Basin")]
    public sealed class Basin : Landform
    {
        [Range(0f, 2f), Tooltip("How high the rim rises above the hills, as a share of the region's relief. 0 = no rim.")]
        public float rimHeight = 0.6f;
        [Range(0f, 0.95f), Tooltip("How much of the level, from the middle out, stays as it was before the ground starts rising (0.5 = the middle half).")]
        public float flatMiddle = 0.5f;

        public override void Shape(LevelPlan plan, System.Random rng)
        {
            var centre = new Vector2(plan.CellsX - 1, plan.CellsY - 1) * 0.5f;
            float rise = rimHeight * plan.Relief;

            for (int y = 0; y < plan.CellsY; y++)
            for (int x = 0; x < plan.CellsX; x++)
            {
                // 0 at the middle, 1 at the nearest edge's midpoint (an ellipse that fits the map).
                float dx = (x - centre.x) / centre.x, dy = (y - centre.y) / centre.y;
                float out01 = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));

                float t = Mathf.InverseLerp(flatMiddle, 1f, out01);
                plan.GroundHeight[y * plan.CellsX + x] += rise * t * t * (3f - 2f * t);    // smoothstep: no crease where it starts
            }
        }
    }
}
