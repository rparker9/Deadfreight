// Scripts/Editor/RegionGen/RegionPreviewWindow.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Top-down preview of a generated region: land, water, forest, fog, roads and places; the
/// entry, trunk and fork; and each route from entry to site with its length, driving time and
/// conditions. Change the seed or edit the settings asset and it regenerates.
/// </summary>
public sealed class RegionPreviewWindow : EditorWindow
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

    RegionMap map;
    Texture2D texture;
    long generateMs;
    string settingsSnapshot;
    Vector2 scroll;

    [MenuItem("Tools/Deadfreight/Region Preview")]
    static void Open() => GetWindow<RegionPreviewWindow>("Region Preview");

    void OnEnable() => Regenerate();

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
        BuildTexture();
    }

    void BuildTexture()
    {
        if (texture == null || texture.width != map.Width || texture.height != map.Height)
        {
            if (texture != null) DestroyImmediate(texture);
            texture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
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
        texture.SetPixels(pixels);
        texture.Apply();
    }

    Color CellColor(int i)
    {
        float e = map.Elevation[i];
        var c = new Color(0.62f, 0.66f, 0.55f) * (0.35f + 0.55f * e);
        if (map.Forest[i]) c = Color.Lerp(c, new Color(0.12f, 0.28f, 0.14f), 0.4f);
        if (map.Marsh[i]) c = new Color(0.35f, 0.42f, 0.33f);
        if (map.Hollow[i]) c = Color.Lerp(c, new Color(0.75f, 0.8f, 0.8f), 0.25f);
        if (map.Water[i] || map.River[i]) c = new Color(0.25f, 0.4f, 0.55f);
        if (map.Road[i] && (showLanes || map.MainRoad[i] || OnAnyRoute(i))) c = new Color(0.82f, 0.78f, 0.68f);
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

        EditorGUI.BeginChangeCheck();
        showPlaceLabels = EditorGUILayout.Toggle("Place labels", showPlaceLabels);
        showLanes = EditorGUILayout.Toggle("Lanes to stops", showLanes);
        bool redraw = EditorGUI.EndChangeCheck();

        if (regenerate) { routeCells = null; Regenerate(); }
        else if (redraw && map != null) { routeCells = null; BuildTexture(); }

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
            $"{map.Routes.Count - 1} alternates · {map.RejectedAlternates} tries rejected for time",
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
        float scale = Mathf.Min((canvas.width - pad * 2f) / map.Width, (canvas.height - pad * 2f) / map.Height);
        var rect = new Rect(canvas.x + pad, canvas.y + pad, map.Width * scale, map.Height * scale);
        GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);

        if (Event.current.type != EventType.Repaint) return;
        Vector3 ToScreen(Vector2Int c) => new(rect.x + (c.x + 0.5f) * scale, rect.y + (c.y + 0.5f) * scale, 0f);

        // Routes, main road on top.
        for (int k = map.Routes.Count - 1; k >= 0; k--)
        {
            if (!routeVisible[k]) continue;
            var cells = map.Routes[k].Cells;
            var points = new Vector3[cells.Count];
            for (int j = 0; j < cells.Count; j++) points[j] = ToScreen(cells[j]);
            Handles.color = RouteColors[k % RouteColors.Length];
            Handles.DrawAAPolyLine(k == 0 ? 6f : 4f, points);
        }

        if (map.Trunk != null) DrawEntryTrunkAndFork(ToScreen);

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
            "Blue = water · dark green = forest · grey-green = marsh · pale = fog hollows · tan = road · " +
            "green arrow = entry · white = trunk · square = fork · red dot = site", legend);
    }

    /// <summary>The trunk as a white line, an arrow coming in from off the map, and a square at the fork.</summary>
    void DrawEntryTrunkAndFork(System.Func<Vector2Int, Vector3> toScreen)
    {
        var trunk = map.Trunk.Cells;
        var points = new Vector3[trunk.Count];
        for (int j = 0; j < trunk.Count; j++) points[j] = toScreen(trunk[j]);
        Handles.color = Color.white;
        Handles.DrawAAPolyLine(7f, points);

        // Arrow: tip on the entry cell, base just outside the map, pointing inland.
        Vector3 inward = new(map.Entry.Inward.x, map.Entry.Inward.y, 0f);
        Vector3 across = new(-inward.y, inward.x, 0f);
        Vector3 tip = toScreen(map.Entry.Cell);
        Vector3 back = tip - inward * 16f;
        var arrow = new[] { tip, back + across * 8f, back - across * 8f };
        Handles.color = Color.black;
        Handles.DrawAAConvexPolygon(Grow(arrow, 1.5f));
        Handles.color = EntryColor;
        Handles.DrawAAConvexPolygon(arrow);

        var fork = toScreen(map.Fork);
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
