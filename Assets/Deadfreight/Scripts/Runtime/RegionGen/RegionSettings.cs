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
    [Min(32)] public int width = 160;
    [Min(32)] public int height = 130;

    [Header("Land")]
    [Tooltip("Cells per hill. Bigger = broader hills.")] public float hillScale = 70f;
    [Tooltip("Above 1 flattens lowland and sharpens peaks.")] public float flatness = 1.4f;
    [Unit("m"), Tooltip("Height difference between lowest and highest ground.")] public float relief = 120f;
    [Range(0f, 1f), Tooltip("Ground below this (0-1 height) is open water.")] public float seaLevel = 0.18f;
    [Range(0, 30)] public int rivers = 8;
    [Range(0f, 1f), Tooltip("Rivers start from springs above this height.")] public float riverSourceHeight = 0.5f;
    [Range(0f, 0.5f), Tooltip("Low ground this far above sea level, near water, is marsh.")] public float marshBand = 0.12f;
    [Tooltip("Cells per forest patch.")] public float forestScale = 40f;
    [Range(-1f, 1f), Tooltip("Higher = less forest.")] public float forestThreshold = 0.05f;
    [Range(0f, 0.2f), Tooltip("How much lower than its surroundings ground must be to collect fog.")] public float hollowDepth = 0.05f;
    [Tooltip("Rise over run counted as a steep grade.")] public float steepSlope = 0.08f;

    [Header("Entry (road into the region)")]
    [Range(0.1f, 1f), Tooltip("The entry is picked from this middle fraction of an edge, keeping it out of corners.")]
    public float entrySpan = 0.6f;
    [Min(1), Tooltip("Cells of straight road leading in from the edge, so it reads as coming from off the map.")]
    public int entryStub = 4;
    [Tooltip("Within this many cells of the entry, roads may touch the edge and no lanes join.")]
    public float entryClear = 8f;

    [Header("Site")]
    [Tooltip("Straight-line distance from the entry. Sets the main road's length.")] public float siteMinDistance = 115f;
    public float siteMaxDistance = 140f;

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
