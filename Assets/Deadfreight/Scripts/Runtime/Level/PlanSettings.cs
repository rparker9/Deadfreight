// Scripts/Runtime/LevelGen/PlanSettings.cs
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// What every level's plan shares, whatever its region: the grid, the border, and where the
/// start, site and settlements go. What makes each region different (land, zones, atmosphere)
/// is in its RegionProfile.
/// </summary>
[CreateAssetMenu(fileName = "PlanSettings", menuName = "Level/Plan Settings")]
public sealed class PlanSettings : ScriptableObject
{
    [Header("Size")]
    [Unit("m"), Tooltip("Metres between neighbouring cells. Every distance in cells below scales with this.")]
    public float cellSize = 50f;
    [FormerlySerializedAs("width")]
    [Range(32, 400), Tooltip("Cells west to east. The level is (cells - 1) × cell size across. More = more land; slower to generate.")]
    public int cellsX = 160;
    [FormerlySerializedAs("height")]
    [Range(32, 400), Tooltip("Cells south to north.")]
    public int cellsY = 160;

    [Header("Border and entry")]
    [Unit("m"), Tooltip("Land around the playable area that's built but not played, so the world never visibly ends. The road in crosses it.")]
    public float borderWidth = 750f;
    [Range(0.1f, 1f), Tooltip("The road comes in somewhere along this middle share of a map edge, keeping it out of corners.")]
    public float entrySpan = 0.6f;
    [Unit("m"), Tooltip("How far inside the playable area the start place sits, along the road in.")]
    public float startInset = 150f;

    [Header("Places")]
    [Range(0.02f, 0.4f), Tooltip("Places need ground flatter than this, rise over run, and dry.")]
    public float maxPlaceSlope = 0.12f;
    [Unit("m"), Tooltip("Straight-line distance from the start to the site: at least this... It sets how long the run's drive is.")]
    public float siteMinDistance = 5500f;
    [Unit("m"), Tooltip("...and at most this.")]
    public float siteMaxDistance = 7000f;
    [Range(0, 60), Tooltip("Settlements (farmsteads) in a level, before the region's Settlement Density. Each makes travel that shapes the roads, and gets a road.")]
    public int settlements = 12;
    [Unit("m"), Tooltip("Minimum distance between any two places.")]
    public float placeSpacing = 550f;

    [Header("Roads")]
    [Range(0.02f, 0.3f), Tooltip("Steepest grade a road climbs before it costs far more, rise over run along the road. Roads wind across slopes to stay under it.")]
    public float maxRoadGrade = 0.08f;
    [Range(0f, 20f), Tooltip("How much climbing costs, below the max grade. 0 = roads ignore hills until the max; more = roads go round hills sooner, and run longer.")]
    public float roadGradeCost = 3f;
    [Range(1f, 200f), Tooltip("How many times more a metre of road through water costs than on dry ground. Roads cross water only to save a long way round, on a raised causeway.")]
    public float roadWaterCost = 30f;

    /// <summary>Keeps sizes positive.</summary>
    void OnValidate()
    {
        cellSize = Mathf.Max(1f, cellSize);
        borderWidth = Mathf.Max(0f, borderWidth);
        siteMaxDistance = Mathf.Max(siteMinDistance, siteMaxDistance);
        maxRoadGrade = Mathf.Max(0.01f, maxRoadGrade);
    }
}
