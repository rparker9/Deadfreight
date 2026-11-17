// Scripts/Runtime/RegionBuild/HeightField.cs
using UnityEngine;

/// <summary>
/// World heights in metres on a square grid of samples covering the region. It is filled from
/// the plan's elevation, roads and place pads are pressed into it, and then it becomes the
/// Unity terrain. Arrays are [row, column]: rows run along z, columns along x (Unity's order).
/// </summary>
public sealed class HeightField
{
    public readonly int Resolution;
    public readonly float SizeX, SizeZ;
    public readonly float[,] Heights;

    // Samples under a road's surface. Later roads and pads leave them alone, so every road mesh
    // keeps the ground it was built on.
    readonly bool[,] roadCore;

    // Samples under river water. Nothing grows there.
    readonly bool[,] channel;

    public HeightField(int resolution, float sizeX, float sizeZ)
    {
        Resolution = resolution;
        SizeX = sizeX;
        SizeZ = sizeZ;
        Heights = new float[resolution, resolution];
        roadCore = new bool[resolution, resolution];
        channel = new bool[resolution, resolution];
    }

    public float StepX => SizeX / (Resolution - 1);
    public float StepZ => SizeZ / (Resolution - 1);

    public Vector2 SampleToWorld(int column, int row) => new(column * StepX, row * StepZ);

    public bool IsRoadCore(int column, int row) => roadCore[row, column];
    public void MarkRoadCore(int column, int row) => roadCore[row, column] = true;
    public bool IsChannel(int column, int row) => channel[row, column];
    public void MarkChannel(int column, int row) => channel[row, column] = true;

    // ---------- building from the plan ----------

    /// <summary>
    /// Heights from the plan: its elevation scaled to metres with smooth (bicubic) interpolation,
    /// bumps and roughness the 50 m grid can't hold, and the escarpment's face redrawn as a cliff.
    /// River channels are cut later, along each river's line (RiverShaper).
    /// </summary>
    public static HeightField FromRegion(RegionMap map, RegionFrame frame, RegionSettings region, RegionBuildSettings build, int seed)
    {
        var field = new HeightField(build.heightmapResolution, frame.SizeX, frame.SizeZ);

        // Noise offsets from the seed, so each region's bumps differ but rebuild the same.
        var rng = new System.Random(seed);
        var bumpOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
        var roughOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));

        for (int row = 0; row < field.Resolution; row++)
        for (int column = 0; column < field.Resolution; column++)
        {
            var world = field.SampleToWorld(column, row);
            var cell = frame.WorldToCell(world);

            float h = GridSampling.Bicubic(map.Elevation, map.Width, map.Height, cell) * region.relief;
            h += Noise(world, build.bumpScale, bumpOffset) * build.bumpHeight;
            h += Noise(world, build.roughScale, roughOffset) * build.roughHeight;

            if (map.HasEscarpment) h += SharperFace(map, region, build, cell);

            field.Heights[row, column] = h;
        }
        return field;
    }

    /// <summary>
    /// The plan draws the escarpment's face across a few 50 m cells: a steep slope, not a cliff.
    /// This swaps that slope for one cliffWidth wide, keeping the same rise. At passes (and
    /// cuttings) the plan's long ramp is left as it is, so roads can still climb there.
    /// </summary>
    static float SharperFace(RegionMap map, RegionSettings region, RegionBuildSettings build, Vector2 cell)
    {
        float scarp = GridSampling.Bilinear(map.Scarp, map.Width, map.Height, cell);
        float pass = Mathf.Clamp01(GridSampling.Bilinear(map.ScarpPass, map.Width, map.Height, cell));

        float planHalfWidth = Mathf.Lerp(region.escarpmentFace, region.passRamp, pass);
        if (Mathf.Abs(scarp) >= planHalfWidth) return 0f;       // off the face, both versions agree

        float cliffHalfWidth = Mathf.Lerp(build.cliffWidth * 0.5f / map.CellSize, region.passRamp, pass);
        float cliff = RegionMath.SmoothStep(-cliffHalfWidth, cliffHalfWidth, scarp);
        float plan = RegionMath.SmoothStep(-planHalfWidth, planHalfWidth, scarp);
        return map.ScarpRise * (cliff - plan);
    }

    /// <summary>Perlin noise from -1 to 1 with features about <paramref name="scale"/> metres across.</summary>
    static float Noise(Vector2 world, float scale, Vector2 offset) =>
        (Mathf.PerlinNoise(world.x / scale + offset.x, world.y / scale + offset.y) - 0.5f) * 2f;

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

    /// <summary>Rise over run at the sample nearest a world (x, z).</summary>
    public float SlopeAt(Vector2 world)
    {
        int c = Mathf.Clamp(Mathf.RoundToInt(world.x / StepX), 1, Resolution - 2);
        int r = Mathf.Clamp(Mathf.RoundToInt(world.y / StepZ), 1, Resolution - 2);
        float dx = (Heights[r, c + 1] - Heights[r, c - 1]) / (2f * StepX);
        float dz = (Heights[r + 1, c] - Heights[r - 1, c]) / (2f * StepZ);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>The columns and rows of samples within <paramref name="radius"/> of a world point, clamped to the field.</summary>
    public void SamplesAround(Vector2 world, float radius, out int column0, out int column1, out int row0, out int row1)
    {
        column0 = Mathf.Max(0, Mathf.FloorToInt((world.x - radius) / StepX));
        column1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt((world.x + radius) / StepX));
        row0 = Mathf.Max(0, Mathf.FloorToInt((world.y - radius) / StepZ));
        row1 = Mathf.Min(Resolution - 1, Mathf.CeilToInt((world.y + radius) / StepZ));
    }

    // ---------- shaping ----------

    /// <summary>
    /// Makes the shoreline match the map. The 3D build adds bumps, terraces and a sharper cliff the
    /// plan never knew about, which can sink land below the lake plane or lift lake bed above it.
    /// Where the plan says water, ground is held at least shoreDepth below lake level; where it
    /// says land, at least landAboveWater above. The shore lands where the map's water layer
    /// crosses one half.
    /// </summary>
    public void KeepShoreline(RegionMap map, RegionFrame frame, float lakeLevel, RegionBuildSettings build)
    {
        for (int row = 0; row < Resolution; row++)
        for (int column = 0; column < Resolution; column++)
        {
            var cell = frame.WorldToCell(SampleToWorld(column, row));
            bool water = GridSampling.Bilinear(map.Water, map.Width, map.Height, cell) >= 0.5f;
            Heights[row, column] = water
                ? Mathf.Min(Heights[row, column], lakeLevel - build.shoreDepth)
                : Mathf.Max(Heights[row, column], lakeLevel + build.landAboveWater);
        }
    }

    /// <summary>
    /// Levels a round pad at the height of its centre: flat out to <paramref name="radius"/>,
    /// then blending back to the ground over <paramref name="blend"/>. Road surfaces are left alone.
    /// </summary>
    public void PressPad(Vector2 centre, float radius, float blend)
    {
        float height = HeightAt(centre);
        SamplesAround(centre, radius + blend, out int c0, out int c1, out int r0, out int r1);

        for (int r = r0; r <= r1; r++)
        for (int c = c0; c <= c1; c++)
        {
            if (roadCore[r, c]) continue;
            float d = Vector2.Distance(SampleToWorld(c, r), centre);
            float weight = 1f - RegionMath.SmoothStep(radius, radius + blend, d);
            Heights[r, c] = Mathf.Lerp(Heights[r, c], height, weight);
        }
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
