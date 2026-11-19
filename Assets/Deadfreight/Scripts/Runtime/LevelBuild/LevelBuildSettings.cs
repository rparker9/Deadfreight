// Scripts/Runtime/LevelBuild/LevelBuildSettings.cs
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
/// How the 3D level is drawn from the generated plan. LevelSettings decides the land, the
/// roads and their heights, and the pads; these only control resolution, how roads are laid
/// into the ground, looks and ground feel, in build order. Materials and the ground layer are
/// optional; plain colours are used without them. Distances are in metres.
/// </summary>
[CreateAssetMenu(fileName = "LevelBuildSettings", menuName = "Level Gen/Level Build Settings")]
public sealed class LevelBuildSettings : ScriptableObject
{
    static readonly int[] HeightmapResolutions = { 513, 1025, 2049, 4097 };

    [Header("Terrain")]
    [Tooltip("Heightmap samples per side: 513, 1025, 2049 or 4097. 2049 is about 4 m between samples on an 8 km level.")]
    public int heightmapResolution = 2049;

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

    [Header("Roads: setting into the ground (heights and grade come from the plan)")]
    [Unit("m"), Tooltip("Ground beside the road blends from road height back to natural ground over this distance. " +
                        "Wider = cuttings and embankments slope gently instead of standing as walls.")]
    public float shoulder = 15f;
    [Unit("m"), Tooltip("Road meshes sit this far above the ground pressed under them.")] public float surfaceLift = 0.05f;

    [Header("Places (pads come from the plan)")]
    [Tooltip("Placeholder box size (m) for stops.")] public Vector3 buildingSize = new(8f, 5f, 10f);
    [Tooltip("Placeholder box size (m) for the site.")] public Vector3 siteSize = new(20f, 8f, 20f);

    [Header("Look (optional)")]
    [Tooltip("Terrain material, e.g. URP's Terrain/Lit. Empty = URP's default.")]
    public Material terrainMaterial;
    [FormerlySerializedAs("grassLayer"), Tooltip("The one texture covering the ground. Empty = plain colour.")]
    public TerrainLayer groundLayer;
    public Material mainRoadMaterial;
    public Material backRoadMaterial;
    public Material laneMaterial;
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

    /// <summary>Snaps the heightmap resolution to what Unity accepts, and keeps sizes positive.</summary>
    void OnValidate()
    {
        heightmapResolution = Closest(HeightmapResolutions, heightmapResolution);
        sampleSpacing = Mathf.Max(0.25f, sampleSpacing);
        shoulder = Mathf.Max(0.1f, shoulder);
    }

    static int Closest(int[] options, int value)
    {
        int best = options[0];
        foreach (int option in options)
            if (Mathf.Abs(option - value) < Mathf.Abs(best - value)) best = option;
        return best;
    }
}
