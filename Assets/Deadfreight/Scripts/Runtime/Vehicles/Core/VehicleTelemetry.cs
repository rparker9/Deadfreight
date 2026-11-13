using UnityEngine;

/// <summary>
/// Snapshot of the vehicle's physical state, refreshed every physics step on the server.
/// Read it for gameplay checks; subscribe to Vehicle's events for moments (impacts, tipping).
/// </summary>
public struct VehicleTelemetry
{
    public Vector3 Velocity;

    /// <summary>m/s along the vehicle's forward. Negative = reversing.</summary>
    public float ForwardSpeed;

    /// <summary>Degrees between the vehicle's up and world up. 0 = level.</summary>
    public float TiltAngle;

    public int WheelsGrounded;
    public int WheelCount;

    /// <summary>Throttle applied, every wheel on the ground, but barely moving (e.g. mud).</summary>
    public bool IsStuck;

    public bool IsAirborne => WheelCount > 0 && WheelsGrounded == 0;
}

/// <summary>A collision hard enough to report (see Vehicle.Impacted).</summary>
public struct VehicleImpact
{
    public Vector3 Point;
    public Vector3 Normal;

    /// <summary>Relative speed at the moment of impact (m/s).</summary>
    public float Speed;

    /// <summary>Collision impulse (N·s). Scales with mass, good for damage.</summary>
    public float Impulse;

    public Collider Other;
}
