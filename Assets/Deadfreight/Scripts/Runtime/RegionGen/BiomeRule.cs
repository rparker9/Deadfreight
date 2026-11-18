// Scripts/Runtime/RegionGen/BiomeRule.cs
using System;
using UnityEngine;

/// <summary>Whether a rule needs a feature in the cell, rules it out, or doesn't care.</summary>
public enum Need { Any, Required, Excluded }

/// <summary>Which side of the escarpment a rule applies on.</summary>
public enum EscarpmentSide { Either, Upland, Lowland }

/// <summary>
/// One entry in a region's biome list: a biome, and the conditions a plan cell must meet to get
/// it. Every condition must hold; ones left at their defaults match everything. The region
/// checks its list top to bottom and the first match wins, so put narrow rules first and a
/// catch-all (no conditions) last. For "this OR that", add the same biome twice.
/// </summary>
[Serializable]
public sealed class BiomeRule
{
    [Tooltip("What grows here, and the ground's fine noise.")]
    public Biome biome;

    [Tooltip("Plan height, 0 (lowest in the region) to 1 (highest): from x to y.")]
    public Vector2 height = new(0f, 1f);
    [Tooltip("Slope, rise over run: from x to y. 0.1 = 10%.")]
    public Vector2 slope = new(0f, 10f);

    public Need forest = Need.Any;
    [Min(0), Tooltip("At least this many cells in from a forest's edge. 0 = anywhere.")]
    public int forestDepth;
    public Need marsh = Need.Any;
    public Need hollow = Need.Any;
    public Need terraced = Need.Any;
    [Min(0), Tooltip("Within this many cells of a river or lake. 0 = anywhere.")]
    public int nearWater;
    public EscarpmentSide side = EscarpmentSide.Either;

    /// <summary>Whether a feature's presence satisfies a need.</summary>
    public static bool Meets(Need need, bool present) =>
        need == Need.Any || (need == Need.Required) == present;
}
