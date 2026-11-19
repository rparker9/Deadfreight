// Scripts/Runtime/Region/RegionProfile.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everything that makes a region look and feel like itself. A level is always exactly one
/// region: the lobby's heist picks the region, and the seed only varies the layout within it.
///
///   Land        how high and hilly the ground is, and the region's big shape (its Landform)
///   Water       how much of the level is under water, and how deep
///   Zones       the kinds of ground inside the level, where each goes, and how each looks
///   Atmosphere  the day-night look: light, sky, ambient and fog
///
/// Used by both LevelGenerator (land, zones) and LevelBuilder (looks, atmosphere).
/// </summary>
[CreateAssetMenu(fileName = "Region", menuName = "Level/Region")]
public sealed class RegionProfile : ScriptableObject
{
    [TextArea(2, 4), Tooltip("What this region is, for whoever edits it. Not shown in game.")]
    public string notes;

    [Header("Land")]
    [Unit("m"), Tooltip("Metres from the lowest ground in the level to the highest.")]
    public float relief = 120f;
    [Range(10f, 300f), Tooltip("Cells across one of the base hills. Bigger = broader, gentler hills.")]
    public float hillScale = 70f;
    [Range(0.5f, 3f), Tooltip("1 = the base hills as the noise makes them. Above 1 flattens the lowland and sharpens the peaks.")]
    public float flatness = 1.4f;
    [Range(0f, 60f), Tooltip("How far, in cells, the base hills are twisted: 0 = round lumps; more = curving ridges, spurs and coves.")]
    public float warp = 28f;
    [Tooltip("The region's big shape, laid over the base hills (a valley, an escarpment...). Empty = base hills only.")]
    public Landform landform;

    [Header("Water")]
    [Range(0f, 0.9f), Tooltip("Share of the level under water. 0 = no water. The land is reshaped so exactly this much lies below the water line, whatever the seed.")]
    public float waterCover = 0f;
    [Range(0.01f, 0.9f), Tooltip("Height of the water line, as a share of the relief: the lower it is, the shallower the water and the more of the relief stands above it.")]
    public float waterLevel = 0.15f;
    [Tooltip("Material for the water surface. Empty = a plain dark water colour.")]
    public Material waterMaterial;

    [Header("Zones")]
    [Tooltip("Kinds of ground in the level. Each cell takes the first zone whose ranges it fits, top to bottom; cells that fit none take the last. At most 255.")]
    public List<ZoneDefinition> zones = new()
    {
        new ZoneDefinition { name = "Steep ground", previewColor = new Color(0.55f, 0.50f, 0.45f), minSlope = 0.15f },
        new ZoneDefinition { name = "Low ground", previewColor = new Color(0.33f, 0.48f, 0.30f), maxHeight = 0.35f },
        new ZoneDefinition { name = "High ground", previewColor = new Color(0.62f, 0.60f, 0.42f) },
    };

    [Header("Atmosphere")]
    [Tooltip("The day-night look for this region, handed to the scene's DayNightCycle when the level is built. Empty = leave the scene's as it is.")]
    public DayNightProfile dayNight;

    /// <summary>Keeps sizes positive and every zone's ranges in order.</summary>
    void OnValidate()
    {
        relief = Mathf.Max(1f, relief);
        foreach (var zone in zones) zone.Validate();
    }
}
