// Scripts/Runtime/RegionBuild/TreeKind.cs
using System;
using UnityEngine;

/// <summary>Where a kind of tree grows.</summary>
public enum TreeHabitat
{
    DeepForest,     // the middle of the plan's forests
    ForestEdge,     // thinning out toward a forest's edge
    Open,           // lone trees out in the fields
    Wet,            // marsh and river banks; used there instead of the others when any are listed
}

/// <summary>One kind of tree a region plants: which prefab, where it grows, how common, how big.</summary>
[Serializable]
public sealed class TreeKind
{
    [Tooltip("A tree prefab, e.g. one made by Tools → Deadfreight → Tree Prefab Builder.")]
    public GameObject prefab;
    public TreeHabitat grows = TreeHabitat.DeepForest;
    [Min(0f), Tooltip("How common this kind is among the trees growing in the same place.")] public float weight = 1f;
    [Tooltip("Random size range, as a multiple of the prefab's own size.")] public Vector2 scale = new(0.8f, 1.2f);
}
