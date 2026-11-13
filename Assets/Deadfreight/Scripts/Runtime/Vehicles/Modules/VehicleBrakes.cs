using UnityEngine;

/// <summary>
/// Passes controls.Brake to every wheel with "Has Brake" ticked.
/// How strong the brake is lives in each wheel's WheelProfile (Brake Friction).
/// </summary>
public sealed class VehicleBrakes : VehicleModule
{
    [Tooltip("How fast the brake engages and releases (per second). 0 = instant.")]
    [SerializeField] private float response = 0f;

    public override int Order => VehicleModuleOrder.Brakes;

    public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
    {
        state.Brake = response > 0f
            ? Mathf.MoveTowards(state.Brake, controls.Brake, response * dt)
            : controls.Brake;

        foreach (var wheel in Vehicle.Wheels)
            wheel.BrakeInput = wheel.HasBrake ? state.Brake : 0f;
    }
}
