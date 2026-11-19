// Scripts/Runtime/LevelBuild/LevelBuilder.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

/// <summary>
/// Generates a plan from the seed and builds it in the scene. For now that's the terrain alone.
/// </summary>
public sealed class LevelBuilder : MonoBehaviour
{
    [FormerlySerializedAs("levelSettings")]
    [SerializeField] PlanSettings planSettings;
    [SerializeField] BuildSettings buildSettings;
    [SerializeField] int seed = 1;
    [SerializeField] bool buildOnStart = true;

    const string RootName = "Generated Level";

    GameObject root;
    readonly List<Object> createdAssets = new();    // meshes, materials, terrain data: destroyed on Clear

    public LevelPlan Plan { get; private set; }

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

        if (planSettings == null || buildSettings == null)
        {
            Debug.LogWarning("LevelBuilder needs Plan Settings and Build Settings.", this);
            return;
        }

        var timer = Stopwatch.StartNew();
        Plan = LevelGenerator.Generate(seed, planSettings);
        if (Plan.Failure != null)
        {
            Debug.LogWarning($"Level {seed} not built: {Plan.Failure}", this);
            return;
        }

        // The plan's ground heights → the terrain's, at its resolution.
        var planToWorld = new PlanToWorld(Plan);
        var heights = TerrainHeights.FromPlan(Plan, planToWorld, buildSettings);

        // Create the root object and the terrain in the scene.
        root = new GameObject(RootName);
        root.transform.SetParent(transform, false);
        CreateTerrain(planToWorld, heights);

        // Remove any leftover objects from a previous build, and hide all generated objects from the scene so they don't get saved.
        if (!Application.isPlaying) 
            HideFromScene();
        
        Debug.Log($"Built level {seed} in {timer.ElapsedMilliseconds * 0.001f:F2}s.", this);
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
        Plan = null;

        // A build left over from before a script reload: the reference is gone, the object isn't.
        var leftover = transform.Find(RootName);
        if (leftover != null) Discard(leftover.gameObject);
    }

    // ---------- Scene Objects ----------

    /// <summary>The terrain from the heights, covered in the one ground layer.</summary>
    void CreateTerrain(PlanToWorld planToWorld, TerrainHeights heights)
    {
        var normalized = heights.Normalized(out float bottom, out float span);

        var data = Track(new TerrainData());
        data.heightmapResolution = heights.Resolution;    // before size: changing resolution rescales size
        data.size = new Vector3(planToWorld.SizeX, span, planToWorld.SizeZ);
        data.SetHeights(0, 0, normalized);
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
