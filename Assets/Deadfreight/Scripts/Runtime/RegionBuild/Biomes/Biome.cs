// Scripts/Runtime/RegionBuild/Biomes/Biome.cs
using System;
using UnityEngine;

/// <summary>
/// The kinds of ground a region is made of. The builder works out where each one is (BiomeMap);
/// what grows there comes from the Biome asset in that role's slot (BiomeSet).
/// </summary>
public enum BiomeRole
{
    DeepForest,     // well inside the plan's forests
    ForestEdge,     // the outer band of a forest, thinning toward the open
    Field,          // open ground: whatever isn't one of the others
    Wetland,        // the plan's marsh
    Riverbank,      // a band along river water
    Hollow,         // the plan's hollows: low, damp, foggy bowls
    Upland,         // high or steep ground
    Verge,          // a band beside every road
}

/// <summary>
/// What grows in one kind of ground: its trees and its foliage, and how thickly. Entries don't
/// say where they grow; the biome does, by being put in a role's slot. One asset can fill
/// several slots.
/// </summary>
[CreateAssetMenu(fileName = "Biome", menuName = "Deadfreight/Biome")]
public sealed class Biome : ScriptableObject
{
    [Header("Trees")]
    public BiomeTree[] trees = new BiomeTree[0];
    [Unit("m"), Tooltip("Trees are considered on a grid this far apart. Smaller = denser (and many more trees).")]
    public float treeSpacing = 10f;
    [Range(0f, 1f), Tooltip("Chance each grid spot gets a tree. Spacing 8 and fill 0.9 is thick forest; 70 and 0.35 is lone field trees.")]
    public float treeFill = 0.5f;

    [Header("Foliage (terrain details)")]
    public BiomeFoliage[] foliage = new BiomeFoliage[0];
    [Range(0f, 1f), Tooltip("0 = foliage is even all over this biome; 1 = it only grows in patches.")]
    public float patchiness = 0.6f;

    void OnValidate() => treeSpacing = Mathf.Max(2f, treeSpacing);
}

/// <summary>One kind of tree in a biome.</summary>
[Serializable]
public sealed class BiomeTree
{
    [Tooltip("A tree prefab or model. Give it a collider (e.g. a capsule round the trunk) if things should hit it.")]
    public GameObject prefab;
    [Min(0f), Tooltip("How common this tree is among this biome's trees.")] public float weight = 1f;
    [Tooltip("Random size range, as a multiple of the prefab's own size.")] public Vector2 scale = new(0.8f, 1.2f);
}

/// <summary>
/// One kind of small plant scattered as a terrain detail (instanced mesh): grass, ferns,
/// mushrooms, wheat. The same model in several biomes is drawn as one detail layer; its look
/// (scale, density, tilt) comes from the first biome that lists it, its amount from each biome.
/// </summary>
[Serializable]
public sealed class BiomeFoliage
{
    [Tooltip("A mesh model or prefab with a MeshFilter and MeshRenderer on its root.")]
    public GameObject prototype;
    [Range(0f, 1f), Tooltip("Share of this biome it covers, before patchiness.")] public float amount = 1f;
    [Tooltip("How tightly instances pack where it grows (Unity's coverage density).")] public float density = 1f;
    [Tooltip("Random size range, as a multiple of the mesh's own size.")] public Vector2 scale = new(0.8f, 1.2f);
    [Range(0f, 1f), Tooltip("0 = stand upright, 1 = tilt with the ground.")] public float alignToGround = 0.5f;
}
