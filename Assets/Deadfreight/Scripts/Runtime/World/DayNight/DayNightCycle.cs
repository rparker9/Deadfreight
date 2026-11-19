// Scripts/Runtime/World/DayNight/DayNightCycle.cs
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

/// <summary>
/// Time of day, and everything it lights. Each frame it works out the hour, then sets from the
/// DayNightProfile:
///
///   - one directional light, which is the sun by day and the moon by night: where it is,
///     colour, brightness (the skybox draws it as its disc);
///   - the procedural skybox: exposure, tint, atmosphere;
///   - ambient light: sky, horizon and ground colours (Trilight, so no costly sky re-sampling);
///   - fog: colour and density, never thinner than needed to hide everything past hideBeyond;
///   - reflections, and the weight of an optional night post-processing Volume.
///
/// In multiplayer the hour comes from Netcode's shared server clock, so every player sees the
/// same sky without sending anything. Offline it runs from when play started. In the editor it
/// shows previewHour, so the day can be scrubbed without pressing Play.
/// </summary>
[ExecuteAlways]
public sealed class DayNightCycle : MonoBehaviour
{
    const float HoursPerDay = 24f;
    const float HiddenFogLevel = 1.731f;    // √ln(20): exponential-squared fog is 95% opaque at density × distance = this

    [Header("Look")]
    [SerializeField] DayNightProfile profile;
    [Tooltip("The one directional light: the sun by day, the moon by night.")]
    [FormerlySerializedAs("sun"), SerializeField] Light skyLight;
    [Tooltip("A material using Skybox/Procedural. A copy is driven at runtime; the original isn't changed.")]
    [SerializeField] Material skybox;
    [Tooltip("Optional: a global Volume with the night look: mostly colour (desaturated, cooler, vignette, grain), with only a mild exposure drop, since the profile already darkens the night. Its weight follows the profile.")]
    [SerializeField] Volume nightVolume;

    [Header("Time")]
    [SerializeField, Range(0f, 24f)] float startHour = 7f;
    [Unit("min"), Tooltip("Real minutes for one full day.")]
    [SerializeField] float dayLength = 30f;
    [Tooltip("Hold the time at Start Hour.")]
    [SerializeField] bool paused;

    [Header("Sun path")]
    [SerializeField, Range(-180f, 180f), Tooltip("Compass direction (degrees) the sun rises from.")] float sunrise = 90f;
    [SerializeField, Range(0f, 60f), Tooltip("How far the sun's path leans from straight overhead. More = lower noon sun, longer shadows.")]
    float pathTilt = 30f;
    [SerializeField, Range(0f, 18f), Tooltip("Degrees the sun must sink below the horizon before moonlight starts. The gap between is the dark blue hour.")]
    float moonDelay = 6f;

    [Header("Fog")]
    [Unit("m"), Tooltip("Fog always fully covers things this far away, e.g. the tree draw distance, so nothing pops in. 0 = off.")]
    [SerializeField] float hideBeyond = 0f;

    [Header("Editor")]
    [SerializeField, Range(0f, 24f), Tooltip("The hour shown in the editor, outside Play mode.")] float previewHour = 9f;

    const float MoonFadeIn = 6f;            // degrees the sun sinks while the moon fades in (about half an hour)

    Material skyInstance;
    bool isSun;                             // whether the light is playing the sun this frame
    float moonUp;                           // 0-1: how far the moon (light and sky) has faded in

    /// <summary>The current hour, 0-24.</summary>
    public float Hour { get; private set; }

    /// <summary>The look of the day. LevelBuilder sets it from the level's region.</summary>
    public DayNightProfile Profile
    {
        get => profile;
        set => profile = value;
    }

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void Update() => Apply();

    void OnDisable()
    {
        // Put the real skybox back so the scene never saves a reference to the temporary copy.
        if (RenderSettings.skybox == skyInstance) RenderSettings.skybox = skybox;
        if (skyInstance != null)
        {
            if (Application.isPlaying) Destroy(skyInstance);
            else DestroyImmediate(skyInstance);
        }
        skyInstance = null;
    }

    // ---------- time ----------

    float CurrentHour()
    {
        if (!Application.isPlaying) return previewHour;
        if (paused) return startHour;

        // Shared server time when networked; otherwise time since this scene loaded.
        var network = NetworkManager.Singleton;
        double seconds = network != null && network.IsListening ? network.ServerTime.Time : Time.timeSinceLevelLoad;
        double hours = startHour + seconds / (Mathf.Max(0.1f, dayLength) * 60.0) * HoursPerDay;
        return (float)(hours % HoursPerDay);
    }

    // ---------- applying ----------

    void Apply()
    {
        if (profile == null || skyLight == null) return;
        Hour = CurrentHour();
        float day = Hour / HoursPerDay;     // 0 midnight, 0.5 noon: how the profile's curves are indexed

        LightSky(day);
        if (skybox != null) DriveSky(day);
        DriveAmbientAndFog(day);
        if (nightVolume != null) nightVolume.weight = Mathf.Clamp01(profile.nightVolumeWeight.Evaluate(day));
    }

    /// <summary>
    /// One directional light plays both the sun and the moon. Two lights would have to hand over
    /// shadows and the skybox disc from one to the other, and that hand-over is a visible pop.
    /// Instead the light follows the sun by day, fades to nothing as the sun sets, and only then
    /// jumps to the moon and fades back in. The jump happens at zero brightness, so nothing pops.
    /// </summary>
    void LightSky(float day)
    {
        // The sun turns once a day around a tilted axis: on the horizon at 6:00 and 18:00,
        // highest at noon. The moon sits opposite.
        float hourAngle = day * 360f - 90f;
        // At 6:00 the light must shine away from the sunrise direction, hence the + 180.
        var path = Quaternion.Euler(0f, sunrise + 180f, 0f) * Quaternion.Euler(0f, 0f, pathTilt);
        var sunRotation = path * Quaternion.Euler(hourAngle, 0f, 0f);
        var moonRotation = path * Quaternion.Euler(hourAngle + 180f, 0f, 0f);

        float sunElevation = Elevation(sunRotation);
        float sunBrightness = Mathf.Max(0f, profile.sunIntensity.Evaluate(day)) * Fade(sunElevation, -2f, 4f);

        // The moon waits until the sun is well down (twilight), so the two never overlap, then
        // fades in while the sun sinks a further MoonFadeIn degrees.
        moonUp = (1f - Fade(sunElevation, -moonDelay - MoonFadeIn, -moonDelay)) * Fade(Elevation(moonRotation), -2f, 4f);
        float moonBrightness = Mathf.Max(0f, profile.moonIntensity.Evaluate(day)) * moonUp;

        isSun = sunBrightness > 0f || moonBrightness <= 0f;
        skyLight.transform.rotation = isSun ? sunRotation : moonRotation;
        skyLight.color = isSun ? profile.sunColor.Evaluate(day) : profile.moonColor;
        skyLight.intensity = isSun ? sunBrightness : moonBrightness;
        skyLight.shadows = LightShadows.Soft;
        RenderSettings.sun = skyLight;
    }

    /// <summary>Degrees above the horizon of a light pointing this way (negative = below).</summary>
    static float Elevation(Quaternion rotation)
    {
        float down = -(rotation * Vector3.forward).y;
        return Mathf.Asin(Mathf.Clamp(down, -1f, 1f)) * Mathf.Rad2Deg;
    }

    /// <summary>Smoothly 0 at or below <paramref name="from"/> degrees, 1 at or above <paramref name="to"/>.</summary>
    static float Fade(float elevation, float from, float to) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, elevation));

    void DriveSky(float day)
    {
        if (skyInstance == null)
        {
            skyInstance = new Material(skybox) { name = skybox.name + " (Day-Night)", hideFlags = HideFlags.DontSave };
        }
        RenderSettings.skybox = skyInstance;
        // The skybox lights itself from the light's direction. While that's the moon (high up), it
        // draws a day sky, which only the low exposure keeps dark. So the moon's sky always uses
        // the profile's midnight values, faded in with the moonlight, and the twilight parts of
        // the curves only apply once the light is the sun again.
        float skyTime = isSun ? day : 0f;
        float exposure = profile.skyExposure.Evaluate(skyTime) * (isSun ? 1f : moonUp);
        skyInstance.SetFloat("_Exposure", exposure);
        skyInstance.SetColor("_SkyTint", profile.skyTint.Evaluate(skyTime));
        skyInstance.SetFloat("_AtmosphereThickness", profile.atmosphereThickness.Evaluate(skyTime));
    }

    void DriveAmbientAndFog(float day)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = profile.ambientSky.Evaluate(day);
        RenderSettings.ambientEquatorColor = profile.ambientHorizon.Evaluate(day);
        RenderSettings.ambientGroundColor = profile.ambientGround.Evaluate(day);
        RenderSettings.reflectionIntensity = Mathf.Clamp01(profile.reflectionIntensity.Evaluate(day));

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = profile.fogColor.Evaluate(day);
        float density = Mathf.Max(0f, profile.fogDensity.Evaluate(day));
        if (hideBeyond > 0f) density = Mathf.Max(density, HiddenFogLevel / hideBeyond);
        RenderSettings.fogDensity = density;
    }
}
