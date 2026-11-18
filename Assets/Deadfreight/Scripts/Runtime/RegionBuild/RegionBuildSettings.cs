// Scripts/Runtime/RegionBuild/RegionBuildSettings.cs
using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>How a wheel feels on a kind of ground. Applied through GroundSurface.</summary>
[Serializable]
public struct SurfaceFeel
{
    [Tooltip("Multiplies each wheel's rolling resistance. Higher = more drag.")] public float rollingResistance;
    [Tooltip("Multiplies sideways grip and braking. Lower = slides more.")] public float grip;

    public SurfaceFeel(float rollingResistance, float grip)
    {
        this.rollingResistance = rollingResistance;
        this.grip = grip;
    }
}

/// <summary>
/// How the 3D region is built from the generated plan (RegionSettings shapes the plan itself),
/// in build order: the terrain's shape, water, roads, places, texture paint, what grows and
/// lies on the ground, then looks and ground feel. Materials and texture layers are optional;
/// plain colours are used without them. Distances are in metres unless shown otherwise.
/// </summary>
[CreateAssetMenu(fileName = "RegionBuildSettings", menuName = "Region Gen/Region Build Settings")]
public sealed class RegionBuildSettings : ScriptableObject
{
    static readonly int[] HeightmapResolutions = { 513, 1025, 2049, 4097 };

    // ---------- terrain shape ----------

    [Header("Terrain: resolution")]
    [Tooltip("Heightmap samples per side: 513, 1025, 2049 or 4097. 2049 is about 4 m between samples on an 8 km region.")]
    public int heightmapResolution = 2049;
    [Tooltip("Texture-paint samples per side (power of two). 1024 is about 8 m per pixel on an 8 km region.")]
    public int alphamapResolution = 1024;

    [Header("Terrain: detail on top of the plan")]
    [Unit("m"), Tooltip("Size of the small bumps added over the plan's hills...")] public float bumpScale = 120f;
    [Unit("m"), Tooltip("...and how tall they are.")] public float bumpHeight = 2.5f;
    [Unit("m"), Tooltip("Size of the finest ground roughness...")] public float roughScale = 25f;
    [Unit("m"), Tooltip("...and how tall it is.")] public float roughHeight = 0.4f;

    [Header("Terrain: escarpment")]
    [Unit("m"), Tooltip("Width of the escarpment's face in 3D. The plan's face is much wider; this redraws it as a cliff.")]
    public float cliffWidth = 8f;

    [Header("Terrain: terraces")]
    [Tooltip("Step hillsides into shelves in some zones of the region.")] public bool terraces = true;
    [Unit("m"), Tooltip("Size of terrace zones.")] public float terraceZoneScale = 4000f;
    [Range(0f, 1f), Tooltip("Zone noise above this starts terracing...")] public float terraceZoneStart = 0.55f;
    [Range(0f, 1f), Tooltip("...and from here it's fully terraced.")] public float terraceZoneFull = 0.7f;
    [Unit("m"), Tooltip("Height of one shelf.")] public float terraceStep = 8f;
    [Range(0.05f, 1f), Tooltip("Share of each step taken by the riser. Lower = sharper risers, wider shelves.")]
    public float terraceRiser = 0.35f;
    [Range(0f, 1f), Tooltip("Share of the original slope shelves keep, so they tilt like real terraces.")]
    public float terraceTilt = 0.2f;
    [Unit("m"), Tooltip("How far shelf edges wander off the contour lines...")] public float terraceWobble = 1.5f;
    [Unit("m"), Tooltip("...and over what distance.")] public float terraceWobbleScale = 120f;
    [Range(0f, 1f), Tooltip("Only ground steeper than this (rise over run) is terraced...")] public float terraceSlopeMin = 0.05f;
    [Range(0f, 1f), Tooltip("...and not ground steeper than this.")] public float terraceSlopeMax = 0.35f;

    // ---------- water ----------

    [Header("Lakes")]
    [Unit("m"), Tooltip("Where the map says water, the lake bed is at least this far below the surface.")]
    public float shoreDepth = 0.4f;
    [Unit("m"), Tooltip("Where the map says land, the ground is at least this far above the water. Roads never go lower either.")]
    public float landAboveWater = 0.2f;

    [Header("Rivers: channel")]
    [Unit("m"), Tooltip("Width where a river starts...")] public float riverMinWidth = 3f;
    [Unit("m"), Tooltip("...growing by this per √(cells drained)...")] public float riverWidthGrowth = 0.3f;
    [Unit("m"), Tooltip("...up to this.")] public float riverMaxWidth = 16f;
    [Unit("m"), Tooltip("Depth where a river starts...")] public float riverMinDepth = 0.6f;
    [Unit("m"), Tooltip("...growing by this per √(cells drained)...")] public float riverDepthGrowth = 0.06f;
    [Unit("m"), Tooltip("...up to this.")] public float riverMaxDepth = 3f;

    [Header("Rivers: banks")]
    [Unit("m"), Tooltip("The water surface sits this far below the ground along the river's line.")]
    public float riverBankDrop = 0.8f;
    [Unit("m"), Tooltip("Ground beside the channel blends back to natural over this distance.")]
    public float riverBankWidth = 6f;
    [Unit("m"), Tooltip("Banks lower than the water are raised to this far above it, so water never spills.")]
    public float riverBankLift = 0.3f;

    [Header("Rivers: line")]
    [Unit("m"), Tooltip("Distance between points along a river's line.")] public float riverSpacing = 2f;
    [Range(0f, 3f), Tooltip("Like Simplify Cells for roads: lower = rivers follow the plan more closely and wind more.")]
    public float riverSimplifyCells = 0.5f;
    [Unit("m"), Tooltip("The water surface is averaged over this distance before being forced downhill.")]
    public float riverSmoothing = 30f;

    // ---------- roads ----------

    [Header("Roads: width")]
    [Unit("m")] public float mainRoadWidth = 6.5f;
    [Unit("m")] public float backRoadWidth = 4.5f;
    [Unit("m")] public float laneWidth = 3.2f;

    [Header("Roads: line")]
    [Unit("m"), Tooltip("Distance between points along a road's centre line.")] 
    public float sampleSpacing = 1.5f;
    
    [Range(0f, 3f), Tooltip("Corners closer than this many cells to a straight line are dropped before smoothing. " +
                            "Higher = straighter roads that follow the plan less exactly (and cut through more ground).")]
    public float simplifyCells = 0.45f;

    [Header("Roads: height and setting into the ground")]
    [Unit("m"), Tooltip("Road heights are averaged over this distance, so roads cut through bumps and fill dips. " +
                        "Longer = flatter roads with deeper cuttings and higher embankments.")]
    public float profileSmoothing = 25f;
    
    [Range(0.02f, 0.25f), Tooltip("Steepest grade allowed, rise over run (0.1 = 10%). Lower = more cutting and filling on hills.")]
    public float maxGrade = 0.12f;
    
    [Unit("m"), Tooltip("Ground beside the road blends from road height back to natural ground over this distance. " +
                        "Wider = cuttings and embankments slope gently instead of standing as walls.")]
    
    public float shoulder = 15f;
    [Unit("m"), Tooltip("Road meshes sit this far above the ground pressed under them.")] public float surfaceLift = 0.05f;

    // ---------- places ----------

    [Header("Places")]
    [Unit("m"), Tooltip("Flat ground around each place...")] public float padRadius = 14f;
    [Unit("m"), Tooltip("...blending back to natural ground over this distance.")] public float padBlend = 10f;
    [Tooltip("Placeholder box size (m) for stops.")] public Vector3 buildingSize = new(8f, 5f, 10f);
    [Tooltip("Placeholder box size (m) for the site.")] public Vector3 siteSize = new(20f, 8f, 20f);

    // ---------- texture paint ----------

    [Header("Paint: rock and stone")]
    [Range(0f, 2f), Tooltip("Slope (rise over run) where rock starts showing...")] public float rockSlopeStart = 0.45f;
    [Range(0f, 2f), Tooltip("...and where it covers the ground.")] public float rockSlopeFull = 0.8f;
    [Unit("m"), Tooltip("Stony verge beside every road, measured from the road's edge.")] public float vergeWidth = 3f;
    [Unit("m"), Tooltip("Stony gravel bar at the water's edge along rivers...")] public float riverGravel = 2f;
    [Unit("m"), Tooltip("...then damp dirt out to this far from the water.")] public float riverDamp = 10f;
    [Unit("m"), Tooltip("Scree: stony ground spilling this far out from the foot of a cliff.")] public float screeReach = 40f;
    [Range(0f, 0.1f), Tooltip("How much lower (0-1 height) than its surroundings ground must be to collect dirt, or higher to go stony.")]
    public float ridgeDepth = 0.02f;
    [Range(0f, 1f), Tooltip("How stony ridges and crests get.")] public float ridgeStony = 0.6f;

    [Header("Paint: borders")]
    [Range(0f, 1f), Tooltip("Wobbles texture borders by up to this many plan cells, so the 50 m grid doesn't show.")]
    public float paintWarp = 0.35f;
    [Range(0f, 0.5f), Tooltip("How far noise pushes each border in or out, so edges wander. Biome borders use this too.")]
    public float edgeNoise = 0.25f;
    [Unit("m"), Tooltip("Size of the wanders in a border.")] public float edgeNoiseScale = 25f;

    [Header("Terrain look")]
    [Tooltip("Terrain material: URP's Terrain/Lit with Enable Height-based Blend on. Empty = URP's default.")]
    public Material terrainMaterial;
    [Tooltip("Open ground. Grass.")] public TerrainLayer grassLayer;
    [Tooltip("Forest floor, damp and low ground. Dirt.")] public TerrainLayer dirtLayer;
    [Tooltip("Road verges, scree below cliffs, river banks, ridges. Gravel, Rocky, or Sand for a lowland region.")]
    public TerrainLayer stonyLayer;
    [Tooltip("Steep faces and cliffs. Rock.")] public TerrainLayer rockLayer;

    // ---------- what grows ----------

    [Header("Biomes")]
    [Tooltip("What grows in each kind of ground. Empty slots grow nothing.")]
    public BiomeSet biomes = new();
    [Tooltip("Points per side of the biome map. 1024 is about 8 m apart on an 8 km region.")]
    public int biomeResolution = 1024;
    [Unit("m"), Tooltip("Verge: the band beside every road.")] public float biomeVergeWidth = 8f;
    [Unit("m"), Tooltip("Riverbank: the band along river water.")] public float riverbankWidth = 15f;
    [Tooltip("Upland: ground steeper than x (rise over run) starts to count, and from y it's all upland...")]
    public Vector2 uplandSlope = new(0.35f, 0.55f);
    [Tooltip("...as does ground higher than x, fully from y, on the plan's 0-1 height scale.")]
    public Vector2 uplandHeight = new(0.75f, 0.9f);
    [Unit("m"), Tooltip("DeepForest: this far in from a forest's edge. Before that it's ForestEdge.")]
    public float deepForestDepth = 60f;

    [Header("Trees: where")]
    [Range(0f, 2f), Tooltip("No trees on ground steeper than this (rise over run).")] public float treeMaxSlope = 0.6f;
    [Unit("m"), Tooltip("Trees keep this far from a road's surface (and from the pads around places).")]
    public float treeRoadClearance = 3f;
    [Unit("m"), Tooltip("Trees keep this far from river water.")] public float treeRiverClearance = 2f;
    [Unit("m"), Tooltip("No two trees closer than this, even where biomes overlap.")] public float treeGap = 2.5f;

    [Header("Trees: how many")]
    [Unit("m"), Tooltip("Trees are at full density within this distance of a road...")] public float treeThinFrom = 250f;
    [Unit("m"), Tooltip("...thinning until this far away...")] public float treeThinTo = 700f;
    [Range(0f, 1f), Tooltip("...to this share of full density. 1 = no thinning.")] public float treeFarDensity = 0.4f;
    [Min(0), Tooltip("Hard cap. If more trees qualify, a random selection is kept, thinning the whole region evenly.")]
    public int maxTrees = 100000;

    [Header("Trees: drawing")]
    [Unit("m"), Tooltip("Trees are drawn out to this distance from the camera.")] public float treeDistance = 500f;
    [Unit("m"), Tooltip("Beyond this distance trees are drawn as flat billboards. Ignored for trees with a LODGroup.")]
    public float treeBillboardDistance = 150f;
    [Unit("m"), Tooltip("Trees cross-fade from mesh to billboard over this distance.")] public float treeCrossFadeLength = 100f;

    [Header("Foliage (terrain details)")]
    [Tooltip("Detail cells per side (power of two). 2048 is about 4 m per cell on an 8 km region.")]
    public int detailResolution = 2048;
    [Unit("m"), Tooltip("Foliage is drawn out to this distance from the camera (Unity caps it at 250).")]
    public float detailDistance = 250f;
    [Range(0f, 1f), Tooltip("Scales the density of all foliage. 1 = full; lower it for speed.")] public float detailDensity = 1f;
    [FormerlySerializedAs("coverPatchScale"), Unit("m"), Tooltip("Size of the patches foliage grows in.")]
    public float foliagePatchScale = 40f;

    [Header("Rocks")]
    [Tooltip("Boulders, stones, logs and stumps, by landform. Empty = none.")] public RockSet rocks;
    [Unit("m"), Tooltip("Rocks keep this far from a road's surface (and from the pads around places).")]
    public float rockRoadClearance = 2f;
    [Min(0), Tooltip("Hard cap. Each rock is a scene object, so keep this in the low thousands.")]
    public int maxRocks = 4000;

    // ---------- looks and feel ----------

    [Header("Look: roads, water, places (optional)")]
    public Material mainRoadMaterial;
    public Material backRoadMaterial;
    public Material laneMaterial;
    public Material waterMaterial;
    [Tooltip("River water. Empty = the water material.")] public Material riverMaterial;
    public Material buildingMaterial;
    public Material siteMaterial;

    [Header("Ground feel (GroundSurface)")]
    public SurfaceFeel offRoad = new(2.5f, 0.9f);
    public SurfaceFeel mainRoad = new(1f, 1f);
    public SurfaceFeel backRoad = new(1.5f, 0.95f);
    public SurfaceFeel lane = new(2f, 0.9f);

    // ---------- lookups ----------

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

    // ---------- checks ----------

    /// <summary>Snaps resolutions to what Unity accepts, keeps sizes positive and every min/max pair in order.</summary>
    void OnValidate()
    {
        heightmapResolution = Closest(HeightmapResolutions, heightmapResolution);
        alphamapResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(alphamapResolution, 64, 4096));
        detailResolution = Mathf.ClosestPowerOfTwo(Mathf.Clamp(detailResolution, 64, 4096));
        biomeResolution = Mathf.Clamp(biomeResolution, 64, 4096);
        detailDistance = Mathf.Clamp(detailDistance, 0f, 250f);

        sampleSpacing = Mathf.Max(0.25f, sampleSpacing);
        riverSpacing = Mathf.Max(0.25f, riverSpacing);
        riverBankWidth = Mathf.Max(0.1f, riverBankWidth);
        profileSmoothing = Mathf.Max(0f, profileSmoothing);
        shoulder = Mathf.Max(0.1f, shoulder);
        padBlend = Mathf.Max(0.1f, padBlend);
        treeGap = Mathf.Max(0f, treeGap);
        foliagePatchScale = Mathf.Max(1f, foliagePatchScale);

        riverMaxWidth = Mathf.Max(riverMinWidth, riverMaxWidth);
        riverMaxDepth = Mathf.Max(riverMinDepth, riverMaxDepth);
        terraceZoneFull = Mathf.Max(terraceZoneStart, terraceZoneFull);
        terraceSlopeMax = Mathf.Max(terraceSlopeMin, terraceSlopeMax);
        rockSlopeFull = Mathf.Max(rockSlopeStart, rockSlopeFull);
        uplandSlope.y = Mathf.Max(uplandSlope.x, uplandSlope.y);
        uplandHeight.y = Mathf.Max(uplandHeight.x, uplandHeight.y);
        treeThinTo = Mathf.Max(treeThinFrom, treeThinTo);
    }

    static int Closest(int[] options, int value)
    {
        int best = options[0];
        foreach (int option in options)
            if (Mathf.Abs(option - value) < Mathf.Abs(best - value)) best = option;
        return best;
    }
}