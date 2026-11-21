// Scripts/Runtime/Region/ZoneDefinition.cs
using System;
using UnityEngine;

/// <summary>
/// A zone is a kind of land, defined by height and slope ranges. Each region has its own set of zones, and each cell in the level plan is assigned to one of them.
/// </summary>
[Serializable]
public sealed class ZoneDefinition
{
    [Tooltip("Shown in the preview.")]
    public string name = "Zone";
    [Tooltip("The zone's colour in the preview's Zones view.")]
    public Color previewColor = Color.gray;

    [Range(0f, 1f), Tooltip("Lowest ground the zone covers: 0 = the lowest in the level, 1 = the highest...")]
    public float minHeight = 0f;
    [Range(0f, 1f), Tooltip("...and the highest.")]
    public float maxHeight = 1f;
    [Range(0f, 1f), Tooltip("Gentlest slope the zone covers, rise over run (0.1 = 10%)...")]
    public float minSlope = 0f;
    [Range(0f, 1f), Tooltip("...and the steepest.")]
    public float maxSlope = 1f;

    [Tooltip("Texture covering the zone's ground. Empty = plain Preview Color.")]
    public TerrainLayer groundLayer;

    /// <summary>
    /// Whether a cell fits: <paramref name="height01"/> is its height as a share of the relief, <paramref name="slope"/> rise over run.
    /// </summary>
    public bool Fits(float height01, float slope) =>
        height01 >= minHeight && height01 <= maxHeight && slope >= minSlope && slope <= maxSlope;

    /// <summary>
    /// Keeps each "lowest / highest" pair in order.
    /// </summary>
    public void Validate()
    {
        maxHeight = Mathf.Max(minHeight, maxHeight);
        maxSlope = Mathf.Max(minSlope, maxSlope);
    }
}
