// Scripts/Runtime/Region/Landforms/Ridges.cs
using UnityEngine;

/// <summary>
/// Long, winding ridges with narrow hollows between them, laid over the base hills: mountain
/// country where the land is mostly slope. Made from ridged noise, itself warped so the ridges
/// curve and branch instead of running straight.
/// </summary>
[CreateAssetMenu(fileName = "Ridges", menuName = "Level/Landforms/Ridges")]
public sealed class Ridges : Landform
{
    const int RidgeOctaves = 5;
    const int WarpOctaves = 3;
    const float WarpScale = 50f;        // cells across one twist of the warp
    const float NoiseOffset = 1300f;    // reads this landform's noise away from the base hills'
    const float CrestPower = 1.4f;      // above 1 narrows the crests and widens the hollows

    [Range(20f, 200f), Tooltip("Cells between neighbouring ridges.")]
    public float ridgeSpacing = 70f;
    [Range(0f, 1f), Tooltip("0 = the base hills only; 1 = ridges only.")]
    public float sharpness = 0.7f;
    [Range(0f, 60f), Tooltip("How far, in cells, the ridges are twisted: 0 = straighter; more = winding and branching.")]
    public float warp = 22f;

    public override void Shape(LevelPlan plan, System.Random rng)
    {
        var noise = new Noise2D(rng);
        int cellsX = plan.CellsX, cellsY = plan.CellsY;
        var ridges = new float[cellsX * cellsY];
        for (int y = 0; y < cellsY; y++)
        for (int x = 0; x < cellsX; x++)
        {
            float warpX = noise.Fbm(x + NoiseOffset, y, WarpScale, WarpOctaves) * warp;
            float warpY = noise.Fbm(x, y + NoiseOffset, WarpScale, WarpOctaves) * warp;
            ridges[y * cellsX + x] = noise.Ridged(x + warpX, y + warpY, ridgeSpacing, RidgeOctaves);
        }
        PlanGrid.Normalize(ridges);

        for (int i = 0; i < ridges.Length; i++)
        {
            float hills = plan.GroundHeight[i] / plan.Relief;
            float shaped = (1f - sharpness) * hills + sharpness * Mathf.Pow(ridges[i], CrestPower);
            plan.GroundHeight[i] = shaped * plan.Relief;
        }
    }
}
