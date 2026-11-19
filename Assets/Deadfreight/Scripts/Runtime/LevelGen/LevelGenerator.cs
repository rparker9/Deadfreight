// Scripts/Runtime/LevelGen/LevelGenerator.cs

/// <summary>
/// Makes a LevelPlan from a seed and PlanSettings. For now that's the land alone
/// (LevelGenerator.Land.cs); later stages go in their own LevelGenerator.*.cs files.
/// </summary>
public sealed partial class LevelGenerator
{
    readonly LevelPlan plan;
    readonly PlanSettings settings;
    readonly System.Random rng;
    readonly int cellsX, cellsY, cellCount;

    public static LevelPlan Generate(int seed, PlanSettings settings)
    {
        var generator = new LevelGenerator(seed, settings);
        generator.Run();
        return generator.plan;
    }

    LevelGenerator(int seed, PlanSettings settings)
    {
        this.settings = settings;
        rng = new System.Random(seed);
        plan = new LevelPlan(settings.cellsX, settings.cellsY, settings.cellSize)
        {
            Relief = settings.relief,
        };
        cellsX = plan.CellsX;
        cellsY = plan.CellsY;
        cellCount = cellsX * cellsY;
    }

    void Run()
    {
        ShapeLand();
    }
}
