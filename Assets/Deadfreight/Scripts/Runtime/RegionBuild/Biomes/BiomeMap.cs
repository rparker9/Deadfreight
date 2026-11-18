// Scripts/Runtime/RegionBuild/Biomes/BiomeMap.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How much of each biome grows at every point of the region, for trees and foliage. Worked out
/// once, at about biomeResolution points per side, from:
///
///   1. the edge bands, narrow strips the plan's 50 m cells can't hold: the verge beside every
///      road, then the bank along river water (each a Biome in the build settings, or none);
///   2. the plan's biome per cell (BiomeBlend) for the rest.
///
/// Each biome asset is one slot, even if it's both an area biome and a band. Weights at a point
/// add up to 1 at most; roads, river water and lakes get none, so nothing grows there.
/// </summary>
public sealed class BiomeMap
{
    public readonly int Resolution;
    public readonly float SizeX, SizeZ;

    /// <summary>Every biome that grows anywhere, once each. Weights are per slot.</summary>
    public readonly IReadOnlyList<Biome> Slots;

    readonly byte[] weights;    // [(row * Resolution + column) * SlotCount + slot], 0-255
    readonly int slotCount;

    BiomeMap(int resolution, float sizeX, float sizeZ, List<Biome> slots)
    {
        Resolution = resolution;
        SizeX = sizeX;
        SizeZ = sizeZ;
        Slots = slots;
        slotCount = Mathf.Max(1, slots.Count);
        weights = new byte[resolution * resolution * slotCount];
    }

    public static BiomeMap Build(GroundSurvey survey, BiomeBlend blend)
    {
        var settings = survey.Settings;
        var plan = survey.Map;

        // Slots: the plan's biomes in order, then the band biomes if they're new.
        var slots = new List<Biome>();
        int SlotOf(Biome biome)
        {
            if (biome == null) return -1;
            int at = slots.IndexOf(biome);
            if (at >= 0) return at;
            slots.Add(biome);
            return slots.Count - 1;
        }
        var planSlot = new int[plan.Biomes.Count];
        for (int b = 0; b < planSlot.Length; b++) planSlot[b] = SlotOf(plan.Biomes[b]);
        int vergeSlot = SlotOf(settings.verge.biome);
        int bankSlot = SlotOf(settings.riverbank.biome);

        var map = new BiomeMap(settings.biomeResolution, survey.Field.SizeX, survey.Field.SizeZ, slots);
        var biomes = new int[BiomeBlend.MaxAtPoint];
        var shares = new float[BiomeBlend.MaxAtPoint];
        var point = new float[map.slotCount];

        for (int row = 0; row < map.Resolution; row++)
        for (int column = 0; column < map.Resolution; column++)
        {
            var world = map.PointToWorld(column, row);
            System.Array.Clear(point, 0, point.Length);

            if (!survey.OnRoad(world) && !survey.InRiver(world) && !survey.BelowLake(world))
            {
                float left = 1f;
                if (vergeSlot >= 0) left = Claim(point, vergeSlot, TerrainPaint.Within(survey.FromRoad(world), settings.verge.width), left);
                if (bankSlot >= 0) left = Claim(point, bankSlot, TerrainPaint.Within(survey.FromRiver(world), settings.riverbank.width), left);

                int count = blend.At(world, biomes, shares);
                for (int k = 0; k < count; k++)
                {
                    int slot = planSlot[biomes[k]];
                    if (slot >= 0) point[slot] += shares[k] * left;
                }
            }

            int at = (row * map.Resolution + column) * map.slotCount;
            for (int slot = 0; slot < map.slotCount; slot++)
                map.weights[at + slot] = (byte)Mathf.RoundToInt(Mathf.Clamp01(point[slot]) * 255f);
        }
        return map;
    }

    /// <summary>Gives a band its share of what's left at a point; returns what's left after it.</summary>
    static float Claim(float[] point, int slot, float strength, float left)
    {
        float share = strength * left;
        point[slot] += share;
        return left - share;
    }

    // ---------- reading ----------

    /// <summary>Weight 0-1 of one slot at a world point (from the nearest map point).</summary>
    public float Weight(Vector2 world, int slot) => weights[Index(world) + slot] / 255f;

    /// <summary>All slots' weights at a world point, 0-1 each. <paramref name="into"/> needs Slots.Count room.</summary>
    public void Weights(Vector2 world, float[] into)
    {
        int at = Index(world);
        for (int slot = 0; slot < Slots.Count; slot++) into[slot] = weights[at + slot] / 255f;
    }

    /// <summary>The slot with the most weight at a map point, or -1 where nothing grows.</summary>
    public int Strongest(int column, int row)
    {
        int at = (row * Resolution + column) * slotCount, best = -1, bestWeight = 0;
        for (int slot = 0; slot < Slots.Count; slot++)
            if (weights[at + slot] > bestWeight) (best, bestWeight) = (slot, weights[at + slot]);
        return best;
    }

    public Vector2 PointToWorld(int column, int row) =>
        new(column / (Resolution - 1f) * SizeX, row / (Resolution - 1f) * SizeZ);

    int Index(Vector2 world)
    {
        int column = Mathf.Clamp(Mathf.RoundToInt(world.x / SizeX * (Resolution - 1)), 0, Resolution - 1);
        int row = Mathf.Clamp(Mathf.RoundToInt(world.y / SizeZ * (Resolution - 1)), 0, Resolution - 1);
        return (row * Resolution + column) * slotCount;
    }
}
