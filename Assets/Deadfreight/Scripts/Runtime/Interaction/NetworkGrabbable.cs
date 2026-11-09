using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-AUTHORITATIVE grabbable physics object.
/// Players can grab, carry, drag, and throw these objects.
/// 
/// - Server simulates Rigidbody and replicates via NetworkTransform
/// - Clients keep Rigidbody kinematic (no local simulation)
/// - Player holds object by sending desired carry target position
/// - Server applies spring-damper force toward that target
/// 
/// Requires: NetworkObject + Rigidbody + Collider + NetworkTransform
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public sealed class NetworkGrabbable : NetworkBehaviour
{
    public enum Mode : byte
    {
        Released = 0,
        Carried = 1,
        Dragged = 2
    }

    [Serializable]
    private struct GrabSettings
    {
        [Header("Spring Motion")]
        public float positionSpring;
        public float positionDamping;
        public float maxAcceleration;

        [Header("Break / Safety")]
        public float maxDistance;

        [Header("Physics While Grabbed")]
        public bool disableGravity;
        public float drag;
        public float angularDrag;
    }

    [Header("Carry Settings")]
    [SerializeField]
    private GrabSettings carrySettings = new GrabSettings
    {
        positionSpring = 180f,
        positionDamping = 22f,
        maxAcceleration = 60f,
        maxDistance = 3.0f,
        disableGravity = true,
        drag = 8f,
        angularDrag = 8f
    };

    [Header("Drag Settings")]
    [SerializeField]
    private GrabSettings dragSettings = new GrabSettings
    {
        positionSpring = 90f,
        positionDamping = 18f,
        maxAcceleration = 40f,
        maxDistance = 3.5f,
        disableGravity = false,
        drag = 3f,
        angularDrag = 3f
    };

    private Rigidbody _rb;

    // Who currently holds this object (ulong.MaxValue = nobody)
    public readonly NetworkVariable<ulong> HolderClientId = new(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Current grab mode (Released/Carried/Dragged)
    public readonly NetworkVariable<Mode> GrabMode = new(
        Mode.Released,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // Server-only target position
    private Vector3 _carryTargetPosition;
    private bool _hasCarryTarget;

    // Cached original physics state (server only)
    private bool _originalUseGravity;
    private float _originalDrag;
    private float _originalAngularDrag;

    public bool IsGrabbed => HolderClientId.Value != ulong.MaxValue;
    public float Mass => _rb != null ? _rb.mass : 0f;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        // Server: authoritative physics
        if (!IsServer)
        {
            if (_rb != null)
                _rb.isKinematic = true;
        }
        else
        {
            if (_rb != null)
            {
                _rb.isKinematic = false;
                CacheDefaultPhysics();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            RestoreDefaultPhysics();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;
        if (_rb == null) return;
        if (!IsGrabbed) return;
        if (!_hasCarryTarget) return;

        GrabSettings settings = GetActiveSettings();

        // Auto-release if too far away (prevents infinite tethering)
        float dist = Vector3.Distance(_rb.position, _carryTargetPosition);
        if (dist > settings.maxDistance)
        {
            ServerRelease();
            return;
        }

        // Spring-damper controller (PD control as acceleration)
        Vector3 posError = _carryTargetPosition - _rb.position;
        Vector3 accel = posError * settings.positionSpring + (-_rb.linearVelocity) * settings.positionDamping;

        // Clamp acceleration magnitude
        float mag = accel.magnitude;
        if (mag > settings.maxAcceleration)
            accel = accel * (settings.maxAcceleration / mag);

        _rb.AddForce(accel, ForceMode.Acceleration);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    /// <summary>
    /// Server: Try to grab this object. Returns false if already grabbed or invalid.
    /// </summary>
    public bool ServerTryGrab(ulong holderClientId, Vector3 initialTargetPos, Mode mode)
    {
        if (!IsServer) return false;
        if (_rb == null) return false;
        if (mode == Mode.Released) return false;
        if (IsGrabbed) return false;

        HolderClientId.Value = holderClientId;
        GrabMode.Value = mode;

        _carryTargetPosition = initialTargetPos;
        _hasCarryTarget = true;

        ApplyGrabbedPhysics(GetActiveSettings());
        return true;
    }

    /// <summary>
    /// Server: Update the carry target position (called every frame by holder).
    /// </summary>
    public void ServerUpdateCarryTarget(ulong holderClientId, Vector3 targetPos)
    {
        if (!IsServer) return;
        if (!IsGrabbed) return;
        if (HolderClientId.Value != holderClientId) return;

        _carryTargetPosition = targetPos;
        _hasCarryTarget = true;
    }

    /// <summary>
    /// Server: Change grab mode (Carried <-> Dragged).
    /// </summary>
    public void ServerSetGrabMode(ulong holderClientId, Mode mode)
    {
        if (!IsServer) return;
        if (!IsGrabbed) return;
        if (HolderClientId.Value != holderClientId) return;

        if (mode == Mode.Released)
        {
            ServerRelease();
            return;
        }

        GrabMode.Value = mode;
        ApplyGrabbedPhysics(GetActiveSettings());
    }

    /// <summary>
    /// Server: Release object (stop carrying/dragging).
    /// </summary>
    public void ServerRelease()
    {
        if (!IsServer) return;

        HolderClientId.Value = ulong.MaxValue;
        GrabMode.Value = Mode.Released;
        _hasCarryTarget = false;

        RestoreDefaultPhysics();
    }

    /// <summary>
    /// Server: Throw object with impulse.
    /// </summary>
    public void ServerThrow(ulong holderClientId, Vector3 impulse)
    {
        if (!IsServer) return;
        if (!IsGrabbed) return;
        if (HolderClientId.Value != holderClientId) return;
        if (_rb == null) return;

        // Release first (restores gravity/drag), then apply impulse
        ServerRelease();
        _rb.AddForce(impulse, ForceMode.Impulse);
        _rb.WakeUp();
    }

    // ============================================================
    // INTERNAL PHYSICS MANAGEMENT
    // ============================================================

    private GrabSettings GetActiveSettings()
    {
        return GrabMode.Value switch
        {
            Mode.Dragged => dragSettings,
            Mode.Carried => carrySettings,
            _ => carrySettings
        };
    }

    private void CacheDefaultPhysics()
    {
        _originalUseGravity = _rb.useGravity;
        _originalDrag = _rb.linearDamping;
        _originalAngularDrag = _rb.angularDamping;
    }

    private void ApplyGrabbedPhysics(GrabSettings settings)
    {
        CacheDefaultPhysics();

        _rb.useGravity = !settings.disableGravity ? _originalUseGravity : false;
        _rb.linearDamping = settings.drag;
        _rb.angularDamping = settings.angularDrag;
        _rb.WakeUp();
    }

    private void RestoreDefaultPhysics()
    {
        if (_rb == null) return;

        _rb.useGravity = _originalUseGravity;
        _rb.linearDamping = _originalDrag;
        _rb.angularDamping = _originalAngularDrag;
    }
}