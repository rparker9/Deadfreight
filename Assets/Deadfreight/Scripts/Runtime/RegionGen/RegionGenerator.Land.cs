// Scripts/Runtime/RegionGen/RegionGenerator.Land.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 1: the land everything else is built on. Order matters: each step reshapes the heights
// the next one reads, and slope is measured last so roads are routed over the finished land.
public sealed partial class RegionGenerator
{
    const float FarmlandSoftening = 4f;         // blur (cells) that smooths farmland outside hill country
    const int HillZoneOctaves = 2;
    const int RidgeOctaves = 4;
    const float SpillStep = 1e-5f;              // a filled pit rises this much per cell, so water can cross it
    const float ValleyFloorMargin = 0.004f;     // valley floors stop this far (0-1 height) above sea level
    const float FloodPlainInnerReach = 0.5f;    // fully flood plain within this share of the reach
    const float FloodPlainInnerBand = 0.6f;     // fully flattened below this share of the band
    const int MarshReach = 4;                   // cells from water that can be marsh
    const int HollowBlurPasses = 8;
    const int HollowBlurStep = 3;               // cells between blur samples
    const float HollowBowlSoftening = 1.5f;     // blur (cells) that rounds a hollow bowl's rim
    static readonly int[] Sideways = { -1, 1 };

    float[] filled;     // heights with pits filled: where water drains

    void BuildLand()
    {
        BuildBaseHeight();
        if (settings.hillCountry) RaiseHillCountry();
        if (settings.escarpment) RaiseEscarpment();     // RegionGenerator.Escarpment.cs
        MarkWater();
        BuildRivers();
        if (settings.riverValleys) CarveRiverValleys();
        if (settings.floodPlains) FlattenFloodPlains();
        BuildMarsh();
        BuildHollows();
        BuildSlope();
        MarkCliffs();
    }

    // ---------------- height ----------------

    /// <summary>Rolling noise heights (0-1, lowland flattened by the flatness power) and the forest pattern.</summary>
    void BuildBaseHeight()
    {
        var e = map.Elevation;
        var raw = RegionNoise.Fbm(width, height, settings.hillScale, 5, rng);
        GridOps.Normalize(raw);
        for (int i = 0; i < cellCount; i++) e[i] = Mathf.Pow(raw[i], settings.flatness);

        var forestNoise = RegionNoise.Fbm(width, height, settings.forestScale, 4, rng);
        for (int i = 0; i < cellCount; i++) map.Forest[i] = forestNoise[i] > settings.forestThreshold;
    }

    /// <summary>
    /// Hill country: a slow noise picks zones where ridged noise raises crests and spines; outside
    /// them the ground is smoothed into farmland. Heights are then rescaled so the same share of the
    /// map is water as before; otherwise lakes grow and cut routes off.
    /// </summary>
    void RaiseHillCountry()
    {
        var e = map.Elevation;
        float waterShare = GridOps.ShareBelow(e, settings.seaLevel);

        // Its own random stream, so it doesn't shift the draws the rest of generation makes.
        var noise = new System.Random(unchecked(seed * 7919 + 1));
        var zone = RegionNoise.Fbm(width, height, settings.hillZoneScale, HillZoneOctaves, noise);
        GridOps.Normalize(zone);
        var ridges = RegionNoise.RidgedFbm(width, height, settings.ridgeScale, RidgeOctaves, noise);
        var farmland = GridOps.Blur(e, width, height, FarmlandSoftening);

        for (int i = 0; i < cellCount; i++)
        {
            float hills = RegionMath.SmoothStep(settings.hillZoneStart, settings.hillZoneFull, zone[i]);
            e[i] = e[i] * hills + farmland[i] * (1f - hills) + hills * settings.ridgeHeight * ridges[i];
        }
        GridOps.KeepWaterShare(e, settings.seaLevel, waterShare);
    }

    void MarkWater()
    {
        for (int i = 0; i < cellCount; i++) map.Water[i] = map.Elevation[i] < settings.seaLevel;
    }

    // ---------------- rivers ----------------

    /// <summary>
    /// Rivers from drainage. Pits are filled first, so every cell has a way downhill to a lake or
    /// the map edge. Each cell drains to its lowest neighbour; going from the highest cell down,
    /// each passes on the area it has collected. Cells collecting at least riverArea are river,
    /// so streams join into bigger rivers the further they run.
    /// </summary>
    void BuildRivers()
    {
        var order = FillPits();
        var drainage = map.Drainage;
        for (int i = 0; i < cellCount; i++)
        {
            drainage[i] = 1f;
            map.FlowTo[i] = -1;
        }

        for (int k = order.Count - 1; k >= 0; k--)     // highest first
        {
            int i = order[k];
            if (map.Water[i]) continue;
            int down = LowestNeighbourBelow(i, filled);
            map.FlowTo[i] = down;
            if (down >= 0) drainage[down] += drainage[i];
        }

        for (int i = 0; i < cellCount; i++)
            map.River[i] = !map.Water[i] && drainage[i] >= settings.riverArea;
        CloseRiverDiagonals();
    }

    /// <summary>
    /// Priority flood: starts from every lake cell and the map border, and always grows from the
    /// lowest cell reached so far. A cell lower than the one that reached it sits in a pit, and is
    /// raised just above it, as if the pit filled until it spilled. Returns cells in the order reached.
    /// </summary>
    List<int> FillPits()
    {
        filled = (float[])map.Elevation.Clone();
        var reached = new bool[cellCount];
        var frontier = new MinHeap();
        var order = new List<int>(cellCount);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int i = y * width + x;
            bool border = x == 0 || y == 0 || x == width - 1 || y == height - 1;
            if (!map.Water[i] && !border) continue;
            reached[i] = true;
            frontier.Push(filled[i], i);
        }

        while (frontier.Count > 0)
        {
            var (level, i) = frontier.Pop();
            order.Add(i);
            int cx = i % width, cy = i / width;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx, ny = cy + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                int j = ny * width + nx;
                if (reached[j]) continue;
                reached[j] = true;
                filled[j] = Mathf.Max(filled[j], level + SpillStep);
                frontier.Push(filled[j], j);
            }
        }
        return order;
    }

    /// <summary>The lowest of a cell's eight neighbours, if it is lower than the cell; otherwise -1.</summary>
    int LowestNeighbourBelow(int i, float[] heights)
    {
        int best = -1;
        float bestHeight = heights[i];
        int cx = i % width, cy = i / width;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int nx = cx + dx, ny = cy + dy;
            if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
            int j = ny * width + nx;
            if (heights[j] < bestHeight)
            {
                bestHeight = heights[j];
                best = j;
            }
        }
        return best;
    }

    /// <summary>
    /// Where a river steps diagonally, also marks the lower of the two corner cells. Roads move in
    /// eight directions and could otherwise slip between the two river cells without crossing a ford.
    /// </summary>
    void CloseRiverDiagonals()
    {
        var river = (bool[])map.River.Clone();
        for (int y = 0; y < height - 1; y++)
        for (int x = 0; x < width; x++)
        {
            if (!river[y * width + x]) continue;
            foreach (int dx in Sideways)
            {
                int nx = x + dx;
                if (nx < 0 || nx >= width) continue;
                int diagonal = (y + 1) * width + nx, side = y * width + nx, below = (y + 1) * width + x;
                if (!river[diagonal] || river[side] || river[below]) continue;
                map.River[filled[side] <= filled[below] ? side : below] = true;
            }
        }
    }

    // ---------------- shaping around water ----------------

    /// <summary>
    /// Lowers the land around each river into a valley: a flat-ish floor with sloping sides, wider
    /// and deeper the more the river drains. Valleys never cut below the lakes.
    /// </summary>
    void CarveRiverValleys()
    {
        GridOps.NearestMarked(map.River, width, height, out var distance, out var nearest);
        var e = map.Elevation;
        float sea = settings.seaLevel;

        for (int i = 0; i < cellCount; i++)
        {
            if (map.Water[i] || nearest[i] < 0) continue;

            float size = Mathf.Sqrt(map.Drainage[nearest[i]]);
            float halfWidth = Mathf.Min(settings.valleyMaxWidth, settings.valleyWidth + settings.valleyWidthGrowth * size);
            if (distance[i] >= halfWidth) continue;

            float depth = Mathf.Min(settings.valleyMaxDepth, settings.valleyDepth + settings.valleyDepthGrowth * size) / settings.relief;
            float lower = depth * (1f - RegionMath.SmoothStep(0f, 1f, distance[i] / halfWidth));
            float floor = Mathf.Min(e[i], sea + ValleyFloorMargin);
            e[i] = Mathf.Max(e[i] - lower, floor);
        }
    }

    /// <summary>Low ground near water is squashed toward sea level: broad, flat bottomland.</summary>
    void FlattenFloodPlains()
    {
        var wet = new bool[cellCount];
        for (int i = 0; i < cellCount; i++) wet[i] = map.Water[i] || map.River[i];
        GridOps.NearestMarked(wet, width, height, out var distance, out _);

        var e = map.Elevation;
        float sea = settings.seaLevel, reach = settings.floodPlainReach, band = settings.floodPlainBand;
        for (int i = 0; i < cellCount; i++)
        {
            if (map.Water[i]) continue;
            float nearWater = 1f - RegionMath.SmoothStep(reach * FloodPlainInnerReach, reach, distance[i]);
            float lowGround = 1f - RegionMath.SmoothStep(sea + band * FloodPlainInnerBand, sea + band, e[i]);
            float flattened = sea + (e[i] - sea) * settings.floodPlainSquash;
            e[i] += (flattened - e[i]) * nearWater * lowGround;
        }
    }

    /// <summary>Marsh: low ground within a few cells of water or a river. Nothing grows on open water.</summary>
    void BuildMarsh()
    {
        var wet = new bool[cellCount];
        for (int i = 0; i < cellCount; i++) wet[i] = map.Water[i] || map.River[i];
        var nearWet = Dilate(wet, MarshReach);

        for (int i = 0; i < cellCount; i++)
        {
            map.Marsh[i] = nearWet[i] && !wet[i] && map.Elevation[i] < settings.seaLevel + settings.marshBand;
            if (wet[i]) map.Forest[i] = false;
        }
    }

    // ---------------- hollows and slope ----------------

    /// <summary>Hollows: ground noticeably lower than its smoothed surroundings. Dug into bowls so fog pools there.</summary>
    void BuildHollows()
    {
        var e = map.Elevation;
        var blur = (float[])e.Clone();
        var tmp = new float[cellCount];
        int k = HollowBlurStep;

        for (int pass = 0; pass < HollowBlurPasses; pass++)
        {
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = blur[y * width + x]
                    + blur[Mathf.Clamp(y - k, 0, height - 1) * width + x] + blur[Mathf.Clamp(y + k, 0, height - 1) * width + x]
                    + blur[y * width + Mathf.Clamp(x - k, 0, width - 1)] + blur[y * width + Mathf.Clamp(x + k, 0, width - 1)];
                tmp[y * width + x] = sum / 5f;
            }
            (blur, tmp) = (tmp, blur);
        }

        for (int i = 0; i < cellCount; i++)
            map.Hollow[i] = blur[i] - e[i] > settings.hollowDepth && !map.Water[i] && !map.River[i];

        if (settings.hollowBowl > 0f) DigHollowBowls();
    }

    /// <summary>Lowers each hollow by up to hollowBowl metres, with a soft rim.</summary>
    void DigHollowBowls()
    {
        var bowl = new float[cellCount];
        for (int i = 0; i < cellCount; i++) bowl[i] = map.Hollow[i] ? 1f : 0f;
        bowl = GridOps.Blur(bowl, width, height, HollowBowlSoftening);

        float deepest = 0f;
        foreach (float b in bowl) deepest = Mathf.Max(deepest, b);
        if (deepest <= 0f) return;

        float depth = settings.hollowBowl / settings.relief;
        for (int i = 0; i < cellCount; i++) map.Elevation[i] -= depth * bowl[i] / deepest;
    }

    /// <summary>Slope as metres of rise per metre, from the finished heights.</summary>
    void BuildSlope()
    {
        var e = map.Elevation;
        float span = 2f * settings.cellSize;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = (e[y * width + Mathf.Min(x + 1, width - 1)] - e[y * width + Mathf.Max(x - 1, 0)]) * settings.relief;
            float dy = (e[Mathf.Min(y + 1, height - 1) * width + x] - e[Mathf.Max(y - 1, 0) * width + x]) * settings.relief;
            map.Slope[y * width + x] = Mathf.Sqrt(dx * dx + dy * dy) / span;
        }
    }
}
