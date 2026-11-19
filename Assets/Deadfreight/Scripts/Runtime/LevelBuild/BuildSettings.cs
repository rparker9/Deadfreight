// Scripts/Runtime/LevelBuild/BuildSettings.cs
using UnityEngine;

/// <summary>
/// How LevelBuilder turns a plan into the 3D level, whatever its region: resolutions and the
/// terrain material. What each region looks like is in its RegionProfile.
/// </summary>
[CreateAssetMenu(fileName = "BuildSettings", menuName = "Level/Build Settings")]
public sealed class BuildSettings : ScriptableObject
{
    static readonly int[] HeightmapResolutions = { 513, 1025, 2049, 4097 };
    static readonly int[] AlphamapResolutions = { 256, 512, 1024, 2048 };

    [Header("Terrain")]
    [Tooltip("Heightmap samples per side: 513, 1025, 2049 or 4097. 2049 is about 4 m between samples on an 8 km level.")]
    public int heightmapResolution = 2049;
    [Tooltip("Ground-paint texels per side, where the zones' layers are painted: 256, 512, 1024 or 2048. 512 is about 16 m per texel on an 8 km level; zones blend over a cell (50 m) anyway.")]
    public int alphamapResolution = 512;

    [Header("Look (optional)")]
    [Tooltip("Terrain material, e.g. URP's Terrain/Lit. Empty = URP's default.")]
    public Material terrainMaterial;

    /// <summary>Snaps the resolutions to what Unity accepts.</summary>
    void OnValidate()
    {
        heightmapResolution = Closest(HeightmapResolutions, heightmapResolution);
        alphamapResolution = Closest(AlphamapResolutions, alphamapResolution);
    }

    static int Closest(int[] options, int value)
    {
        int best = options[0];
        foreach (int option in options)
            if (Mathf.Abs(option - value) < Mathf.Abs(best - value)) best = option;
        return best;
    }
}
