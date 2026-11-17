// Scripts/Runtime/RegionBuild/RegionBuilder.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Splines;
using Debug = UnityEngine.Debug;

/// <summary>
/// Builds a drivable greybox of a generated region in the scene: terrain, water, roads and
/// simple boxes for places. Generates the plan (RegionGenerator), then:
///
///   1. Heights from the plan's elevation, with bumps and the escarpment's cliff (HeightField),
///      then terraces on some hillsides (Terraces), then the shoreline made to match the map.
///   2. Rivers traced downstream (RiverNetwork), then one at a time smoothed, given a downhill
///      water surface, and cut into the ground (RiverShaper).
///   3. Roads split so none overlap (RoadNetwork), then one at a time smoothed, given a
///      grade-limited height, and pressed into the ground (RoadShaper).
///   4. Pads levelled under places.
///   5. Scene objects: terrain (painted by TerrainPaint, ground cover by GroundCoverPaint), lake
///      water, river water, a mesh per road, boxes for places.
///
/// Build from the inspector buttons, or on Start. Keep this object at the world origin.
/// Edit-mode builds aren't saved with the scene; they're rebuilt from the seed.
/// </summary>
public sealed class RegionBuilder : MonoBehaviour
{
    [SerializeField] RegionSettings regionSettings;
    [SerializeField] RegionBuildSettings buildSettings;
    [SerializeField] int seed = 1;
    [SerializeField] bool buildOnStart = true;
    [Tooltip("Optional: moved onto the road just inside the entry, facing inland (e.g. the wagon, for testing).")]
    [SerializeField] Transform placeAtEntry;

    const string RootName = "Generated Region";
    const float EntrySpawnDistance = 15f;   // metres along the road from the map edge
    const float BuildingGap = 3f;           // metres between a road's edge and a building

    GameObject root;
    readonly List<Object> createdAssets = new();    // meshes, materials, terrain data: destroyed on Clear
    Material plainWater;    // made once per build when no water material is assigned

    public RegionMap Map { get; private set; }
    public IReadOnlyList<RoadPath> Roads { get; private set; }
    public IReadOnlyList<RiverChannel> Rivers { get; private set; }
    public Pose EntryPose { get; private set; }

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
        if (regionSettings == null || buildSettings == null)
        {
            Debug.LogWarning("RegionBuilder needs Region Settings and Region Build Settings.", this);
            return;
        }

        var timer = Stopwatch.StartNew();
        Map = RegionGenerator.Generate(seed, regionSettings);
        if (Map.Failure != null)
        {
            Debug.LogWarning($"Region {seed} not built: {Map.Failure}", this);
            return;
        }

        // 1. Heights, then terraces, then the shoreline (last, so nothing after it moves the shore).
        var frame = new RegionFrame(Map);
        var field = HeightField.FromRegion(Map, frame, regionSettings, buildSettings, seed);
        Terraces.Apply(field, Map, frame, buildSettings, seed);
        float lakeLevel = regionSettings.seaLevel * regionSettings.relief;
        field.KeepShoreline(Map, frame, lakeLevel, buildSettings);

        // 2. Rivers, each cut in before the next is shaped, so tributaries meet them at their level.
        var rivers = new RiverShaper(field, frame, Map, buildSettings, lakeLevel);
        foreach (var path in RiverNetwork.Trace(Map, regionSettings.riverArea))
        {
            var river = rivers.Shape(path);
            rivers.Press(river);
        }
        Rivers = rivers.Built;

        // 3. Roads, in priority order, each pressed in before the next is shaped. Never below the lakes.
        var shaper = new RoadShaper(field, frame, buildSettings, lakeLevel + buildSettings.landAboveWater);
        foreach (var segment in RoadNetwork.Split(Map))
        {
            var road = shaper.Shape(segment);
            shaper.Press(road);
        }
        Roads = shaper.Built;

        // 4. Pads, after the roads so each takes the height of the road it sits on.
        foreach (var place in Map.Places)
            field.PressPad(frame.CellToWorld(place.Cell), buildSettings.padRadius, buildSettings.padBlend);

        // 5. Scene.
        root = new GameObject(RootName);
        root.transform.SetParent(transform, false);
        CreateTerrain(frame, field);
        CreateWater(frame, lakeLevel);
        foreach (var river in rivers.Built) CreateRiver(river);
        foreach (var road in shaper.Built) CreateRoad(road);
        foreach (var place in Map.Places) CreatePlace(place, frame, field, shaper);
        PlaceEntry(shaper.Built[0]);

        if (!Application.isPlaying) HideFromScene();
        Debug.Log($"Built region {seed} in {timer.ElapsedMilliseconds} ms: " +
                  $"{shaper.Built.Count} road segments, {rivers.Built.Count} rivers, {Map.Places.Count} places.", this);
    }

    public void Clear()
    {
        if (root != null) Discard(root);
        root = null;
        foreach (var asset in createdAssets)
            if (asset != null) Discard(asset);
        createdAssets.Clear();
        Map = null;
        Roads = null;
        Rivers = null;
        plainWater = null;

        // A build left over from before a script reload: the reference is gone, the object isn't.
        var leftover = transform.Find(RootName);
        if (leftover != null) Discard(leftover.gameObject);
    }

    // ---------- scene objects ----------

    void CreateTerrain(RegionFrame frame, HeightField field)
    {
        var heights = field.Normalized(out float bottom, out float span);

        var data = Track(new TerrainData());
        data.heightmapResolution = field.Resolution;    // before size: changing resolution rescales size
        data.size = new Vector3(frame.SizeX, span, frame.SizeZ);
        data.SetHeights(0, 0, heights);

        data.terrainLayers = TerrainLayers();
        data.alphamapResolution = buildSettings.alphamapResolution;
        var paint = TerrainPaint.Weights(Map, frame, field, buildSettings, seed);
        data.SetAlphamaps(0, 0, paint);

        // Ground cover grows where the paint says its ground is.
        float waterHeight = regionSettings.seaLevel * regionSettings.relief;
        GroundCoverPaint.Apply(data, paint, field, waterHeight, buildSettings, seed);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, bottom, 0f);
        AddSurface(go, buildSettings.offRoad);

        var terrain = go.GetComponent<Terrain>();
        if (buildSettings.terrainMaterial != null) terrain.materialTemplate = buildSettings.terrainMaterial;
        terrain.detailObjectDistance = buildSettings.detailDistance;
    }

    /// <summary>
    /// The four layers in TerrainPaint's order. Any role without a layer assigned gets a plain
    /// colour, so a missing slot is easy to spot.
    /// </summary>
    TerrainLayer[] TerrainLayers()
    {
        var layers = new TerrainLayer[TerrainPaint.LayerCount];
        layers[TerrainPaint.Grass] = buildSettings.grassLayer != null ? buildSettings.grassLayer : PlainLayer(new Color(0.42f, 0.50f, 0.30f));
        layers[TerrainPaint.Dirt] = buildSettings.dirtLayer != null ? buildSettings.dirtLayer : PlainLayer(new Color(0.30f, 0.25f, 0.18f));
        layers[TerrainPaint.Stony] = buildSettings.stonyLayer != null ? buildSettings.stonyLayer : PlainLayer(new Color(0.52f, 0.49f, 0.44f));
        layers[TerrainPaint.Rock] = buildSettings.rockLayer != null ? buildSettings.rockLayer : PlainLayer(new Color(0.40f, 0.39f, 0.37f));
        return layers;
    }

    /// <summary>
    /// Lakes: one flat plane at lake level. The terrain hides it everywhere the map says land
    /// (KeepShoreline holds land above it); rivers have their own water (CreateRiver).
    /// </summary>
    void CreateWater(RegionFrame frame, float lakeLevel)
    {
        var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
        water.name = "Lake Water";
        Discard(water.GetComponent<Collider>());     // vehicles and players sink, not float
        water.transform.SetParent(root.transform, false);

        // Unity's plane is 10 × 10 units.
        water.transform.localPosition = new Vector3(frame.SizeX * 0.5f, lakeLevel, frame.SizeZ * 0.5f);
        water.transform.localScale = new Vector3(frame.SizeX / 10f, 1f, frame.SizeZ / 10f);
        water.GetComponent<MeshRenderer>().sharedMaterial = WaterMaterial();
    }

    /// <summary>A river's water strip, following its channel downhill. No collider: wheels and feet reach the bed.</summary>
    void CreateRiver(RiverChannel river)
    {
        var go = new GameObject("River");
        go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = Track(RiverMesh.Build(river));
        go.AddComponent<MeshRenderer>().sharedMaterial =
            buildSettings.riverMaterial != null ? buildSettings.riverMaterial : WaterMaterial();
    }

    Material WaterMaterial()
    {
        if (buildSettings.waterMaterial != null) return buildSettings.waterMaterial;
        if (plainWater == null) plainWater = PlainMaterial(new Color(0.18f, 0.30f, 0.40f));
        return plainWater;
    }

    void CreateRoad(RoadPath road)
    {
        var go = new GameObject($"{road.Class} road");
        go.transform.SetParent(root.transform, false);

        var mesh = Track(RoadMesh.Build(road, buildSettings.surfaceLift));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = RoadMaterial(road.Class);
        go.AddComponent<MeshCollider>().sharedMesh = mesh;
        AddSurface(go, buildSettings.RoadFeel(road.Class));

        // The centre line, for inspecting in the scene and for later tools (fences, signs).
        go.AddComponent<SplineContainer>().Spline = road.Spline;
    }

    Material RoadMaterial(RoadClass roadClass)
    {
        var assigned = roadClass switch
        {
            RoadClass.Main => buildSettings.mainRoadMaterial,
            RoadClass.Back => buildSettings.backRoadMaterial,
            _ => buildSettings.laneMaterial,
        };
        if (assigned != null) return assigned;

        return PlainMaterial(roadClass switch
        {
            RoadClass.Main => new Color(0.62f, 0.58f, 0.50f),
            RoadClass.Back => new Color(0.50f, 0.42f, 0.32f),
            _ => new Color(0.44f, 0.38f, 0.30f),
        });
    }

    /// <summary>A box for each place, beside the nearest road and facing it, on the place's pad.</summary>
    void CreatePlace(Place place, RegionFrame frame, HeightField field, RoadShaper shaper)
    {
        bool isSite = place.Kind == PlaceKind.Site;
        var size = isSite ? buildSettings.siteSize : buildSettings.buildingSize;
        var centre = frame.CellToWorld(place.Cell);

        var position = new Vector3(centre.x, 0f, centre.y);
        var facing = Vector3.forward;
        if (shaper.NearestRoad(centre, out var roadPoint, out var direction, out float halfWidth))
        {
            // Step off the road sideways, onto whichever side the place's cell lies.
            var side = new Vector3(direction.z, 0f, -direction.x);
            var toPlace = new Vector3(centre.x - roadPoint.x, 0f, centre.y - roadPoint.z);
            if (Vector3.Dot(side, toPlace) < 0f) side = -side;

            float offset = halfWidth + BuildingGap + size.z * 0.5f;
            position = new Vector3(roadPoint.x, 0f, roadPoint.z) + side * offset;
            facing = -side;
        }
        position.y = field.HeightAt(new Vector2(position.x, position.z)) + size.y * 0.5f;

        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = place.Kind.ToString();
        box.transform.SetParent(root.transform, false);
        box.transform.SetPositionAndRotation(position, Quaternion.LookRotation(facing));
        box.transform.localScale = size;

        var assigned = isSite ? buildSettings.siteMaterial : buildSettings.buildingMaterial;
        box.GetComponent<MeshRenderer>().sharedMaterial = assigned != null
            ? assigned
            : PlainMaterial(isSite ? new Color(0.55f, 0.20f, 0.18f) : new Color(0.55f, 0.52f, 0.48f));
    }

    /// <summary>A spot on the main road just inside the entry, facing inland. Moves placeAtEntry there.</summary>
    void PlaceEntry(RoadPath mainRoad)
    {
        var points = mainRoad.Points;
        int index = Mathf.Min(points.Count - 2, Mathf.RoundToInt(EntrySpawnDistance / buildSettings.sampleSpacing));
        var ahead = points[index + 1] - points[index];
        ahead.y = 0f;

        EntryPose = new Pose(points[index] + Vector3.up * 1f, Quaternion.LookRotation(ahead.normalized));
        if (placeAtEntry != null) placeAtEntry.SetPositionAndRotation(EntryPose.position, EntryPose.rotation);
    }

    // ---------- helpers ----------

    void AddSurface(GameObject go, SurfaceFeel feel) =>
        go.AddComponent<GroundSurface>().Configure(feel.rollingResistance, feel.grip);

    Material PlainMaterial(Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogWarning("URP Lit shader not found; assign materials in Region Build Settings.", this);
            shader = Shader.Find("Hidden/InternalErrorShader");
        }
        return Track(new Material(shader) { color = color });
    }

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

    /// <summary>Edit-mode builds stay out of the saved scene; they would only store stale data.</summary>
    void HideFromScene()
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        foreach (var asset in createdAssets) asset.hideFlags = HideFlags.DontSave;
    }

    /// <summary>Destroy at end of frame in play mode, at once in the editor (where Destroy isn't allowed).</summary>
    static void Discard(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
