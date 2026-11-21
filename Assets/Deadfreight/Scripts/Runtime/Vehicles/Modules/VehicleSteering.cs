using UnityEngine;

/// <summary>
/// Pivoting front axle: turns toward controls.Steer at a limited rate.
/// The front wheel mounts must be children of the axle, so they turn with it.
/// </summary>
public sealed class VehicleSteering : VehicleModule
{
    [Tooltip("The pivoting axle (turntable)")]
    [SerializeField] private Transform axle;
    [Unit("°")] [SerializeField] private float maxSteerAngle = 35f;
    [Tooltip("Degrees per second the axle can turn")]
    [Unit("°/s")] [SerializeField] private float steerSpeed = 200f;

    [Header("Speed-sensitive steering")]
    [Tooltip("Fraction of max steer angle allowed at High Speed (1 = no reduction)")]
    [Range(0.1f, 1f)][SerializeField] private float steerAtHighSpeed = 0.45f;
    [Tooltip("Speed (m/s) at which steering is reduced to Steer At High Speed")]
    [Unit("m/s")][SerializeField] private float highSpeed = 16f;

    public override int Order => VehicleModuleOrder.Steering;
    public float MaxSteerAngle => maxSteerAngle;

    public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
    {
        // Less lock the faster we go: sharp turns at speed are what flip vehicles.
        float speed01 = Mathf.Clamp01(Mathf.Abs(Vehicle.ForwardSpeed) / Mathf.Max(highSpeed, 0.01f));
        float limit = maxSteerAngle * Mathf.Lerp(1f, steerAtHighSpeed, speed01);
        float target = controls.Steer * limit;
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