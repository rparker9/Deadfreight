using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Core of a wheeled vehicle. SERVER-AUTHORITATIVE.
///
/// Owns the Rigidbody, the synced state and the physics step. All behaviour lives in
/// VehicleModules (steering, power, brakes, wheels...) found on this object and its children.
/// Each physics step the server:
///   1. asks the control source for VehicleControls (none = parked)
///   2. runs every module's Simulate, in Order
///   3. syncs VehicleNetState and refreshes Telemetry / raises events
/// Every frame, on every machine, modules Present (visuals) from the synced state.
///
/// Requires: NetworkObject + Rigidbody + NetworkTransform (Server authority, position + rotation).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed partial class Vehicle : NetworkBehaviour
{
    [Header("Body")]
    [Tooltip("Center of mass relative to the vehicle root. Lower = harder to tip.")]
    [SerializeField] private Vector3 centerOfMass = new Vector3(0f, 0.55f, 0f);
    [SerializeField] private float maxAngularVelocity = 7f;

    [Header("Events")]
    [Tooltip("Collisions at least this fast (m/s) raise Impacted")]
    [SerializeField] private float impactSpeedThreshold = 3f;
    [Tooltip("Tilt (degrees) that counts as tipped over")]
    [SerializeField] private float tipAngle = 60f;
    [Tooltip("Below this speed (m/s) with throttle applied, the vehicle counts as stuck")]
    [SerializeField] private float stuckSpeed = 0.05f;

    // ---- Events (SERVER only) ----
    /// <summary>
    /// Hit something at or above impactSpeedThreshold.
    /// </summary>
    public event Action<VehicleImpact> Impacted;
   
    /// <summary>
    /// Tilted past tipAngle.
    /// </summary>
    public event Action Tipped;
    
    /// <summary>
    /// Back below tipAngle after tipping.
    /// </summary>
    public event Action Righted;

    // ---- Read-only state ----
    public Rigidbody Body => _rb;
    public IReadOnlyList<VehicleWheel> Wheels => _wheels;
    
    /// <summary>
    /// Server: refreshed every physics step.
    /// </summary>
    public VehicleTelemetry Telemetry { get; private set; }
    
    /// <summary>
    /// Synced visual state (steer angle, brake...).
    /// </summary>
    public VehicleNetState State => IsServer ? _working : _netState.Value;
    
    /// <summary>
    /// Share of the vehicle's mass each wheel is responsible for stopping.
    /// </summary>
    public float MassPerWheel => _rb.mass / Mathf.Max(1, _wheels.Count);
    public float ForwardSpeed => Vector3.Dot(_rb.linearVelocity, transform.forward);
    public bool HasControlSource => _controlSource != null;

    private readonly NetworkVariable<VehicleNetState> _netState = new(
        default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Rigidbody _rb;
    private readonly List<VehicleModule> _modules = new();
    private readonly List<VehicleWheel> _wheels = new();
    private IVehicleControlSource _controlSource;
    private VehicleControls _lastControls = VehicleControls.Parked;
    private VehicleNetState _working;
    private bool _tipped;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.centerOfMass = centerOfMass;
        _rb.maxAngularVelocity = maxAngularVelocity;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        CollectModules();
    }

    public override void OnNetworkSpawn()
    {
        _rb.isKinematic = !IsServer;
        if (IsServer) _working = _netState.Value;
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        float dt = Time.fixedDeltaTime;
        _lastControls = _controlSource != null ? _controlSource.GetControls() : VehicleControls.Parked;

        foreach (var module in _modules)
            module.Simulate(in _lastControls, ref _working, dt);

        if (!_working.Equals(_netState.Value))
            _netState.Value = _working;

        UpdateTelemetry();
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;

        VehicleNetState state = State;
        float dt = Time.deltaTime;

        foreach (var module in _modules)
            module.Present(in state, dt);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;

        float speed = collision.relativeVelocity.magnitude;
        if (speed < impactSpeedThreshold) return;

        ContactPoint contact = collision.GetContact(0);
        var impact = new VehicleImpact
        {
            Point = contact.point,
            Normal = contact.normal,
            Speed = speed,
            Impulse = collision.impulse.magnitude,
            Other = collision.collider
        };

        RecordImpactForDebug(impact);
        Impacted?.Invoke(impact);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    /// <summary>
    /// Hand control to a source (a driver, an AI). Replaces any current source.
    /// </summary>
    public void ServerSetControlSource(IVehicleControlSource source)
    {
        if (!IsServer) return;
        _controlSource = source;
    }

    /// <summary>
    /// Remove a source, if it's the current one. The vehicle parks (brake on).
    /// </summary>
    public void ServerClearControlSource(IVehicleControlSource source)
    {
        if (!IsServer || _controlSource != source) return;
        _controlSource = null;
    }

    /// <summary>
    /// Push or pull the vehicle at a world point (players, creatures, explosions).
    /// </summary>
    public void ServerApplyForce(Vector3 force, Vector3 worldPoint)
    {
        if (!IsServer) return;
        _rb.AddForceAtPosition(force, worldPoint);
    }

    /// <summary>
    /// First module of a type, e.g. GetModule&lt;VehicleSteering&gt;(). Null if absent.
    /// </summary>
    public T GetModule<T>() where T : VehicleModule
    {
        foreach (var module in _modules)
            if (module is T match) return match;
        return null;
    }

    // ============================================================
    // INTERNALS
    // ============================================================

    private void CollectModules()
    {
        _modules.Clear();
        _modules.AddRange(GetComponentsInChildren<VehicleModule>(true));

        // Stable sort by Order (keeps hierarchy order for equal Order, e.g. the four wheels).
        for (int i = 0; i < _modules.Count; i++) _modules[i].SortIndex = i;
        _modules.Sort((a, b) => a.Order != b.Order ? a.Order.CompareTo(b.Order) : a.SortIndex.CompareTo(b.SortIndex));

        _wheels.Clear();
        foreach (var module in _modules)
        {
            module.Bind(this);
            if (module is VehicleWheel wheel) _wheels.Add(wheel);
        }
    }

    private void UpdateTelemetry()
    {
        int grounded = 0;
        foreach (var wheel in _wheels)
            if (wheel.IsGrounded) grounded++;

        float tilt = Vector3.Angle(transform.up, Vector3.up);
        Vector3 velocity = _rb.linearVelocity;

        Telemetry = new VehicleTelemetry
        {
            Velocity = velocity,
            ForwardSpeed = ForwardSpeed,
            TiltAngle = tilt,
            WheelsGrounded = grounded,
            WheelCount = _wheels.Count,
            IsStuck = Mathf.Abs(_lastControls.Throttle) > 0.1f
                      && grounded == _wheels.Count
                      && velocity.sqrMagnitude < stuckSpeed * stuckSpeed
        };

        UpdateDebugBroadcast();

        // Tipping, with 10° of hysteresis so it doesn't flicker at the threshold.
        if (!_tipped && tilt > tipAngle)
        {
            _tipped = true;
            Tipped?.Invoke();
        }
        else if (_tipped && tilt < tipAngle - 10f)
        {
            _tipped = false;
            Righted?.Invoke();
        }
    }
}
