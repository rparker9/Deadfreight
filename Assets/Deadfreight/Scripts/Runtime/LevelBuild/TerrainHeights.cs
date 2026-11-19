// Scripts/Runtime/LevelBuild/TerrainHeights.cs
using UnityEngine;

/// <summary>
/// The terrain's heights in world metres, on a square grid of samples covering the level.
/// Filled from the plan, then handed to Unity's TerrainData. Arrays are [row, column]: rows run
/// along z (south to north), columns along x (Unity's order).
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
        for (int row = 0; row < terrain.Resolution; row++)
        for (int column = 0; column < terrain.Resolution; column++)
        {
            var cell = planToWorld.WorldToCell(terrain.SampleToWorld(column, row));
            terrain.Heights[row, column] = PlanGrid.Bicubic(plan.GroundHeight, plan.CellsX, plan.CellsY, cell);
        }
        return terrain;
    }

    // ---------- to the terrain ----------

    /// <summary>
    /// The heights in the terrain's 0-1 range. Also returns the world height of 0 and the height
    /// span, for TerrainData.size.y and the terrain's position.
    /// </summary>
    public float[,] Normalized(out float bottom, out float span)
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float h in Heights)
        {
            min = Mathf.Min(min, h);
            max = Mathf.Max(max, h);
        }
        bottom = min - 1f;                      // a metre of room either side
        span = Mathf.Max(1f, max - min + 2f);

        var normalized = new float[Resolution, Resolution];
        for (int r = 0; r < Resolution; r++)
        for (int c = 0; c < Resolution; c++)
            normalized[r, c] = (Heights[r, c] - bottom) / span;
        return normalized;
    }
}
