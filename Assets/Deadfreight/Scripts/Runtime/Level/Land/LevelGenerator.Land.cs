// Scripts/Runtime/Level/Land/LevelGenerator.Land.cs
using System;
using UnityEngine;

namespace Deadfreight.Level
{
    // Stage 1: the land. Warped base hills everywhere, then the region's landform over them, then
    // the water line, then slope.
    public sealed partial class LevelGenerator
    {
        const int HillOctaves = 5;
        const int WarpOctaves = 3;
        const float WarpScale = 60f;        // cells across one twist of the warp
        const float WarpOffset = 300f;      // reads the warp from elsewhere in the noise, so it doesn't follow the hills

        void ShapeLand()
        {
            ShapeHills();
            ShapeLandform();
            FloodWater();
            ComputeSlope();
        }

        /// <summary>
        /// Noise hills, 0 to Relief metres high, with the lowland flattened by the flatness power.
        /// Each cell reads the noise at a point moved by a second noise (the warp), which twists
        /// round lumps into curving ridges, spurs and coves.
        /// </summary>
        void ShapeHills()
        {
            Noise2D noise = new Noise2D(rng);
            float[] hills = new float[cellCount];

            for (int y = 0; y < cellsY; y++)
            {
                for (int x = 0; x < cellsX; x++)
                {
                    float warpX = noise.Fbm(x + WarpOffset, y, WarpScale, WarpOctaves) * region.warp;
                    float warpY = noise.Fbm(x, y + WarpOffset, WarpScale, WarpOctaves) * region.warp;
                    hills[y * cellsX + x] = noise.Fbm(x + warpX, y + warpY, region.hillScale, HillOctaves);
                }
            }

            PlanGrid.Normalize(hills);
            for (int i = 0; i < cellCount; i++)
                plan.GroundHeight[i] = Mathf.Pow(hills[i], region.flatness) * region.relief;
        }

        /// <summary>
        /// The region's landform reshapes the hills, and the result goes back to 0 to Relief metres.
        /// </summary>
        void ShapeLandform()
        {
            if (region.landform == null) 
                return;
            region.landform.Shape(plan, rng);
            PlanGrid.Normalize(plan.GroundHeight);

            // Iterate through the cells and scale them to the region's relief. The landform only decides the shape, not the height range.
            for (int i = 0; i < cellCount; i++) 
                plan.GroundHeight[i] *= region.relief;
        }

        /// <summary>
        /// Sets the water line so exactly Water Cover of the level lies under it, at Water Level of
        /// the relief, whatever the seed: the land is stretched on each side of the line so the
        /// lowest Water Cover of cells ends up below it and the rest above it, keeping their order.
        /// </summary>
        void FloodWater()
        {
            if (region.waterCover <= 0f)
                return;

            // The ground height that Water Cover of the cells lie below.
            var sorted = (float[])plan.GroundHeight.Clone();
            Array.Sort(sorted);
            float shoreline = Mathf.Max(1e-4f, sorted[Mathf.Clamp((int)(region.waterCover * cellCount), 0, cellCount - 1)]);

            float top = region.relief, waterLine = region.waterLevel * top;
            float landSpan = Mathf.Max(1e-4f, top - shoreline);
            for (int i = 0; i < cellCount; i++)
            {
                float ground = plan.GroundHeight[i];
                plan.GroundHeight[i] = ground <= shoreline
                    ? ground / shoreline * waterLine
                    : waterLine + (ground - shoreline) / landSpan * (top - waterLine);
            }
            plan.WaterHeight = waterLine;
        }

        /// <summary>
        /// Rise over run at each cell, from the cells either side.
        /// </summary>
        void ComputeSlope()
        {
            var ground = plan.GroundHeight;
            float run = 2f * settings.cellSize;

            for (int y = 0; y < cellsY; y++)
            for (int x = 0; x < cellsX; x++)
            {
                float riseX = ground[y * cellsX + Mathf.Min(x + 1, cellsX - 1)] - ground[y * cellsX + Mathf.Max(x - 1, 0)];
                float riseY = ground[Mathf.Min(y + 1, cellsY - 1) * cellsX + x] - ground[Mathf.Max(y - 1, 0) * cellsX + x];
                plan.Slope[y * cellsX + x] = Mathf.Sqrt(riseX * riseX + riseY * riseY) / run;
            }
        }
    }
}
