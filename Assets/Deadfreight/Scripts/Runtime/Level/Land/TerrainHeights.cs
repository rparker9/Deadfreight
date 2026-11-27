// Scripts/Runtime/Level/Land/TerrainHeights.cs
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Deadfreight.Level
{
    /// <summary>
    /// The terrain's heights in world metres, on a square grid of samples covering the level.
    /// Filled from the plan, then handed to Unity's TerrainData. Arrays are [row, column]: rows run
    /// along z (south to north), columns along x (Unity's order).
    ///
    /// Rows don't depend on each other, so the work on every sample is split across CPU cores by
    /// row (Parallel.For). Nothing here touches Unity objects, which must stay on the main thread.
    /// </summary>
    public sealed class TerrainHeights
    {
        public readonly int Resolution;         // samples per side
        public readonly float SizeX, SizeZ;     // metres covered
        public readonly float[,] Heights;

        public TerrainHeights(int resolution, float sizeX, float sizeZ)
        {
            Resolution = resolution;
            SizeX = sizeX;
            SizeZ = sizeZ;
            Heights = new float[resolution, resolution];
        }

        public float StepX => SizeX / (Resolution - 1);
        public float StepZ => SizeZ / (Resolution - 1);

        /// <summary>World (x, z) of a sample.</summary>
        public Vector2 SampleToWorld(int column, int row) => new(column * StepX, row * StepZ);

        // ---------- from the plan ----------

        /// <summary>The plan's ground heights, smoothly (bicubic) between cells, and exactly the plan's at each cell.</summary>
        public static TerrainHeights FromPlan(LevelPlan plan, PlanToWorld planToWorld, BuildSettings build)
        {
            var terrain = new TerrainHeights(build.heightmapResolution, planToWorld.SizeX, planToWorld.SizeZ);
            Parallel.For(0, terrain.Resolution, row =>
            {
                for (int column = 0; column < terrain.Resolution; column++)
                {
                    var cell = planToWorld.WorldToCell(terrain.SampleToWorld(column, row));
                    terrain.Heights[row, column] = PlanGrid.Bicubic(plan.GroundHeight, plan.CellsX, plan.CellsY, cell);
                }
            });
            return terrain;
        }

        // ---------- roads ----------

        /// <summary>
        /// Cuts and fills the ground along the roads. Within <paramref name="flatHalfWidth"/> of a
        /// road, the ground is set to the road's surface; over the next <paramref name="blendWidth"/>
        /// metres it eases back to the land. Where roads meet, each sample follows the nearest road.
        /// </summary>
        public void CarveRoads(IReadOnlyList<RoadShape> roads, float flatHalfWidth, float blendWidth)
        {
            float reach = flatHalfWidth + blendWidth;
            var distance = new float[Resolution, Resolution];   // metres to the nearest road sample
            var target = new float[Resolution, Resolution];     // that sample's road surface height
            Parallel.For(0, Resolution, r =>
            {
                for (int c = 0; c < Resolution; c++)
                    distance[r, c] = float.MaxValue;
            });

            // Stamp a disc of the road's height round every sample. Roads' samples are closer
            // together than the terrain's, so the discs overlap into a band. One thread: discs overlap.
            foreach (var road in roads)
            foreach (var point in road.Points)
            {
                int c0 = Mathf.Max(0, Mathf.FloorToInt((point.x - reach) / StepX)), c1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt((point.x + reach) / StepX));
                int r0 = Mathf.Max(0, Mathf.FloorToInt((point.z - reach) / StepZ)), r1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt((point.z + reach) / StepZ));
                for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    float dx = c * StepX - point.x, dz = r * StepZ - point.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d < distance[r, c])
                    {
                        distance[r, c] = d;
                        target[r, c] = point.y;
                    }
                }
            }

            Parallel.For(0, Resolution, r =>
            {
                for (int c = 0; c < Resolution; c++)
                {
                    float d = distance[r, c];
                    if (d >= reach)
                        continue;
                    float land = Mathf.SmoothStep(0f, 1f, (d - flatHalfWidth) / Mathf.Max(0.01f, blendWidth));    // 0 within the flat
                    Heights[r, c] = Mathf.Lerp(target[r, c], Heights[r, c], land);
                }
            });
        }

        // ---------- to the terrain ----------

        /// <summary>
        /// The heights in the terrain's 0-1 range. Also returns the world height of 0 and the height
        /// span, for TerrainData.size.y and the terrain's position.
        /// </summary>
        public float[,] Normalized(out float bottom, out float span)
        {
            // Lowest and highest of each row, in parallel; then of the whole field.
            var rowMin = new float[Resolution];
            var rowMax = new float[Resolution];
            Parallel.For(0, Resolution, r =>
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int c = 0; c < Resolution; c++)
                {
                    lo = Mathf.Min(lo, Heights[r, c]);
                    hi = Mathf.Max(hi, Heights[r, c]);
                }
                rowMin[r] = lo;
                rowMax[r] = hi;
            });

            float min = float.MaxValue, max = float.MinValue;
            for (int r = 0; r < Resolution; r++)
            {
                min = Mathf.Min(min, rowMin[r]);
                max = Mathf.Max(max, rowMax[r]);
            }
            float zero = min - 1f;                  // a metre of room either side
            float range = Mathf.Max(1f, max - min + 2f);

            var normalized = new float[Resolution, Resolution];
            Parallel.For(0, Resolution, r =>
            {
                for (int c = 0; c < Resolution; c++)
                    normalized[r, c] = (Heights[r, c] - zero) / range;
            });

            bottom = zero;
            span = range;
            return normalized;
        }
    }
}
