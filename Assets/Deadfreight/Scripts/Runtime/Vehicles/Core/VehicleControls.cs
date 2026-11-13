using System;
using UnityEngine;

/// <summary>
/// What a control source wants the vehicle to do this physics step.
/// Plain data: modules read it, they never care where it came from.
/// </summary>
[Serializable]
public struct VehicleControls
{
    /// <summary>-1 (full reverse) .. 1 (full forward)</summary>
    public float Throttle;

    /// <summary>-1 (full left) .. 1 (full right)</summary>
    public float Steer;

    /// <summary>0 (off) .. 1 (full)</summary>
    public float Brake;

    public VehicleControls(float throttle, float steer, float brake)
    {
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Brake = Mathf.Clamp01(brake);
    }

    /// <summary>No input, brake fully on. Used when nothing is controlling the vehicle.</summary>
    public static VehicleControls Parked => new VehicleControls(0f, 0f, 1f);
}
