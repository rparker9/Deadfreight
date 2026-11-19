// Scripts/Runtime/LevelBuild/HeightField.cs
using UnityEngine;

/// <summary>
/// World heights in metres on a square grid of samples covering the level. It is filled from
/// the plan, the plan's roads and pads are pressed into it, and then it becomes the Unity terrain. Arrays are [row, column]: rows run along z, columns along x (Unity's order).
/// </summary>
public sealed class HeightField
{
    public readonly int Resolution;
    public readonly float SizeX, SizeZ;
    public readonly float[,] Heights;

    // Samples under a road's surface. Later roads and pads leave them alone, so every road mesh
    // keeps the ground it was built on.
    readonly bool[,] roadCore;

    public HeightField(int resolution, float sizeX, float sizeZ)
    {
        Resolution = resolution;
        SizeX = sizeX;
        SizeZ = sizeZ;
        Heights = new float[resolution, resolution];
        roadCore = new bool[resolution, resolution];
    }

    public float StepX => SizeX / (Resolution - 1);
    public float StepZ => SizeZ / (Resolution - 1);

    public Vector2 SampleToWorld(int column, int row) => new(column * StepX, row * StepZ);

    public bool IsRoadCore(int column, int row) => roadCore[row, column];
    public void MarkRoadCore(int column, int row) => roadCore[row, column] = true;

    // ---------- building from the plan ----------

    /// <summary>The plan's heights in metres, smoothly (bicubic) between cell centres, which it passes through exactly.</summary>
    public static HeightField FromLayout(LevelLayout map, LevelFrame frame, LevelBuildSettings build)
    {
        var field = new HeightField(build.heightmapResolution, frame.SizeX, frame.SizeZ);

        var metres = new float[map.Elevation.Length];
        for (int i = 0; i < metres.Length; i++) metres[i] = map.Metres(i);

        for (int row = 0; row < field.Resolution; row++)
        for (int column = 0; column < field.Resolution; column++)
        {
            var cell = frame.WorldToCell(field.SampleToWorld(column, row));
            field.Heights[row, column] = GridSampling.Bicubic(metres, map.Width, map.Height, cell);
        }
        return field;
    }

    // ---------- reading ----------

    /// <summary>Height at any world (x, z), bilinear between samples.</summary>
    public float HeightAt(Vector2 world)
    {
        float fx = Mathf.Clamp(world.x / StepX, 0f, Resolution - 1.001f);
        float fz = Mathf.Clamp(world.y / StepZ, 0f, Resolution - 1.001f);
        int c = (int)fx, r = (int)fz;
        float tx = fx - c, tz = fz - r;

        float near = Mathf.Lerp(Heights[r, c], Heights[r, c + 1], tx);
        float far = Mathf.Lerp(Heights[r + 1, c], Heights[r + 1, c + 1], tx);
        return Mathf.Lerp(near, far, tz);
    }

    /// <summary>The columns and rows of samples within <paramref name="radius"/> of a world point, clamped to the field.</summary>
    public void SamplesAround(Vector2 world, float radius, out int column0, out int column1, out int row0, out int row1)
    {
        column0 = Mathf.Max(0, Mathf.FloorToInt((world.x - radius) / StepX));
        column1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt((world.x + radius) / StepX));
        row0 = Mathf.Max(0, Mathf.FloorToInt((world.y - radius) / StepZ));
        row1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt((world.y + radius) / StepZ));
    }

    /// <summary>
    /// Copies the heights into the terrain's 0-1 range. Returns the world height of 0 and the
    /// height span, for TerrainData.size.y and the terrain's position.
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
