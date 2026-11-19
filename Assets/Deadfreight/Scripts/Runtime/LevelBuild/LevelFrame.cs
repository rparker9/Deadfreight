// Scripts/Runtime/LevelBuild/LevelFrame.cs
using UnityEngine;

/// <summary>
/// Used by LevelBuilder to convert between cell positions and world positions, and to get the level's size.
/// </summary>
public readonly struct LevelFrame
{
    public readonly int Width, Height;
    public readonly float CellSize;

    public LevelFrame(LevelLayout map)
    {
        Width = map.Width;
        Height = map.Height;
        CellSize = map.CellSize;
    }

    public float SizeX => (Width - 1) * CellSize;
    public float SizeZ => (Height - 1) * CellSize;

    /// <summary>
    /// World (x, z) of a cell position. Fractional cells are fine.
    /// </summary>
    public Vector2 CellToWorld(Vector2 cell) => new(cell.x * CellSize, (Height - 1 - cell.y) * CellSize);

    /// <summary>
    /// Cell position (fractional) of a world (x, z).
    /// </summary>
    public Vector2 WorldToCell(Vector2 world) => new(world.x / CellSize, Height - 1 - world.y / CellSize);
}
