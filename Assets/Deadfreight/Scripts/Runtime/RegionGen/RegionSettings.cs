// Scripts/Runtime/RegionGen/RegionSettings.cs
using UnityEngine;

/// <summary>
/// Everything that shapes a generated region, in the order the generator uses it: the land,
/// where the road comes in, the site and the stops, then how roads are routed and how fast
/// they are. One asset per region type.
///
/// Distances are in grid cells unless a unit is shown (one cell = cellSize metres). Heights
/// marked 0-1 are on the plan's own scale: 0 the lowest ground in the region, 1 the highest.
/// Placeholder values, tuned so the main road takes ~7-10 min one way and alternates
/// 15-22.5 min (trunk included).
/// </summary>
[CreateAssetMenu(fileName = "RegionSettings", menuName = "Region Gen/Region Settings")]
public sealed class RegionSettings : ScriptableObject
{
    // ---------- the map ----------

    [Header("Size")]
    [Unit("m"), Tooltip("Size of one grid cell. Every distance in cells below scales with this.")]
    public float cellSize = 50f;
    [Range(32, 400), Tooltip("Cells west to east. Real width = (cells - 1) × cell size. Bigger = more land, places and road; slower to generate.")]
    public int width = 160;
    [Range(32, 400), Tooltip("Cells north to south (not how tall the land is: that's Relief).")]
    public int height = 160;

    // ---------- land ----------

    [Header("Land (placeholder: simple hills)")]
    [Range(10f, 300f), Tooltip("Cells across one hill. Bigger = broader, gentler hills.")]
    public float hillScale = 70f;
    [Range(0.5f, 3f), Tooltip("1 = as the noise makes it. Above 1 flattens the lowland and sharpens the peaks.")]
    public float flatness = 1.4f;
    [Unit("m"), Tooltip("Height from the lowest to the highest ground in the region.")]
    public float relief = 120f;

    // ---------- the route ----------

    [Header("Entry (the road into the region)")]
    [Range(0.1f, 1f), Tooltip("The entry is picked from this middle share of an edge, keeping it out of corners.")]
    public float entrySpan = 0.6f;
    [Range(1, 20), Tooltip("Cells of straight road leading in from the edge, so it reads as coming from off the map.")]
    public int entryStub = 4;
    [Range(0f, 30f), Tooltip("Within this many cells of the entry, roads may touch the edge and no lanes join.")]
    public float entryClear = 8f;

    [Header("Trunk (shared road from the entry to the fork)")]
    [Range(1, 60), Tooltip("Cells of road from the entry to the fork. Every route drives it both ways.")]
    public int trunkLength = 12;

    [Header("Site (the destination)")]
    [Tooltip("Straight-line cells from the entry to the site: at least this... Sets the main road's length; must fit inside the map.")]
    public float siteMinDistance = 105f;
    [Tooltip("...and at most this.")]
    public float siteMaxDistance = 128f;

    [Header("Stops (farms, churches, mills...)")]
    [Range(0, 40), Tooltip("How many stops, each with a lane to the nearest road. Via points are added on top.")]
    public int stops = 16;
    [Range(5f, 60f), Tooltip("Minimum cells between any two places.")]
    public float placeSpacing = 17f;
    [Range(0.02f, 0.4f), Tooltip("Places need ground flatter than this (rise over run).")]
    public float maxPlaceSlope = 0.12f;
    [Unit("m"), Tooltip("Flat ground around each place, at the height of the road beside it...")]
    public float padRadius = 14f;
    [Unit("m"), Tooltip("...blending back to the ground over this distance.")]
    public float padBlend = 10f;

    // ---------- roads ----------

    [Header("Roads: height")]
    [Range(0.02f, 0.25f), Tooltip("Steepest grade a road may have, rise over run (0.12 = 12%). Lower = more cutting and filling on hills.")]
    public float maxGrade = 0.12f;
    [Unit("m"), Tooltip("Road heights are averaged over this distance, so roads cut through bumps and fill dips. Longer = flatter, deeper cuts.")]
    public float profileSmoothing = 25f;

    [Header("Road routing")]
    [Range(0.01f, 0.3f), Tooltip("Rise over run counted as a steep grade: costs Slope Cost to route over, and slows travel by Steep Factor.")]
    public float steepSlope = 0.08f;
    [Min(0f), Tooltip("Extra cost for steep ground. Higher = roads go round hills.")]
    public float slopeCost = 25f;
    [Range(0, 30), Tooltip("Roads avoid this many cells next to the map edge (except at the entry)...")]
    public int edgeMargin = 6;
    [Min(0f), Tooltip("...at this extra cost per cell.")]
    public float edgeCost = 2f;
    [Range(0f, 2f), Tooltip("A* heuristic weight. Lower = closer to the cheapest road, slower to find.")]
    public float heuristicWeight = 0.35f;

    [Header("Alternate routes: via points")]
    [Range(0, 6), Tooltip("Alternate routes from the fork to the site, besides the main road.")]
    public int alternates = 3;
    [Range(1f, 3f), Tooltip("Each alternate passes a via point. Its detour (fork→via + via→site, over fork→site straight) is at least this...")]
    public float viaMin = 1.25f;
    [Range(1f, 3f), Tooltip("...and at most this.")]
    public float viaMax = 1.9f;
    [Range(0f, 1f), Tooltip("Via points sit along the fork→site line between this (0 = fork, 1 = site)...")]
    public float viaAlongMin = 0.3f;
    [Range(0f, 1f), Tooltip("...and this, so routes bow out instead of running alongside.")]
    public float viaAlongMax = 0.7f;
    [Range(1, 50), Tooltip("Via points tried per alternate before giving up on it.")]
    public int viaTries = 12;

    [Header("Alternate routes: keeping apart")]
    [Range(0, 40), Tooltip("Alternates are pushed away from existing road out to this many cells.")]
    public int avoidRadius = 12;
    [Range(1f, 50f), Tooltip("Cost multiplier on and near existing road.")]
    public float avoidCost = 10f;
    [Tooltip("On: the push fades with distance, so routes keep moving away. Off: a hard band that routes tend to hug.")]
    public bool avoidFalloff = true;
    [Range(0f, 30f), Tooltip("Within this many cells of the fork and the site, alternates may share road...")]
    public float sharedEnds = 8f;
    [Range(0.1f, 1f), Tooltip("...at this cost multiplier. Below 1 = they prefer to.")]
    public float reuseCost = 0.65f;

    [Header("Alternate routes: time window (one way, trunk included)")]
    [Unit("min"), Tooltip("Alternates must take at least this long...")]
    public float altMinMinutes = 15f;
    [Unit("min"), Tooltip("...and at most this long.")]
    public float altMaxMinutes = 22.5f;
    [Unit("min"), Tooltip("If none fits, keep the closest route if it misses the window by no more than this.")]
    public float altTolerance = 2f;

    // ---------- travel ----------

    [Header("Travel speed (for route times)")]
    [Unit("mph"), Tooltip("Speed on the main road (and trunk). The rest are multipliers on it.")]
    public float mainRoadSpeed = 40f;
    [Range(0.1f, 1f), Tooltip("Back roads and lanes are dirt.")]
    public float backRoadFactor = 0.65f;
    [Range(0.1f, 1f), Tooltip("On grades steeper than Steep Slope.")]
    public float steepFactor = 0.6f;

    // ---------- checks ----------

    /// <summary>Keeps every "at least / at most" pair in order, and sizes positive.</summary>
    void OnValidate()
    {
        cellSize = Mathf.Max(1f, cellSize);
        relief = Mathf.Max(1f, relief);
        siteMaxDistance = Mathf.Max(siteMinDistance, siteMaxDistance);
        viaMax = Mathf.Max(viaMin, viaMax);
        viaAlongMax = Mathf.Max(viaAlongMin, viaAlongMax);
        altMaxMinutes = Mathf.Max(altMinMinutes, altMaxMinutes);
        padBlend = Mathf.Max(0.1f, padBlend);
        profileSmoothing = Mathf.Max(0f, profileSmoothing);
    }
}
