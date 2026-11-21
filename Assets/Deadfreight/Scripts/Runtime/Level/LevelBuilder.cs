// Scripts/Runtime/LevelBuild/LevelBuilder.cs
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;
using Debug = UnityEngine.Debug;

/// <summary>
/// Generates a plan from the seed and region, and builds it in the scene: the terrain, painted
/// with the region's zones and carved for the roads, the roads, the water surface, a
/// placeholder box at each place, and the region's atmosphere handed to the scene's
/// DayNightCycle. The player spawn and the vehicle go to the start.
/// </summary>
public sealed class LevelBuilder : MonoBehaviour
{
    [FormerlySerializedAs("levelSettings")]
    [SerializeField] PlanSettings planSettings;
    [SerializeField] BuildSettings buildSettings;
    [Tooltip("The level's region. The lobby sets it from the chosen heist; set here for testing.")]
    [SerializeField] RegionProfile region;
    [SerializeField] Transform playerSpawn;
    [Tooltip("The vehicle the run starts with. Moved to the start, pulled over beside the road, when the level is built.")]
    [SerializeField] Transform vehicle;
    [SerializeField] int seed = 1;
    [SerializeField] bool buildOnStart = true;

    const string RootName = "Generated Level";
    static readonly Color DefaultGround = new(0.42f, 0.50f, 0.30f);    // for a region with no zones
    static readonly Color DefaultWater = new(0.10f, 0.20f, 0.20f);     // for a region with no water material
    static readonly Color RoadColor = new(0.45f, 0.38f, 0.30f);        // for no road material: packed dirt
    const float MainRoadLift = 0.15f;   // metres the main road's mesh sits above the carved ground
    const float BranchLift = 0.10f;     // ...and branches', a little lower
    const int VehicleAhead = 5;         // road samples (2 m each) the vehicle waits ahead of the player
    const float VehicleSideGap = 2.5f;  // metres from the road's edge to the vehicle's centre: on the carved flat
    const float VehicleDrop = 0.5f;     // metres above the ground it starts, to settle onto its wheels

    GameObject root;
    readonly List<Object> createdAssets = new();    // meshes, materials, terrain data: destroyed on Clear

    public LevelPlan Plan { get; private set; }

    public int Seed
    {
        get => seed;
        set => seed = value;
    }

    public RegionProfile Region
    {
        get => region;
        set => region = value;
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

        if (planSettings == null || buildSettings == null || region == null)
        {
            Debug.LogWarning("LevelBuilder needs Plan Settings, Build Settings and a Region.", this);
            return;
        }

        var timer = Stopwatch.StartNew();
        long last = 0;
        long Lap()      // milliseconds since the last lap
        {
            long now = timer.ElapsedMilliseconds, lap = now - last;
            last = now;
            return lap;
        }

        Plan = LevelGenerator.Generate(seed, planSettings, region);
        long planMs = Lap();
        if (Plan.Failure != null)
        {
            Debug.LogWarning($"Level {seed} not built: {Plan.Failure}", this);
            return;
        }

        // The plan's ground heights → the terrain's, at its resolution.
        PlanToWorld planToWorld = new PlanToWorld(Plan);
        TerrainHeights heights = TerrainHeights.FromPlan(Plan, planToWorld, buildSettings);
        long heightsMs = Lap();

        // The roads' curves, and the ground carved to them before the terrain is made.
        var roads = ShapeRoads();
        CarveRoads(heights, roads);
        long roadsMs = Lap();

        // Create the root object and the terrain in the scene.
        root = new GameObject(RootName);
        root.transform.SetParent(transform, false);
        CreateTerrain(planToWorld, heights);
        long terrainMs = Lap();

        CreateRoads(roads);
        roadsMs += Lap();

        CreateWater(planToWorld);
        CreatePlaceMarkers();
        ApplyAtmosphere();
        int start = NearestPoint(roads[0], Plan.WorldXZ(Plan.Start.Cell));
        PlaceSpawn(roads[0], start);
        PlaceVehicle(roads[0], start);

        // Remove any leftover objects from a previous build, and hide all generated objects from the scene so they don't get saved.
        if (!Application.isPlaying) 
            HideFromScene();
        long restMs = Lap();

        Debug.Log($"Built level {seed} ({region.name}) in {timer.ElapsedMilliseconds * 0.001f:F2}s: " +
                  $"plan {planMs} ms, heights {heightsMs} ms, roads {roadsMs} ms, terrain {terrainMs} ms, " +
                  $"rest {restMs} ms.", this);
    }

    public void Clear()
    {
        if (root != null) 
            Discard(root);
        root = null;

        foreach (var asset in createdAssets)
            if (asset != null) 
                Discard(asset);
        
        createdAssets.Clear();
        Plan = null;

        // A build left over from before a script reload: the reference is gone, the object isn't.
        var leftover = transform.Find(RootName);
        if (leftover != null) Discard(leftover.gameObject);
    }

    // ---------- Scene Objects ----------

    /// <summary>The terrain from the heights, painted with the zones.</summary>
    void CreateTerrain(PlanToWorld planToWorld, TerrainHeights heights)
    {
        var normalized = heights.Normalized(out float bottom, out float span);

        var data = Track(new TerrainData());
        data.heightmapResolution = heights.Resolution;    // before size: changing resolution rescales size
        data.size = new Vector3(planToWorld.SizeX, span, planToWorld.SizeZ);
        data.SetHeights(0, 0, normalized);
        PaintZones(data, planToWorld);

        var go = Terrain.CreateTerrainGameObject(data);
        go.name = "Terrain";
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, bottom, 0f);

        var terrain = go.GetComponent<Terrain>();
        if (buildSettings.terrainMaterial != null) terrain.materialTemplate = buildSettings.terrainMaterial;
    }

    /// <summary>
    /// One terrain layer per zone (its Ground Layer, or a plain layer in its preview colour),
    /// each painted where its zone is. Weights blend between neighbouring cells, so zone edges
    /// fade over one cell rather than stepping.
    /// </summary>
    void PaintZones(TerrainData data, PlanToWorld planToWorld)
    {
        var zones = region.zones;
        int layerCount = Mathf.Max(1, zones.Count);
        var layers = new TerrainLayer[layerCount];
        for (int z = 0; z < layerCount; z++)
        {
            bool defined = z < zones.Count;
            layers[z] = defined && zones[z].groundLayer != null
                ? zones[z].groundLayer
                : PlainLayer(defined ? zones[z].previewColor : DefaultGround);
        }
        data.terrainLayers = layers;

        int resolution = buildSettings.alphamapResolution;
        data.alphamapResolution = resolution;
        var weights = new float[resolution, resolution, layerCount];     // [row (z), column (x), layer]
        for (int row = 0; row < resolution; row++)
        for (int column = 0; column < resolution; column++)
        {
            var world = new Vector2(column * planToWorld.SizeX / (resolution - 1), row * planToWorld.SizeZ / (resolution - 1));
            AddZoneWeights(weights, row, column, planToWorld.WorldToCell(world));
        }
        data.SetAlphamaps(0, 0, weights);
    }

    /// <summary>Splits a texel's weight between the zones of the four cells around it, by how near each is.</summary>
    void AddZoneWeights(float[,,] weights, int row, int column, Vector2 cell)
    {
        int x0 = Mathf.Clamp((int)cell.x, 0, Plan.CellsX - 2), y0 = Mathf.Clamp((int)cell.y, 0, Plan.CellsY - 2);
        float tx = Mathf.Clamp01(cell.x - x0), ty = Mathf.Clamp01(cell.y - y0);

        weights[row, column, Plan.Zone[Plan.Index(new Vector2Int(x0, y0))]] += (1f - tx) * (1f - ty);
        weights[row, column, Plan.Zone[Plan.Index(new Vector2Int(x0 + 1, y0))]] += tx * (1f - ty);
        weights[row, column, Plan.Zone[Plan.Index(new Vector2Int(x0, y0 + 1))]] += (1f - tx) * ty;
        weights[row, column, Plan.Zone[Plan.Index(new Vector2Int(x0 + 1, y0 + 1))]] += tx * ty;
    }

    /// <summary>
    /// One flat surface over the whole level at the water line. The terrain hides it wherever the
    /// ground is above it, so it shows only over ground below the line. No collider yet: things
    /// sink to the bottom.
    /// </summary>
    void CreateWater(PlanToWorld planToWorld)
    {
        if (!Plan.HasWater)
            return;

        float h = Plan.WaterHeight, x = planToWorld.SizeX, z = planToWorld.SizeZ;
        var mesh = Track(new Mesh { name = "Water" });
        mesh.vertices = new[] { new Vector3(0f, h, 0f), new Vector3(x, h, 0f), new Vector3(0f, h, z), new Vector3(x, h, z) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };        // clockwise seen from above: faces up
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Water", typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(root.transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        go.GetComponent<MeshRenderer>().sharedMaterial = region.waterMaterial;
    }

    // ---------- Roads ----------

    List<RoadShape> ShapeRoads()
    {
        var shapes = new List<RoadShape>(Plan.Roads.Count);
        foreach (var road in Plan.Roads)
            shapes.Add(RoadShape.FromRoad(road, Plan));
        return shapes;
    }

    /// <summary>
    /// Flattens the ground under each road and eases it back to the land beside it. The flat
    /// part reaches one heightmap sample past the road's edge, so the terrain between samples
    /// can't rise through the road surface.
    /// </summary>
    void CarveRoads(TerrainHeights heights, List<RoadShape> roads)
    {
        float flatHalfWidth = buildSettings.roadWidth * 0.5f + Mathf.Max(heights.StepX, heights.StepZ);
        heights.CarveRoads(roads, flatHalfWidth, buildSettings.roadShoulder);
    }

    /// <summary>
    /// A mesh strip along each road, with a collider so wheels ride on it. Branches sit a
    /// little lower than the main road, so where they overlap at a junction the main road shows.
    /// </summary>
    void CreateRoads(List<RoadShape> roads)
    {
        var parent = new GameObject("Roads").transform;
        parent.SetParent(root.transform, false);
        var material = buildSettings.roadMaterial != null ? buildSettings.roadMaterial : PlainMaterial("Road", RoadColor);

        foreach (var road in roads)
        {
            var mesh = Track(road.ToMesh(buildSettings.roadWidth, road.Kind == RoadKind.Main ? MainRoadLift : BranchLift));
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
        }
    }

    // ---------- Places ----------

    /// <summary>
    /// Stand-ins until places have real buildings: a box at each place, sized and coloured by
    /// kind, so they can be found and driven to.
    /// </summary>
    void CreatePlaceMarkers()
    {
        var parent = new GameObject("Places").transform;
        parent.SetParent(root.transform, false);

        foreach (var place in Plan.Places)
        {
            var (size, color) = place.Kind switch
            {
                PlaceKind.Start => (new Vector3(6f, 4f, 10f), new Color(0.3f, 0.8f, 0.3f)),
                PlaceKind.Site => (new Vector3(24f, 12f, 24f), new Color(0.8f, 0.2f, 0.2f)),
                PlaceKind.Hamlet => (new Vector3(14f, 7f, 14f), new Color(0.6f, 0.45f, 0.9f)),
                _ => (new Vector3(8f, 5f, 10f), new Color(0.75f, 0.72f, 0.65f)),
            };

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = place.Kind.ToString();
            box.transform.SetParent(parent, false);
            box.transform.localPosition = GroundAt(place.Cell) + Vector3.up * (size.y * 0.5f);
            box.transform.localScale = size;
            box.GetComponent<MeshRenderer>().sharedMaterial = PlainMaterial("Place Marker", color);
        }
    }

    /// <summary>
    /// The player spawn goes on the main road at the start place, just above the surface, facing
    /// along the road inland (away from the map edge, where the road starts).
    /// </summary>
    void PlaceSpawn(RoadShape mainRoad, int start)
    {
        if (playerSpawn == null)
            return;

        playerSpawn.position = transform.TransformPoint(mainRoad.Points[start] + Vector3.up);
        playerSpawn.rotation = transform.rotation * Quaternion.LookRotation(Forward(mainRoad, start));
    }

    /// <summary>
    /// The vehicle waits a little ahead of the player, pulled over on the right of the main road
    /// and facing along it. The ground there is carved flat at the road's height (see
    /// CarveRoads), so it doesn't tip. In play mode it spawns on the network from here when
    /// the host starts.
    /// </summary>
    void PlaceVehicle(RoadShape mainRoad, int start)
    {
        if (vehicle == null)
            return;

        int i = Mathf.Min(start + VehicleAhead, mainRoad.Points.Length - 1);
        var forward = Forward(mainRoad, i);
        var right = new Vector3(forward.z, 0f, -forward.x);
        var spot = mainRoad.Points[i] + right * (buildSettings.roadWidth * 0.5f + VehicleSideGap) + Vector3.up * VehicleDrop;

        var position = transform.TransformPoint(spot);
        var rotation = transform.rotation * Quaternion.LookRotation(forward);
        vehicle.SetPositionAndRotation(position, rotation);

        // The body has to move too: with interpolation on, physics copies the body's pose back onto
        // the transform every frame. Velocity only applies to a dynamic body.
        if (Application.isPlaying && vehicle.TryGetComponent<Rigidbody>(out var body))
        {
            body.position = position;
            body.rotation = rotation;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }
    }

    /// <summary>The index of a road's sample nearest a world (x, z).</summary>
    static int NearestPoint(RoadShape road, Vector2 worldXZ)
    {
        int nearest = 0;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < road.Points.Length; i++)
        {
            float d = Vector2.Distance(new Vector2(road.Points[i].x, road.Points[i].z), worldXZ);
            if (d < nearestDistance)
            {
                nearestDistance = d;
                nearest = i;
            }
        }
        return nearest;
    }

    /// <summary>The flat direction along a road at a sample, from the sample before to the one after.</summary>
    static Vector3 Forward(RoadShape road, int i)
    {
        var points = road.Points;
        var span = points[Mathf.Min(i + 1, points.Length - 1)] - points[Mathf.Max(i - 1, 0)];
        span.y = 0f;
        return span.sqrMagnitude > 1e-6f ? span.normalized : Vector3.forward;
    }

    /// <summary>
    /// The ground under a cell's centre, in the builder's local space: the same bicubic height the
    /// terrain is built from, so no physics raycast is needed.
    /// </summary>
    Vector3 GroundAt(Vector2Int cell)
    {
        var world = Plan.WorldXZ(cell);
        float height = PlanGrid.Bicubic(Plan.GroundHeight, Plan.CellsX, Plan.CellsY, cell);
        return new Vector3(world.x, height, world.y);
    }

    /// <summary>A plain lit material in one colour, for stand-ins.</summary>
    Material PlainMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        return Track(new Material(shader) { name = name, color = color });
    }

    /// <summary>
    /// Hands the region's day-night look to the scene's DayNightCycle, if the region has one and
    /// the scene has a cycle. In edit mode this changes the cycle in the open scene too.
    /// </summary>
    void ApplyAtmosphere()
    {
        if (region.dayNight == null) return;
        var cycle = FindAnyObjectByType<DayNightCycle>();
        if (cycle != null) cycle.Profile = region.dayNight;
    }

    // ---------- Helpers ----------

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

    /// <summary>
    /// Edit-mode builds stay out of the saved scene; they would only store stale data.
    /// </summary>
    void HideFromScene()
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
        foreach (var asset in createdAssets) asset.hideFlags = HideFlags.DontSave;
    }

    /// <summary>
    /// Destroy at end of frame in play mode, at once in the editor (where Destroy isn't allowed).
    /// </summary>
    static void Discard(Object obj)
    {
        if (Application.isPlaying) Destroy(obj);
        else DestroyImmediate(obj);
    }
}
