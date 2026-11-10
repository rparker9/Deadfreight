using UnityEngine;

/// <summary>
/// One wagon wheel: raycast suspension + tire forces. Driven by Wagon (not self-updating).
///
/// Place this on an empty "mount" transform where the wheel attaches to the chassis:
///   - +Z (forward) = rolling direction (Wagon turns the front axle for steering)
///   - The wheel hangs below the mount by up to (suspensionTravel + radius)
/// The wheel mesh goes in "visual" (a child), with its axle along local X.
/// </summary>
public sealed class WagonWheel : MonoBehaviour
{
    [Header("Geometry")]
    [SerializeField] private float radius = 0.4f;
    [Tooltip("How far the wheel can drop below its mount (m). Wooden wagons barely flex: keep small.")]
    [SerializeField] private float suspensionTravel = 0.08f;
    [SerializeField] private Transform visual;

    [Header("Suspension")]
    [Tooltip("Spring stiffness (N/m). Rough start: (total mass * 9.81 / wheels) / (travel * 0.3)")]
    [SerializeField] private float spring = 20000f;
    [Tooltip("Damping (N per m/s). Too low = bouncy, too high = harsh.")]
    [SerializeField] private float damper = 1200f;

    [Header("Tire")]
    [Tooltip("Grip against sliding sideways (friction coefficient). This is what makes it go where the wheels point.")]
    [SerializeField] private float lateralFriction = 1.2f;
    [Tooltip("Rolling resistance as a fraction of load. Road ~0.02-0.04; the surface multiplies this.")]
    [SerializeField] private float rollingResistance = 0.03f;

    [Header("Drive")]
    [Tooltip("Engine force is applied through this wheel")]
    [SerializeField] private bool isDriven = false;

    [Header("Brake")]
    [SerializeField] private bool hasBrake = false;
    [Tooltip("Friction coefficient when the brake is fully on")]
    [SerializeField] private float brakeFriction = 0.8f;

    [Header("Ground")]
    [SerializeField] private LayerMask groundMask = ~0;

    // ---- Runtime (read-only) ----
    public bool IsGrounded { get; private set; }
    /// <summary>Force the wheel is carrying right now (N). Useful for "stuck" checks, sounds, dust.</summary>
    public float Load { get; private set; }
    /// <summary>Surface under the wheel (null = default).</summary>
    public WagonSurface Surface { get; private set; }
    public float Radius => radius;
    public bool IsDriven => isDriven;
    /// <summary>Engine force for this wheel (N), set by Wagon each physics step.</summary>
    public float DriveForce { get; set; }

    private readonly RaycastHit[] _hits = new RaycastHit[8];
    private RaycastHit _hit;
    private float _lastCompression;
    private float _wheelDrop;        // distance from mount down to wheel center
    private float _spinDegrees;
    private Vector3 _lastPosition;

    private void Awake()
    {
        _lastPosition = transform.position;
        _wheelDrop = suspensionTravel;
    }

    // ============================================================
    // SIMULATION
    // ============================================================

    /// <summary>
    /// Raycast suspension; on the server also applies forces to the chassis.
    /// massShare = how much of the wagon's mass this wheel is responsible for stopping (mass / wheel count).
    /// </summary>
    public void Simulate(Rigidbody rb, float massShare, float brake, float dt, bool applyForces)
    {
        Vector3 up = rb.transform.up;
        float maxDist = suspensionTravel + radius;

        IsGrounded = CastGround(rb, transform.position, -up, maxDist, out _hit);
        if (!IsGrounded)
        {
            Load = 0f;
            Surface = null;
            _lastCompression = 0f;
            _wheelDrop = suspensionTravel;
            return;
        }

        // ---- Suspension (spring-damper) ----
        float compression = maxDist - _hit.distance;                 // 0 .. travel(+)
        float compressionSpeed = (compression - _lastCompression) / dt;
        _lastCompression = compression;
        _wheelDrop = _hit.distance - radius;

        float suspension = Mathf.Max(0f, compression * spring + compressionSpeed * damper);
        Load = suspension;
        Surface = WagonSurface.For(_hit.collider);

        if (!applyForces) return;

        rb.AddForceAtPosition(up * suspension, _hit.point);

        // ---- Tire forces ----
        float grip = Surface != null ? Surface.Grip : 1f;
        float rollMul = Surface != null ? Surface.RollingResistance : 1f;

        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, _hit.normal).normalized;
        Vector3 side = Vector3.Cross(_hit.normal, forward);
        Vector3 velocity = rb.GetPointVelocity(_hit.point);

        float forwardSpeed = Vector3.Dot(velocity, forward);
        float sideSpeed = Vector3.Dot(velocity, side);

        // Sideways: cancel slip, up to what friction allows (so hard turns / tipping can still slide).
        float sideNeeded = -sideSpeed * massShare / dt;
        float sideMax = lateralFriction * grip * Load;
        float sideForce = Mathf.Clamp(sideNeeded, -sideMax, sideMax);

        // Rolling: resist motion (+brake), but never push it backwards (clamped to what stops it).
        float mu = rollingResistance * rollMul + (hasBrake ? brake * brakeFriction * grip : 0f);
        float rollNeeded = -forwardSpeed * massShare / dt;
        float rollMax = mu * Load;
        float rollForce = Mathf.Clamp(rollNeeded, -rollMax, rollMax);

        // Engine: push along the wheel, limited by grip (spins out on mud/ice instead of pushing).
        float driveMax = lateralFriction * grip * Load;
        float drive = isDriven ? Mathf.Clamp(DriveForce, -driveMax, driveMax) : 0f;

        rb.AddForceAtPosition(side * sideForce + forward * (rollForce + drive), _hit.point);
    }

    /// <summary>Ground raycast that ignores the wagon's own colliders.</summary>
    private bool CastGround(Rigidbody self, Vector3 origin, Vector3 dir, float maxDist, out RaycastHit best)
    {
        best = default;
        int count = Physics.RaycastNonAlloc(origin, dir, _hits, maxDist, groundMask, QueryTriggerInteraction.Ignore);

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            var h = _hits[i];
            if (h.rigidbody == self) continue;   // our own chassis / cargo on it
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
    // VISUALS (all machines)
    // ============================================================

    /// <summary>Positions and spins the wheel mesh from how far the mount actually moved.</summary>
    public void UpdateVisual(Vector3 chassisUp)
    {
        Vector3 pos = transform.position;
        float rolled = Vector3.Dot(pos - _lastPosition, transform.forward);
        _lastPosition = pos;

        if (IsGrounded)
            _spinDegrees = (_spinDegrees + rolled / radius * Mathf.Rad2Deg) % 360f;

        if (visual == null) return;

        visual.position = pos - chassisUp * _wheelDrop;
        visual.localRotation = Quaternion.Euler(_spinDegrees, 0f, 0f);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 down = -transform.up;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, transform.position + down * (suspensionTravel + radius));
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + down * (Application.isPlaying ? _wheelDrop : suspensionTravel), radius);
    }
}
