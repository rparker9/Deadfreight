// Scripts/Runtime/RegionGen/RegionSettings.cs
using UnityEngine;

/// <summary>
/// Everything that shapes a generated region, in the order the generator uses it: the land,
/// water, forest and hollows; then where the road comes in, the site and the stops; then how
/// roads are routed and how fast they are. One asset per region type.
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
    [Range(32, 400), Tooltip("Cells west to east. Real width = (cells - 1) × cell size. Bigger = more land, rivers, places and road; slower to generate.")]
    public int width = 160;
    [Range(32, 400), Tooltip("Cells north to south (not how tall the land is: that's Relief).")]
    public int height = 160;

    // ---------- land ----------

    [Header("Land: base terrain")]
    [Range(10f, 300f), Tooltip("Cells across one hill of the base terrain. Bigger = broader, gentler hills.")]
    public float hillScale = 70f;
    [Range(0.5f, 3f), Tooltip("1 = as the noise makes it. Above 1 flattens the lowland and sharpens the peaks.")]
    public float flatness = 1.4f;
    [Unit("m"), Tooltip("Height from the lowest to the highest ground in the region.")]
    public float relief = 120f;
    [Range(0f, 0.6f), Tooltip("Ground below this (0-1 height) is lake. The land is rescaled so about this share stays under water.")]
    public float seaLevel = 0.18f;

    [Header("Land: hill country")]
    [Tooltip("Raise ridged hills in some parts of the map; the rest stays rolling farmland.")]
    public bool hillCountry = true;
    [Range(20f, 400f), Tooltip("Cells across one hill-country zone. Bigger = fewer, larger zones.")]
    public float hillZoneScale = 110f;
    [Range(0f, 1f), Tooltip("Where the zone noise passes this, farmland starts turning to hill country...")]
    public float hillZoneStart = 0.4f;
    [Range(0f, 1f), Tooltip("...and from here on it's fully hill country. Further from Start = softer zone edges.")]
    public float hillZoneFull = 0.65f;
    [Range(5f, 150f), Tooltip("Cells across one ridge.")]
    public float ridgeScale = 30f;
    [Range(0f, 2f), Tooltip("Ridge height, as a share of the land's height range before rescaling.")]
    public float ridgeHeight = 0.9f;

    [Header("Land: escarpment")]
    [Tooltip("Raise one side of the region into an upland behind a steep face, crossable only at passes.")]
    public bool escarpment = true;
    [Unit("m"), Tooltip("How far the upland stands above the lowland.")]
    public float escarpmentHeight = 35f;
    [Range(0f, 0.4f), Tooltip("How far off the map's centre the face may run, as a share of the map. 0 = always through the middle.")]
    public float escarpmentOffset = 0.15f;
    [Range(0f, 30f), Tooltip("Cells the face wanders either side of a straight line.")]
    public float escarpmentWobble = 8f;
    [Range(0.5f, 6f), Tooltip("Half-width of the face in the plan, in cells. The 3D build redraws it as a much steeper cliff.")]
    public float escarpmentFace = 1.5f;

    [Space]
    [Range(1, 8), Tooltip("Passes through the face: at least this many...")]
    public int passCountMin = 3;
    [Range(1, 8), Tooltip("...and at most this many.")]
    public int passCountMax = 4;
    [Range(2f, 30f), Tooltip("Cells along the face that each pass spans.")]
    public float passWidth = 8f;
    [Range(2f, 40f), Tooltip("Cells either side of the face that a pass's climb is spread over. Bigger = a longer, gentler ramp.")]
    public float passRamp = 12f;

    // ---------- water ----------

    [Header("Water: rivers")]
    [Range(20f, 1000f), Tooltip("A cell becomes river once this many cells drain through it. Lower = more, smaller streams.")]
    public float riverArea = 250f;

    [Header("Water: river valleys")]
    [Tooltip("Lower the land around rivers into valleys, wider and deeper downstream.")]
    public bool riverValleys = true;
    [Range(0f, 20f), Tooltip("Valley half-width in cells where a river starts...")]
    public float valleyWidth = 4f;
    [Range(0f, 0.5f), Tooltip("...growing by this many cells per √(cells drained)...")]
    public float valleyWidthGrowth = 0.08f;
    [Range(0f, 40f), Tooltip("...up to this.")]
    public float valleyMaxWidth = 10f;
    [Unit("m"), Tooltip("Valley depth where a river starts...")]
    public float valleyDepth = 12f;
    [Unit("m"), Tooltip("...growing by this per √(cells drained)...")]
    public float valleyDepthGrowth = 0.3f;
    [Unit("m"), Tooltip("...up to this.")]
    public float valleyMaxDepth = 30f;

    [Header("Water: flood plains and marsh")]
    [Tooltip("Flatten low ground near water into broad bottomland.")]
    public bool floodPlains = true;
    [Range(0f, 40f), Tooltip("Cells from water that can become flood plain.")]
    public float floodPlainReach = 10f;
    [Range(0f, 0.5f), Tooltip("Only ground up to this far (0-1 height) above sea level is flattened.")]
    public float floodPlainBand = 0.12f;
    [Range(0f, 1f), Tooltip("Flood-plain height above sea level is scaled by this. Lower = flatter.")]
    public float floodPlainSquash = 0.25f;
    [Range(0f, 0.5f), Tooltip("Low ground near water, up to this far (0-1 height) above sea level, is marsh.")]
    public float marshBand = 0.12f;

    // ---------- forest and hollows ----------

    [Header("Forest")]
    [Range(5f, 200f), Tooltip("Cells across one forest patch. Bigger = fewer, larger forests.")]
    public float forestScale = 40f;
    [Range(-1f, 1f), Tooltip("Higher = less forest. Around 0 is about half forest.")]
    public float forestThreshold = 0.05f;

    [Header("Hollows")]
    [Range(0f, 0.2f), Tooltip("How much lower (0-1 height) than its surroundings ground must be to count as a hollow, where fog collects.")]
    public float hollowDepth = 0.05f;
    [Unit("m"), Tooltip("Hollows are dug this much deeper into bowls, so fog has somewhere to pool.")]
    public float hollowBowl = 4f;

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

    // ---------- roads ----------

    [Header("Road routing: cost per cell")]
    [Range(0.01f, 0.3f), Tooltip("Rise over run counted as a steep grade: costs Slope Cost to route over, and slows travel by Steep Factor.")]
    public float steepSlope = 0.08f;
    [Min(0f), Tooltip("Extra cost for steep ground. Higher = roads go round hills.")]
    public float slopeCost = 25f;
    [Min(0f), Tooltip("Extra cost through forest.")]
    public float forestCost = 2f;
    [Min(0f), Tooltip("Extra cost through marsh.")]
    public float marshCost = 3f;
    [Min(0f), Tooltip("Extra cost to ford a river. Higher = fewer crossings.")]
    public float fordCost = 12f;
    [Min(0f), Tooltip("Extra cost per cell of cliff, so roads use the passes. Not impassable: if no pass can be reached, a road cuts through.")]
    public float cliffCost = 200f;

    [Header("Road routing: map edge and search")]
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
    [Unit("mph"), Tooltip("Speed on the main road (and trunk) in good conditions. The rest are multipliers on it.")]
    public float mainRoadSpeed = 40f;
    [Range(0.1f, 1f), Tooltip("Back roads are dirt.")]
    public float backRoadFactor = 0.65f;
    [Range(0.1f, 1f), Tooltip("On grades steeper than Steep Slope.")]
    public float steepFactor = 0.6f;
    [Range(0.1f, 1f), Tooltip("Through forest.")]
    public float forestFactor = 0.75f;
    [Range(0.1f, 1f), Tooltip("Through marsh (mud).")]
    public float mudFactor = 0.45f;
    [Range(0.1f, 1f), Tooltip("Fording a river.")]
    public float fordFactor = 0.3f;

    // ---------- checks ----------

    /// <summary>Keeps every "at least / at most" pair in order, and sizes positive.</summary>
    void OnValidate()
    {
        cellSize = Mathf.Max(1f, cellSize);
        relief = Mathf.Max(1f, relief);
        hillZoneFull = Mathf.Max(hillZoneStart, hillZoneFull);
        passCountMax = Mathf.Max(passCountMin, passCountMax);
        valleyMaxWidth = Mathf.Max(valleyWidth, valleyMaxWidth);
        valleyMaxDepth = Mathf.Max(valleyDepth, valleyMaxDepth);
        siteMaxDistance = Mathf.Max(siteMinDistance, siteMaxDistance);
        viaMax = Mathf.Max(viaMin, viaMax);
        viaAlongMax = Mathf.Max(viaAlongMin, viaAlongMax);
        altMaxMinutes = Mathf.Max(altMinMinutes, altMaxMinutes);
    }
}
