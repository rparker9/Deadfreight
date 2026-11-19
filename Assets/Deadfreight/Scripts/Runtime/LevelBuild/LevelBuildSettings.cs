// Scripts/Runtime/LevelBuild/LevelBuildSettings.cs
using System;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>How a wheel feels on a kind of ground. Applied through GroundSurface.</summary>
[Serializable]
public struct SurfaceFeel
{
    [Tooltip("Multiplies each wheel's rolling resistance. Higher = more drag.")] public float rollingResistance;
    [Tooltip("Multiplies sideways grip and braking. Lower = slides more.")] public float grip;

    public SurfaceFeel(float rollingResistance, float grip)
    {
        this.rollingResistance = rollingResistance;
        this.grip = grip;
    }
}

/// <summary>
/// How the 3D level is drawn from the generated plan. LevelSettings decides the land, the
/// roads and their heights, and the pads; these only control resolution, how roads are laid
/// into the ground, looks and ground feel, in build order. Materials and the ground layer are
/// optional; plain colours are used without them. Distances are in metres.
/// </summary>
[CreateAssetMenu(fileName = "LevelBuildSettings", menuName = "Level Gen/Level Build Settings")]
public sealed class LevelBuildSettings : ScriptableObject
{
    static readonly int[] HeightmapResolutions = { 513, 1025, 2049, 4097 };

    [Header("Terrain")]
    [Tooltip("Heightmap samples per side: 513, 1025, 2049 or 4097. 2049 is about 4 m between samples on an 8 km level.")]
    public int heightmapResolution = 2049;

    [Header("Look (optional)")]
    [Tooltip("Terrain material, e.g. URP's Terrain/Lit. Empty = URP's default.")]
    public Material terrainMaterial;
    [Tooltip("The one texture covering the ground. Empty = plain colour.")]
    public TerrainLayer groundLayer;

    /// <summary>
    /// Snaps the heightmap resolution to what Unity accepts, and keeps sizes positive.
    /// </summary>
    void OnValidate()
    {
        heightmapResolution = Closest(HeightmapResolutions, heightmapResolution);
    }

    static int Closest(int[] options, int value)
    {
        int best = options[0];
        foreach (int option in options)
            if (Mathf.Abs(option - value) < Mathf.Abs(best - value)) best = option;
        return best;
    }
}
