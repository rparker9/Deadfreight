// Scripts/Editor/LevelGen/LevelPreviewWindow.Views.cs
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>What the land is coloured by.</summary>
public enum LandView
{
    ShadedRelief,   // colour by height, lit from the north-west, so landforms read as 3D
    Height,         // greyscale, low dark to high light
    Slope,          // flat green to steep red, against the steep-grade setting
    RoadCost,       // what the road pathfinder pays per cell: why roads go where they go
}

/// <summary>What the roads are coloured by.</summary>
public enum RoadView
{
    Plain,          // one road colour; routes in their own colours
    Class,          // main road, back roads, lanes
    Steepness,      // the grade under each road cell
    Speed,          // driving speed: road class and grade together, i.e. how easy it is to travel
    CutFill,        // how far each road cell is planned below the ground (cutting) or above it (embankment)
    Hidden,         // no roads, to see the land underneath
}

// The colours and legends for each view. The window itself is in LevelPreviewWindow.cs.
public sealed partial class LevelPreviewWindow
{
    static readonly Color RoadColour = new(0.82f, 0.78f, 0.68f);
    static readonly Color MainRoadColour = new(0.95f, 0.9f, 0.75f);
    static readonly Color BackRoadColour = new(0.75f, 0.55f, 0.35f);
    static readonly Color LaneColour = new(0.55f, 0.45f, 0.38f);
    static readonly Color CutColour = new(0.9f, 0.3f, 0.2f);
    static readonly Color FillColour = new(0.3f, 0.55f, 0.95f);

    const float CutFillFull = 6f;       // metres of cut or fill drawn at full colour
    const float ContourDarken = 0.45f;  // how much a contour line darkens the cell under it
    const float ShadeExaggeration = 3f; // vertical exaggeration for hill shading, so gentle land still reads

    // ---------- land ----------

    Color LandColour(int i)
    {
        float e = map.Elevation[i];
        return landView switch
        {
            LandView.Height => Grey(0.08f + 0.9f * e),
            LandView.Slope => Ramp(map.Slope[i] / (settings.steepSlope * 3f)),
            LandView.RoadCost => RoadCostColour(i),
            _ => HeightTint(e) * Shade(i),
        };
    }

    /// <summary>Cheap ground dark blue, dear ground red; log scale, so the cheap end isn't all one colour.</summary>
    Color RoadCostColour(int i)
    {
        if (map.RoadCost == null) return Color.black;
        return CostRamp(Mathf.Log(map.RoadCost[i]) / Mathf.Log(maxRoadCost));
    }

    /// <summary>Low ground green, through tan and grey, to pale high ground.</summary>
    static Color HeightTint(float e)
    {
        if (e < 0.35f) return Color.Lerp(new Color(0.3f, 0.45f, 0.28f), new Color(0.55f, 0.6f, 0.38f), e / 0.35f);
        if (e < 0.6f) return Color.Lerp(new Color(0.55f, 0.6f, 0.38f), new Color(0.68f, 0.6f, 0.45f), (e - 0.35f) / 0.25f);
        if (e < 0.8f) return Color.Lerp(new Color(0.68f, 0.6f, 0.45f), new Color(0.6f, 0.56f, 0.52f), (e - 0.6f) / 0.2f);
        return Color.Lerp(new Color(0.6f, 0.56f, 0.52f), new Color(0.9f, 0.9f, 0.88f), (e - 0.8f) / 0.2f);
    }

    /// <summary>Hill shading: how much a cell faces a light low in the north-west (the top left of the map).</summary>
    float Shade(int i)
    {
        int x = i % map.Width, y = i / map.Width;
        float Metres(int cx, int cy) => map.Elevation[Mathf.Clamp(cy, 0, map.Height - 1) * map.Width + Mathf.Clamp(cx, 0, map.Width - 1)]
                                        * settings.relief * ShadeExaggeration;

        float east = (Metres(x + 1, y) - Metres(x - 1, y)) / (2f * map.CellSize);
        float south = (Metres(x, y + 1) - Metres(x, y - 1)) / (2f * map.CellSize);     // the map's y runs south
        var normal = new Vector3(-east, -south, 1f).normalized;
        var light = new Vector3(-1f, -1f, 1.2f).normalized;                             // from the west and the north
        return 0.35f + 0.75f * Mathf.Clamp01(Vector3.Dot(normal, light));
    }

    /// <summary>Whether a contour line passes through this cell: its height band differs from the cell east or south of it.</summary>
    bool OnContour(int i)
    {
        int x = i % map.Width, y = i / map.Width;
        int Band(int j) => Mathf.FloorToInt(map.Elevation[j] * settings.relief / contourInterval);
        int band = Band(i);
        return (x + 1 < map.Width && Band(i + 1) != band) || (y + 1 < map.Height && Band(i + map.Width) != band);
    }

    // ---------- roads ----------

    /// <summary>A road cell's colour in the current road view.</summary>
    Color RoadCellColour(int i) => roadView switch
    {
        RoadView.Class => map.MainRoad[i] ? MainRoadColour : OnAnyRoute(i) ? BackRoadColour : LaneColour,
        RoadView.Steepness => Ramp(map.Slope[i] / (settings.steepSlope * 2f)),
        RoadView.Speed => Ramp(1f - LevelGenerator.SpeedMph(map, settings, i) / settings.mainRoadSpeed),
        RoadView.CutFill => CutFillColour(map.RoadCut[i]),
        _ => RoadColour,
    };

    /// <summary>Level with the ground pale; cuttings red, embankments blue, full colour at CutFillFull metres.</summary>
    static Color CutFillColour(float cut)
    {
        if (float.IsNaN(cut)) return RoadColour;
        return Color.Lerp(Grey(0.9f), cut > 0f ? CutColour : FillColour, Mathf.Abs(cut) / CutFillFull);
    }

    // ---------- ramps ----------

    static Color Grey(float value) => new(value, value, value, 1f);

    /// <summary>0 green, 0.5 yellow, 1 red (clamped).</summary>
    static Color Ramp(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? Color.Lerp(new Color(0.25f, 0.7f, 0.3f), new Color(0.95f, 0.85f, 0.25f), t * 2f)
            : Color.Lerp(new Color(0.95f, 0.85f, 0.25f), new Color(0.9f, 0.2f, 0.18f), (t - 0.5f) * 2f);
    }

    /// <summary>0 dark blue, 0.5 yellow, 1 red (clamped).</summary>
    static Color CostRamp(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f
            ? Color.Lerp(new Color(0.1f, 0.15f, 0.35f), new Color(0.95f, 0.85f, 0.25f), t * 2f)
            : Color.Lerp(new Color(0.95f, 0.85f, 0.25f), new Color(0.9f, 0.2f, 0.18f), (t - 0.5f) * 2f);
    }

    // ---------- legends ----------

    void DrawLandLegend()
    {
        switch (landView)
        {
            case LandView.Height:
                GradientBar(t => Grey(0.08f + 0.9f * t), "0 m", $"{settings.relief * 0.5f:0} m", $"{settings.relief:0} m");
                break;
            case LandView.Slope:
                GradientBar(Ramp, "0%", $"{settings.steepSlope * 150f:0}%", $"{settings.steepSlope * 300f:0}%+");
                EditorGUILayout.LabelField($"Roads count as steep from {settings.steepSlope * 100f:0}% (a third of the way along).",
                    EditorStyles.wordWrappedMiniLabel);
                break;
            case LandView.RoadCost:
                GradientBar(CostRamp, "cheap", "", $"dear ({maxRoadCost:0} per cell)");
                break;
            default:
                GradientBar(HeightTint, "low", "", "high");
                break;
        }
    }

    void DrawRoadLegend()
    {
        switch (roadView)
        {
            case RoadView.Class:
                Swatches(("main", MainRoadColour), ("back road", BackRoadColour), ("lane", LaneColour));
                break;
            case RoadView.Steepness:
                GradientBar(Ramp, "flat", $"{settings.steepSlope * 100f:0}% (steep)", $"{settings.steepSlope * 200f:0}%+");
                break;
            case RoadView.Speed:
                GradientBar(t => Ramp(1f - t), "crawl", "", $"{settings.mainRoadSpeed:0} mph");
                break;
            case RoadView.CutFill:
                GradientBar(t => t < 0.5f ? Color.Lerp(FillColour, Grey(0.9f), t * 2f) : Color.Lerp(Grey(0.9f), CutColour, (t - 0.5f) * 2f),
                    $"{CutFillFull:0} m up (embankment)", "level", $"{CutFillFull:0} m down (cutting)");
                break;
        }
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

    /// <summary>Colour swatches with names, two to a row.</summary>
    static void Swatches(params (string name, Color colour)[] keys)
    {
        for (int k = 0; k < keys.Length; k += 2)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Swatch(keys[k]);
                if (k + 1 < keys.Length) Swatch(keys[k + 1]);
            }
        }
    }

    static void Swatch((string name, Color colour) key)
    {
        var box = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f));
        EditorGUI.DrawRect(box, key.colour);
        EditorGUILayout.LabelField(key.name, EditorStyles.miniLabel, GUILayout.Width(130f));
    }

    // ---------- hover ----------

    /// <summary>Everything worth knowing about one cell, for the box under the mouse.</summary>
    string CellReport(Vector2Int cell)
    {
        int i = map.Index(cell);
        var lines = new List<string>
        {
            $"Cell {cell.x}, {cell.y}",
            $"Height {map.Metres(i):0} m · slope {map.Slope[i] * 100f:0}%",
        };
        if (map.RoadCost != null) lines.Add($"Road cost {map.RoadCost[i]:0.0}");
        if (map.Road[i])
        {
            string kind = map.MainRoad[i] ? "Main road" : OnAnyRoute(i) ? "Back road" : "Lane";
            bool steep = map.ConditionAt(i, settings.steepSlope) == RoadCondition.Steep;
            lines.Add($"{kind} · {LevelGenerator.SpeedMph(map, settings, i):0} mph" + (steep ? " · steep" : ""));
            if (!float.IsNaN(map.RoadHeight[i]))
            {
                float cut = map.RoadCut[i];
                string how = Mathf.Abs(cut) < 0.25f ? "at ground level" : cut > 0f ? $"cut {cut:0.#} m down" : $"built {-cut:0.#} m up";
                lines.Add($"Road at {map.RoadHeight[i]:0.#} m, {how}");
            }
        }
        return string.Join("\n", lines);
    }
}
