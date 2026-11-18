// Scripts/Runtime/RegionGen/RegionGenerator.Terraces.cs
using UnityEngine;

// Stage 1, last step: where hillsides are terraced, and the shape of the shelves.
public sealed partial class RegionGenerator
{
    const float TerraceSlopeFade = 0.05f;   // terracing fades in and out over this much slope
    const int TerraceSalt = 0x7e44ace5;     // own random stream, so adding terraces doesn't reshuffle anything else

    /// <summary>
    /// Terraces go where a slow zone noise is high and the ground is a hillside: steeper than
    /// terraceSlopeMin, gentler than terraceSlopeMax. Never on water, rivers, marsh or cliffs.
    /// Writes map.Terrace (0-1 per cell) and map.Terraces (the shelves' shape).
    ///
    /// The plan's own heights aren't stepped: shelves are narrower than a cell, and the stepping
    /// averages out over each step, so the cell heights (and slopes, and everything routed over
    /// them) already describe terraced ground. The 3D build draws the shelves (Terraces.cs).
    /// </summary>
    void PlanTerraces()
    {
        var terraceRng = new System.Random(seed ^ TerraceSalt);
        var zoneOffset = new Vector2(terraceRng.Next(0, 10000), terraceRng.Next(0, 10000));
        float scale = Mathf.Max(1f, settings.terraceZoneScale);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            if (map.Water[i] || map.River[i] || map.Marsh[i] || map.Cliff[i]) continue;

            float noise = Mathf.PerlinNoise(x / scale + zoneOffset.x, y / scale + zoneOffset.y);
            float zone = RegionMath.SmoothStep(settings.terraceZoneStart, settings.terraceZoneFull, noise);

            float slope = map.Slope[i];
            float hillside = RegionMath.SmoothStep(settings.terraceSlopeMin, settings.terraceSlopeMin + TerraceSlopeFade, slope)
                * (1f - RegionMath.SmoothStep(settings.terraceSlopeMax - TerraceSlopeFade, settings.terraceSlopeMax + TerraceSlopeFade, slope));

            map.Terrace[i] = zone * hillside;
        }

        map.Terraces = new TerraceSpec
        {
            Step = settings.terraceStep,
            Riser = settings.terraceRiser,
            Tilt = settings.terraceTilt,
            Wobble = settings.terraceWobble,
            WobbleScale = settings.terraceWobbleScale,
            WobbleOffset = new Vector2(terraceRng.Next(0, 10000), terraceRng.Next(0, 10000)),
        };
    }
}
