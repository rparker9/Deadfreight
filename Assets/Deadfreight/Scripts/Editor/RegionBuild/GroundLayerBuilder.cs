// Scripts/Editor/RegionBuild/GroundLayerBuilder.cs
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns ground texture sets into Terrain Layers, in one click. Expects one folder per set:
///
///     {root}/{Category}/{Set}/{Set}_basecolor.png, _normal, _height, _roughness, _ambientocclusion, _metallic
///
/// For each set it fixes the import settings (colour as sRGB, normals as normal maps, the rest as
/// linear data), packs a mask map the way URP's terrain reads it, and creates or updates
/// {Set}.terrainlayer beside the textures. Running it again just refreshes everything.
///
/// Mask map channels: R metallic, G ambient occlusion, B height (for height blending),
/// A smoothness (1 - roughness).
/// </summary>
public sealed class GroundLayerBuilder : EditorWindow
{
    const string DefaultRoot = "Assets/Deadfreight/Textures/Ground";

    // Starting tile sizes (metres per texture repeat) by category; anything else gets the default.
    static readonly Dictionary<string, float> TileSizes = new()
    {
        { "Grass", 3f }, { "Dirt", 3f }, { "Gravel", 2f }, { "Sand", 3f }, { "Rocky", 4f }, { "Rock", 6f },
    };
    const float DefaultTileSize = 3f;

    [SerializeField] string root = DefaultRoot;
    [SerializeField] bool normalsAreDirectX;

    [MenuItem("Tools/Deadfreight/Ground Layer Builder")]
    static void Open() => GetWindow<GroundLayerBuilder>("Ground Layers");

    void OnGUI()
    {
        EditorGUILayout.LabelField("Texture sets → Terrain Layers", EditorStyles.boldLabel);
        root = EditorGUILayout.TextField("Root folder", root);
        normalsAreDirectX = EditorGUILayout.Toggle(
            new GUIContent("Normals are DirectX", "Tick if bumps look lit from the wrong side: flips the normal maps' green channel."),
            normalsAreDirectX);

        var sets = FindSets(root);
        EditorGUILayout.HelpBox($"{sets.Count} texture sets found. Each gets a packed mask map and a .terrainlayer " +
                                "in its own folder. Tile sizes start per category; adjust them on the layer assets.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(sets.Count == 0))
            if (GUILayout.Button("Build Terrain Layers")) Build(sets);
    }

    // ---------- finding sets ----------

    /// <summary>Every folder under root holding a {Set}_basecolor.png named after the folder.</summary>
    static List<string> FindSets(string root)
    {
        var sets = new List<string>();
        if (!AssetDatabase.IsValidFolder(root)) return sets;
        foreach (string category in AssetDatabase.GetSubFolders(root))
        foreach (string set in AssetDatabase.GetSubFolders(category))
            if (File.Exists(MapPath(set, "basecolor"))) sets.Add(set);
        return sets;
    }

    static string SetName(string set) => Path.GetFileName(set);
    static string CategoryName(string set) => Path.GetFileName(Path.GetDirectoryName(set));
    static string MapPath(string set, string map) => $"{set}/{SetName(set)}_{map}.png";

    // ---------- building ----------

    void Build(List<string> sets)
    {
        try
        {
            // Phase 1: import settings and mask maps. Batched, so Unity reimports once at the end.
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int k = 0; k < sets.Count; k++)
                {
                    EditorUtility.DisplayProgressBar("Ground layers", $"Preparing {SetName(sets[k])}", (float)k / sets.Count * 0.8f);
                    ConfigureImports(sets[k]);
                    WriteMaskMap(sets[k]);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Phase 2: the mask maps now exist as assets; set them up and build the layers.
            for (int k = 0; k < sets.Count; k++)
            {
                EditorUtility.DisplayProgressBar("Ground layers", $"Layer {SetName(sets[k])}", 0.8f + (float)k / sets.Count * 0.2f);
                Configure(MaskPath(sets[k]), linear: true);
                CreateOrUpdateLayer(sets[k]);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Ground Layer Builder: {sets.Count} terrain layers built under {root}.");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    void ConfigureImports(string set)
    {
        Configure(MapPath(set, "basecolor"), linear: false);
        ConfigureNormal(MapPath(set, "normal"));
        foreach (string data in new[] { "height", "roughness", "ambientocclusion", "metallic" })
            Configure(MapPath(set, data), linear: true);
    }

    /// <summary>Colour textures are sRGB; data textures (height, roughness...) must be read as-is.</summary>
    static void Configure(string path, bool linear)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        bool changed = importer.textureType != TextureImporterType.Default || importer.sRGBTexture == linear;
        if (!changed) return;
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = !linear;
        importer.SaveAndReimport();
    }

    void ConfigureNormal(string path)
    {
        if (AssetImporter.GetAtPath(path) is not TextureImporter importer) return;
        bool changed = importer.textureType != TextureImporterType.NormalMap || importer.flipGreenChannel != normalsAreDirectX;
        if (!changed) return;
        importer.textureType = TextureImporterType.NormalMap;
        importer.flipGreenChannel = normalsAreDirectX;
        importer.SaveAndReimport();
    }

    static string MaskPath(string set) => MapPath(set, "mask");

    /// <summary>
    /// Packs the mask map from the source PNGs. They're read straight from disk, so their import
    /// settings (readable or not, compressed or not) don't matter. The occlusion map sets the size;
    /// a map of another size is resampled to fit.
    /// </summary>
    static void WriteMaskMap(string set)
    {
        var occlusionTexture = Load(MapPath(set, "ambientocclusion"));
        int w = occlusionTexture.width, h = occlusionTexture.height;
        byte[] occlusion = Grey(occlusionTexture, w, h);
        byte[] metallic = GreyFromFile(MapPath(set, "metallic"), w, h);
        byte[] height = GreyFromFile(MapPath(set, "height"), w, h);
        byte[] roughness = GreyFromFile(MapPath(set, "roughness"), w, h);
        DestroyImmediate(occlusionTexture);

        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(metallic[i], occlusion[i], height[i], (byte)(255 - roughness[i]));

        var mask = new Texture2D(w, h, TextureFormat.RGBA32, false);
        mask.SetPixels32(pixels);
        File.WriteAllBytes(MaskPath(set), mask.EncodeToPNG());
        DestroyImmediate(mask);
        AssetDatabase.ImportAsset(MaskPath(set));
    }

    static Texture2D Load(string path)
    {
        var texture = new Texture2D(2, 2);
        texture.LoadImage(File.ReadAllBytes(path));
        return texture;
    }

    static byte[] GreyFromFile(string path, int width, int height)
    {
        var texture = Load(path);
        try { return Grey(texture, width, height); }
        finally { DestroyImmediate(texture); }
    }

    /// <summary>A greyscale map's values at width × height, resampled only if its own size differs.</summary>
    static byte[] Grey(Texture2D texture, int width, int height)
    {
        // A greyscale PNG can load as alpha-only; then the value is in alpha, not red.
        bool inAlpha = texture.format == TextureFormat.Alpha8;
        var values = new byte[width * height];

        if (texture.width == width && texture.height == height)
        {
            var pixels = texture.GetPixels32();
            for (int i = 0; i < values.Length; i++) values[i] = inAlpha ? pixels[i].a : pixels[i].r;
            return values;
        }

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            var c = texture.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height);
            values[y * width + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(inAlpha ? c.a : c.r) * 255f);
        }
        return values;
    }

    static void CreateOrUpdateLayer(string set)
    {
        string path = $"{set}/{SetName(set)}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
        bool isNew = layer == null;
        if (isNew) layer = new TerrainLayer();

        layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath(set, "basecolor"));
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath(set, "normal"));
        layer.maskMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(MaskPath(set));
        if (isNew)  // keep tile sizes you've tuned by hand
        {
            float size = TileSizes.TryGetValue(CategoryName(set), out float s) ? s : DefaultTileSize;
            layer.tileSize = new Vector2(size, size);
        }

        if (isNew) AssetDatabase.CreateAsset(layer, path);
        else EditorUtility.SetDirty(layer);
    }
}
