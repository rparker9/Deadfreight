// Scripts/Runtime/RegionBuild/Biomes/Biome.cs
using System;
using UnityEngine;

/// <summary>
/// One kind of ground: what grows there (trees, foliage) and its fine ground noise. Where a
/// biome goes is up to each region: its RegionSettings list rules for which cells get which
/// biome, so one asset can be placed differently in different region types.
///
/// The noise is the only way a biome changes the land, and it's kept from changing the plan:
/// each cell's own average of it is subtracted, which takes out anything larger than a cell, so
/// cell heights, slopes and water stay as planned.
/// It fades out on cliffs and lakes; roads and rivers are pressed in over it.
/// </summary>
[CreateAssetMenu(fileName = "Biome", menuName = "Deadfreight/Biome")]
public sealed class Biome : ScriptableObject
{
    [Tooltip("How this biome shows in the Region Preview and the builder's biome picture.")]
    public Color previewColour = new(0.6f, 0.65f, 0.45f);

    [Header("Ground noise (fine detail only)")]
    [Unit("m"), Tooltip("Size of the bumps. Features bigger than a plan cell (50 m) are mostly taken out, so keep this well below it.")]
    public float bumpScale = 15f;
    [Unit("m"), Tooltip("How tall the bumps are.")] public float bumpHeight = 0.3f;
    [Unit("m"), Tooltip("Size of the finest roughness...")] public float roughScale = 4f;
    [Unit("m"), Tooltip("...and how tall it is.")] public float roughHeight = 0.08f;

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

    public bool HasNoise => bumpHeight > 0f || roughHeight > 0f;

    void OnValidate()
    {
        treeSpacing = Mathf.Max(2f, treeSpacing);
        bumpScale = Mathf.Max(0.5f, bumpScale);
        roughScale = Mathf.Max(0.5f, roughScale);
    }
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
