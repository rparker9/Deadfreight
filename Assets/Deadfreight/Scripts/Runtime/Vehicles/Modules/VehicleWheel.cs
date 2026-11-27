using UnityEngine;
using Deadfreight.World;

namespace Deadfreight.Vehicles
{
    /// <summary>
    /// One wheel: raycast suspension + tire forces (server), and wheel mesh spin/position (all machines).
    ///
    /// Place on an empty "mount" transform where the wheel attaches to the chassis:
    ///   - +Z (forward) = rolling direction (steering turns the axle it's under)
    ///   - The wheel hangs below the mount by up to (suspensionTravel + radius)
    /// Tuning lives in a WheelProfile; this component only says what's specific to this wheel.
    /// </summary>
    public sealed class VehicleWheel : VehicleModule
    {
        [SerializeField] private WheelProfile profile;

        [Tooltip("Wheel mesh (child). Spun on its local X, so put the mesh's own rotation on a child of this.")]
        [SerializeField] private Transform visual;

        [Tooltip("Engine force is applied through this wheel")]
        [SerializeField] private bool isDriven = false;

        [Tooltip("The brake acts on this wheel")]
        [SerializeField] private bool hasBrake = false;

        [Tooltip("The handbrake makes this wheel slide sideways (Drift Grip in the profile). Tick on the rear wheels to drift.")]
        [SerializeField] private bool slidesOnHandbrake = false;

        public override int Order => VehicleModuleOrder.Wheels;

        // ---- Inputs (set by other modules each step) ----
        /// <summary>Engine force for this wheel (N). Set by VehiclePower.</summary>
        public float DriveForce { get; set; }
        /// <summary>Brake amount 0..1. Set by VehicleBrakes.</summary>
        public float BrakeInput { get; set; }
        /// <summary>Handbrake amount 0..1: how much of its sideways grip this wheel has lost. Set by VehicleBrakes.</summary>
        public float HandbrakeInput { get; set; }

        // ---- Read-only state ----
        public bool IsDriven => isDriven;
        public bool HasBrake => hasBrake;
        public bool SlidesOnHandbrake => slidesOnHandbrake;
        public bool IsGrounded { get; private set; }
        /// <summary>Force the wheel is carrying right now (N). Useful for sounds, dust, stuck checks.</summary>
        public float Load { get; private set; }
        /// <summary>Surface under the wheel (null = default).</summary>
        public GroundSurface Surface { get; private set; }
        public float Radius => profile != null ? profile.radius : 0f;

        // ---- Debug (server, last physics step) ----
        public float LastSideForce { get; private set; }
        public float LastRollForce { get; private set; }
        public float LastDriveForce { get; private set; }
        public float ContactSpeed { get; private set; }
        public float SlipSpeed { get; private set; }

        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private RaycastHit _hit;
        private float _lastCompression;
        private float _wheelDrop;        // distance from mount down to wheel center
        private float _spinDegrees;
        private Vector3 _lastPosition;

        protected override void OnBind()
        {
            _lastPosition = transform.position;
            _wheelDrop = profile != null ? profile.suspensionTravel : 0f;

            if (profile == null)
                Debug.LogError($"{name}: VehicleWheel has no WheelProfile assigned.", this);
        }

        // ============================================================
        // SIMULATION (server)
        // ============================================================

        public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
        {
            if (profile == null) return;

            Rigidbody rb = Vehicle.Body;
            if (!UpdateContact(dt))
            {
                // Reset values for debug
                LastSideForce = LastRollForce = LastDriveForce = ContactSpeed = SlipSpeed = 0f;
                return;
            }

            Vector3 up = Vehicle.transform.up;
            rb.AddForceAtPosition(up * Load, _hit.point);

            // ---- Tire forces ----
            float grip = Surface != null ? Surface.Grip : 1f;
            float rollMul = Surface != null ? Surface.RollingResistance : 1f;
            float massShare = Vehicle.MassPerWheel;

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _hit.normal).normalized;
            Vector3 side = Vector3.Cross(_hit.normal, forward);
            Vector3 velocity = rb.GetPointVelocity(_hit.point);

            float forwardSpeed = Vector3.Dot(velocity, forward);
            float sideSpeed = Vector3.Dot(velocity, side);

            // Sideways: cancel slip, up to what friction allows (so hard turns / tipping can still slide).
            // The handbrake takes most of that grip away, so the wheel drifts.
            float driftGrip = Mathf.Lerp(1f, profile.driftGrip, HandbrakeInput);
            float sideNeeded = -sideSpeed * massShare / dt;
            float sideMax = profile.lateralFriction * grip * driftGrip * Load;
            float sideForce = Mathf.Clamp(sideNeeded, -sideMax, sideMax);

            // Rolling: resist motion (+brake), but never push it backwards (clamped to what stops it).
            float mu = profile.rollingResistance * rollMul + BrakeInput * profile.brakeFriction * grip;
            float rollNeeded = -forwardSpeed * massShare / dt;
            float rollMax = mu * Load;
            float rollForce = Mathf.Clamp(rollNeeded, -rollMax, rollMax);

            // Drive: push along the wheel, limited by grip (spins out on mud/ice instead of pushing).
            float driveMax = profile.lateralFriction * grip * Load;
            float drive = Mathf.Clamp(DriveForce, -driveMax, driveMax);

            // Tire forces push from a raised point (see WheelProfile.tireForceHeight) to reduce roll.
            Vector3 tirePoint = _hit.point + up * profile.tireForceHeight;
            rb.AddForceAtPosition(side * sideForce + forward * (rollForce + drive), tirePoint);

            // Record values
            LastSideForce = sideForce;
            LastRollForce = rollForce;
            LastDriveForce = drive;
            ContactSpeed = forwardSpeed;
            SlipSpeed = sideSpeed;
        }

        /// <summary>
        /// Sphere-casts for ground and updates suspension load. False if the wheel is in the air.
        /// A sphere (not a ray) touches curbs and edges before the wheel reaches them, so it rolls over
        /// them instead of the body snagging. The cast starts one radius above the mount so it never
        /// begins inside the ground.
        /// </summary>
        private bool UpdateContact(float dt)
        {
            Vector3 up = Vehicle.transform.up;
            float castRadius = profile.radius * profile.castRadiusScale;
            float headroom = profile.radius;
            Vector3 start = transform.position + up * headroom;

            IsGrounded = CastGround(start, castRadius, -up, headroom + profile.suspensionTravel + (profile.radius - castRadius), out _hit);
            if (!IsGrounded)
            {
                Load = 0f;
                Surface = null;
                _lastCompression = 0f;
                _wheelDrop = profile.suspensionTravel;
                return false;
            }

            // How far below the mount the wheel center sits (negative = pushed up past full compression).
            float drop = _hit.distance - headroom - (profile.radius - castRadius);

            // Spring-damper. Past full compression the spring keeps stiffening, acting as a bump stop.
            float compression = profile.suspensionTravel - drop;
            float compressionSpeed = (compression - _lastCompression) / Mathf.Max(dt, 1e-4f);
            _lastCompression = compression;
            _wheelDrop = Mathf.Clamp(drop, 0f, profile.suspensionTravel);

            Load = Mathf.Max(0f, compression * profile.spring + compressionSpeed * profile.damper);
            Surface = GroundSurface.For(_hit.collider);
            return true;
        }

        /// <summary>Ground sphere cast that ignores the vehicle's own colliders (and cargo on its Rigidbody).</summary>
        private bool CastGround(Vector3 origin, float radius, Vector3 dir, float maxDist, out RaycastHit best)
        {
            best = default;
            int count = Physics.SphereCastNonAlloc(origin, radius, dir, _hits, maxDist, profile.groundMask, QueryTriggerInteraction.Ignore);

            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var h = _hits[i];
                if (h.rigidbody == Vehicle.Body) continue;
                if (h.distance <= 0f) continue; // overlapping at the start: no usable contact point
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                    found = true;
                }
            }
            return found;
        }

        // ============================================================
        // PRESENTATION (all machines)
        // ============================================================

        public override void Present(in VehicleNetState state, float dt)
        {
            if (profile == null) return;

            // Clients don't simulate, so raycast here just to place the wheel on the ground.
            if (!Vehicle.IsServer)
                UpdateContact(dt);

            // Spin from how far the mount actually moved.
            Vector3 pos = transform.position;
            float rolled = Vector3.Dot(pos - _lastPosition, transform.forward);
            _lastPosition = pos;

            if (IsGrounded)
                _spinDegrees = (_spinDegrees + rolled / profile.radius * Mathf.Rad2Deg) % 360f;

            if (visual == null) return;

            visual.position = pos - Vehicle.transform.up * _wheelDrop;
            visual.localRotation = Quaternion.Euler(_spinDegrees, 0f, 0f);
        }

        private void OnDrawGizmosSelected()
        {
            if (profile == null) return;

            Vector3 down = -transform.up;
            float travel = profile.suspensionTravel;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position - down * profile.radius, transform.position + down * (travel + profile.radius));
            Gizmos.color = IsGrounded ? Color.green : Color.red;
            Gizmos.DrawWireSphere(transform.position + down * (Application.isPlaying ? _wheelDrop : travel), profile.radius);
        }
    }
}
