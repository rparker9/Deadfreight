// Scripts/Runtime/RegionGen/RegionGenerator.Biomes.cs
using System.Collections.Generic;
using UnityEngine;

// Stage 1, last: a biome per cell, from the region's rule list. Biomes never change the land
// or the roads; the 3D build uses them for what grows and for fine ground noise.
public sealed partial class RegionGenerator
{
    /// <summary>
    /// Each cell gets the biome of the first rule in settings.biomes whose conditions all hold
    /// (BiomeRule). Rules without a biome are skipped. A cell no rule matches gets none, and
    /// nothing grows there. Lakes and rivers get biomes too: their cells also hold shore and bank,
    /// and the 3D build keeps plants out of the water itself.
    /// </summary>
    void AssignBiomes()
    {
        map.Biomes.Clear();
        var rules = settings.biomes;
        if (rules == null || rules.Length == 0) return;

        // Distances the rules can ask about: cells in from a forest's edge, cells to water.
        var open = new bool[cellCount];
        var wet = new bool[cellCount];
        for (int i = 0; i < cellCount; i++)
        {
            open[i] = !map.Forest[i];
            wet[i] = map.Water[i] || map.River[i];
        }
        GridOps.NearestMarked(open, width, height, out var forestDepth, out _);
        GridOps.NearestMarked(wet, width, height, out var toWater, out _);

        // Each distinct biome once, in the order the rules first use it.
        var indexOf = new Dictionary<Biome, int>();
        var ruleBiome = new int[rules.Length];
        for (int r = 0; r < rules.Length; r++)
        {
            var biome = rules[r]?.biome;
            if (biome == null) { ruleBiome[r] = -1; continue; }
            if (!indexOf.TryGetValue(biome, out int index))
            {
                index = map.Biomes.Count;
                indexOf[biome] = index;
                map.Biomes.Add(biome);
            }
            ruleBiome[r] = index;
        }

        for (int i = 0; i < cellCount; i++)
        {
            map.BiomeIndex[i] = -1;
            for (int r = 0; r < rules.Length; r++)
            {
                if (ruleBiome[r] < 0 || !Matches(rules[r], i, forestDepth[i], toWater[i])) continue;
                map.BiomeIndex[i] = ruleBiome[r];
                break;
            }
        }
    }

    /// <param name="cellsToOpen">Cells from this one to the nearest without forest (a forest's edge lies halfway).</param>
    /// <param name="cellsToWater">Cells from this one to the nearest lake or river.</param>
    bool Matches(BiomeRule rule, int i, float cellsToOpen, float cellsToWater)
    {
        float e = map.Elevation[i], slope = map.Slope[i];
        if (e < rule.height.x || e > rule.height.y) return false;
        if (slope < rule.slope.x || slope > rule.slope.y) return false;

        if (!BiomeRule.Meets(rule.forest, map.Forest[i])) return false;
        if (!BiomeRule.Meets(rule.marsh, map.Marsh[i])) return false;
        if (!BiomeRule.Meets(rule.hollow, map.Hollow[i])) return false;
        if (!BiomeRule.Meets(rule.terraced, map.Terrace[i] >= 0.5f)) return false;

        if (rule.forestDepth > 0 && cellsToOpen - 0.5f < rule.forestDepth) return false;
        if (rule.nearWater > 0 && cellsToWater > rule.nearWater) return false;

        if (rule.side != EscarpmentSide.Either)
        {
            if (!map.HasEscarpment) return false;
            bool upland = map.Scarp[i] > 0f;
            if (upland != (rule.side == EscarpmentSide.Upland)) return false;
        }
        return true;
    }
}
