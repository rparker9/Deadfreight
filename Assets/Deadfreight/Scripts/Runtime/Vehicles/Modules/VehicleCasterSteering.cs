using UnityEngine;

/// <summary>
/// Passive steering for towed vehicles: the axle turns to follow the direction it's
/// actually moving, like a caster or a dolly under a wagon. Use instead of VehicleSteering
/// on a four-wheeled trailer, so its front wheels track behind the tow vehicle instead of
/// scrubbing sideways.
/// </summary>
public sealed class VehicleCasterSteering : VehicleModule
{
    [Tooltip("The axle that turns (front wheel mounts must be its children)")]
    [SerializeField] private Transform axle;
    [Unit("°")] [SerializeField] private float maxSteerAngle = 45f;
    
    [Tooltip("Degrees per second the axle can turn")]
    [Unit("°/s")] [SerializeField] private float steerSpeed = 180f;
    
    [Tooltip("Below this speed (m/s) the axle holds its angle")]
    [Unit("m/s")] [SerializeField] private float minSpeed = 0.3f;

    public override int Order => VehicleModuleOrder.Steering;

    public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
    {
        if (axle == null) return;

        Transform body = Vehicle.transform;
        Vector3 up = body.up;
        Vector3 velocity = Vector3.ProjectOnPlane(Vehicle.Body.GetPointVelocity(axle.position), up);

        if (velocity.sqrMagnitude > minSpeed * minSpeed)
        {
            // Reversing: follow the opposite direction so the wheels don't spin round 180°.
            if (Vector3.Dot(velocity, body.forward) < 0f) velocity = -velocity;

            float target = Mathf.Clamp(Vector3.SignedAngle(body.forward, velocity, up), -maxSteerAngle, maxSteerAngle);
            state.SteerAngle = Mathf.MoveTowards(state.SteerAngle, target, steerSpeed * dt);
        }

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