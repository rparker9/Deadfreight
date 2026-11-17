// Scripts/Runtime/RegionBuild/RegionBuildSettings.cs
using System;
using UnityEngine;

/// <summary>How a wheel feels on a kind of ground. Applied through GroundSurface.</summary>
[Serializable]
public struct SurfaceFeel
{
    [Tooltip("Multiplies each wheel's rolling resistance.")] public float rollingResistance;
    [Tooltip("Multiplies sideways grip and braking.")] public float grip;

    public SurfaceFeel(float rollingResistance, float grip)
    {
        this.rollingResistance = rollingResistance;
        this.grip = grip;
    }
}

/// <summary>
/// How the 3D region is built from the generated plan (RegionSettings shapes the plan itself):
/// terrain resolution and detail, how roads are smoothed and set into the ground, places, and
/// texture painting. Materials and texture layers are optional; plain colours are used without them.
/// </summary>
[CreateAssetMenu(fileName = "RegionBuildSettings", menuName = "Region Gen/Region Build Settings")]
public sealed class RegionBuildSettings : ScriptableObject
{
    static readonly int[] HeightmapResolutions = { 513, 1025, 2049, 4097 };

    [Header("Terrain")]
    [Tooltip("Heightmap samples per side: 513, 1025, 2049 or 4097. 2049 is about 4 m between samples on an 8 km region.")]
    public int heightmapResolution = 2049;
    [Tooltip("Texture-painting samples per side (power of two).")]
    public int alphamapResolution = 1024;
    [Unit("m"), Tooltip("Size of the small bumps added on top of the plan's hills.")] public float bumpScale = 120f;
    [Unit("m")] public float bumpHeight = 2.5f;
    [Unit("m"), Tooltip("Size of the finest ground roughness.")] public float roughScale = 25f;
    [Unit("m")] public float roughHeight = 0.4f;

    [Header("Lakes")]
    [Unit("m"), Tooltip("Where the map says water, the lake bed is at least this far below the surface.")]
    public float shoreDepth = 0.4f;
    [Unit("m"), Tooltip("Where the map says land, the ground is at least this far above the water.")]
    public float landAboveWater = 0.2f;

    [Header("Rivers")]
    [Unit("m"), Tooltip("Width of a river where it starts...")] public float riverMinWidth = 3f;
    [Unit("m"), Tooltip("...growing by this per √(cells drained)...")] public float riverWidthGrowth = 0.3f;
    [Unit("m"), Tooltip("...up to this.")] public float riverMaxWidth = 16f;
    [Unit("m"), Tooltip("Depth of a river where it starts...")] public float riverMinDepth = 0.6f;
    [Unit("m"), Tooltip("...growing by this per √(cells drained)...")] public float riverDepthGrowth = 0.06f;
    [Unit("m"), Tooltip("...up to this.")] public float riverMaxDepth = 3f;
    [Unit("m"), Tooltip("The water surface sits this far below the ground along the river's line.")]
    public float riverBankDrop = 0.8f;
    [Unit("m"), Tooltip("Ground beside the channel blends back to natural over this distance.")] public float riverBankWidth = 6f;
    [Unit("m"), Tooltip("Banks lower than the water are raised to this far above it, so water never spills.")]
    public float riverBankLift = 0.3f;
    [Unit("m"), Tooltip("Distance between points along a river's line.")] public float riverSpacing = 2f;
    [Tooltip("Like simplifyCells for roads: lower = rivers follow the plan more closely and wind more.")]
    public float riverSimplifyCells = 0.5f;
    [Unit("m"), Tooltip("The water surface is averaged over this distance before being forced downhill.")]
    public float riverSmoothing = 30f;

    [Header("Escarpment")]
    [Unit("m"), Tooltip("Width of the escarpment's face in 3D. The plan's face is much wider; this redraws it as a cliff.")]
    public float cliffWidth = 8f;

    [Header("Terraces")]
    [Tooltip("Step hillsides into shelves in some zones of the region.")] public bool terraces = true;
    [Unit("m"), Tooltip("Size of terrace zones.")] public float terraceZoneScale = 4000f;
    [Range(0f, 1f), Tooltip("Zone noise above this starts terracing...")] public float terraceZoneStart = 0.55f;
    [Range(0f, 1f), Tooltip("...and from here it is fully terraced.")] public float terraceZoneFull = 0.7f;
    [Unit("m"), Tooltip("Height of one shelf.")] public float terraceStep = 8f;
    [Range(0.05f, 1f), Tooltip("Share of each step taken by the riser. Lower = sharper risers, wider shelves.")]
    public float terraceRiser = 0.35f;
    [Range(0f, 1f), Tooltip("Share of the original slope shelves keep, so they tilt like real terraces.")]
    public float terraceTilt = 0.2f;
    [Unit("m"), Tooltip("How far shelf edges wander off the contour lines...")] public float terraceWobble = 1.5f;
    [Unit("m"), Tooltip("...and over what distance.")] public float terraceWobbleScale = 120f;
    [Tooltip("Only ground steeper than this (rise over run) is terraced...")] public float terraceSlopeMin = 0.05f;
    [Tooltip("...and not ground steeper than this.")] public float terraceSlopeMax = 0.35f;

    [Header("Roads")]
    [Unit("m")] public float mainRoadWidth = 6.5f;
    [Unit("m")] public float backRoadWidth = 4.5f;
    [Unit("m")] public float laneWidth = 3.2f;
    [Unit("m"), Tooltip("Ground beside the road blends from road height back to natural ground over this distance.")]
    public float shoulder = 10f;
    [Unit("m"), Tooltip("Distance between points along a road's centre line.")] public float sampleSpacing = 1.5f;
    [Tooltip("Corners closer than this many cells to a straight line are dropped before smoothing. " +
             "Higher = straighter, smoother roads that follow the plan less exactly.")]
    public float simplifyCells = 0.75f;
    [Unit("m"), Tooltip("Road heights are averaged over this distance, so roads cut through bumps and fill dips.")]
    public float profileSmoothing = 60f;
    [Tooltip("Steepest grade allowed, rise over run. 0.09 = 9%.")] public float maxGrade = 0.09f;
    [Unit("m"), Tooltip("Road meshes sit this far above the ground pressed under them.")] public float surfaceLift = 0.05f;

    [Header("Places")]
    [Unit("m"), Tooltip("Flat ground around each place.")] public float padRadius = 14f;
    [Unit("m")] public float padBlend = 10f;
    public Vector3 buildingSize = new(8f, 5f, 10f);
    public Vector3 siteSize = new(20f, 8f, 20f);

    [Header("Painting")]
    [Tooltip("Slope (rise over run) where rock starts showing...")] public float rockSlopeStart = 0.45f;
    [Tooltip("...and where it covers the ground.")] public float rockSlopeFull = 0.8f;
    [Tooltip("Wobbles texture borders by up to this many cells, so the 50 m grid doesn't show.")] public float paintWarp = 0.35f;

    [Header("Ground feel (GroundSurface)")]
    public SurfaceFeel offRoad = new(2.5f, 0.9f);
    public SurfaceFeel mainRoad = new(1f, 1f);
    public SurfaceFeel backRoad = new(1.5f, 0.95f);
    public SurfaceFeel lane = new(2f, 0.9f);

    [Header("Look (optional)")]
    public Material mainRoadMaterial;
    public Material backRoadMaterial;
    public Material laneMaterial;
    public Material waterMaterial;
    [Tooltip("Optional: river water. Empty = the water material.")] public Material riverMaterial;
    public Material buildingMaterial;
    public Material siteMaterial;

    [Header("Terrain look")]
    [Tooltip("Terrain material, e.g. the Shader Graph samples' TerrainLit (height-blended layers). Empty = URP's default.")]
    public Material terrainMaterial;
    [Tooltip("Four layers in this order: grass, forest floor, mud, rock. Anything else = plain colours.")]
    public TerrainLayer[] terrainLayers = new TerrainLayer[0];

    [Header("Ground cover (terrain details)")]
    [Tooltip("Detail cells per side (power of two). 2048 is about 4 m per cell on an 8 km region.")]
    public int detailResolution = 2048;
    [Unit("m"), Tooltip("Ground cover is drawn out to this distance from the camera (Unity caps it at 250).")]
    public float detailDistance = 80f;
    [Unit("m"), Tooltip("Size of the patches ground cover grows in.")] public float coverPatchScale = 40f;
    [Range(0f, 1f), Tooltip("0 = cover is even wherever its ground is; 1 = it only grows in patches.")]
    public float coverPatchiness = 0.6f;
    public GroundCover[] groundCover = new GroundCover[0];

    public float RoadWidth(RoadClass roadClass) => roadClass switch
    {
        RoadClass.Main => mainRoadWidth,
        RoadClass.Back => backRoadWidth,
        _ => laneWidth,
    };

    public SurfaceFeel RoadFeel(RoadClass roadClass) => roadClass switch
    {
        RoadClass.Main => mainRoad,
        RoadClass.Back => backRoad,
        _ => lane,
    };

    void OnValidate()
    {
        heightmapResolution = Closest(HeightmapResolutions, heightmapResolution);
        alphamapResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(alphamapResolution, 64, 4096));
        detailResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(detailResolution, 64, 4096));
        detailDistance = Mathf.Clamp(detailDistance, 0f, 250f);
        sampleSpacing = Mathf.Max(0.25f, sampleSpacing);
        riverSpacing = Mathf.Max(0.25f, riverSpacing);
        riverBankWidth = Mathf.Max(0.1f, riverBankWidth);
        profileSmoothing = Mathf.Max(0f, profileSmoothing);
        shoulder = Mathf.Max(0.1f, shoulder);
        padBlend = Mathf.Max(0.1f, padBlend);
    }

    static int Closest(int[] options, int value)
    {
        int best = options[0];
        foreach (int option in options)
            if (Mathf.Abs(option - value) < Mathf.Abs(best - value)) best = option;
        return best;
    }
}
