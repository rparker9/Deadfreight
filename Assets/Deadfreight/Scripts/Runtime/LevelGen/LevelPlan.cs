// Scripts/Runtime/LevelGen/LevelPlan.cs
using UnityEngine;

/// <summary>
/// A generated level, as plain data on a grid of cells. LevelGenerator makes it; the preview
/// reads it and LevelBuilder builds it. Every level is one region.
///
/// Cell (x, y) sits at (x, y) × CellSize metres in the world: x east, y north (the world's z).
/// A cell's index into the per-cell arrays is y × CellsX + x.
/// </summary>
public sealed class LevelPlan
{
    public readonly int CellsX, CellsY;
    public readonly float CellSize;             // metres between neighbouring cells
    public float Relief;                        // metres from the lowest ground to the highest
    public RegionProfile Region;

    public readonly float[] GroundHeight;       // metres above the lowest ground (0 to Relief)
    public readonly float[] Slope;              // rise over run
    public readonly byte[] Zone;                // index into Region.zones

    /// <summary>Why generation stopped early, or null if it finished.</summary>
    public string Failure;

    public LevelPlan(int cellsX, int cellsY, float cellSize)
    {
        CellsX = cellsX;
        CellsY = cellsY;
        CellSize = cellSize;
        int count = cellsX * cellsY;
        GroundHeight = new float[count];
        Slope = new float[count];
        Zone = new byte[count];
    }

    public int Index(Vector2Int cell) => cell.y * CellsX + cell.x;
    public Vector2Int CellAt(int index) => new(index % CellsX, index / CellsX);
    public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < CellsX && cell.y < CellsY;
}
