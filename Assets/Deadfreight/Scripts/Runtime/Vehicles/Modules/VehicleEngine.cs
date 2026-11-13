using UnityEngine;

/// <summary>
/// Simple engine: fixed force at full throttle, cut off above a top speed.
/// </summary>
public sealed class VehicleEngine : VehiclePower
{
    [Tooltip("Total forward force at full throttle (N), split across driven wheels")]
    [SerializeField] private float forwardForce = 1800f;
    [SerializeField] private float reverseForce = 700f;

    [Tooltip("Engine stops pushing above this speed (m/s)")]
    [SerializeField] private float maxForwardSpeed = 7f;
    [SerializeField] private float maxReverseSpeed = 2.5f;

    protected override float ComputeForce(float throttle, float forwardSpeed, float dt)
    {
        if (throttle > 0f && forwardSpeed < maxForwardSpeed) return throttle * forwardForce;
        if (throttle < 0f && forwardSpeed > -maxReverseSpeed) return throttle * reverseForce;
        return 0f;
    }
}
