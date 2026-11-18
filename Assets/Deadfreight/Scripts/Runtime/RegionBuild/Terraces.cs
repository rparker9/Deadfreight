// Scripts/Runtime/RegionBuild/Terraces.cs
using UnityEngine;

/// <summary>
/// Draws the plan's terraces into the 3D heights. The plan decides everything (where, map.Terrace;
/// how, map.Terraces); this only evaluates it at full resolution, since shelves are narrower than
/// the plan's 50 m cells. Applied before rivers and roads are pressed in, so they cut cleanly
/// through.
///
/// The stepping is centred so it averages out over each step: the ground rises and falls about
/// its original height rather than sinking, so the 3D ground keeps the plan's cell heights.
/// Shelf edges wander off the contour lines, and shelves keep some of the hill's slope.
/// </summary>
public static class Terraces
{
    public static void Apply(HeightField field, RegionMap map, RegionFrame frame)
    {
        var spec = map.Terraces;
        if (spec == null || spec.Step <= 0f) return;

        for (int row = 0; row < field.Resolution; row++)
        for (int column = 0; column < field.Resolution; column++)
        {
            var world = field.SampleToWorld(column, row);
            float weight = GridSampling.Bilinear(map.Terrace, map.Width, map.Height, frame.WorldToCell(world));
            if (weight <= 0f) continue;

            // Wobble shifts where the shelf edges fall without changing the heights between them.
            float h = field.Heights[row, column];
            float wobble = (Perlin(world, spec.WobbleScale, spec.WobbleOffset) - 0.5f) * 2f * spec.Wobble;
            float stepped = Stepped(h + wobble, spec.Step, spec.Riser) - wobble;
            stepped = Mathf.Lerp(stepped, h, spec.Tilt);

            field.Heights[row, column] = Mathf.Lerp(h, stepped, weight);
        }
    }

    /// <summary>
    /// A staircase through the height: flat shelves, rising to the next over the last
    /// <paramref name="riser"/> share of each step. Shifted up by half a shelf so that, over a
    /// whole step, it's as often above the original height as below: the average is unchanged.
    /// </summary>
    static float Stepped(float height, float step, float riser)
    {
        float steps = height / step;
        float shelf = Mathf.Floor(steps);
        float centring = 0.5f * (1f - riser);     // a shelf sits this share of a step below the slope's middle
        return (shelf + RegionMath.SmoothStep(1f - riser, 1f, steps - shelf) + centring) * step;
    }

    /// <summary>Unity's Perlin noise, 0-1, with features about <paramref name="scale"/> metres across.</summary>
    static float Perlin(Vector2 world, float scale, Vector2 offset) =>
        Mathf.PerlinNoise(world.x / Mathf.Max(1f, scale) + offset.x, world.y / Mathf.Max(1f, scale) + offset.y);
}
