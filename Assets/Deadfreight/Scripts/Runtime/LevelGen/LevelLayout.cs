// Scripts/Runtime/LevelGen/LevelLayout.cs
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>

/// </summary>
public sealed class LevelLayout
{
    public readonly int Width, Height;
    public readonly float CellSize;
    public float Relief;                 // metres from 0 to 1 on the Elevation scale

    public readonly float[] Elevation;   // 0-1
    public readonly float[] Slope;       // rise over run

    /// <summary>
    /// Why generation stopped early, or null if it finished.
    /// </summary>
    public string Failure;

    /// <summary>
    /// Alternates thrown away for falling outside the time window (for tuning).
    /// </summary>
    public int RejectedAlternates;

    public LevelLayout(int width, int height, float cellSize)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        int n = width * height;
        Elevation = new float[n];
        Slope = new float[n];
    }

    /// <summary>
    /// Plan height of a cell in metres.
    /// </summary>
    public float Metres(int i) => Elevation[i] * Relief;

    public int Index(Vector2Int c) => c.y * Width + c.x;
    public Vector2Int CellAt(int index) => new(index % Width, index / Width);
    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;
}
