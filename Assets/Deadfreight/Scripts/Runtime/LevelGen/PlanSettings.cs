// Scripts/Runtime/LevelGen/PlanSettings.cs
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// What LevelGenerator makes a plan from. One asset per kind of level.
/// Distances are in cells unless a unit is shown (one cell = Cell Size metres).
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

    [Header("Land")]
    [Range(10f, 300f), Tooltip("Cells across one hill. Bigger = broader, gentler hills.")]
    public float hillScale = 70f;
    [Range(0.5f, 3f), Tooltip("1 = as the noise makes it. Above 1 flattens the lowland and sharpens the peaks.")]
    public float flatness = 1.4f;
    [Unit("m"), Tooltip("Metres from the lowest ground in the level to the highest.")]
    public float relief = 120f;

    /// <summary>Keeps sizes positive.</summary>
    void OnValidate()
    {
        cellSize = Mathf.Max(1f, cellSize);
        relief = Mathf.Max(1f, relief);
    }
}
