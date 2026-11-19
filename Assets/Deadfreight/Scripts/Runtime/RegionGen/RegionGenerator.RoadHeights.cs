// Scripts/Runtime/RegionGen/RegionGenerator.RoadHeights.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 3, last: the height every road is built at, and the pad under every place. Decided here
// so the 3D build only presses roads to it, and the cuttings and embankments are part of the plan.
public sealed partial class RegionGenerator
{
    const int RoadSmoothingPasses = 3;
    const int RoadGradePasses = 2;

    /// <summary>
    /// Roads in priority order (main road with its trunk, then alternates, then lanes), the same
    /// order the 3D build lays them. Along each: the ground's height, averaged over
    /// profileSmoothing, then held to maxGrade (a forward and a backward pass). Cells an earlier
    /// road already set keep their height, so roads meet level. Writes map.RoadHeight and map.RoadCut.
    /// </summary>
    void PlanRoadHeights()
    {
        var main = new List<Vector2Int>(map.Trunk.Cells);
        var mainRoute = map.Routes[0].Cells;
        main.AddRange(mainRoute.GetRange(1, mainRoute.Count - 1));
        PlanRoadHeight(main);

        for (int k = 1; k < map.Routes.Count; k++) PlanRoadHeight(map.Routes[k].Cells);
        foreach (var lane in map.Lanes) PlanRoadHeight(lane);
    }

    void PlanRoadHeight(List<Vector2Int> path)
    {
        int n = path.Count;
        if (n == 0) return;

        var index = new int[n];
        var heights = new float[n];
        var pinned = new bool[n];           // set by an earlier road: kept as it is
        for (int k = 0; k < n; k++)
        {
            index[k] = map.Index(path[k]);
            pinned[k] = !float.IsNaN(map.RoadHeight[index[k]]);
            heights[k] = pinned[k] ? map.RoadHeight[index[k]] : map.Metres(index[k]);
        }

        int halfWindow = Mathf.Max(1, Mathf.RoundToInt(settings.profileSmoothing / settings.cellSize * 0.5f));
        for (int pass = 0; pass < RoadSmoothingPasses; pass++)
            heights = KeepPinned(Smoothed(heights, halfWindow), heights, pinned);

        for (int pass = 0; pass < RoadGradePasses; pass++)
        {
            for (int k = 1; k < n; k++)
                if (!pinned[k]) heights[k] = Clamp(heights[k], heights[k - 1], MaxRise(path[k - 1], path[k]));
            for (int k = n - 2; k >= 0; k--)
                if (!pinned[k]) heights[k] = Clamp(heights[k], heights[k + 1], MaxRise(path[k], path[k + 1]));
        }

        for (int k = 0; k < n; k++)
        {
            if (pinned[k]) continue;
            int i = index[k];
            map.RoadHeight[i] = heights[k];
            map.RoadCut[i] = map.Metres(i) - map.RoadHeight[i];
        }
    }

    /// <summary>Each place stands on a pad at the height of the road at its cell (or the ground, if no road reaches it).</summary>
    void PlanPads()
    {
        foreach (var place in map.Places)
        {
            int i = map.Index(place.Cell);
            place.PadHeight = float.IsNaN(map.RoadHeight[i]) ? map.Metres(i) : map.RoadHeight[i];
        }
    }

    // ---------- helpers ----------

    /// <summary>Metres a road may climb or drop between two neighbouring cells.</summary>
    float MaxRise(Vector2Int from, Vector2Int to) => settings.maxGrade * StepLength(from, to) * settings.cellSize;

    /// <summary>Holds <paramref name="height"/> within <paramref name="rise"/> of <paramref name="neighbour"/>.</summary>
    static float Clamp(float height, float neighbour, float rise) => Mathf.Clamp(height, neighbour - rise, neighbour + rise);

    /// <summary>A moving average along the path, the window shrinking at the ends.</summary>
    static float[] Smoothed(float[] values, int halfWindow)
    {
        var result = new float[values.Length];
        for (int k = 0; k < values.Length; k++)
        {
            int from = Mathf.Max(0, k - halfWindow), to = Mathf.Min(values.Length - 1, k + halfWindow);
            float sum = 0f;
            for (int j = from; j <= to; j++) sum += values[j];
            result[k] = sum / (to - from + 1);
        }
        return result;
    }

    static float[] KeepPinned(float[] smoothed, float[] original, bool[] pinned)
    {
        for (int k = 0; k < smoothed.Length; k++)
            if (pinned[k]) smoothed[k] = original[k];
        return smoothed;
    }
}
