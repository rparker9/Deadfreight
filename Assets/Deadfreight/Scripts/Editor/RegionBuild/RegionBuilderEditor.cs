// Scripts/Editor/RegionBuild/RegionBuilderEditor.cs
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build / Clear buttons for RegionBuilder, a picture of the last build's biome map, and clean-up
/// of edit-mode builds before Play (they aren't part of the scene, so Play would otherwise show
/// them alongside its own build).
/// </summary>
[CustomEditor(typeof(RegionBuilder))]
public sealed class RegionBuilderEditor : Editor
{
    const string DefaultBuildSettingsPath = "Assets/Deadfreight/Data/RegionGen/DefaultRegionBuildSettings.asset";
    const int PreviewSize = 256;

    Texture2D preview;
    BiomeMap previewOf;     // the map the preview was drawn from, to redraw after a new build

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var builder = (RegionBuilder)target;

        if (serializedObject.FindProperty("buildSettings").objectReferenceValue == null &&
            GUILayout.Button("Create default build settings"))
        {
            CreateDefaultBuildSettings();
        }

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Build")) builder.Build();
            if (GUILayout.Button("Random seed + Build"))
            {
                Undo.RecordObject(builder, "Random region seed");
                builder.Seed = Random.Range(0, int.MaxValue);
                builder.Build();
            }
            if (GUILayout.Button("Clear")) builder.Clear();
        }

        if (builder.Map?.Failure != null)
        {
            EditorGUILayout.HelpBox($"Not built: {builder.Map.Failure} Try another seed.", MessageType.Warning);
        }
        else if (builder.Map != null && builder.Roads != null)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Entry on the {builder.Map.Entry.Edge} edge · {builder.Roads.Count} road segments",
                EditorStyles.miniLabel);
            foreach (var route in builder.Map.Routes)
                EditorGUILayout.LabelField($"{route.Name}: {route.OneWayMiles:0.0} mi · ~{route.OneWayMinutes:0} min one way",
                    EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"{builder.TreeCount} trees · {builder.RockCount} rocks", EditorStyles.miniLabel);
        }

        if (builder.Biomes != null) DrawBiomePreview(builder.Biomes);
    }

    void OnDisable()
    {
        if (preview != null) DestroyImmediate(preview);
    }

    // ---------- biome preview ----------

    static readonly Color32 Nothing = new(40, 40, 40, 255);     // roads, water: nothing grows

    /// <summary>What grows where, as a picture: each point in its strongest biome's preview colour, with a key. North is up.</summary>
    void DrawBiomePreview(BiomeMap biomes)
    {
        if (previewOf != biomes || preview == null) RedrawPreview(biomes);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Biomes and edge bands (strongest at each point)", EditorStyles.boldLabel);
        var rect = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxWidth(PreviewSize * 1.5f));
        GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);

        for (int slot = 0; slot < biomes.Slots.Count; slot += 2)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Key(biomes.Slots[slot]);
                if (slot + 1 < biomes.Slots.Count) Key(biomes.Slots[slot + 1]);
            }
        }
    }

    static void Key(Biome biome)
    {
        var swatch = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f));
        EditorGUI.DrawRect(swatch, biome.previewColour);
        EditorGUILayout.LabelField(biome.name, EditorStyles.miniLabel, GUILayout.Width(110f));
    }

    void RedrawPreview(BiomeMap biomes)
    {
        if (preview == null)
            preview = new Texture2D(PreviewSize, PreviewSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };

        var pixels = new Color32[PreviewSize * PreviewSize];
        for (int y = 0; y < PreviewSize; y++)
        for (int x = 0; x < PreviewSize; x++)
        {
            int column = x * (biomes.Resolution - 1) / (PreviewSize - 1);
            int row = y * (biomes.Resolution - 1) / (PreviewSize - 1);
            int slot = biomes.Strongest(column, row);
            // Texture row 0 is the bottom: world z = 0, south.
            pixels[y * PreviewSize + x] = slot < 0 ? Nothing : (Color32)biomes.Slots[slot].previewColour;
        }
        preview.SetPixels32(pixels);
        preview.Apply();
        previewOf = biomes;
    }

    void CreateDefaultBuildSettings()
    {
        var asset = CreateInstance<RegionBuildSettings>();
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(DefaultBuildSettingsPath));
        AssetDatabase.SaveAssets();
        serializedObject.FindProperty("buildSettings").objectReferenceValue = asset;
        serializedObject.ApplyModifiedProperties();
    }

    [InitializeOnLoadMethod]
    static void ClearBuildsBeforePlay()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;
            foreach (var builder in Object.FindObjectsByType<RegionBuilder>(FindObjectsSortMode.None)) builder.Clear();
        };
    }
}
