// Scripts/Runtime/RegionBuild/GroundCover.cs
using System;
using UnityEngine;

/// <summary>Which painted ground a kind of ground cover grows on. Matches TerrainPaint's layers.</summary>
public enum CoverGround { Grass = 0, Dirt = 1, Stony = 2, Rock = 3 }

/// <summary>
/// One kind of small plant or debris scattered over the terrain as a terrain detail (instanced
/// mesh): grass blades, ferns, nettles, pebbles. Where it grows follows the terrain's paint, so
/// ferns sit on dirt under trees and pebbles on stony ground.
/// </summary>
[Serializable]
public sealed class GroundCover
{
    [Tooltip("A mesh model or prefab with a MeshFilter and MeshRenderer on its root.")]
    public GameObject prototype;
    [Tooltip("Which painted ground it grows on.")] public CoverGround grows = CoverGround.Grass;
    [Range(0f, 1f), Tooltip("Share of that ground it covers, before patchiness.")] public float amount = 1f;
    [Tooltip("How tightly instances pack where it grows (Unity's coverage density).")] public float density = 1f;
    [Tooltip("Random size range, as a multiple of the mesh's own size.")] public Vector2 scale = new(0.8f, 1.2f);
    [Range(0f, 1f), Tooltip("0 = stand upright, 1 = tilt with the ground.")] public float alignToGround = 0.5f;
}
