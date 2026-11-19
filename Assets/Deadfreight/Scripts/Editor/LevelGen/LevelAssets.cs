// Scripts/Editor/LevelGen/LevelAssets.cs
using UnityEditor;
using UnityEngine;

/// <summary>Creating level-generation assets from editor tools.</summary>
public static class LevelAssets
{
    /// <summary>
    /// A new asset of type T at <paramref name="path"/> (numbered if the name is taken), making
    /// any folders along the path that don't exist yet. Saved, ready to use.
    /// </summary>
    public static T Create<T>(string path) where T : ScriptableObject
    {
        EnsureFolder(System.IO.Path.GetDirectoryName(path).Replace('\\', '/'));
        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, AssetDatabase.GenerateUniqueAssetPath(path));
        AssetDatabase.SaveAssets();
        return asset;
    }

    /// <summary>Makes the folder and any missing parents, e.g. "Assets/Deadfreight/Data/LevelGen/Regions".</summary>
    public static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;

        int slash = folder.LastIndexOf('/');
        string parent = folder.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, folder.Substring(slash + 1));
    }
}
