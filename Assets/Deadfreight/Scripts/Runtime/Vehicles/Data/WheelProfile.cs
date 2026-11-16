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
    [Unit("m")] public float radius = 0.4f;
    [Tooltip("How far the wheel can drop below its mount (m). More travel = absorbs bigger bumps and curbs.")]
    [Unit("m")] public float suspensionTravel = 0.15f;

    [Header("Ground Detection")]
    [Tooltip("Size of the sphere used to find the ground, as a fraction of the radius. 1 = the whole wheel " +
             "(rolls over curbs and edges). Lower it if wheels catch on walls they drive alongside.")]
    [Range(0.2f, 1f)] public float castRadiusScale = 1f;

    [Header("Suspension")]
    [Tooltip("Spring stiffness (N/m). Rough start: (mass * 9.81 / wheels) / (travel * 0.3)")]
    [Unit("N/m")] public float spring = 20000f;
    [Tooltip("Damping (N per m/s). Too low = bouncy, too high = harsh.")]
    [Unit("N/(m/s)")] public float damper = 1200f;

    [Header("Tire")]
    [Tooltip("Height above the contact point (m) where grip, rolling and drive forces push. " +
             "Raising it toward the center of mass reduces body roll and tipping (arcade trick).")]
    [Unit("m")] public float tireForceHeight = 0.3f;
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