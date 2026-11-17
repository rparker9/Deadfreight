// Scripts/Runtime/RegionBuild/RegionFrame.cs
using UnityEngine;

/// <summary>
/// Converts between region grid cells and world metres. Each cell's value is treated as a
/// sample at a grid point, so the world spans (width - 1) × (height - 1) cells.
///
/// The grid's y runs south and the world's z runs north, so from above the built region looks
/// like the preview window. World positions are passed as Vector2 (x, z).
/// </summary>
public readonly struct RegionFrame
{
    public readonly int Width, Height;
    public readonly float CellSize;

    public RegionFrame(RegionMap map)
    {
        Width = map.Width;
        Height = map.Height;
        CellSize = map.CellSize;
    }

    public float SizeX => (Width - 1) * CellSize;
    public float SizeZ => (Height - 1) * CellSize;

    /// <summary>World (x, z) of a cell position. Fractional cells are fine.</summary>
    public Vector2 CellToWorld(Vector2 cell) => new(cell.x * CellSize, (Height - 1 - cell.y) * CellSize);

    /// <summary>Cell position (fractional) of a world (x, z).</summary>
    public Vector2 WorldToCell(Vector2 world) => new(world.x / CellSize, Height - 1 - world.y / CellSize);
}
