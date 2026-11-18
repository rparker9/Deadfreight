// Scripts/Runtime/RegionBuild/GroundSurvey.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything worth knowing about the finished ground at any point, for deciding what grows or
/// lies there: how far to the nearest road and river, how deep into a forest, how steep, how
/// high, whether it's marsh, hollow, ridge, cliff or scree. The distance fields are worked out
/// once here, so BiomeMap, TreePlanting and RockPlacing can ask cheaply and agree with each other.
/// </summary>
public sealed class GroundSurvey
{
    const float SoftFrom = 0.3f, SoftTo = 0.7f;     // plan yes/no layers firmed into soft 0-1 edges

    public readonly RegionMap Map;
    public readonly RegionFrame Frame;
    public readonly HeightField Field;
    public readonly RegionBuildSettings Settings;
    public readonly float LakeLevel;

    readonly float[] toRoad, toRiver;       // metres, per height sample
    readonly float[] forestDepth;           // per plan cell: cells to the nearest cell without forest
    readonly float[] cellsToCliff;          // per plan cell
    readonly float[] relief;                // per plan cell: positive on ridges, negative in hollows
    readonly List<Vector2> pads = new();

    public GroundSurvey(RegionMap map, RegionFrame frame, HeightField field, RegionBuildSettings settings, float lakeLevel)
    {
        Map = map;
        Frame = frame;
        Field = field;
        Settings = settings;
        LakeLevel = lakeLevel;

        toRoad = MetresToMarked(field, field.IsRoadCore);
        toRiver = MetresToMarked(field, field.IsChannel);

        var open = new bool[map.Forest.Length];
        for (int i = 0; i < open.Length; i++) open[i] = !map.Forest[i];
        GridOps.NearestMarked(open, map.Width, map.Height, out forestDepth, out _);
        GridOps.NearestMarked(map.Cliff, map.Width, map.Height, out cellsToCliff, out _);
        relief = TerrainPaint.RidgesAndHollows(map);

        foreach (var place in map.Places) pads.Add(frame.CellToWorld(place.Cell));
    }

    // ---------- measured on the finished ground ----------

    public float Height(Vector2 world) => Field.HeightAt(world);
    public float Slope(Vector2 world) => Field.SlopeAt(world);
    /// <summary>Metres to the nearest road surface (huge if there are no roads).</summary>
    public float FromRoad(Vector2 world) => toRoad[SampleIndex(world)];
    /// <summary>Metres to the nearest river water (huge if there are no rivers).</summary>
    public float FromRiver(Vector2 world) => toRiver[SampleIndex(world)];

    public bool OnRoad(Vector2 world) => FromRoad(world) < 0.5f * Field.StepX;
    public bool InRiver(Vector2 world) => FromRiver(world) < 0.5f * Field.StepX;
    public bool BelowLake(Vector2 world, float margin = 0f) => Height(world) < LakeLevel + margin;

    /// <summary>Whether a point is on, or within <paramref name="margin"/> metres of, the flat pad around a place.</summary>
    public bool NearPad(Vector2 world, float margin)
    {
        float reach = Settings.padRadius + margin;
        foreach (var pad in pads)
            if ((pad - world).sqrMagnitude < reach * reach) return true;
        return false;
    }

    /// <summary>The ground's up direction, from the heights either side.</summary>
    public Vector3 Normal(Vector2 world)
    {
        float step = Field.StepX;
        float dx = Height(world + new Vector2(step, 0f)) - Height(world - new Vector2(step, 0f));
        float dz = Height(world + new Vector2(0f, step)) - Height(world - new Vector2(0f, step));
        return new Vector3(-dx, 2f * step, -dz).normalized;
    }

    // ---------- read from the plan, softly ----------

    public Vector2 Cell(Vector2 world) => Frame.WorldToCell(world);

    /// <summary>0-1 height on the plan's scale (0 lowest, 1 highest in the region).</summary>
    public float Elevation(Vector2 cell) => GridSampling.Bilinear(Map.Elevation, Map.Width, Map.Height, cell);
    public float Forest(Vector2 cell) => Soft(Map.Forest, cell);
    public float Marsh(Vector2 cell) => Soft(Map.Marsh, cell);
    public float Hollow(Vector2 cell) => Soft(Map.Hollow, cell);
    public float Cliff(Vector2 cell) => GridSampling.Bilinear(Map.Cliff, Map.Width, Map.Height, cell);

    /// <summary>Metres in from a forest's edge; 0 outside forests.</summary>
    public float ForestDepth(Vector2 cell)
    {
        // A forest cell next to open ground is 1 cell away, but the edge lies halfway between them.
        float cells = GridSampling.Bilinear(forestDepth, Map.Width, Map.Height, cell) - 0.5f;
        return Mathf.Max(0f, cells * Map.CellSize);
    }

    /// <summary>0-1: how much the land stands above its surroundings (a ridge or crest).</summary>
    public float Ridge(Vector2 cell) =>
        RegionMath.SmoothStep(0f, Settings.ridgeDepth, GridSampling.Bilinear(relief, Map.Width, Map.Height, cell));

    /// <summary>0-1: loose rock spilling out from the foot of the escarpment.</summary>
    public float Scree(Vector2 cell) => TerrainPaint.Scree(Map, cell, cellsToCliff, Settings);

    // ---------- helpers ----------

    float Soft(bool[] mask, Vector2 cell) =>
        RegionMath.SmoothStep(SoftFrom, SoftTo, GridSampling.Bilinear(mask, Map.Width, Map.Height, cell));

    int SampleIndex(Vector2 world)
    {
        int c = Mathf.Clamp(Mathf.RoundToInt(world.x / Field.StepX), 0, Field.Resolution - 1);
        int r = Mathf.Clamp(Mathf.RoundToInt(world.y / Field.StepZ), 0, Field.Resolution - 1);
        return r * Field.Resolution + c;
    }

    static float[] MetresToMarked(HeightField field, System.Func<int, int, bool> isMarked)
    {
        int n = field.Resolution;
        var marked = new bool[n * n];
        for (int r = 0; r < n; r++)
        for (int c = 0; c < n; c++)
            marked[r * n + c] = isMarked(c, r);

        GridOps.NearestMarked(marked, n, n, out var samples, out _);
        float step = 0.5f * (field.StepX + field.StepZ);
        for (int i = 0; i < samples.Length; i++) samples[i] *= step;
        return samples;
    }
}
