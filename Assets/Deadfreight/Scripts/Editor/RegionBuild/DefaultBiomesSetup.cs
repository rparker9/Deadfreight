// Scripts/Editor/RegionBuild/DefaultBiomesSetup.cs
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → Deadfreight → Create Default Biomes and Rocks. Makes a starting set of Biome assets and
/// a RockSet from the PSX Nature models, saves them under Data/RegionGen, then:
///
///   - gives the default region settings a biome rule list, if it has none;
///   - puts the Verge and Riverbank biomes in the default build settings' edge bands, and the
///     rock set in its slot, if they're empty;
///   - gives existing biome assets their preview colour if they still have the plain default.
///
/// Safe to run again: anything already filled in is left alone, so your tuning is never
/// overwritten. Delete an asset (or empty a list) to have it remade with the defaults.
/// </summary>
public static class DefaultBiomesSetup
{
    const string Models = "Assets/PSX Nature v1.7.3c/PSX Nature/Models/";
    const string BiomeFolder = "Assets/Deadfreight/Data/RegionGen/Biomes";
    const string RockFolder = "Assets/Deadfreight/Data/RegionGen/Rocks";
    const string RockSetPath = RockFolder + "/DefaultRocks.asset";
    const string BuildSettingsPath = "Assets/Deadfreight/Data/RegionGen/DefaultRegionBuildSettings.asset";
    const string RegionSettingsPath = "Assets/Deadfreight/Data/RegionGen/DefaultRegionSettings.asset";

    static readonly string[] Pines =
    {
        "pine_tree_n_1", "pine_tree_n_1_1", "pine_tree_n_1_2", "pine_tree_n_1_3", "pine_tree_n_1_4",
        "pine_tree_n_2", "pine_tree_n_2_1", "pine_tree_n_2_2", "pine_tree_n_2_3", "pine_tree_n_2_4",
        "pine_tree_n_3", "pine_tree_n_3_1", "pine_tree_n_3_2", "pine_tree_n_3_3", "pine_tree_n_3_4",
    };
    static readonly string[] Broadleaf = { "tree_1", "tree_5", "tree_6", "tree_7", "tree_8", "tree_9" };
    static readonly string[] Mushrooms =
    {
        "mushroom_n_1", "mushroom_n_1_1", "mushroom_n_2", "mushroom_n_2_1", "mushroom_n_3", "mushroom_n_4",
    };
    static readonly Vector2 TreeScale = new(2f, 3f);   // the PSX trees are small: every tree is 2-3× its model size
    static readonly string[] Stones = { "stone_1", "stone_2", "stone_3", "stone_4", "stone_5" };

    /// <summary>
    /// How each foliage model looks wherever it grows. A model is one detail layer however many
    /// biomes use it, so its size and density must match in every biome; keeping them here does that.
    /// </summary>
    static readonly Dictionary<string, (Vector2 scale, float density, float align)> FoliageLook = new()
    {
        ["grass_1"] = (new Vector2(0.8f, 1.2f), 1f, 0.6f),
        ["grass_2"] = (new Vector2(0.8f, 1.2f), 1f, 0.6f),
        ["grass_3"] = (new Vector2(0.9f, 1.4f), 0.8f, 0.4f),
        ["grass_4"] = (new Vector2(0.9f, 1.4f), 0.8f, 0.4f),
        ["fern_1"] = (new Vector2(0.8f, 1.3f), 0.6f, 0.7f),
        ["fern_2"] = (new Vector2(0.8f, 1.3f), 0.6f, 0.7f),
        ["fern_3"] = (new Vector2(0.8f, 1.3f), 0.6f, 0.7f),
        ["wheat_1"] = (new Vector2(0.9f, 1.1f), 1f, 0.2f),
        ["wheat_1_1"] = (new Vector2(0.9f, 1.1f), 1f, 0.2f),
        ["mushroom_n_1"] = (new Vector2(0.7f, 1.3f), 0.3f, 1f),
        ["mushroom_n_1_1"] = (new Vector2(0.7f, 1.3f), 0.3f, 1f),
        ["mushroom_n_2"] = (new Vector2(0.7f, 1.3f), 0.3f, 1f),
        ["mushroom_n_2_1"] = (new Vector2(0.7f, 1.3f), 0.3f, 1f),
        ["mushroom_n_3"] = (new Vector2(0.7f, 1.3f), 0.3f, 1f),
        ["mushroom_n_4"] = (new Vector2(0.7f, 1.3f), 0.3f, 1f),
    };

    [MenuItem("Tools/Deadfreight/Create Default Biomes and Rocks")]
    static void Create()
    {
        Directory.CreateDirectory(BiomeFolder);
        Directory.CreateDirectory(RockFolder);
        AssetDatabase.Refresh();
        var made = new List<string>();

        var deepForest = MakeBiome("DeepForest", new Color(0.10f, 0.28f, 0.13f), made, biome =>
        {
            SetNoise(biome, 12f, 0.4f, 3f, 0.1f);
            SetTrees(biome, 8f, 0.9f, Trees(Pines, 1f, TreeScale.x, TreeScale.y), Trees(Broadleaf, 0.15f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.6f, ("fern_1", 0.35f), ("fern_2", 0.25f), ("fern_3", 0.2f), ("mushroom_n_1", 0.05f), ("mushroom_n_2", 0.05f));
        });
        var forestEdge = MakeBiome("ForestEdge", new Color(0.27f, 0.47f, 0.2f), made, biome =>
        {
            SetNoise(biome, 15f, 0.3f, 4f, 0.08f);
            SetTrees(biome, 11f, 0.6f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y), Trees(Pines, 0.4f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.5f, ("grass_1", 0.4f), ("grass_2", 0.3f), ("fern_1", 0.2f));
        });
        var field = MakeBiome("Field", new Color(0.69f, 0.73f, 0.37f), made, biome =>
        {
            SetNoise(biome, 20f, 0.15f, 5f, 0.05f);
            SetTrees(biome, 70f, 0.3f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.6f, ("grass_1", 0.5f), ("grass_2", 0.4f), ("grass_3", 0.3f), ("grass_4", 0.2f), ("wheat_1", 0.15f));
        });
        var wetland = MakeBiome("Wetland", new Color(0.27f, 0.47f, 0.47f), made, biome =>
        {
            SetNoise(biome, 15f, 0.1f, 4f, 0.05f);
            SetTrees(biome, 25f, 0.25f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.4f, ("grass_3", 0.6f), ("grass_4", 0.5f), ("fern_2", 0.2f));
        });
        var riverbank = MakeBiome("Riverbank", new Color(0.31f, 0.59f, 0.78f), made, biome =>
        {
            SetTrees(biome, 18f, 0.35f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.5f, ("grass_3", 0.5f), ("grass_4", 0.4f), ("fern_1", 0.25f));
        });
        var hollow = MakeBiome("Hollow", new Color(0.47f, 0.35f, 0.55f), made, biome =>
        {
            SetNoise(biome, 12f, 0.3f, 3f, 0.08f);
            SetTrees(biome, 30f, 0.15f, Trees(Pines, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.5f, ("fern_1", 0.3f), ("fern_2", 0.3f), ("fern_3", 0.3f),
                ("mushroom_n_1_1", 0.1f), ("mushroom_n_2_1", 0.1f), ("mushroom_n_3", 0.1f), ("mushroom_n_4", 0.1f));
        });
        var upland = MakeBiome("Upland", new Color(0.59f, 0.55f, 0.49f), made, biome =>
        {
            SetNoise(biome, 10f, 0.6f, 3f, 0.15f);
            SetTrees(biome, 18f, 0.4f, Trees(Pines, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.8f, ("grass_2", 0.2f));
        });
        var verge = MakeBiome("Verge", new Color(0.82f, 0.67f, 0.43f), made, biome =>
        {
            SetTrees(biome, 10f, 0f);
            SetFoliage(biome, 0.3f, ("grass_1", 0.6f), ("grass_2", 0.4f), ("grass_3", 0.3f));
        });

        var rocks = AssetDatabase.LoadAssetAtPath<RockSet>(RockSetPath);
        if (rocks == null)
        {
            rocks = ScriptableObject.CreateInstance<RockSet>();
            rocks.outcrops.pieces = Rocks(Stones, 1f, 2f, 4f);
            rocks.scree.pieces = Rocks(Stones, 1f, 0.6f, 1.4f);
            rocks.riverbeds.pieces = Rocks(Stones, 1f, 0.4f, 1f);
            rocks.forestFloor.pieces = Join(Rocks(Stones, 0.6f, 0.8f, 1.6f),
                Rocks(new[] { "tree_log_1" }, 2f, 0.9f, 1.2f), Rocks(new[] { "tree_stump_1" }, 2f, 0.9f, 1.2f));
            rocks.forestFloor.clusterSize = new Vector2Int(1, 2);
            rocks.openGround.pieces = Rocks(Stones, 1f, 1f, 2f);
            AssetDatabase.CreateAsset(rocks, RockSetPath);
            made.Add(RockSetPath);
        }

        FillRules(made, deepForest, forestEdge, field, wetland, hollow, upland);
        FillBuildSettings(made, rocks, verge, riverbank);
        AssetDatabase.SaveAssets();
        Debug.Log(made.Count == 0
            ? "Default biomes and rocks: everything already exists; nothing changed."
            : "Default biomes and rocks: made or filled\n  " + string.Join("\n  ", made));
    }

    // ---------- biomes ----------

    static Biome MakeBiome(string name, Color colour, List<string> made, System.Action<Biome> fill)
    {
        string path = $"{BiomeFolder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Biome>(path);
        if (existing != null)
        {
            // Made before biomes had a preview colour: give it one, leave the rest alone.
            if (existing.previewColour == DefaultColour)
            {
                existing.previewColour = colour;
                EditorUtility.SetDirty(existing);
                made.Add($"{path} (colour)");
            }
            return existing;
        }

        var biome = ScriptableObject.CreateInstance<Biome>();
        biome.previewColour = colour;
        fill(biome);
        AssetDatabase.CreateAsset(biome, path);
        made.Add(path);
        return biome;
    }

    /// <summary>The colour a Biome starts with, to tell an untouched one.</summary>
    static readonly Color DefaultColour = new(0.6f, 0.65f, 0.45f);

    static void SetNoise(Biome biome, float bumpScale, float bumpHeight, float roughScale, float roughHeight)
    {
        biome.bumpScale = bumpScale;
        biome.bumpHeight = bumpHeight;
        biome.roughScale = roughScale;
        biome.roughHeight = roughHeight;
    }

    static void SetTrees(Biome biome, float spacing, float fill, params BiomeTree[][] groups)
    {
        biome.treeSpacing = spacing;
        biome.treeFill = fill;
        biome.trees = Join(groups);
    }

    static void SetFoliage(Biome biome, float patchiness, params (string model, float amount)[] entries)
    {
        biome.patchiness = patchiness;
        var foliage = new List<BiomeFoliage>();
        foreach (var (model, amount) in entries)
        {
            var prototype = Model(model);
            if (prototype == null) continue;
            var look = FoliageLook[model];
            foliage.Add(new BiomeFoliage { prototype = prototype, amount = amount, scale = look.scale, density = look.density, alignToGround = look.align });
        }
        biome.foliage = foliage.ToArray();
    }

    /// <summary>One tree entry per model that exists, all with the same weight and size range.</summary>
    static BiomeTree[] Trees(string[] models, float weight, float minScale, float maxScale)
    {
        var trees = new List<BiomeTree>();
        foreach (var model in models)
        {
            var prefab = Model(model);
            if (prefab != null) trees.Add(new BiomeTree { prefab = prefab, weight = weight, scale = new Vector2(minScale, maxScale) });
        }
        return trees.ToArray();
    }

    // ---------- rocks ----------

    static RockPiece[] Rocks(string[] models, float weight, float minScale, float maxScale)
    {
        var pieces = new List<RockPiece>();
        foreach (var model in models)
        {
            var prefab = Model(model);
            if (prefab != null) pieces.Add(new RockPiece { prefab = prefab, weight = weight, scale = new Vector2(minScale, maxScale) });
        }
        return pieces.ToArray();
    }

    // ---------- build settings ----------

    /// <summary>
    /// A starting rule list for the default region, if it has none. First match wins:
    /// marsh, then hollows, then high or steep ground, then deep forest, then forest, then fields.
    /// </summary>
    static void FillRules(List<string> made, Biome deepForest, Biome forestEdge, Biome field, Biome wetland, Biome hollow, Biome upland)
    {
        var region = AssetDatabase.LoadAssetAtPath<RegionSettings>(RegionSettingsPath);
        if (region == null)
        {
            Debug.LogWarning($"No region settings at {RegionSettingsPath}; add the biome rules yourself.");
            return;
        }
        if (region.biomes != null && region.biomes.Length > 0) return;

        Undo.RecordObject(region, "Default biome rules");
        region.biomes = new[]
        {
            new BiomeRule { biome = wetland, marsh = Need.Required },
            new BiomeRule { biome = hollow, hollow = Need.Required },
            new BiomeRule { biome = upland, height = new Vector2(0.75f, 1f) },
            new BiomeRule { biome = upland, slope = new Vector2(0.35f, 10f) },
            new BiomeRule { biome = deepForest, forest = Need.Required, forestDepth = 2 },
            new BiomeRule { biome = forestEdge, forest = Need.Required },
            new BiomeRule { biome = field },
        };
        EditorUtility.SetDirty(region);
        made.Add($"{RegionSettingsPath} (biome rules)");
    }

    /// <summary>Puts the band biomes and the rock set into the default build settings, where empty.</summary>
    static void FillBuildSettings(List<string> made, RockSet rocks, Biome verge, Biome riverbank)
    {
        var settings = AssetDatabase.LoadAssetAtPath<RegionBuildSettings>(BuildSettingsPath);
        if (settings == null)
        {
            Debug.LogWarning($"No build settings at {BuildSettingsPath}; assign the edge bands and rock set yourself.");
            return;
        }

        Undo.RecordObject(settings, "Fill edge bands and rocks");
        bool changed = false;
        if (settings.verge.biome == null) { settings.verge.biome = verge; changed = true; made.Add("Verge band"); }
        if (settings.riverbank.biome == null) { settings.riverbank.biome = riverbank; changed = true; made.Add("Riverbank band"); }
        if (settings.rocks == null) { settings.rocks = rocks; changed = true; made.Add("Rocks slot"); }
        if (changed) EditorUtility.SetDirty(settings);
    }

    // ---------- helpers ----------

    static GameObject Model(string name)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Models + name + ".fbx");
        if (model == null) Debug.LogWarning($"Model not found: {Models}{name}.fbx");
        return model;
    }

    static T[] Join<T>(params T[][] groups)
    {
        var all = new List<T>();
        foreach (var group in groups) all.AddRange(group);
        return all.ToArray();
    }
}
