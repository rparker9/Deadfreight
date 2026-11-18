// Scripts/Editor/RegionGen/RegionPreviewWindow.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Top-down preview of a generated region: land, water, forest, fog, roads and places; the
/// entry, trunk and fork; and each route from entry to site with its length, driving time and
/// conditions. Change the seed or edit the settings asset and it regenerates.
///
/// The land and the roads can each be coloured several ways (LandView, RoadView; colours and
/// legends in RegionPreviewWindow.Views.cs), and hovering a cell shows everything about it.
/// </summary>
public sealed partial class RegionPreviewWindow : EditorWindow
{
    const float PanelWidth = 320f;
    const string DefaultSettingsPath = "Assets/Deadfreight/Data/RegionGen/DefaultRegionSettings.asset";

    static readonly Color EntryColor = new(0.3f, 0.9f, 0.3f);
    static readonly Color SiteColor = new(0.95f, 0.3f, 0.3f);

    static readonly Color[] RouteColors =
    {
        new(0.90f, 0.27f, 0.27f), new(0.27f, 0.78f, 0.94f), new(0.98f, 0.78f, 0.24f),
        new(0.70f, 0.47f, 1.00f), new(0.40f, 0.95f, 0.50f), new(1.00f, 0.55f, 0.80f), new(0.95f, 0.95f, 0.95f),
    };

    [SerializeField] RegionSettings settings;
    [SerializeField] int seed = 1;
    [SerializeField] bool showPlaceLabels = true;
    [SerializeField] bool showLanes = true;
    [SerializeField] List<bool> routeVisible = new();

    [SerializeField] LandView landView = LandView.Overview;
    [SerializeField] RoadView roadView = RoadView.Plain;
    [SerializeField] bool contours;
    [SerializeField] float contourInterval = 10f;
    [SerializeField] bool smoothPixels;

    RegionMap map;
    // Worked out once per generation, for the views.
    float maxDrainage = 1f, maxRoadCost = 2f;

    // Where the map was last drawn, and the cell under the mouse (if any).
    Rect mapRect;
    float mapScale;
    Vector2Int? hoverCell;
    Texture2D texture;
    long generateMs;
    string settingsSnapshot;
    Vector2 scroll;

    [MenuItem("Tools/Deadfreight/Region Preview")]
    static void Open() => GetWindow<RegionPreviewWindow>("Region Preview");

    void OnEnable()
    {
        wantsMouseMove = true;
        Regenerate();
    }

    void OnDisable()
    {
        if (texture != null) DestroyImmediate(texture);
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

    // ---------- generation ----------

    void Regenerate()
    {
        map = null;
        if (settings == null) return;

        var timer = Stopwatch.StartNew();
        map = RegionGenerator.Generate(seed, settings);
        generateMs = timer.ElapsedMilliseconds;

        while (routeVisible.Count < map.Routes.Count) routeVisible.Add(true);
        routeCells = null;
        MeasureForViews();
        BuildTexture();
    }

    /// <summary>The things the views need that the map doesn't hold directly.</summary>
    void MeasureForViews()
    {
        maxDrainage = 1f;
        maxRoadCost = 2f;
        for (int i = 0; i < map.Elevation.Length; i++)
        {
            maxDrainage = Mathf.Max(maxDrainage, map.Drainage[i]);
            if (map.RoadCost != null && !map.Water[i]) maxRoadCost = Mathf.Max(maxRoadCost, map.RoadCost[i]);
        }
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

    /// <summary>A cell's colour: the land view, darkened on contour lines, with roads on top.</summary>
    Color CellColor(int i)
    {
        var c = LandColour(i);
        if (contours && contourInterval > 0f && OnContour(i)) c *= 1f - ContourDarken;
        if (roadView != RoadView.Hidden && map.Road[i] && (showLanes || map.MainRoad[i] || OnAnyRoute(i))) c = RoadCellColour(i);
        c.a = 1f;
        return c;
    }

    HashSet<int> routeCells;
    bool OnAnyRoute(int i)
    {
        if (routeCells == null)
        {
            routeCells = new HashSet<int>();
            foreach (var r in map.Routes)
                foreach (var c in r.Cells) routeCells.Add(map.Index(c));
            if (map.Trunk != null)
                foreach (var c in map.Trunk.Cells) routeCells.Add(map.Index(c));
        }
        return routeCells.Contains(i);
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
        EditorGUILayout.LabelField("Region", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        settings = (RegionSettings)EditorGUILayout.ObjectField("Settings", settings, typeof(RegionSettings), false);
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
        roadView = (RoadView)EditorGUILayout.EnumPopup("Roads", roadView);
        showLanes = EditorGUILayout.Toggle("Lanes to stops", showLanes);
        showPlaceLabels = EditorGUILayout.Toggle("Place labels", showPlaceLabels);
        smoothPixels = EditorGUILayout.Toggle("Smooth pixels", smoothPixels);
        bool redraw = EditorGUI.EndChangeCheck();

        if (regenerate) { routeCells = null; Regenerate(); }
        else if (redraw && map != null) { routeCells = null; BuildTexture(); }

        if (map != null && settings != null)
        {
            DrawLandLegend();
            if (roadView != RoadView.Plain && roadView != RoadView.Hidden) DrawRoadLegend();
        }
        if (landView == LandView.Biomes && map != null && GUILayout.Button("Redraw (after editing a biome's colour)")) BuildTexture();

        if (settings == null)
        {
            EditorGUILayout.HelpBox("Assign Region Settings to start.", MessageType.Info);
            if (GUILayout.Button("Create default settings")) CreateDefaultSettings();
            return;
        }
        if (map == null) return;

        EditorGUILayout.Space();
        float km = map.Width * map.CellSize / 1000f, kmTall = map.Height * map.CellSize / 1000f;
        EditorGUILayout.LabelField($"{km:0.#} × {kmTall:0.#} km · {map.Places.Count} places · generated in {generateMs} ms",
            EditorStyles.miniLabel);

        if (map.Failure != null)
        {
            EditorGUILayout.HelpBox(map.Failure + " Try another seed or loosen the settings.", MessageType.Warning);
            return;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Entry on the {map.Entry.Edge} edge", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"Trunk to the fork: {map.Trunk.LegMiles:0.00} mi · {map.Trunk.LegMinutes:0.0} min each way",
            EditorStyles.miniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Routes, entry → site (trunk included)", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(
            $"{map.Routes.Count - 1} alternates · {map.RejectedAlternates} tries rejected for time" +
            (map.HasEscarpment ? $" · escarpment {map.ScarpRise:0} m, {map.Cuttings} cut cells" : ""),
            EditorStyles.miniLabel);

        float mainMinutes = map.Routes[0].OneWayMinutes;
        for (int k = 0; k < map.Routes.Count; k++)
        {
            var r = map.Routes[k];
            var color = RouteColors[k % RouteColors.Length];

            using (new EditorGUILayout.HorizontalScope())
            {
                routeVisible[k] = EditorGUILayout.Toggle(routeVisible[k], GUILayout.Width(16f));
                var style = new GUIStyle(EditorStyles.boldLabel);
                style.normal.textColor = color;
                string via = r.Via != null ? $" via {r.Via.Kind}" : "";
                EditorGUILayout.LabelField(r.Name + via, style);
            }

            string ratio = k == 0 ? "" : $" · {r.OneWayMinutes / mainMinutes:0.0}× main";
            EditorGUILayout.LabelField(
                $"{r.OneWayMiles:0.0} mi · ~{r.OneWayMinutes:0} min one way · ~{r.RoundTripMinutes:0} min round trip{ratio}",
                EditorStyles.miniLabel);
            EditorGUILayout.LabelField(Conditions(r), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField(
            "Times are driving only, from the speed settings, and include the trunk; round trip uses the " +
            "same route both ways. Conditions are the share of each route past the fork that runs through " +
            "mud, fords, steep grades, forest and fog.", EditorStyles.wordWrappedMiniLabel);
    }

    static string Conditions(Route r)
    {
        var parts = new List<string>();
        foreach (var pair in r.Share)
            if (pair.Value >= 0.03f) parts.Add($"{pair.Key} {pair.Value * 100f:0}%");
        return parts.Count == 0 ? "clear" : string.Join(" · ", parts);
    }

    void DrawCanvas(Rect canvas)
    {
        EditorGUI.DrawRect(canvas, new Color(0.13f, 0.14f, 0.15f));
        if (map == null || texture == null) return;

        // Fit the map into the canvas, keeping its proportions.
        const float pad = 12f;
        mapScale = Mathf.Min((canvas.width - pad * 2f) / map.Width, (canvas.height - pad * 2f) / map.Height);
        mapRect = new Rect(canvas.x + pad, canvas.y + pad, map.Width * mapScale, map.Height * mapScale);
        TrackHover();
        GUI.DrawTexture(mapRect, texture, ScaleMode.StretchToFill, false);

        if (Event.current.type != EventType.Repaint) return;

        if (roadView != RoadView.Hidden)
        {
            // Routes, main road on top.
            for (int k = map.Routes.Count - 1; k >= 0; k--)
                if (routeVisible[k]) DrawRoute(map.Routes[k].Cells, k == 0 ? 6f : 4f, RouteColors[k % RouteColors.Length]);
            if (map.Trunk != null) DrawRoute(map.Trunk.Cells, 7f, Color.white);
        }
        if (map.Trunk != null) DrawEntryAndFork();

        // Places.
        var label = new GUIStyle(EditorStyles.miniBoldLabel);
        label.normal.textColor = Color.white;
        foreach (var p in map.Places)
        {
            var s = ToScreen(p.Cell);
            Handles.color = Color.black;
            Handles.DrawSolidDisc(s, Vector3.forward, 6f);
            Handles.color = p.Kind == PlaceKind.Site ? SiteColor : Color.white;
            Handles.DrawSolidDisc(s, Vector3.forward, 4.5f);
            if (showPlaceLabels)
                GUI.Label(new Rect(s.x + 7f, s.y - 8f, 120f, 16f), p.Kind.ToString(), label);
        }

        var legend = new GUIStyle(EditorStyles.miniLabel);
        legend.normal.textColor = new Color(0.75f, 0.75f, 0.75f);
        GUI.Label(new Rect(canvas.x + 8f, canvas.yMax - 18f, canvas.width - 16f, 16f),
            "Green arrow = entry · white = trunk · square = fork · red dot = site · hover a cell for details", legend);

        if (hoverCell.HasValue) DrawHoverReport(hoverCell.Value);
    }

    Vector3 ToScreen(Vector2Int c) => new(mapRect.x + (c.x + 0.5f) * mapScale, mapRect.y + (c.y + 0.5f) * mapScale, 0f);

    /// <summary>
    /// A route as a line. In the Plain view, all in its own colour; in the others, each step in
    /// the colour of the cell it steps onto, over a dark outline so it stands out from the land.
    /// </summary>
    void DrawRoute(List<Vector2Int> cells, float width, Color plainColour)
    {
        var points = new Vector3[cells.Count];
        for (int j = 0; j < cells.Count; j++) points[j] = ToScreen(cells[j]);

        if (roadView == RoadView.Plain)
        {
            Handles.color = plainColour;
            Handles.DrawAAPolyLine(width, points);
            return;
        }

        Handles.color = Color.black;
        Handles.DrawAAPolyLine(width + 2f, points);
        for (int j = 1; j < points.Length; j++)
        {
            Handles.color = RoadCellColour(map.Index(cells[j]));
            Handles.DrawAAPolyLine(width, points[j - 1], points[j]);
        }
    }

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

    /// <summary>A box beside the mouse with the hovered cell's details, kept inside the map.</summary>
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

    /// <summary>An arrow coming in from off the map at the entry, and a square at the fork.</summary>
    void DrawEntryAndFork()
    {
        // Arrow: tip on the entry cell, base just outside the map, pointing inland.
        Vector3 inward = new(map.Entry.Inward.x, map.Entry.Inward.y, 0f);
        Vector3 across = new(-inward.y, inward.x, 0f);
        Vector3 tip = ToScreen(map.Entry.Cell);
        Vector3 back = tip - inward * 16f;
        var arrow = new[] { tip, back + across * 8f, back - across * 8f };
        Handles.color = Color.black;
        Handles.DrawAAConvexPolygon(Grow(arrow, 1.5f));
        Handles.color = EntryColor;
        Handles.DrawAAConvexPolygon(arrow);

        var fork = ToScreen(map.Fork);
        Handles.DrawSolidRectangleWithOutline(new Rect(fork.x - 6f, fork.y - 6f, 12f, 12f), Color.white, Color.black);
    }

    /// <summary>A triangle pushed out from its centre, for an outline behind it.</summary>
    static Vector3[] Grow(Vector3[] triangle, float amount)
    {
        var centre = (triangle[0] + triangle[1] + triangle[2]) / 3f;
        var grown = new Vector3[triangle.Length];
        for (int j = 0; j < triangle.Length; j++)
            grown[j] = triangle[j] + (triangle[j] - centre).normalized * amount;
        return grown;
    }

    // ---------- default settings ----------

    void CreateDefaultSettings()
    {
        const string parent = "Assets/Deadfreight/Data";
        if (!AssetDatabase.IsValidFolder(parent + "/RegionGen"))
            AssetDatabase.CreateFolder(parent, "RegionGen");

        var asset = CreateInstance<RegionSettings>();
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(DefaultSettingsPath));
        AssetDatabase.SaveAssets();
        settings = asset;
        Regenerate();
    }
}
