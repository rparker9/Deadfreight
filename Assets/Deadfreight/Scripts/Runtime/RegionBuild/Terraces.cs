// Scripts/Runtime/RegionBuild/Terraces.cs
using UnityEngine;

/// <summary>
/// Steps hillsides into shelves in some zones of the region. Drawn in the 3D build only: on the
/// plan's 50 m grid terraces come out as stripes. Applied before roads are pressed in, so roads
/// cut cleanly through them.
///
/// To look worked rather than drawn with a ruler: shelf edges wander off the contour lines,
/// shelves keep some of the hill's slope, and only hillsides are terraced (not flats or cliffs).
/// </summary>
public static class Terraces
{
    const float SlopeFade = 0.05f;      // terracing fades in and out over this much slope

    public static void Apply(HeightField field, RegionMap map, RegionFrame frame, RegionBuildSettings settings, int seed)
    {
        if (!settings.terraces) return;

        var rng = new System.Random(seed ^ 0x7e44ace5);
        var zoneOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));
        var wobbleOffset = new Vector2(rng.Next(0, 10000), rng.Next(0, 10000));

        for (int row = 0; row < field.Resolution; row++)
        for (int column = 0; column < field.Resolution; column++)
        {
            var world = field.SampleToWorld(column, row);

            float zone = RegionMath.SmoothStep(settings.terraceZoneStart, settings.terraceZoneFull,
                Perlin(world, settings.terraceZoneScale, zoneOffset));
            if (zone <= 0f) continue;

            float slope = GridSampling.Bilinear(map.Slope, map.Width, map.Height, frame.WorldToCell(world));
            float hillside = RegionMath.SmoothStep(settings.terraceSlopeMin, settings.terraceSlopeMin + SlopeFade, slope)
                * (1f - RegionMath.SmoothStep(settings.terraceSlopeMax - SlopeFade, settings.terraceSlopeMax + SlopeFade, slope));
            float weight = zone * hillside;
            if (weight <= 0f) continue;

            // Wobble shifts where the shelf edges fall without changing the heights between them.
            float h = field.Heights[row, column];
            float wobble = (Perlin(world, settings.terraceWobbleScale, wobbleOffset) - 0.5f) * 2f * settings.terraceWobble;
            float stepped = Stepped(h + wobble, settings.terraceStep, settings.terraceRiser) - wobble;
            stepped = Mathf.Lerp(stepped, h, settings.terraceTilt);

            field.Heights[row, column] = Mathf.Lerp(h, stepped, weight);
        }
    }

    /// <summary>
    /// Rounds a height down to its shelf, then rises to the next shelf over the last
    /// <paramref name="riser"/> share of each step.
    /// </summary>
    static float Stepped(float height, float step, float riser)
    {
        float steps = height / step;
        float shelf = Mathf.Floor(steps);
        return (shelf + RegionMath.SmoothStep(1f - riser, 1f, steps - shelf)) * step;
    }

    /// <summary>Unity's Perlin noise, 0-1, with features about <paramref name="scale"/> metres across.</summary>
    static float Perlin(Vector2 world, float scale, Vector2 offset) =>
        Mathf.PerlinNoise(world.x / scale + offset.x, world.y / scale + offset.y);
}
