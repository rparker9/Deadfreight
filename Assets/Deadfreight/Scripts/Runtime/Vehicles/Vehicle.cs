using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// SERVER-AUTHORITATIVE wheeled vehicle (wagon, carriage, ...). The server simulates the chassis Rigidbody and wheels;
/// clients see it through NetworkTransform (Server authority) and only run wheel visuals.
///
/// Others (players, creatures) affect it through the Server API:
///   ServerApplyForce, ServerSetSteer, ServerSetBrake.
///
/// Requires: NetworkObject + Rigidbody + NetworkTransform (sync position + rotation, interpolate).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class Vehicle : NetworkBehaviour
{
    [Header("Wheels")]
    [SerializeField] private VehicleWheel[] wheels;

    [Header("Steering")]
    [Tooltip("Pivoting front axle (turntable). Front wheel mounts must be its children.")]
    [SerializeField] private Transform frontAxle;
    [SerializeField] private float maxSteerAngle = 35f;
    [Tooltip("Degrees per second the axle can turn")]
    [SerializeField] private float steerSpeed = 90f;

    [Header("Engine")]
    [Tooltip("Total forward force at full throttle (N), split across driven wheels")]
    [SerializeField] private float engineForce = 1800f;
    [SerializeField] private float reverseForce = 700f;
    [Tooltip("Engine stops pushing above this speed (m/s)")]
    [SerializeField] private float maxForwardSpeed = 7f;
    [SerializeField] private float maxReverseSpeed = 2.5f;

    [Header("Body")]
    [Tooltip("Center of mass relative to the vehicle root. Lower = harder to tip.")]
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, -0.2f, 0f);
    [SerializeField] private float maxAngularVelocity = 7f;

    [Header("Debug (host only)")]
    [Tooltip("Arrows: push / steer. B: brake. For tuning before player pushing exists.")]
    [SerializeField] private bool debugControls = true;
    [SerializeField] private float debugPushForce = 1000f;

    /// <summary>Front axle angle (degrees). Server writes; everyone renders.</summary>
    public readonly NetworkVariable<float> SteerAngle = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    /// <summary>Brake amount 0..1. Server writes.</summary>
    public readonly NetworkVariable<float> Brake = new(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public Rigidbody Body => _rb;
    public VehicleWheel[] Wheels => wheels;

    private Rigidbody _rb;
    private float _steerTarget;
    private float _throttle;
    private bool _hasDriver;
    private bool _debugBrakeHeld;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.centerOfMass = centerOfMass;
        _rb.maxAngularVelocity = maxAngularVelocity;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    public override void OnNetworkSpawn()
    {
        _rb.isKinematic = !IsServer;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        float dt = Time.fixedDeltaTime;

        if (debugControls && !_hasDriver) ApplyDebugControls();

        ApplyEngine();

        // Steering: turn the axle toward the target at a limited rate.
        SteerAngle.Value = Mathf.MoveTowards(SteerAngle.Value, _steerTarget, steerSpeed * dt);
        if (frontAxle != null)
            frontAxle.localRotation = Quaternion.Euler(0f, SteerAngle.Value, 0f);

        float massShare = _rb.mass / Mathf.Max(1, wheels.Length);
        foreach (var wheel in wheels)
            wheel.Simulate(_rb, massShare, Brake.Value, dt, applyForces: true);
    }

    private void Update()
    {
        // Clients: raycast wheels for visuals only (the server already did it in FixedUpdate).
        if (IsServer || !IsSpawned) return;

        foreach (var wheel in wheels)
            wheel.Simulate(_rb, 0f, 0f, Mathf.Max(Time.deltaTime, 1e-4f), applyForces: false);
    }

    private void LateUpdate()
    {
        if (!IsServer && frontAxle != null)
            frontAxle.localRotation = Quaternion.Euler(0f, SteerAngle.Value, 0f);

        Vector3 up = transform.up;
        foreach (var wheel in wheels)
            wheel.UpdateVisual(up);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    /// <summary>Push/pull the vehicle at a world point (e.g. where a player's hands are).</summary>
    public void ServerApplyForce(Vector3 force, Vector3 worldPoint)
    {
        if (!IsServer) return;
        _rb.AddForceAtPosition(force, worldPoint);
    }

    /// <summary>Target front axle angle in degrees (clamped). Turns at steerSpeed.</summary>
    public void ServerSetSteer(float degrees)
    {
        if (!IsServer) return;
        _steerTarget = Mathf.Clamp(degrees, -maxSteerAngle, maxSteerAngle);
    }

    public void ServerSetBrake(float amount)
    {
        if (!IsServer) return;
        Brake.Value = Mathf.Clamp01(amount);
    }

    /// <summary>Driver controls from VehicleSeating. Throttle/steer are -1..1.</summary>
    public void ServerSetDriverInput(float throttle, float steer, bool brake)
    {
        if (!IsServer) return;
        _hasDriver = true;
        _throttle = Mathf.Clamp(throttle, -1f, 1f);
        _steerTarget = Mathf.Clamp(steer, -1f, 1f) * maxSteerAngle;

        float b = brake ? 1f : 0f;
        if (Brake.Value != b) Brake.Value = b;
    }

    /// <summary>Driver left: hand controls back (debug keys work again).</summary>
    public void ServerClearDriver()
    {
        if (!IsServer) return;
        _hasDriver = false;
        _throttle = 0f;
        _steerTarget = 0f;
    }

    /// <summary>Splits throttle across driven wheels, with a soft speed limit.</summary>
    private void ApplyEngine()
    {
        int driven = 0;
        foreach (var w in wheels) if (w.IsDriven) driven++;
        if (driven == 0) return;

        float speed = Vector3.Dot(_rb.linearVelocity, transform.forward);
        float force = 0f;

        if (_throttle > 0f && speed < maxForwardSpeed) force = _throttle * engineForce;
        else if (_throttle < 0f && speed > -maxReverseSpeed) force = _throttle * reverseForce;

        float perWheel = force / driven;
        foreach (var w in wheels)
            w.DriveForce = w.IsDriven ? perWheel : 0f;
    }

    /// <summary>True if every wheel is loaded but the vehicle isn't moving (e.g. stuck in mud).</summary>
    public bool IsStuck(float speedThreshold = 0.05f)
    {
        if (_rb.linearVelocity.sqrMagnitude > speedThreshold * speedThreshold) return false;
        foreach (var w in wheels)
            if (!w.IsGrounded) return false;
        return true;
    }

    // ============================================================
    // DEBUG
    // ============================================================

    private void ApplyDebugControls()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float push = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
        float steer = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);

        if (push != 0f)
        {
            // Pull from the front axle, like a team on the tongue.
            Vector3 point = frontAxle != null ? frontAxle.position : _rb.worldCenterOfMass;
            Vector3 dir = frontAxle != null ? frontAxle.forward : transform.forward;
            _rb.AddForceAtPosition(dir * push * debugPushForce, point);
        }

        _steerTarget = steer * maxSteerAngle;

        bool brakeHeld = kb.bKey.isPressed;
        if (brakeHeld != _debugBrakeHeld)
        {
            _debugBrakeHeld = brakeHeld;
            Brake.Value = brakeHeld ? 1f : 0f;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(transform.TransformPoint(centerOfMass), 0.08f);
    }
}
