using UnityEngine;

/// <summary>
/// Pivoting front axle: turns toward controls.Steer at a limited rate.
/// The front wheel mounts must be children of the axle, so they turn with it.
/// </summary>
public sealed class VehicleSteering : VehicleModule
{
    [Tooltip("The pivoting axle (turntable)")]
    [SerializeField] private Transform axle;
    [SerializeField] private float maxSteerAngle = 35f;
    [Tooltip("Degrees per second the axle can turn")]
    [SerializeField] private float steerSpeed = 90f;

    public override int Order => VehicleModuleOrder.Steering;
    public float MaxSteerAngle => maxSteerAngle;

    public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
    {
        float target = controls.Steer * maxSteerAngle;
        state.SteerAngle = Mathf.MoveTowards(state.SteerAngle, target, steerSpeed * dt);

        // Applied now so the wheels use this step's direction.
        ApplyAngle(state.SteerAngle);
    }

    public override void Present(in VehicleNetState state, float dt)
    {
        if (!Vehicle.IsServer)
            ApplyAngle(state.SteerAngle);
    }

    private void ApplyAngle(float degrees)
    {
        if (axle != null)
            axle.localRotation = Quaternion.Euler(0f, degrees, 0f);
    }
}
