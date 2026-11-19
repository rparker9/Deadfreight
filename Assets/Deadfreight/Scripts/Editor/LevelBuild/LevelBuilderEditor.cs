// Scripts/Editor/LevelBuild/LevelBuilderEditor.cs
using UnityEditor;
using UnityEngine;

/// <summary>
/// Build / Clear buttons for LevelBuilder, and clean-up of edit-mode builds before Play (they
/// aren't part of the scene, so Play would otherwise show them alongside its own build).
/// </summary>
[CustomEditor(typeof(LevelBuilder))]
public sealed class LevelBuilderEditor : Editor
{
    const string DefaultBuildSettingsPath = "Assets/Deadfreight/Data/LevelGen/BuildSettings.asset";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var builder = (LevelBuilder)target;

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
                Undo.RecordObject(builder, "Random level seed");
                builder.Seed = Random.Range(0, int.MaxValue);
                builder.Build();
            }
            if (GUILayout.Button("Clear")) builder.Clear();
        }

        if (builder.Plan?.Failure != null)
        {
            EditorGUILayout.HelpBox($"Not built: {builder.Plan.Failure} Try another seed.", MessageType.Warning);
        }
        else if (builder.Plan != null)
        {
            var plan = builder.Plan;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                $"{plan.CellsX * plan.CellSize / 1000f:0.#} × {plan.CellsY * plan.CellSize / 1000f:0.#} km · {plan.Relief:0} m relief",
                EditorStyles.miniLabel);
        }
    }

    void CreateDefaultBuildSettings()
    {
        var asset = CreateInstance<BuildSettings>();
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
            foreach (var builder in FindObjectsByType<LevelBuilder>())
                builder.Clear();
        };
    }
}
