// Scripts/Runtime/LevelBuild/PlanToWorld.cs
using UnityEngine;

/// <summary>
/// Converts between plan cells and world positions, and gives the level's size in metres.
/// Cell (x, y) sits at world (x, z) = (x, y) × CellSize; fractional cells are fine. World
/// positions here are Vector2s holding (x, z).
/// </summary>
public readonly struct PlanToWorld
{
    public readonly int CellsX, CellsY;
    public readonly float CellSize;

    public PlanToWorld(LevelPlan plan)
    {
        CellsX = plan.CellsX;
        CellsY = plan.CellsY;
        CellSize = plan.CellSize;
    }

    /// <summary>Metres from the first cell to the last, west to east.</summary>
    public float SizeX => (CellsX - 1) * CellSize;
    /// <summary>Metres from the first cell to the last, south to north.</summary>
    public float SizeZ => (CellsY - 1) * CellSize;

    /// <summary>World (x, z) of a cell.</summary>
    public Vector2 CellToWorld(Vector2 cell) => cell * CellSize;

    /// <summary>The (fractional) cell at a world (x, z).</summary>
    public Vector2 WorldToCell(Vector2 world) => world / CellSize;
}
