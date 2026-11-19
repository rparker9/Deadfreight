// Scripts/Runtime/LevelBuild/BuildSettings.cs
using UnityEngine;

/// <summary>
/// How LevelBuilder turns a plan into the 3D level. PlanSettings decides the land; these
/// only control the terrain's resolution and looks. Material and ground layer are optional:
/// URP's default and a plain colour are used without them.
/// </summary>
[CreateAssetMenu(fileName = "BuildSettings", menuName = "Level/Build Settings")]
public sealed class BuildSettings : ScriptableObject
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

    /// <summary>Snaps the heightmap resolution to what Unity accepts.</summary>
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
