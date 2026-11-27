using UnityEngine;
using Deadfreight.Core;

namespace Deadfreight.Vehicles
{
    /// <summary>
    /// The brake and the handbrake.
    ///
    /// Brake (controls.Brake): acts on every wheel with "Has Brake" ticked. VehiclePower cuts the
    /// engine while it's held, so it always stops the vehicle. How strong it is lives in each
    /// wheel's WheelProfile (Brake Friction).
    ///
    /// Handbrake (controls.Handbrake): for drifting. Wheels with "Slides On Handbrake" ticked (the
    /// rear ones) lose most of their sideways grip (WheelProfile Drift Grip), so steering swings
    /// the back end out. With no gas they also drag, like locked wheels, and the slide only works
    /// when moving. With the gas held they spin instead, like a burnout: no drag, and they keep
    /// sliding even when slow, so holding gas, handbrake and full lock does donuts. The engine
    /// keeps pulling throughout.
    /// </summary>
    public sealed class VehicleBrakes : VehicleModule
    {
        [Header("Brake")]
        [Tooltip("How fast the brake comes on (per second). 0 = instant. 6 = full in about a sixth of a second; a tap gives a lighter brake.")]
        [Unit("x/s")] [SerializeField] private float response = 6f;
        [Tooltip("How fast the brake lets go (per second). 0 = instant.")]
        [Unit("x/s")] [SerializeField] private float releaseResponse = 0f;

        [Header("Handbrake")]

        [Tooltip("Brake on the sliding wheels while the handbrake is held off the gas, 0 to 1: slows the drift a little. 0 = slide freely.")]
        [Range(0f, 1f)][SerializeField] private float handbrakeDrag = 0.2f;

        [Tooltip("How fast the handbrake's slide comes on and goes off (per second). 0 = instant. Lower = grip returns more gently after a drift, so it's easier to catch.")]
        [Unit("x/s")] [SerializeField] private float handbrakeResponse = 5f;

        [Tooltip("Off the gas, below this speed (m/s) the handbrake doesn't slide the wheels; it fades in over the next 2 m/s. Stops slides while crawling. On the gas it always slides.")]
        [Unit("m/s")][SerializeField] private float handbrakeMinSpeed = 4f;

        private float _handbrake;       // server: the handbrake as applied, 0..1

        public override int Order => VehicleModuleOrder.Brakes;

        public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
        {

            bool releasing = controls.Brake < state.Brake;
            state.Brake = Ramp(state.Brake, controls.Brake, releasing ? releaseResponse : response, dt);
            // Handbrake with no gas: the rear wheels lock, slide and slow down (only when moving).
            // Handbrake with gas: they spin instead, so they keep sliding even when slow, and don't drag: donuts.
            float gas = Mathf.Clamp01(controls.Throttle);
            float fast = Mathf.InverseLerp(handbrakeMinSpeed, handbrakeMinSpeed + 2f, Mathf.Abs(Vehicle.ForwardSpeed));
            float slide = controls.Handbrake * Mathf.Max(fast, gas);
            _handbrake = Ramp(_handbrake, slide, handbrakeResponse, dt);

            foreach (var wheel in Vehicle.Wheels)
            {
                float brake = wheel.HasBrake ? state.Brake : 0f;
                if (wheel.SlidesOnHandbrake)
                    brake = Mathf.Max(brake, _handbrake * handbrakeDrag * (1f - gas));

                wheel.BrakeInput = brake;
                wheel.HandbrakeInput = wheel.SlidesOnHandbrake ? _handbrake : 0f;
            }
        }

        /// <summary>Moves toward the target at a rate per second; a rate of 0 jumps straight there.</summary>
        private static float Ramp(float current, float target, float rate, float dt) =>
            rate > 0f ? Mathf.MoveTowards(current, target, rate * dt) : target;
    }
}
