// Scripts/Runtime/World/DayNight/DayNightProfile.cs
using UnityEngine;

namespace Deadfreight.World
{
    /// <summary>
    /// The look of a whole day: every curve and gradient runs over the day from midnight (0) through
    /// noon (0.5) to midnight again (1); before its first key and after its last, each holds that value. DayNightCycle reads it each frame. The defaults are a dim,
    /// misty rural day with warm sunrise and sunset and dark blue nights; make one per region type.
    /// </summary>
    [CreateAssetMenu(fileName = "DayNightProfile", menuName = "Deadfreight/Day-Night Profile")]
    public sealed class DayNightProfile : ScriptableObject
    {
        // Times of day as 0-1, for the defaults below. With the cycle's default sun path the sun sets
        // at 18:00 (0.75); the light switches to the moon at about 18:28 (0.77), when it's darkest,
        // and moonlight has faded in by about 18:55 (0.79). Dawn mirrors it.
        const float NightEnds = 0.2f, BlueDawn = 0.23f, Sunrise = 0.25f, Morning = 0.31f,
                    Noon = 0.5f, Evening = 0.69f, Sunset = 0.75f, BlueDusk = 0.77f, Night = 0.8f;

        [Header("Sun")]
        public Gradient sunColor = Colors(
            (Sunrise, Rgb(1f, 0.42f, 0.18f)), (Morning, Rgb(1f, 0.78f, 0.55f)), (Noon, Rgb(1f, 0.96f, 0.88f)),
            (Evening, Rgb(1f, 0.74f, 0.5f)), (Sunset, Rgb(1f, 0.38f, 0.15f)));
        [Tooltip("Brightness. Below the horizon the sun is also faded out, whatever this says.")]
        public AnimationCurve sunIntensity = Curve(
            (BlueDawn, 0f), (0.26f, 0.3f), (0.33f, 1.3f), (Noon, 1.7f), (0.67f, 1.3f), (0.74f, 0.3f), (BlueDusk, 0f));

        [Header("Moon")]
        [Tooltip("Keep it close to grey: a strongly blue moon tints the whole scene the moment it comes in.")]
        public Color moonColor = Rgb(0.7f, 0.75f, 0.85f);
        [Tooltip("Brightness: the only direct light at night. 0.02 at most: night is lit mostly by ambient and fog. "
               + "The cycle fades it in after sunset by itself; this is just how bright it gets.")]
        public AnimationCurve moonIntensity = Curve((0f, 0.018f), (1f, 0.018f));

        [Header("Sky (procedural skybox)")]
        [Tooltip("While the moon is up, the sky holds the midnight value (the first key), faded in with the moon. The twilight keys only apply once the sun is back.")]
        public AnimationCurve skyExposure = Curve(
            (NightEnds, 0.025f), (BlueDawn, 0.18f), (Sunrise, 0.6f), (Morning, 1.1f), (Noon, 1.25f),
            (Evening, 1.1f), (Sunset, 0.6f), (BlueDusk, 0.18f), (Night, 0.025f));
        public Gradient skyTint = Colors(
            (NightEnds, Rgb(0.1f, 0.1f, 0.12f)), (Sunrise, Rgb(0.55f, 0.45f, 0.5f)), (Noon, Rgb(0.5f, 0.52f, 0.55f)),
            (Sunset, Rgb(0.55f, 0.4f, 0.42f)), (Night, Rgb(0.1f, 0.1f, 0.12f)));
        [Tooltip("Thicker = redder sunrises and sunsets, hazier sky. Thinner at night = darker, less blue.")]
        public AnimationCurve atmosphereThickness = Curve((NightEnds, 0.35f), (Sunrise, 1.6f), (Noon, 1.1f), (Sunset, 1.7f), (Night, 0.35f));

        [Header("Ambient light (from the sky, the horizon and the ground)")]
        [Tooltip("This is what you see by in the blue hour, when there's no sun or moon. Keep it dim blue there, not black.")]
        public Gradient ambientSky = Colors(
            (NightEnds, Rgb(0.012f, 0.014f, 0.024f)), (BlueDawn, Rgb(0.05f, 0.055f, 0.08f)), (Sunrise, Rgb(0.22f, 0.19f, 0.22f)),
            (Noon, Rgb(0.42f, 0.47f, 0.56f)), (Sunset, Rgb(0.28f, 0.19f, 0.2f)), (BlueDusk, Rgb(0.05f, 0.055f, 0.08f)),
            (Night, Rgb(0.012f, 0.014f, 0.024f)));
        public Gradient ambientHorizon = Colors(
            (NightEnds, Rgb(0.006f, 0.007f, 0.012f)), (BlueDawn, Rgb(0.04f, 0.04f, 0.055f)), (Sunrise, Rgb(0.26f, 0.18f, 0.16f)),
            (Noon, Rgb(0.34f, 0.35f, 0.34f)), (Sunset, Rgb(0.3f, 0.17f, 0.12f)), (BlueDusk, Rgb(0.04f, 0.04f, 0.055f)),
            (Night, Rgb(0.006f, 0.007f, 0.012f)));
        public Gradient ambientGround = Colors(
            (NightEnds, Rgb(0.003f, 0.003f, 0.004f)), (Sunrise, Rgb(0.07f, 0.06f, 0.05f)), (Noon, Rgb(0.14f, 0.12f, 0.09f)),
            (Sunset, Rgb(0.08f, 0.05f, 0.04f)), (Night, Rgb(0.003f, 0.003f, 0.004f)));

        [Header("Fog")]
        [Tooltip("Match the sky near the horizon, so distant hills fade into it. Near-black at night, so the dark closes in.")]
        public Gradient fogColor = Colors(
            (NightEnds, Rgb(0.008f, 0.009f, 0.014f)), (BlueDawn, Rgb(0.06f, 0.065f, 0.09f)), (Sunrise, Rgb(0.42f, 0.38f, 0.4f)),
            (Noon, Rgb(0.52f, 0.56f, 0.6f)), (Sunset, Rgb(0.42f, 0.29f, 0.24f)), (BlueDusk, Rgb(0.06f, 0.065f, 0.09f)),
            (Night, Rgb(0.008f, 0.009f, 0.014f)));
        [Tooltip("Exponential-squared fog density. 0.002 is clear-ish; 0.006 is thick. Morning mist peaks just after sunrise.")]
        public AnimationCurve fogDensity = Curve(
            (NightEnds, 0.006f), (0.27f, 0.008f), (0.36f, 0.003f), (Noon, 0.002f), (Sunset, 0.0035f), (Night, 0.006f));

        [Header("Reflections and post-processing")]
        [Tooltip("How strongly the sky reflects in surfaces. Low at night so wet and shiny things don't glow.")]
        public AnimationCurve reflectionIntensity = Curve((NightEnds, 0.05f), (Sunrise, 1f), (Sunset, 1f), (Night, 0.05f));
        [Tooltip("Weight of DayNightCycle's night Volume: 0 by day, 1 by night. Spread over the whole twilight so it's never noticed.")]
        public AnimationCurve nightVolumeWeight = Curve((NightEnds, 1f), (Sunrise, 0f), (Sunset, 0f), (Night, 1f));

        // ---------- building the defaults (public, so editor tools can build profiles the same way) ----------

        public static Color Rgb(float r, float g, float b) => new(r, g, b, 1f);

        public static Gradient Colors(params (float time, Color color)[] keys)
        {
            var colorKeys = new GradientColorKey[keys.Length];
            for (int k = 0; k < keys.Length; k++) colorKeys[k] = new GradientColorKey(keys[k].color, keys[k].time);
            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        /// <summary>
        /// A curve through the keys that never overshoots them (like Unity's "Clamped Auto"): between
        /// two keys it only rises or only falls, so a fade never wobbles. Each key's slope is the
        /// harmonic mean of the slopes either side, or flat where the curve turns round or ends.
        /// </summary>
        public static AnimationCurve Curve(params (float time, float value)[] keys)
        {
            var frames = new Keyframe[keys.Length];
            for (int k = 0; k < keys.Length; k++)
            {
                float slope = 0f;
                if (k > 0 && k < keys.Length - 1)
                {
                    float before = (keys[k].value - keys[k - 1].value) / (keys[k].time - keys[k - 1].time);
                    float after = (keys[k + 1].value - keys[k].value) / (keys[k + 1].time - keys[k].time);
                    if (before * after > 0f) slope = 2f * before * after / (before + after);
                }
                frames[k] = new Keyframe(keys[k].time, keys[k].value, slope, slope);
            }
            return new AnimationCurve(frames);
        }
    }
}
