using UnityEngine;

/// <summary>
/// One piece of vehicle behaviour: steering, power, brakes, a wheel...
/// Put it on the vehicle or any child; the Vehicle finds it and calls it in Order.
/// Modules have no FixedUpdate of their own, so the step order is always explicit.
/// </summary>
public abstract class VehicleModule : MonoBehaviour
{
    /// <summary>The vehicle this module belongs to (set before the first step).</summary>
    public Vehicle Vehicle { get; private set; }

    /// <summary>Lower runs first. Use the VehicleModuleOrder constants.</summary>
    public abstract int Order { get; }

    internal int SortIndex { get; set; }

    internal void Bind(Vehicle vehicle)
    {
        Vehicle = vehicle;
        OnBind();
    }

    /// <summary>Called once when the vehicle collects its modules (Awake).</summary>
    protected virtual void OnBind() { }

    /// <summary>
    /// SERVER, every physics step. Read controls, change physics, and write anything
    /// clients need to see into state (it's synced after all modules run).
    /// </summary>
    public virtual void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt) { }

    /// <summary>EVERY MACHINE, every frame. Visuals only, from the synced state.</summary>
    public virtual void Present(in VehicleNetState state, float dt) { }
}

/// <summary>Step order for modules. Leave gaps so new modules can slot in between.</summary>
public static class VehicleModuleOrder
{
    public const int Steering = 100;
    public const int Power = 200;
    public const int Brakes = 300;
    public const int Wheels = 400;
}
