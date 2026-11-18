// Scripts/Runtime/RegionBuild/Rocks/RockSet.cs
using System;
using UnityEngine;

/// <summary>
/// The solid things lying on a region's ground (boulders, stones, outcrops, logs, stumps), one
/// list per landform they belong to. Unlike foliage these are real objects with colliders, so
/// the vehicle hits them. RockPlacing lays them out; trees and foliage then keep clear of them.
/// </summary>
[CreateAssetMenu(fileName = "RockSet", menuName = "Deadfreight/Rock Set")]
public sealed class RockSet : ScriptableObject
{
    [Tooltip("Cliffs, steep faces and ridge tops: big rocks breaking through the ground.")]
    public RockScatter outcrops = new() { spacing = 30f, chance = 0.6f, clusterSize = new Vector2Int(2, 5), sink = 0.35f };
    [Tooltip("Loose rock fallen from the escarpment, spread out from the foot of its cliffs.")]
    public RockScatter scree = new() { spacing = 15f, chance = 0.6f, clusterSize = new Vector2Int(3, 8), clusterRadius = 8f };
    [Tooltip("Stones in and beside rivers.")]
    public RockScatter riverbeds = new() { spacing = 12f, chance = 0.35f, clusterSize = new Vector2Int(2, 5), clusterRadius = 3f };
    [Unit("m"), Tooltip("River stones lie up to this far out from the water.")]
    public float riverReach = 4f;
    [Tooltip("Under the trees: mossy boulders, fallen logs, stumps.")]
    public RockScatter forestFloor = new() { spacing = 25f, chance = 0.4f };
    [Tooltip("Out in the fields: the odd boulder or pile of cleared stones.")]
    public RockScatter openGround = new() { spacing = 90f, chance = 0.3f };
}

/// <summary>
/// How one landform's rocks are scattered. The region is split into squares <c>spacing</c>
/// across; each square gets a cluster with <c>chance</c> (times how strongly the landform is
/// there). A cluster is one large piece with smaller ones around it.
/// </summary>
[Serializable]
public sealed class RockScatter
{
    public RockPiece[] pieces = new RockPiece[0];
    [Unit("m"), Tooltip("One cluster considered per square this size. Smaller = more clusters.")]
    public float spacing = 40f;
    [Range(0f, 1f), Tooltip("Chance of a cluster in each square, where the landform is fully there.")]
    public float chance = 0.4f;
    [Tooltip("Pieces per cluster, picked at random in this range. The first is the biggest.")]
    public Vector2Int clusterSize = new(1, 3);
    [Unit("m"), Tooltip("The rest of a cluster lies within this distance of its first piece.")]
    public float clusterRadius = 5f;
    [Range(0.1f, 1f), Tooltip("The rest of a cluster are about this size compared to the first piece.")]
    public float followerScale = 0.5f;
    [Range(0f, 0.8f), Tooltip("Share of each piece's height sunk into the ground, so rocks look bedded in rather than set down.")]
    public float sink = 0.2f;
    [Range(0f, 1f), Tooltip("0 = stand upright, 1 = tilt with the ground.")]
    public float alignToGround = 0.7f;

    public bool IsEmpty
    {
        get
        {
            foreach (var piece in pieces)
                if (piece != null && piece.prefab != null && piece.weight > 0f) return false;
            return true;
        }
    }
}

/// <summary>One kind of rock (or log, or stump) in a scatter.</summary>
[Serializable]
public sealed class RockPiece
{
    [Tooltip("A model or prefab. If it has no collider, a mesh collider is added to each mesh when placed.")]
    public GameObject prefab;
    [Min(0f), Tooltip("How common this piece is in its scatter.")] public float weight = 1f;
    [Tooltip("Random size range, as a multiple of the prefab's own size.")] public Vector2 scale = new(0.8f, 1.4f);
}
