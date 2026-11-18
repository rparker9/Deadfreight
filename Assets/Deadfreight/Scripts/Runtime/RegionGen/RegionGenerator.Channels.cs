// Scripts/Runtime/RegionGen/RegionGenerator.Channels.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 1, after the land is finished: the channel each river cell has, so the 3D build cuts
// exactly that, and road costs and travel can rely on it.
public sealed partial class RegionGenerator
{
    /// <summary>
    /// For every river cell: width and depth from how much drains through it, and the water level
    /// riverBankDrop below the ground. Then, walking downstream (cells in order of drainage, which
    /// always grows downstream), the level never rises and the width and depth never shrink from
    /// one cell to the next. No water sits below the lakes.
    /// </summary>
    void PlanRiverChannels()
    {
        var rivers = new List<int>();
        for (int i = 0; i < cellCount; i++)
        {
            if (!map.River[i] || map.Water[i]) continue;
            float size = Mathf.Sqrt(map.Drainage[i]);
            map.RiverWidth[i] = Mathf.Clamp(settings.riverMinWidth + settings.riverWidthGrowth * size, settings.riverMinWidth, settings.riverMaxWidth);
            map.RiverDepth[i] = Mathf.Clamp(settings.riverMinDepth + settings.riverDepthGrowth * size, settings.riverMinDepth, settings.riverMaxDepth);
            map.RiverLevel[i] = Mathf.Max(map.Metres(i) - settings.riverBankDrop, map.LakeLevel);
            rivers.Add(i);
        }

        rivers.Sort((a, b) => map.Drainage[a].CompareTo(map.Drainage[b]));
        foreach (int i in rivers)
        {
            int down = map.FlowTo[i];
            if (down < 0 || float.IsNaN(map.RiverLevel[down])) continue;    // off the map, or into a lake
            map.RiverLevel[down] = Mathf.Min(map.RiverLevel[down], map.RiverLevel[i]);
            map.RiverWidth[down] = Mathf.Max(map.RiverWidth[down], map.RiverWidth[i]);
            map.RiverDepth[down] = Mathf.Max(map.RiverDepth[down], map.RiverDepth[i]);
        }
    }
}
