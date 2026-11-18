// Scripts/Editor/RegionBuild/DefaultBiomesSetup.cs
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools → Deadfreight → Create Default Biomes and Rocks. Makes a starting set of Biome assets
/// (one per role) and a RockSet from the PSX Nature models, saves them under Data/RegionGen, and
/// puts them in the default build settings' empty slots.
///
/// Safe to run again: assets that already exist and slots already filled are left alone, so
/// your tuning is never overwritten. Delete an asset to have it remade with the defaults.
/// </summary>
public static class DefaultBiomesSetup
{
    const string Models = "Assets/PSX Nature v1.7.3c/PSX Nature/Models/";
    const string BiomeFolder = "Assets/Deadfreight/Data/RegionGen/Biomes";
    const string RockFolder = "Assets/Deadfreight/Data/RegionGen/Rocks";
    const string RockSetPath = RockFolder + "/DefaultRocks.asset";
    const string BuildSettingsPath = "Assets/Deadfreight/Data/RegionGen/DefaultRegionBuildSettings.asset";

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

        var deepForest = MakeBiome("DeepForest", made, biome =>
        {
            SetTrees(biome, 8f, 0.9f, Trees(Pines, 1f, TreeScale.x, TreeScale.y), Trees(Broadleaf, 0.15f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.6f, ("fern_1", 0.35f), ("fern_2", 0.25f), ("fern_3", 0.2f), ("mushroom_n_1", 0.05f), ("mushroom_n_2", 0.05f));
        });
        var forestEdge = MakeBiome("ForestEdge", made, biome =>
        {
            SetTrees(biome, 11f, 0.6f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y), Trees(Pines, 0.4f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.5f, ("grass_1", 0.4f), ("grass_2", 0.3f), ("fern_1", 0.2f));
        });
        var field = MakeBiome("Field", made, biome =>
        {
            SetTrees(biome, 70f, 0.3f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.6f, ("grass_1", 0.5f), ("grass_2", 0.4f), ("grass_3", 0.3f), ("grass_4", 0.2f), ("wheat_1", 0.15f));
        });
        var wetland = MakeBiome("Wetland", made, biome =>
        {
            SetTrees(biome, 25f, 0.25f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.4f, ("grass_3", 0.6f), ("grass_4", 0.5f), ("fern_2", 0.2f));
        });
        var riverbank = MakeBiome("Riverbank", made, biome =>
        {
            SetTrees(biome, 18f, 0.35f, Trees(Broadleaf, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.5f, ("grass_3", 0.5f), ("grass_4", 0.4f), ("fern_1", 0.25f));
        });
        var hollow = MakeBiome("Hollow", made, biome =>
        {
            SetTrees(biome, 30f, 0.15f, Trees(Pines, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.5f, ("fern_1", 0.3f), ("fern_2", 0.3f), ("fern_3", 0.3f),
                ("mushroom_n_1_1", 0.1f), ("mushroom_n_2_1", 0.1f), ("mushroom_n_3", 0.1f), ("mushroom_n_4", 0.1f));
        });
        var upland = MakeBiome("Upland", made, biome =>
        {
            SetTrees(biome, 18f, 0.4f, Trees(Pines, 1f, TreeScale.x, TreeScale.y));
            SetFoliage(biome, 0.8f, ("grass_2", 0.2f));
        });
        var verge = MakeBiome("Verge", made, biome =>
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

        FillSlots(made, rocks, deepForest, forestEdge, field, wetland, riverbank, hollow, upland, verge);
        AssetDatabase.SaveAssets();
        Debug.Log(made.Count == 0
            ? "Default biomes and rocks: everything already exists; nothing changed."
            : "Default biomes and rocks: made or filled\n  " + string.Join("\n  ", made));
    }

    // ---------- biomes ----------

    static Biome MakeBiome(string name, List<string> made, System.Action<Biome> fill)
    {
        string path = $"{BiomeFolder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Biome>(path);
        if (existing != null) return existing;

        var biome = ScriptableObject.CreateInstance<Biome>();
        fill(biome);
        AssetDatabase.CreateAsset(biome, path);
        made.Add(path);
        return biome;
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

    /// <summary>Puts each asset in the default build settings' matching slot, if that slot is empty.</summary>
    static void FillSlots(List<string> made, RockSet rocks, Biome deepForest, Biome forestEdge, Biome field, Biome wetland,
        Biome riverbank, Biome hollow, Biome upland, Biome verge)
    {
        var settings = AssetDatabase.LoadAssetAtPath<RegionBuildSettings>(BuildSettingsPath);
        if (settings == null)
        {
            Debug.LogWarning($"No build settings at {BuildSettingsPath}; assign the biomes and rock set yourself.");
            return;
        }

        Undo.RecordObject(settings, "Fill biome slots");
        var slots = settings.biomes;
        bool changed = false;
        void Fill(ref Biome slot, Biome biome, string role)
        {
            if (slot != null) return;
            slot = biome;
            changed = true;
            made.Add($"{role} slot");
        }

        Fill(ref slots.deepForest, deepForest, "DeepForest");
        Fill(ref slots.forestEdge, forestEdge, "ForestEdge");
        Fill(ref slots.field, field, "Field");
        Fill(ref slots.wetland, wetland, "Wetland");
        Fill(ref slots.riverbank, riverbank, "Riverbank");
        Fill(ref slots.hollow, hollow, "Hollow");
        Fill(ref slots.upland, upland, "Upland");
        Fill(ref slots.verge, verge, "Verge");
        if (settings.rocks == null)
        {
            settings.rocks = rocks;
            changed = true;
            made.Add("Rocks slot");
        }
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
