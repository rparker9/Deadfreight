// Scripts/Editor/Level/LevelPreviewWindow.Roads.cs
using UnityEditor;
using UnityEngine;

// The roads, drawn over the map as straight lines between their points (the builder smooths
// them), and their lengths in the panel. The window itself is in LevelPreviewWindow.cs.
public sealed partial class LevelPreviewWindow
{
    const float GuessSpeed = 10f;       // m/s: a rough average for the main road, slowed by bends and hills

    static readonly Color MainRoadColor = new(0.95f, 0.85f, 0.55f);
    static readonly Color BranchColor = new(0.8f, 0.72f, 0.55f);

    [SerializeField] bool showRoads = true;

    void DrawRoads()
    {
        if (!showRoads)
            return;

        foreach (var road in plan.Roads)
        {
            var points = new Vector3[road.Points.Count];
            for (int i = 0; i < points.Length; i++)
                points[i] = ToScreen(road.Points[i]);

            bool main = road.Kind == RoadKind.Main;
            float width = main ? 3f : 2f;
            Handles.color = Color.black;
            Handles.DrawAAPolyLine(width + 2f, points);
            Handles.color = main ? MainRoadColor : BranchColor;
            Handles.DrawAAPolyLine(width, points);
        }
    }

    /// <summary>What the panel says about the roads: the main road's length and a rough drive time, and the rest.</summary>
    void DrawRoadSummary()
    {
        if (plan.Roads.Count == 0)
            return;

        float mainKm = 0f, branchKm = 0f;
        int branches = 0;
        foreach (var road in plan.Roads)
        {
            float km = road.Length(plan.CellSize) / 1000f;
            if (road.Kind == RoadKind.Main)
                mainKm += km;
            else
            {
                branchKm += km;
                branches++;
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Roads", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Main road {mainKm:0.0} km from the map edge · about {mainKm * 1000f / GuessSpeed / 60f:0} min at {GuessSpeed:0} m/s", EditorStyles.miniLabel);
        EditorGUILayout.LabelField($"{branches} branches · {branchKm:0.0} km", EditorStyles.miniLabel);
    }
}
