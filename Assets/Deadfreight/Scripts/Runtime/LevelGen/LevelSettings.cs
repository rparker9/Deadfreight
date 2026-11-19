// Scripts/Runtime/LevelGen/LevelSettings.cs
using UnityEngine;

/// <summary>
/// The settings for generating a level. These are stored in a ScriptableObject so they can be easily edited in the inspector and reused across levels.
/// </summary>
[CreateAssetMenu(fileName = "LevelSettings", menuName = "Level Gen/Level Settings")]
public sealed class LevelSettings : ScriptableObject
{
    [Header("Size")]
    [Unit("m"), Tooltip("Size of one grid cell. Every distance in cells below scales with this.")]
    public float cellSize = 50f;
    [Range(32, 400), Tooltip("Cells west to east. Real width = (cells - 1) × cell size. Bigger = more land, places and road; slower to generate.")]
    public int width = 160;
    [Range(32, 400), Tooltip("Cells north to south (not how tall the land is: that's Relief).")]
    public int height = 160;

    [Header("Land")]
    [Range(10f, 300f), Tooltip("Cells across one hill. Bigger = broader, gentler hills.")]
    public float hillScale = 70f;
    [Range(0.5f, 3f), Tooltip("1 = as the noise makes it. Above 1 flattens the lowland and sharpens the peaks.")]
    public float flatness = 1.4f;
    [Unit("m"), Tooltip("Height from the lowest to the highest ground in the level.")]
    public float relief = 120f;

    /// <summary>
    /// Keeps every "at least / at most" pair in order, and sizes positive.
    /// </summary>
    void OnValidate()
    {
        cellSize = Mathf.Max(1f, cellSize);
        relief = Mathf.Max(1f, relief);
    }
}
