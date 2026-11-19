// Scripts/Editor/RegionBuild/RegionBuilderEditor.cs
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build / Clear buttons for RegionBuilder, and clean-up of edit-mode builds before Play (they
/// aren't part of the scene, so Play would otherwise show them alongside its own build).
/// </summary>
[CustomEditor(typeof(RegionBuilder))]
public sealed class RegionBuilderEditor : Editor
{
    const string DefaultBuildSettingsPath = "Assets/Deadfreight/Data/RegionGen/BuildSettings.asset";

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
        }
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
            foreach (var builder in FindObjectsByType<RegionBuilder>())
                builder.Clear();
        };
    }
}
