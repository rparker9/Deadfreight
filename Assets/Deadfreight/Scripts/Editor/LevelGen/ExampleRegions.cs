// Scripts/Editor/LevelGen/ExampleRegions.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools/Deadfreight/Create Example Regions: makes the Drowned Bayou and The Hollers, each with
/// its landform and day-night profile (ExampleRegions.Atmosphere.cs), as assets to build from
/// and tune. Running it again makes numbered copies; it never overwrites. Ground layers are left
/// empty for the art.
/// </summary>
public static partial class ExampleRegions
{
    const string Folder = "Assets/Deadfreight/Data/LevelGen/Regions";

    [MenuItem("Tools/Deadfreight/Create Example Regions")]
    static void Create()
    {
        var bayou = CreateBayou();
        CreateHollers();
        AssetDatabase.SaveAssets();
        Selection.activeObject = bayou;
        EditorGUIUtility.PingObject(bayou);
    }

    /// <summary>Wet, flat lowland: a wide river valley, a fifth of it under water, cypress swamp along the shores.</summary>
    static RegionProfile CreateBayou()
    {
        var valley = LevelAssets.Create<RiverValley>($"{Folder}/Landforms/Bayou River Valley.asset");    // its defaults are the bayou's

        var region = LevelAssets.Create<RegionProfile>($"{Folder}/Drowned Bayou.asset");
        region.notes = "Cypress swamp along a slow river. Flat and wet: the challenge is water, not slopes.";
        region.relief = 45f;
        region.hillScale = 70f;
        region.flatness = 1.2f;
        region.warp = 28f;
        region.landform = valley;
        region.waterCover = 0.22f;
        region.waterLevel = 0.16f;
        region.dayNight = CreateBayouDayNight();
        region.zones = new List<ZoneDefinition>
        {
            Zone("Open water", "#3b6e7a", maxHeight: 0.16f),
            Zone("Cypress swamp", "#4d6b3a", minHeight: 0.16f, maxHeight: 0.21f),
            Zone("Levee & hammock", "#7c8a4e", minHeight: 0.21f, maxHeight: 0.40f),
            Zone("Pine upland", "#93915c"),
        };
        EditorUtility.SetDirty(region);
        return region;
    }

    /// <summary>Steep, dry highland: winding ridges and dark hollows, laurel on the steepest sides.</summary>
    static RegionProfile CreateHollers()
    {
        var ridges = LevelAssets.Create<Ridges>($"{Folder}/Landforms/Hollers Ridges.asset");             // its defaults are the hollers'

        var region = LevelAssets.Create<RegionProfile>($"{Folder}/The Hollers.asset");
        region.notes = "Appalachian ridges and hollows. Steep: roads follow the hollows and switch back up the ridges.";
        region.relief = 240f;
        region.hillScale = 80f;
        region.flatness = 1.1f;
        region.warp = 28f;
        region.landform = ridges;
        region.waterCover = 0f;
        region.dayNight = CreateHollersDayNight();
        region.zones = new List<ZoneDefinition>
        {
            Zone("Bald", "#b5ad7c", minHeight: 0.78f, maxSlope: 0.30f),
            Zone("Laurel thicket", "#3e5a35", minSlope: 0.30f),
            Zone("Hollow", "#2f4a3a", maxHeight: 0.30f),
            Zone("Hardwood slope", "#6a7a40"),
        };
        EditorUtility.SetDirty(region);
        return region;
    }

    static ZoneDefinition Zone(string name, string color, float minHeight = 0f, float maxHeight = 1f, float minSlope = 0f, float maxSlope = 1f)
    {
        ColorUtility.TryParseHtmlString(color, out var previewColor);
        return new ZoneDefinition
        {
            name = name,
            previewColor = previewColor,
            minHeight = minHeight,
            maxHeight = maxHeight,
            minSlope = minSlope,
            maxSlope = maxSlope,
        };
    }
}
