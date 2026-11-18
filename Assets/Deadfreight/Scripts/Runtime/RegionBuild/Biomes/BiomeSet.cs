// Scripts/Runtime/RegionBuild/Biomes/BiomeSet.cs
using System;

/// <summary>
/// Which Biome asset grows in each role. An empty slot grows nothing there. Reuse one asset in
/// several slots to share its contents (e.g. the same Field biome for Hollow).
/// </summary>
[Serializable]
public sealed class BiomeSet
{
    public const int RoleCount = 8;     // one per BiomeRole

    public Biome deepForest;
    public Biome forestEdge;
    public Biome field;
    public Biome wetland;
    public Biome riverbank;
    public Biome hollow;
    public Biome upland;
    public Biome verge;

    public Biome this[BiomeRole role] => role switch
    {
        BiomeRole.DeepForest => deepForest,
        BiomeRole.ForestEdge => forestEdge,
        BiomeRole.Field => field,
        BiomeRole.Wetland => wetland,
        BiomeRole.Riverbank => riverbank,
        BiomeRole.Hollow => hollow,
        BiomeRole.Upland => upland,
        BiomeRole.Verge => verge,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, null),
    };

    public Biome this[int role] => this[(BiomeRole)role];
}
