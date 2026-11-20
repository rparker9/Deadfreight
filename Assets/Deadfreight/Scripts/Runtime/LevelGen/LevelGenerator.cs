// Scripts/Runtime/LevelGen/LevelGenerator.cs

/// <summary>
/// Makes a LevelPlan from a seed, the shared PlanSettings and the level's region, in stages,
/// each in its own LevelGenerator.*.cs file:
///   Land    warped base hills, the region's landform, the water line, slope
///   Zones   which of the region's zones each cell is
///   Anchors the playable area, the road in and the start place, the site, settlements
/// A stage that can't do its job sets plan.Failure, and the stages after it don't run.
/// </summary>
public sealed partial class LevelGenerator
{
    readonly LevelPlan plan;
    readonly PlanSettings settings;
    readonly RegionProfile region;
    readonly System.Random rng;
    readonly int cellsX, cellsY, cellCount;

    public static LevelPlan Generate(int seed, PlanSettings settings, RegionProfile region)
    {
        if (settings == null) throw new System.ArgumentNullException(nameof(settings));
        if (region == null) throw new System.ArgumentNullException(nameof(region));
        var generator = new LevelGenerator(seed, settings, region);
        generator.Run();
        return generator.plan;
    }

    LevelGenerator(int seed, PlanSettings settings, RegionProfile region)
    {
        this.settings = settings;
        this.region = region;
        rng = new System.Random(seed);
        plan = new LevelPlan(settings.cellsX, settings.cellsY, settings.cellSize)
        {
            Relief = region.relief,
            Region = region,
        };
        cellsX = plan.CellsX;
        cellsY = plan.CellsY;
        cellCount = cellsX * cellsY;
    }

    void Run()
    {
        ShapeLand();
        PlaceZones();
        PlaceAnchors();
    }
}
