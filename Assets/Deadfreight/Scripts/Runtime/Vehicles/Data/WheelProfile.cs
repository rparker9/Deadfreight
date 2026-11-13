using UnityEngine;

/// <summary>
/// Shared tuning for wheels: size, suspension, tire and brake.
/// Assign one profile to all wheels of a vehicle (or separate front/rear profiles).
/// Values can be tuned in Play mode and are kept.
/// </summary>
[CreateAssetMenu(fileName = "WheelProfile", menuName = "Vehicles/Wheel Profile")]
public sealed class WheelProfile : ScriptableObject
{
    [Header("Geometry")]
    public float radius = 0.4f;
    [Tooltip("How far the wheel can drop below its mount (m). Wooden wheels barely flex: keep small.")]
    public float suspensionTravel = 0.08f;

    [Header("Suspension")]
    [Tooltip("Spring stiffness (N/m). Rough start: (mass * 9.81 / wheels) / (travel * 0.3)")]
    public float spring = 20000f;
    [Tooltip("Damping (N per m/s). Too low = bouncy, too high = harsh.")]
    public float damper = 1200f;

    [Header("Tire")]
    [Tooltip("Grip against sliding sideways (friction coefficient). Makes it go where the wheels point.")]
    public float lateralFriction = 1.2f;
    [Tooltip("Rolling resistance as a fraction of load. Road ~0.02-0.04; GroundSurface multiplies this.")]
    public float rollingResistance = 0.03f;

    [Header("Brake")]
    [Tooltip("Friction coefficient when the brake is fully on (wheels with Has Brake only)")]
    public float brakeFriction = 0.8f;

    [Header("Ground")]
    public LayerMask groundMask = ~0;
}
