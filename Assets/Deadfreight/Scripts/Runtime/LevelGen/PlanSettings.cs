// Scripts/Runtime/LevelGen/PlanSettings.cs
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// What every level's plan shares, whatever its region: the grid. What makes each region
/// different (land, zones, atmosphere) is in its RegionProfile.
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

    /// <summary>Keeps sizes positive.</summary>
    void OnValidate()
    {
        cellSize = Mathf.Max(1f, cellSize);
    }
}
