// Scripts/Editor/LevelGen/LevelPreviewWindow.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// </summary>
public sealed partial class LevelPreviewWindow : EditorWindow
{
    const float PanelWidth = 320f;
    const string DefaultSettingsPath = "Assets/Deadfreight/Data/LevelGen/LevelSettings.asset";

    [SerializeField] LevelSettings settings;
    [SerializeField] int seed = 1;
    [SerializeField] LandView landView = LandView.ShadedRelief;
    [SerializeField] bool contours;
    [SerializeField] float contourInterval = 10f;
    [SerializeField] bool smoothPixels;

    LevelLayout map;

    // Where the map was last drawn, and the cell under the mouse (if any).
    Rect mapRect;
    float mapScale;
    Vector2Int? hoverCell;
    Texture2D texture;
    long generateMs;
    string settingsSnapshot;
    Vector2 scroll;

    [MenuItem("Tools/Deadfreight/Level Preview")]
    static void Open() => GetWindow<LevelPreviewWindow>("Level Preview");

    void OnEnable()
    {
        wantsMouseMove = true;
        Regenerate();
    }

    void OnDisable()
    {
        if (texture != null) 
            DestroyImmediate(texture);
    }

    // Settings assets are edited in the inspector; regenerate when their contents change.
    void OnInspectorUpdate()
    {
        string snapshot = settings != null ? JsonUtility.ToJson(settings) : null;
        if (snapshot == settingsSnapshot) return;
        settingsSnapshot = snapshot;
        Regenerate();
        Repaint();
    }

    // ---------- Generation ----------
    void Regenerate()
    {
        map = null;
        map = null;
        if (settings == null) return;

        var timer = Stopwatch.StartNew();
        map = LevelGenerator.Generate(seed, settings);
        generateMs = timer.ElapsedMilliseconds;

        BuildTexture();
    }

    void BuildTexture()
    {
        if (texture == null || texture.width != map.Width || texture.height != map.Height)
        {
            if (texture != null) DestroyImmediate(texture);
            texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        var pixels = new Color[map.Width * map.Height];
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                int i = y * map.Width + x;
                // Textures start at the bottom row; the map's y runs downward.
                pixels[(map.Height - 1 - y) * map.Width + x] = CellColor(i);
            }
        }
        texture.filterMode = smoothPixels ? FilterMode.Bilinear : FilterMode.Point;
        texture.SetPixels(pixels);
        texture.Apply();
    }

    /// <summary>
    /// Gets the colour of a cell, based on the land view, and whether it's on a contour or route.
    /// </summary>
    Color CellColor(int i)
    {
        var c = LandColour(i);
        if (contours && contourInterval > 0f && OnContour(i)) 
            c *= 1f - ContourDarken;

        c.a = 1f;
        return c;
    }

    // ---------- GUI ----------
    void OnGUI()
    {
        var panel = new Rect(0f, 0f, PanelWidth, position.height);
        var canvas = new Rect(PanelWidth, 0f, position.width - PanelWidth, position.height);

        GUILayout.BeginArea(panel, EditorStyles.helpBox);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawPanel();
        EditorGUILayout.EndScrollView();
        GUILayout.EndArea();

        DrawCanvas(canvas);
    }

    void DrawPanel()
    {
        EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        settings = (LevelSettings)EditorGUILayout.ObjectField("Settings", settings, typeof(LevelSettings), false);
        using (new EditorGUILayout.HorizontalScope())
        {
            seed = EditorGUILayout.IntField("Seed", seed);
            if (GUILayout.Button("◀", GUILayout.Width(24f))) seed--;
            if (GUILayout.Button("▶", GUILayout.Width(24f))) seed++;
            if (GUILayout.Button("Random", GUILayout.Width(60f))) seed = Random.Range(0, int.MaxValue);
        }
        bool regenerate = EditorGUI.EndChangeCheck();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("View", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        landView = (LandView)EditorGUILayout.EnumPopup("Land", landView);
        using (new EditorGUILayout.HorizontalScope())
        {
            contours = EditorGUILayout.Toggle("Contours", contours);
            using (new EditorGUI.DisabledScope(!contours))
                contourInterval = Mathf.Max(1f, EditorGUILayout.FloatField(contourInterval, GUILayout.Width(50f)));
            GUILayout.Label("m", EditorStyles.miniLabel, GUILayout.Width(14f));
        }

        smoothPixels = EditorGUILayout.Toggle("Smooth pixels", smoothPixels);
        bool redraw = EditorGUI.EndChangeCheck();

        if (map != null && settings != null)
        {
            DrawLandLegend();
        }


        if (settings == null)
        {
            EditorGUILayout.HelpBox("Assign Level Settings to start.", MessageType.Info);
            if (GUILayout.Button("Create default settings")) CreateDefaultSettings();
            return;
        }

        if (map == null) 
            return;

        EditorGUILayout.Space();
        if (map.Failure != null)
        {
            EditorGUILayout.HelpBox(map.Failure + " Try another seed or loosen the settings.", MessageType.Warning);
            return;
        }
    }

    void DrawCanvas(Rect canvas)
    {
        EditorGUI.DrawRect(canvas, new Color(0.13f, 0.14f, 0.15f));
        if (map == null || texture == null) 
            return;

        // Fit the map into the canvas, keeping its proportions.
        const float pad = 12f;
        mapScale = Mathf.Min((canvas.width - pad * 2f) / map.Width, (canvas.height - pad * 2f) / map.Height);
        mapRect = new Rect(canvas.x + pad, canvas.y + pad, map.Width * mapScale, map.Height * mapScale);
        TrackHover();
        GUI.DrawTexture(mapRect, texture, ScaleMode.StretchToFill, false);

        if (Event.current.type != EventType.Repaint) 
            return;

        if (hoverCell.HasValue) 
            DrawHoverReport(hoverCell.Value);
    }

    Vector3 ToScreen(Vector2Int c) => new(mapRect.x + (c.x + 0.5f) * mapScale, mapRect.y + (c.y + 0.5f) * mapScale, 0f);

    /// <summary>Works out which cell the mouse is over, and repaints as it moves.</summary>
    void TrackHover()
    {
        var e = Event.current;
        if (e.type == EventType.MouseLeaveWindow && hoverCell != null)
        {
            hoverCell = null;
            Repaint();
            return;
        }
        if (e.type != EventType.MouseMove) return;

        Vector2Int? cell = null;
        if (mapRect.Contains(e.mousePosition))
        {
            var local = (e.mousePosition - mapRect.position) / mapScale;
            cell = new Vector2Int(Mathf.Clamp((int)local.x, 0, map.Width - 1), Mathf.Clamp((int)local.y, 0, map.Height - 1));
        }
        if (cell != hoverCell)
        {
            hoverCell = cell;
            Repaint();
        }
    }

    /// <summary>
    /// A box beside the mouse with the hovered cell's details, kept inside the map.
    /// </summary>
    void DrawHoverReport(Vector2Int cell)
    {
        var text = new GUIContent(CellReport(cell));
        var style = new GUIStyle(EditorStyles.helpBox) { fontSize = 11 };
        style.normal.textColor = Color.white;
        var size = style.CalcSize(text);
        var at = ToScreen(cell) + new Vector3(14f, 14f);
        var box = new Rect(Mathf.Min(at.x, mapRect.xMax - size.x), Mathf.Min(at.y, mapRect.yMax - size.y), size.x, size.y);

        Handles.color = Color.white;
        Handles.DrawWireCube(ToScreen(cell), new Vector3(mapScale, mapScale));
        EditorGUI.DrawRect(box, new Color(0.1f, 0.1f, 0.1f, 0.9f));
        GUI.Label(box, text, style);
    }

    // ---------- Default Settings ----------

    void CreateDefaultSettings()
    {
        const string parent = "Assets/Deadfreight/Data";
        if (!AssetDatabase.IsValidFolder(parent + "/LevelGen"))
            AssetDatabase.CreateFolder(parent, "LevelGen");

        var asset = CreateInstance<LevelSettings>();
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(DefaultSettingsPath));
        AssetDatabase.SaveAssets();
        settings = asset;
        Regenerate();
    }
}
