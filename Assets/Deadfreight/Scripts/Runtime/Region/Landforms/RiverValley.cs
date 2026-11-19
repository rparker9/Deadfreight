// Scripts/Runtime/Region/Landforms/RiverValley.cs
using UnityEngine;

/// <summary>
/// A wide valley winding across the level, west to east or south to north, with a river channel
/// down the middle. The floor is nearly flat but keeps small hummocks and hollows, so with water
/// in the region it breaks into pools, channels and islands rather than one lake. The walls rise
/// gently back to the hills. Widths are shares of the level's size, so they suit any map size.
/// </summary>
[CreateAssetMenu(fileName = "RiverValley", menuName = "Level/Landforms/River Valley")]
public sealed class RiverValley : Landform
{
    const int RiverPoints = 250;        // points along the river line, for measuring distance to it
    const float MeanderScale = 55f;     // cells along the river between big bends
    const float WidthScale = 45f;       // cells along the valley between wide and narrow stretches
    const float NoiseOffset = 777f;     // reads this landform's noise away from the base hills'

    [Header("Valley")]
    [Range(0.05f, 0.6f), Tooltip("Half-width of the flat floor, as a share of the level's size. It widens and narrows along the valley by a quarter either way.")]
    public float floorWidth = 0.35f;
    [Range(0.02f, 0.6f), Tooltip("How far the walls take to rise from the floor to the hills, as a share of the level's size. Small = steep walls.")]
    public float wallWidth = 0.25f;
    [Range(0f, 0.5f), Tooltip("How far the river swings from side to side, as a share of the level's size.")]
    public float meander = 0.3f;

    [Header("Floor")]
    [Range(0f, 1f), Tooltip("How much of the hills' shape the floor keeps, plus as much again in hummocks. Low = flat; with water, a maze of pools.")]
    public float floorRoughness = 0.12f;
    [Range(3f, 40f), Tooltip("Cells between hummocks on the floor.")]
    public float hummockSize = 9f;

    [Header("River channel")]
    [Unit("m"), Tooltip("Half-width of the river channel.")]
    public float channelWidth = 100f;
    [Range(0f, 0.3f), Tooltip("How far the channel dips below the floor, as a share of the relief.")]
    public float channelDepth = 0.06f;

    public override void Shape(LevelPlan plan, System.Random rng)
    {
        var noise = new Noise2D(rng);
        int cellsX = plan.CellsX, cellsY = plan.CellsY, count = cellsX * cellsY;
        float size = Mathf.Max(cellsX, cellsY) - 1;           // widths are shares of this
        var river = RiverLine(noise, cellsX, cellsY, northSouth: rng.Next(2) == 1);

        // Along the valley the floor widens and narrows; on it, hummocks. Both 0-1.
        var widthNoise = new float[count];
        var hummocks = new float[count];
        for (int y = 0; y < cellsY; y++)
        for (int x = 0; x < cellsX; x++)
        {
            widthNoise[y * cellsX + x] = noise.Fbm(x + NoiseOffset, y, WidthScale, 2);
            hummocks[y * cellsX + x] = noise.Fbm(x - NoiseOffset, y + NoiseOffset, hummockSize, 3);
        }
        PlanGrid.Normalize(widthNoise);
        PlanGrid.Normalize(hummocks);

        for (int y = 0; y < cellsY; y++)
        for (int x = 0; x < cellsX; x++)
        {
            int i = y * cellsX + x;
            float fromRiver = DistanceTo(river, x, y);         // cells

            // 0 on the floor, rising smoothly to 1 where the walls meet the hills.
            float floorHalfWidth = floorWidth * (0.75f + 0.5f * widthNoise[i]);
            float t = Mathf.Clamp01((fromRiver / size - floorHalfWidth) / wallWidth);
            float rise = t * t * (3f - 2f * t);

            float hills = plan.GroundHeight[i] / plan.Relief;
            float floor = (hills + hummocks[i]) * floorRoughness;
            float shaped = floor + (hills - floor) * rise;
            shaped -= channelDepth * Mathf.Clamp01(1f - fromRiver * plan.CellSize / channelWidth);

            plan.GroundHeight[i] = shaped * plan.Relief;
        }
    }

    /// <summary>
    /// The river: a wandering line across the level, starting and ending a little beyond its
    /// edges so it never stops short. Points in cells.
    /// </summary>
    Vector2[] RiverLine(Noise2D noise, int cellsX, int cellsY, bool northSouth)
    {
        float along = (northSouth ? cellsY : cellsX) - 1;
        float across = (northSouth ? cellsX : cellsY) - 1;
        var points = new Vector2[RiverPoints];
        for (int k = 0; k < RiverPoints; k++)
        {
            float a = Mathf.Lerp(-0.1f, 1.1f, k / (RiverPoints - 1f)) * along;
            float c = (0.5f + noise.Fbm(a, NoiseOffset, MeanderScale, 3) * meander) * across;
            points[k] = northSouth ? new Vector2(c, a) : new Vector2(a, c);
        }
        return points;
    }

    /// <summary>Distance in cells from a cell to the nearest point of the line.</summary>
    static float DistanceTo(Vector2[] line, int x, int y)
    {
        var cell = new Vector2(x, y);
        float best = float.MaxValue;
        foreach (var p in line)
            best = Mathf.Min(best, (p - cell).sqrMagnitude);
        return Mathf.Sqrt(best);
    }
}
