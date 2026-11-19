// Scripts/Runtime/LevelBuild/LevelBuilder.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds a level in the scene, with terrain, roads, and buildings.
/// </summary>
public sealed class LevelBuilder : MonoBehaviour
{
    [SerializeField] LevelSettings levelSettings;
    [SerializeField] LevelBuildSettings buildSettings;
    [SerializeField] int seed = 1;
    [SerializeField] bool buildOnStart = true;

    const string RootName = "Generated Level";

    GameObject root;
    readonly List<Object> createdAssets = new();    // meshes, materials, terrain data: destroyed on Clear

    public LevelLayout Map { get; private set; }
    public Pose EntryPose { get; private set; }

    public int Seed
    {
        get => seed;
        set => seed = value;
    }

    void Start()
    {
        if (buildOnStart) Build();
    }

    void OnDestroy() => Clear();

    // ---------- build ----------

    public void Build()
    {
        Clear();

        if (levelSettings == null || buildSettings == null)
        {
            Debug.LogWarning("LevelBuilder needs Level Settings and Level Build Settings.", this);
            return;
        }

        // Generate the level layout.
        var timer = Stopwatch.StartNew();
        Map = LevelGenerator.Generate(seed, levelSettings);

        // Early exit if the level generation failed; the map will have a Failure string.
        if (Map.Failure != null)
        {
            Debug.LogWarning($"Level {seed} not built: {Map.Failure}", this);
            return;
        }

        // 
        var frame = new LevelFrame(Map);
        var field = HeightField.FromLayout(Map, frame, buildSettings);

        // Create the root object and the terrain in the scene.
        root = new GameObject(RootName);
        root.transform.SetParent(transform, false);
        CreateTerrain(frame, field);

        // Remove any leftover objects from a previous build, and hide all generated objects from the scene so they don't get saved.
        if (!Application.isPlaying) 
            HideFromScene();
        
        Debug.Log($"Built level {seed} in {timer.ElapsedMilliseconds * 0.001f:F2}s, {Map.RejectedAlternates} alternates rejected.", this);
    }

    public void Clear()
    {
        if (root != null) 
            Discard(root);
        root = null;

        foreach (var asset in createdAssets)
            if (asset != null) 
                Discard(asset);
        
        createdAssets.Clear();
        Map = null;

        // A build left over from before a script reload: the reference is gone, the object isn't.
        var leftover = transform.Find(RootName);
        if (leftover != null) Discard(leftover.gameObject);
    }

    // ---------- Scene Objects ----------

    /// <summary>The terrain from the field's heights, covered in the one ground layer.</summary>
    void CreateTerrain(LevelFrame frame, HeightField field)
    {
        var heights = field.Normalized(out float bottom, out float span);

        var data = Track(new TerrainData());
        data.heightmapResolution = field.Resolution;    // before size: changing resolution rescales size
        data.size = new Vector3(frame.SizeX, span, frame.SizeZ);
        data.SetHeights(0, 0, heights);
        data.terrainLayers = new[]
        {
            buildSettings.groundLayer != null ? buildSettings.groundLayer : PlainLayer(new Color(0.42f, 0.50f, 0.30f)),
        };

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, bottom, 0f);

        var terrain = go.GetComponent<Terrain>();
        if (buildSettings.terrainMaterial != null) terrain.materialTemplate = buildSettings.terrainMaterial;
    }
    // ---------- Helpers ----------

    TerrainLayer PlainLayer(Color color)
    {
        var texture = Track(new Texture2D(4, 4));
        var pixels = new Color[16];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        texture.SetPixels(pixels);
        texture.Apply();
        return Track(new TerrainLayer { diffuseTexture = texture, tileSize = new Vector2(8f, 8f) });
    }

    T Track<T>(T asset) where T : Object
    {
        createdAssets.Add(asset);
        return asset;
    }

    /// <summary>
    /// Edit-mode builds stay out of the saved scene; they would only store stale data.
    /// </summary>
    void HideFromScene()
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        foreach (var asset in createdAssets) asset.hideFlags = HideFlags.DontSave;
    }

    /// <summary>
    /// Destroy at end of frame in play mode, at once in the editor (where Destroy isn't allowed).
    /// </summary>
    static void Discard(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
