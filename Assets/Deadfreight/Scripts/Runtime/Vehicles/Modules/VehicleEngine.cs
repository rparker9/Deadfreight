using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Engine with a "self-shifting gearbox": constant power with a force cap.
/// Force = min(maxForce, power / speed), so it pulls hardest when slow (climbing a hill,
/// starting from a stop) and eases off as it speeds up, like low vs high gear.
///
/// Sizing:
///   maxForce ≈ total mass (incl. anything towed) × 9.81 × (sin(steepest slope) + 0.03)
///     e.g. 450 kg up 25° ≈ 2000 N
///   power    = how fast it climbs and cruises; top speed is where power runs out.
/// The drive wheels' grip still caps the force (they spin on mud or steep slopes).
/// </summary>
public sealed class VehicleEngine : VehiclePower
{
    [Tooltip("Most force the engine can ever push with (N): decides the steepest hill it can climb from a stop")]
    [FormerlySerializedAs("forwardForce")]
    [Unit("N")] [SerializeField] private float maxForce = 3500f;

    [Tooltip("Engine power (W). Higher = climbs and accelerates faster at speed.")]
    [Unit("W")] [SerializeField] private float power = 30000f;

    [Tooltip("Force cap when reversing (N)")]
    [Unit("N")] [SerializeField] private float reverseForce = 1800f;

    [Tooltip("Hard top speed (m/s): the engine stops pushing above this")]
    [Unit("m/s")] [SerializeField] private float maxForwardSpeed = 16f;
    [Unit("m/s")] [SerializeField] private float maxReverseSpeed = 6f;

    /// <summary>Below this speed (m/s) power isn't divided by speed (avoids infinite force at a standstill).</summary>
    private const float MinPowerSpeed = 0.5f;

    protected override float ComputeForce(float throttle, float forwardSpeed, float dt)
    {
        if (throttle > 0f && forwardSpeed < maxForwardSpeed)
            return throttle * AvailableForce(forwardSpeed, maxForce);

        if (throttle < 0f && forwardSpeed > -maxReverseSpeed)
            return throttle * AvailableForce(-forwardSpeed, reverseForce);

        return 0f;
    }

    /// <summary>Force from power at this speed, never more than the cap.</summary>
    private float AvailableForce(float speedInDriveDirection, float cap)
    {
        float speed = Mathf.Max(speedInDriveDirection, MinPowerSpeed);
        return Mathf.Min(cap, power / speed);
    }
}