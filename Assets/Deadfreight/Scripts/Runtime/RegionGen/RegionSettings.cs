// Scripts/Runtime/RegionGen/RegionSettings.cs
using UnityEngine;

/// <summary>
/// Everything that shapes a generated region: land, where the road comes in, places,
/// how roads are routed, and how fast each kind of road is. One asset per region type.
/// Distances are in grid cells unless a unit is shown. Placeholder values, tuned so the
/// main road takes ~7-10 min one way and alternates 15-22.5 min (trunk included).
/// </summary>
[CreateAssetMenu(fileName = "RegionSettings", menuName = "Region Gen/Region Settings")]
public sealed class RegionSettings : ScriptableObject
{
    [Header("Size")]
    [Unit("m"), Tooltip("Size of one grid cell.")] public float cellSize = 50f;

    [Tooltip("Width and height of the region in cells. Bigger = more land, more road, more places.")]
    [Min(32)] public int width = 160;
    [Min(32)] public int height = 130;

    [Header("Land")]
    [Tooltip("Cells per hill. Bigger = broader hills.")] public float hillScale = 70f;
    [Tooltip("Above 1 flattens lowland and sharpens peaks.")] public float flatness = 1.4f;
    [Unit("m"), Tooltip("Height difference between lowest and highest ground.")] public float relief = 120f;
    [Range(0f, 1f), Tooltip("Ground below this (0-1 height) is open water.")] public float seaLevel = 0.18f;
    [Range(0f, 0.5f), Tooltip("Low ground this far above sea level, near water, is marsh.")] public float marshBand = 0.12f;
    [Tooltip("Cells per forest patch.")] public float forestScale = 40f;
    [Range(-1f, 1f), Tooltip("Higher = less forest.")] public float forestThreshold = 0.05f;
    [Tooltip("Rise over run counted as a steep grade.")] public float steepSlope = 0.08f;

    [Header("Hill country")]
    [Tooltip("Raise ridged hills in some parts of the map; the rest stays rolling farmland.")]
    public bool hillCountry = true;
    [Tooltip("Cells across a hill-country zone. Bigger = fewer, larger zones.")] public float hillZoneScale = 110f;
    [Range(0f, 1f), Tooltip("Where the zone noise passes this, farmland starts turning to hill country...")]
    public float hillZoneStart = 0.4f;
    [Range(0f, 1f), Tooltip("...and from here it is fully hill country.")] public float hillZoneFull = 0.65f;
    [Tooltip("Cells across one ridge.")] public float ridgeScale = 30f;
    [Range(0f, 2f), Tooltip("Ridge height, as a share of the land's height range before rescaling.")]
    public float ridgeHeight = 0.9f;

    [Header("Escarpment")]
    [Tooltip("Raise one side of the region into an upland behind a steep face, crossable only at passes.")]
    public bool escarpment = true;
    [Unit("m"), Tooltip("How far the upland stands above the lowland.")] public float escarpmentHeight = 35f;
    [Range(0f, 0.4f), Tooltip("How far off the map's centre the face may run, as a share of the map.")]
    public float escarpmentOffset = 0.15f;
    [Tooltip("Cells the face wanders either side of a straight line.")] public float escarpmentWobble = 8f;
    [Tooltip("Half-width of the face in cells, in the plan. The 3D build draws it much steeper.")]
    public float escarpmentFace = 1.5f;
    [Min(1)] public int passCountMin = 3;
    [Min(1)] public int passCountMax = 4;
    [Tooltip("Cells along the face each pass spreads over.")] public float passWidth = 8f;
    [Tooltip("At a pass, the climb is spread over this half-width in cells: a long ramp.")] public float passRamp = 12f;

    [Header("Rivers")]
    [Min(10), Tooltip("A cell becomes river once this many cells drain through it. Lower = more, smaller streams.")]
    public float riverArea = 250f;

    [Header("River valleys")]
    [Tooltip("Lower the land around rivers into valleys, bigger downstream.")] public bool riverValleys = true;
    [Tooltip("Valley half-width in cells at a river's start...")] public float valleyWidth = 4f;
    [Tooltip("...growing by this many cells per √(cells drained)...")] public float valleyWidthGrowth = 0.08f;
    [Tooltip("...up to this.")] public float valleyMaxWidth = 10f;
    [Unit("m"), Tooltip("Valley depth at a river's start...")] public float valleyDepth = 12f;
    [Unit("m"), Tooltip("...growing by this per √(cells drained)...")] public float valleyDepthGrowth = 0.3f;
    [Unit("m"), Tooltip("...up to this.")] public float valleyMaxDepth = 30f;

    [Header("Flood plains")]
    [Tooltip("Flatten low ground near water into broad bottomland.")] public bool floodPlains = true;
    [Tooltip("Cells from water that can be flood plain.")] public float floodPlainReach = 10f;
    [Range(0f, 0.5f), Tooltip("Only ground within this height (0-1) above sea level is flattened.")]
    public float floodPlainBand = 0.12f;
    [Range(0f, 1f), Tooltip("Flood-plain height above sea level is scaled by this. Lower = flatter.")]
    public float floodPlainSquash = 0.25f;

    [Header("Hollows")]
    [Range(0f, 0.2f), Tooltip("How much lower than its surroundings ground must be to collect fog.")] public float hollowDepth = 0.05f;
    [Unit("m"), Tooltip("Hollows are dug this much deeper into bowls, so fog has somewhere to pool.")]
    public float hollowBowl = 4f;

    [Header("Entry (road into the region)")]
    [Range(0.1f, 1f), Tooltip("The entry is picked from this middle fraction of an edge, keeping it out of corners.")]
    public float entrySpan = 0.6f;
    [Min(1), Tooltip("Cells of straight road leading in from the edge, so it reads as coming from off the map.")]
    public int entryStub = 4;
    [Tooltip("Within this many cells of the entry, roads may touch the edge and no lanes join.")]
    public float entryClear = 8f;

    [Header("Site")]
    [Tooltip("Straight-line distance from the entry. Sets the main road's length.")] public float siteMinDistance = 105f;
    public float siteMaxDistance = 128f;

    [Header("Stops")]
    [Range(0, 40), Tooltip("Farms, churches, mills... each gets a lane to the road. Vias are added on top.")] public int stops = 16;
    [Tooltip("Minimum cells between places.")] public float placeSpacing = 17f;
    [Tooltip("Places need ground flatter than this.")] public float maxPlaceSlope = 0.12f;

    [Header("Trunk")]
    [Min(1), Tooltip("Cells of shared road from the entry to the fork. Every route drives it both ways.")]
    public int trunkLength = 12;

    [Header("Road routing costs (per cell)")]
    public float slopeCost = 25f;
    public float forestCost = 2f;
    public float marshCost = 3f;
    public float fordCost = 12f;
    [Tooltip("Extra cost per cell of cliff, so roads use the passes. Not impassable: if no pass can be reached, a road cuts through.")]
    public float cliffCost = 200f;
    [Tooltip("A* heuristic weight. Lower = more exact, slower.")] public float heuristicWeight = 0.35f;
    [Tooltip("Roads avoid this many cells next to the map edge (except at the entry).")] public int edgeMargin = 6;
    public float edgeCost = 2f;

    [Header("Alternate routes")]
    [Range(0, 6)] public int alternates = 3;
    [Tooltip("Via point detour: (fork→via + via→site) over the straight fork→site distance.")] public float viaMin = 1.25f;
    public float viaMax = 1.9f;
    [Range(0f, 1f), Tooltip("Vias sit over this stretch of the fork→site line (0 = fork, 1 = site), so routes bow out instead of running alongside.")]
    public float viaAlongMin = 0.3f;
    [Range(0f, 1f)] public float viaAlongMax = 0.7f;
    [Min(1), Tooltip("Via points tried per alternate before giving up.")] public int viaTries = 12;
    [Tooltip("Alternates are pushed away from existing road out to this many cells.")] public int avoidRadius = 12;
    [Tooltip("Cost multiplier on existing road. Fades to none at the avoid radius when falloff is on.")] public float avoidCost = 10f;
    [Tooltip("On: the push fades with distance, so routes keep moving away. Off: a hard band that routes tend to hug.")]
    public bool avoidFalloff = true;
    [Tooltip("Alternates may share road within this many cells of the fork and site.")] public float sharedEnds = 8f;
    [Tooltip("Cost multiplier for that shared road.")] public float reuseCost = 0.65f;

    [Header("Alternate time window (one way, trunk included)")]
    [Unit("min")] public float altMinMinutes = 15f;
    [Unit("min")] public float altMaxMinutes = 22.5f;
    [Unit("min"), Tooltip("If nothing fits, keep the closest route if it misses the window by no more than this.")]
    public float altTolerance = 2f;

    [Header("Travel")]
    [Unit("mph"), Tooltip("Speed on the main road (and trunk) in good conditions.")] public float mainRoadSpeed = 40f;
    [Range(0.1f, 1f), Tooltip("Back roads are dirt: speed multiplier.")] public float backRoadFactor = 0.65f;
    [Range(0.1f, 1f)] public float steepFactor = 0.6f;
    [Range(0.1f, 1f)] public float forestFactor = 0.75f;
    [Range(0.1f, 1f)] public float mudFactor = 0.45f;
    [Range(0.1f, 1f)] public float fordFactor = 0.3f;
}
