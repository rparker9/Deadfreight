using System;
using UnityEngine;

namespace Deadfreight.Vehicles
{
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

        /// <summary>0 (off) .. 1 (full). Also cuts the engine.</summary>
        public float Brake;

        /// <summary>0 (off) .. 1 (full). Rear wheels lose grip to drift; the engine keeps pulling.</summary>
        public float Handbrake;

        public VehicleControls(float throttle, float steer, float brake, float handbrake = 0f)
        {
            Throttle = Mathf.Clamp(throttle, -1f, 1f);
            Steer = Mathf.Clamp(steer, -1f, 1f);
            Brake = Mathf.Clamp01(brake);
            Handbrake = Mathf.Clamp01(handbrake);
        }

        /// <summary>No input, brake fully on. Used when nothing is controlling the vehicle.</summary>
        public static VehicleControls Parked => new VehicleControls(0f, 0f, 1f);
    }
}
