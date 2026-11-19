// Scripts/Runtime/Region/Landform.cs
using UnityEngine;

/// <summary>
/// A region's big shape, laid over the base hills.
/// Each kind is a subclass with its own few settings; make an asset of one and pick it in a RegionProfile. 
/// Several regions can share a landform and still differ in everything else.
/// </summary>
public abstract class Landform : ScriptableObject
{
    /// <summary>
    /// Reshapes the plan's ground heights in place. On entry they're the base hills, 0 to
    /// plan.Relief metres. Anything is allowed: the generator rescales the result back to
    /// 0 to Relief afterwards, so a landform only decides the shape, not the height range.
    /// </summary>
    public abstract void Shape(LevelPlan plan, System.Random rng);
}
