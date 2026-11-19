// Scripts/Editor/LevelGen/LevelPreviewWindow.Views.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>What the land is coloured by.</summary>
public enum LandView
{
    ShadedRelief,   // colour by height, lit from the north-west, so landforms read as 3D
    Height,         // grayscale, low dark to high light
    Slope,          // flat green to steep red
    Zones,          // each zone in its preview colour, hill-shaded
}

// The colours and legends for each view. The window itself is in LevelPreviewWindow.cs.
public sealed partial class LevelPreviewWindow
{
    const float SlopeFullRed = 0.25f;   // rise over run drawn full red in the Slope view
    const float ContourDarken = 0.45f;  // how much a contour line darkens the cell under it
    static readonly Color WaterColor = new(0.17f, 0.33f, 0.38f);
    const float ShadeExaggeration = 3f; // vertical exaggeration for hill shading, so gentle land still reads

    // ---------- land ----------

    Color LandColor(int i)
    {
        float t = plan.GroundHeight[i] / plan.Relief;      // 0 lowest ground, 1 highest
        return landView switch
        {
            LandView.Height => Gray(0.08f + 0.9f * t),
            LandView.Slope => Ramp(plan.Slope[i] / SlopeFullRed),
            LandView.Zones => plan.IsWater(i) ? Water(i) : ZoneColor(i) * Shade(i),
            _ => plan.IsWater(i) ? Water(i) : HeightTint(t) * Shade(i),
        };
    }

    /// <summary>Water, a little darker the deeper it is.</summary>
    Color Water(int i)
    {
        float depth01 = (plan.WaterHeight - plan.GroundHeight[i]) / Mathf.Max(1f, plan.WaterHeight);
        return WaterColor * (1f - 0.35f * depth01);
    }

    /// <summary>The cell's zone's preview colour (gray for a region with no zones).</summary>
    Color ZoneColor(int i)
    {
        var zones = plan.Region.zones;
        return zones.Count > 0 ? zones[plan.Zone[i]].previewColor : Color.gray;
    }

    /// <summary>Low ground green, through tan and gray, to pale high ground (t: 0 lowest, 1 highest).</summary>
    static Color HeightTint(float e)
    {
        if (e < 0.35f) return Color.Lerp(new Color(0.3f, 0.45f, 0.28f), new Color(0.55f, 0.6f, 0.38f), e / 0.35f);
        if (e < 0.6f) return Color.Lerp(new Color(0.55f, 0.6f, 0.38f), new Color(0.68f, 0.6f, 0.45f), (e - 0.35f) / 0.25f);
        if (e < 0.8f) return Color.Lerp(new Color(0.68f, 0.6f, 0.45f), new Color(0.6f, 0.56f, 0.52f), (e - 0.6f) / 0.2f);
        return Color.Lerp(new Color(0.6f, 0.56f, 0.52f), new Color(0.9f, 0.9f, 0.88f), (e - 0.8f) / 0.2f);
    }

    /// <summary>Hill shading: how much a cell faces a light low in the north-west (the top left of the preview).</summary>
    float Shade(int i)
    {
        int x = i % plan.CellsX, y = i / plan.CellsX;
        float Height(int cx, int cy) =>
            plan.GroundHeight[Mathf.Clamp(cy, 0, plan.CellsY - 1) * plan.CellsX + Mathf.Clamp(cx, 0, plan.CellsX - 1)] * ShadeExaggeration;

        float east = (Height(x + 1, y) - Height(x - 1, y)) / (2f * plan.CellSize);
        float north = (Height(x, y + 1) - Height(x, y - 1)) / (2f * plan.CellSize);
        var normal = new Vector3(-east, -north, 1f).normalized;                         // (east, north, up)
        var light = new Vector3(-1f, 1f, 1.2f).normalized;                              // from the west and the north
        return 0.35f + 0.75f * Mathf.Clamp01(Vector3.Dot(normal, light));
    }

    /// <summary>Whether a contour line passes through this cell: its height band differs from the cell east or north of it.</summary>
    bool OnContour(int i)
    {
        int x = i % plan.CellsX, y = i / plan.CellsX;
        int Band(int j) => Mathf.FloorToInt(plan.GroundHeight[j] / contourInterval);
        int band = Band(i);
        return (x + 1 < plan.CellsX && Band(i + 1) != band) || (y + 1 < plan.CellsY && Band(i + plan.CellsX) != band);
    }

    // ---------- ramps ----------

    static Color Gray(float value) => new(value, value, value, 1f);

    /// <summary>0 green, 0.5 yellow, 1 red (clamped).</summary>
    static Color Ramp(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? Color.Lerp(new Color(0.25f, 0.7f, 0.3f), new Color(0.95f, 0.85f, 0.25f), t * 2f)
            : Color.Lerp(new Color(0.95f, 0.85f, 0.25f), new Color(0.9f, 0.2f, 0.18f), (t - 0.5f) * 2f);
    }

    // ---------- legends ----------

    void DrawLandLegend()
    {
        switch (landView)
        {
            case LandView.Height:
                GradientBar(t => Gray(0.08f + 0.9f * t), "0 m", $"{plan.Relief * 0.5f:0} m", $"{plan.Relief:0} m");
                break;
            case LandView.Slope:
                GradientBar(Ramp, "0%", $"{SlopeFullRed * 50f:0.#}%", $"{SlopeFullRed * 100f:0}%+");
                break;
            case LandView.Zones:
                ZoneKey();
                break;
            default:
                GradientBar(HeightTint, "low", "", "high");
                break;
        }
    }

    /// <summary>Each zone's colour and name, with the share of the level it covers.</summary>
    void ZoneKey()
    {
        var zones = plan.Region.zones;
        var cells = new int[Mathf.Max(1, zones.Count)];
        foreach (byte z in plan.Zone) cells[z]++;

        for (int z = 0; z < zones.Count; z++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                var box = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f));
                EditorGUI.DrawRect(box, zones[z].previewColor);
                EditorGUILayout.LabelField($"{zones[z].name} · {cells[z] * 100f / plan.Zone.Length:0}%", EditorStyles.miniLabel);
            }
        }
        if (zones.Count == 0) EditorGUILayout.LabelField("This region has no zones.", EditorStyles.miniLabel);
    }

    /// <summary>A colour bar with labels under its ends and middle.</summary>
    static void GradientBar(System.Func<float, Color> colour, string low, string middle, string high)
    {
        const int Slices = 48;
        var bar = GUILayoutUtility.GetRect(0f, 12f, GUILayout.ExpandWidth(true));
        for (int k = 0; k < Slices; k++)
        {
            var slice = new Rect(bar.x + bar.width * k / Slices, bar.y, bar.width / Slices + 1f, bar.height);
            EditorGUI.DrawRect(slice, colour((k + 0.5f) / Slices));
        }
        var labels = GUILayoutUtility.GetRect(0f, 14f, GUILayout.ExpandWidth(true));
        GUI.Label(labels, low, EditorStyles.miniLabel);
        GUI.Label(labels, middle, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter });
        GUI.Label(labels, high, new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight });
    }

    // ---------- hover ----------

    /// <summary>Everything worth knowing about one cell, for the box under the mouse.</summary>
    string CellReport(Vector2Int cell)
    {
        int i = plan.Index(cell);
        var lines = new List<string>
        {
            $"Cell {cell.x}, {cell.y}",
            $"Ground {plan.GroundHeight[i]:0} m · slope {plan.Slope[i] * 100f:0}%",
            plan.Region.zones.Count > 0 ? $"Zone: {plan.Region.zones[plan.Zone[i]].name}" : "No zones",
        };
        if (plan.IsWater(i))
            lines.Add($"Under water, {plan.WaterHeight - plan.GroundHeight[i]:0.#} m deep");
        return string.Join("\n", lines);
    }
}
