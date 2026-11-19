// Scripts/Runtime/LevelGen/LevelGenerator.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a level layout with land, roads, and stops. 
/// </summary>
public sealed partial class LevelGenerator
{
    readonly LevelLayout map;
    readonly LevelSettings settings;
    readonly System.Random rng;
    readonly int width, height, cellCount;

    GridPathfinder finder;

    public static LevelLayout Generate(int seed, LevelSettings settings)
    {
        var generator = new LevelGenerator(seed, settings);
        generator.Run();
        return generator.map;
    }

    LevelGenerator(int seed, LevelSettings settings)
    {
        this.settings = settings;
        rng = new System.Random(seed);
        map = new LevelLayout(settings.width, settings.height, settings.cellSize)
        {
            Relief = settings.relief,
        };
        width = map.Width;
        height = map.Height;
        cellCount = width * height;
    }

    void Run()
    {
        BuildLand();
    }
}
