// Scripts/Editor/LevelGen/ExampleRegions.Atmosphere.cs
using UnityEditor;
using UnityEngine;
using static DayNightProfile;       // Rgb, Colors, Curve

// The example regions' day-night profiles: starting points to tune by eye, not final values.
// Each is a variation on DayNightProfile's defaults (a dim, misty rural day), keyed at the same
// times of day so they're easy to compare.
public static partial class ExampleRegions
{
    // Times of day, 0 midnight to 1 midnight, as in DayNightProfile's defaults.
    const float NightEnds = 0.2f, BlueDawn = 0.23f, Sunrise = 0.25f, Morning = 0.31f,
                Noon = 0.5f, Evening = 0.69f, Sunset = 0.75f, BlueDusk = 0.77f, Night = 0.8f;

    /// <summary>
    /// Tools/Deadfreight/Create Example Day-Night Profiles: for regions made before the profiles
    /// existed. Makes both profiles and gives each to its example region, if that region is at its
    /// usual path.
    /// </summary>
    [MenuItem("Tools/Deadfreight/Create Example Day-Night Profiles")]
    static void CreateDayNightProfiles()
    {
        Assign($"{Folder}/Drowned Bayou.asset", CreateBayouDayNight());
        Assign($"{Folder}/The Hollers.asset", CreateHollersDayNight());
        AssetDatabase.SaveAssets();
    }

    static void Assign(string regionPath, DayNightProfile profile)
    {
        var region = AssetDatabase.LoadAssetAtPath<RegionProfile>(regionPath);
        if (region == null)
        {
            Debug.Log($"Made {profile.name}; no region at {regionPath} to give it to.", profile);
            return;
        }
        region.dayNight = profile;
        EditorUtility.SetDirty(region);
        Debug.Log($"Made {profile.name} and gave it to {region.name}.", region);
    }

    /// <summary>
    /// Humid and hazy. Sunlight comes through thick air, yellow-green and soft; the sky is pale
    /// grey-green; fog is heavy at dawn and never really clears. Nights are close and faintly green.
    /// More reflection than usual, for all the water.
    /// </summary>
    static DayNightProfile CreateBayouDayNight()
    {
        var p = LevelAssets.Create<DayNightProfile>($"{Folder}/Atmosphere/Drowned Bayou Day-Night.asset");

        p.sunColor = Colors(
            (Sunrise, Rgb(0.95f, 0.45f, 0.2f)), (Morning, Rgb(0.98f, 0.8f, 0.55f)), (Noon, Rgb(1f, 0.95f, 0.78f)),
            (Evening, Rgb(0.98f, 0.76f, 0.48f)), (Sunset, Rgb(0.95f, 0.4f, 0.16f)));
        p.sunIntensity = Curve(
            (BlueDawn, 0f), (0.26f, 0.2f), (0.33f, 1f), (Noon, 1.35f), (0.67f, 1f), (0.74f, 0.2f), (BlueDusk, 0f));

        p.moonColor = Rgb(0.7f, 0.78f, 0.75f);
        p.moonIntensity = Curve((0f, 0.015f), (1f, 0.015f));

        p.skyExposure = Curve(
            (NightEnds, 0.02f), (BlueDawn, 0.15f), (Sunrise, 0.5f), (Morning, 0.95f), (Noon, 1.1f),
            (Evening, 0.95f), (Sunset, 0.5f), (BlueDusk, 0.15f), (Night, 0.02f));
        p.skyTint = Colors(
            (NightEnds, Rgb(0.09f, 0.11f, 0.1f)), (Sunrise, Rgb(0.55f, 0.5f, 0.42f)), (Noon, Rgb(0.48f, 0.52f, 0.45f)),
            (Sunset, Rgb(0.55f, 0.42f, 0.35f)), (Night, Rgb(0.09f, 0.11f, 0.1f)));
        p.atmosphereThickness = Curve((NightEnds, 0.4f), (Sunrise, 2.2f), (Noon, 1.6f), (Sunset, 2.3f), (Night, 0.4f));

        p.ambientSky = Colors(
            (NightEnds, Rgb(0.01f, 0.016f, 0.014f)), (BlueDawn, Rgb(0.045f, 0.06f, 0.055f)), (Sunrise, Rgb(0.22f, 0.21f, 0.18f)),
            (Noon, Rgb(0.36f, 0.42f, 0.38f)), (Sunset, Rgb(0.26f, 0.2f, 0.16f)), (BlueDusk, Rgb(0.045f, 0.06f, 0.055f)),
            (Night, Rgb(0.01f, 0.016f, 0.014f)));
        p.ambientHorizon = Colors(
            (NightEnds, Rgb(0.006f, 0.009f, 0.008f)), (BlueDawn, Rgb(0.035f, 0.045f, 0.04f)), (Sunrise, Rgb(0.24f, 0.2f, 0.15f)),
            (Noon, Rgb(0.33f, 0.36f, 0.3f)), (Sunset, Rgb(0.28f, 0.18f, 0.12f)), (BlueDusk, Rgb(0.035f, 0.045f, 0.04f)),
            (Night, Rgb(0.006f, 0.009f, 0.008f)));
        p.ambientGround = Colors(
            (NightEnds, Rgb(0.003f, 0.004f, 0.003f)), (Sunrise, Rgb(0.07f, 0.07f, 0.05f)), (Noon, Rgb(0.12f, 0.13f, 0.09f)),
            (Sunset, Rgb(0.08f, 0.06f, 0.04f)), (Night, Rgb(0.003f, 0.004f, 0.003f)));

        p.fogColor = Colors(
            (NightEnds, Rgb(0.007f, 0.011f, 0.01f)), (BlueDawn, Rgb(0.05f, 0.065f, 0.06f)), (Sunrise, Rgb(0.45f, 0.42f, 0.35f)),
            (Noon, Rgb(0.5f, 0.55f, 0.48f)), (Sunset, Rgb(0.42f, 0.33f, 0.24f)), (BlueDusk, Rgb(0.05f, 0.065f, 0.06f)),
            (Night, Rgb(0.007f, 0.011f, 0.01f)));
        p.fogDensity = Curve(
            (NightEnds, 0.008f), (0.27f, 0.012f), (0.36f, 0.005f), (Noon, 0.0035f), (Sunset, 0.005f), (Night, 0.008f));

        p.reflectionIntensity = Curve((NightEnds, 0.08f), (Sunrise, 1f), (Sunset, 1f), (Night, 0.08f));
        p.nightVolumeWeight = Curve((NightEnds, 1f), (Sunrise, 0f), (Sunset, 0f), (Night, 1f));

        EditorUtility.SetDirty(p);
        return p;
    }

    /// <summary>
    /// Clear, cool mountain air. Crisp sunlight but a low, late sun over the ridges; a blue haze
    /// that fades distant ridges into the sky; morning mist that burns off. Nights are cold blue
    /// and very dark under the trees.
    /// </summary>
    static DayNightProfile CreateHollersDayNight()
    {
        var p = LevelAssets.Create<DayNightProfile>($"{Folder}/Atmosphere/The Hollers Day-Night.asset");

        p.sunColor = Colors(
            (Sunrise, Rgb(1f, 0.5f, 0.25f)), (Morning, Rgb(1f, 0.82f, 0.62f)), (Noon, Rgb(1f, 0.97f, 0.92f)),
            (Evening, Rgb(1f, 0.78f, 0.55f)), (Sunset, Rgb(1f, 0.42f, 0.2f)));
        p.sunIntensity = Curve(
            (BlueDawn, 0f), (0.26f, 0.15f), (0.33f, 1.1f), (Noon, 1.8f), (0.67f, 1.1f), (0.74f, 0.15f), (BlueDusk, 0f));

        p.moonColor = Rgb(0.72f, 0.76f, 0.86f);
        p.moonIntensity = Curve((0f, 0.02f), (1f, 0.02f));

        p.skyExposure = Curve(
            (NightEnds, 0.025f), (BlueDawn, 0.18f), (Sunrise, 0.6f), (Morning, 1.15f), (Noon, 1.3f),
            (Evening, 1.15f), (Sunset, 0.6f), (BlueDusk, 0.18f), (Night, 0.025f));
        p.skyTint = Colors(
            (NightEnds, Rgb(0.09f, 0.1f, 0.13f)), (Sunrise, Rgb(0.52f, 0.46f, 0.52f)), (Noon, Rgb(0.45f, 0.52f, 0.6f)),
            (Sunset, Rgb(0.55f, 0.42f, 0.45f)), (Night, Rgb(0.09f, 0.1f, 0.13f)));
        p.atmosphereThickness = Curve((NightEnds, 0.35f), (Sunrise, 1.5f), (Noon, 1f), (Sunset, 1.8f), (Night, 0.35f));

        p.ambientSky = Colors(
            (NightEnds, Rgb(0.01f, 0.012f, 0.022f)), (BlueDawn, Rgb(0.045f, 0.05f, 0.08f)), (Sunrise, Rgb(0.2f, 0.19f, 0.24f)),
            (Noon, Rgb(0.4f, 0.47f, 0.58f)), (Sunset, Rgb(0.27f, 0.19f, 0.22f)), (BlueDusk, Rgb(0.045f, 0.05f, 0.08f)),
            (Night, Rgb(0.01f, 0.012f, 0.022f)));
        p.ambientHorizon = Colors(
            (NightEnds, Rgb(0.005f, 0.006f, 0.011f)), (BlueDawn, Rgb(0.035f, 0.038f, 0.055f)), (Sunrise, Rgb(0.24f, 0.18f, 0.18f)),
            (Noon, Rgb(0.33f, 0.36f, 0.4f)), (Sunset, Rgb(0.29f, 0.17f, 0.14f)), (BlueDusk, Rgb(0.035f, 0.038f, 0.055f)),
            (Night, Rgb(0.005f, 0.006f, 0.011f)));
        p.ambientGround = Colors(
            (NightEnds, Rgb(0.002f, 0.002f, 0.003f)), (Sunrise, Rgb(0.06f, 0.05f, 0.045f)), (Noon, Rgb(0.11f, 0.1f, 0.08f)),
            (Sunset, Rgb(0.07f, 0.045f, 0.04f)), (Night, Rgb(0.002f, 0.002f, 0.003f)));

        p.fogColor = Colors(
            (NightEnds, Rgb(0.006f, 0.007f, 0.012f)), (BlueDawn, Rgb(0.05f, 0.055f, 0.08f)), (Sunrise, Rgb(0.44f, 0.42f, 0.48f)),
            (Noon, Rgb(0.5f, 0.57f, 0.66f)), (Sunset, Rgb(0.45f, 0.32f, 0.3f)), (BlueDusk, Rgb(0.05f, 0.055f, 0.08f)),
            (Night, Rgb(0.006f, 0.007f, 0.012f)));
        p.fogDensity = Curve(
            (NightEnds, 0.006f), (0.27f, 0.009f), (0.36f, 0.003f), (Noon, 0.0025f), (Sunset, 0.004f), (Night, 0.006f));

        p.reflectionIntensity = Curve((NightEnds, 0.05f), (Sunrise, 1f), (Sunset, 1f), (Night, 0.05f));
        p.nightVolumeWeight = Curve((NightEnds, 1f), (Sunrise, 0f), (Sunset, 0f), (Night, 1f));

        EditorUtility.SetDirty(p);
        return p;
    }
}
