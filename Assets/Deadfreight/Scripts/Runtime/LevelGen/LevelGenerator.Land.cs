// Scripts/Runtime/LevelGen/LevelGenerator.Land.cs
using UnityEngine;

// Stage 1: the land. Placeholder: rolling noise hills.
public sealed partial class LevelGenerator
{
    const int HillOctaves = 5;

    void ShapeLand()
    {
        ShapeHills();
        ComputeSlope();
    }

    /// <summary>
    /// Noise hills, 0 to Relief metres high, with the lowland flattened by the flatness power.
    /// </summary>
    void ShapeHills()
    {
        var noise = GridNoise.Fbm(cellsX, cellsY, settings.hillScale, HillOctaves, rng);
        PlanGrid.Normalize(noise);
        for (int i = 0; i < cellCount; i++)
            plan.GroundHeight[i] = Mathf.Pow(noise[i], settings.flatness) * settings.relief;
    }

    /// <summary>Rise over run at each cell, from the cells either side.</summary>
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
