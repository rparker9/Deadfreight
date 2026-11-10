using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-AUTHORITATIVE grabbable object.
///
/// Carried (light objects): the object turns kinematic and follows a hold point in
/// front of the holder at a fixed angle. Simple and predictable; physics returns on drop/throw.
///
/// Dragged (heavy objects): stays a physics object. A spring pulls it toward the holder's
/// target, but upward pull is capped by mass, so heavy objects stay on the ground.
///
/// Clients keep the Rigidbody kinematic; NetworkTransform (Server authority) replicates it.
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

    [Header("Carry")]
    [Tooltip("How quickly the object catches up to the hold point (higher = snappier)")]
    [SerializeField] private float carryFollowSpeed = 20f;

    [Tooltip("Rotation while carried, relative to the holder's facing (degrees)")]
    [SerializeField] private Vector3 carryRotation = Vector3.zero;

    [Header("Drag")]
    [SerializeField] private float dragSpring = 90f;
    [SerializeField] private float dragDamping = 18f;
    [SerializeField] private float dragMaxAcceleration = 40f;

    [Tooltip("Upward force (Newtons) the holder can apply. Lift = force / mass; below gravity it can't leave the ground.")]
    [SerializeField] private float dragLiftForce = 0f;

    [Tooltip("Let go if the object ends up this far (horizontally) from where the holder wants it")]
    [SerializeField] private float dragMaxDistance = 3.5f;

    [SerializeField] private float dragLinearDamping = 3f;
    [SerializeField] private float dragAngularDamping = 3f;

    // Who currently holds this object (ulong.MaxValue = nobody)
    public readonly NetworkVariable<ulong> HolderClientId = new(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public readonly NetworkVariable<Mode> GrabMode = new(
        Mode.Released, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsGrabbed => HolderClientId.Value != ulong.MaxValue;
    public float Mass => _rb != null ? _rb.mass : 0f;

    private Rigidbody _rb;

    // Server-only
    private Vector3 _target;
    private float _holderYaw;
    private float _defaultLinearDamping;
    private float _defaultAngularDamping;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _defaultLinearDamping = _rb.linearDamping;
        _defaultAngularDamping = _rb.angularDamping;
    }

    public override void OnNetworkSpawn()
    {
        // Only the server simulates; clients just display the replicated transform.
        _rb.isKinematic = !IsServer;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsGrabbed) return;

        if (GrabMode.Value == Mode.Carried)
            FollowHoldPoint();
        else if (GrabMode.Value == Mode.Dragged)
            PullTowardTarget();
    }

    // ============================================================
    // CARRY: kinematic follow
    // ============================================================

    private void FollowHoldPoint()
    {
        float t = 1f - Mathf.Exp(-carryFollowSpeed * Time.fixedDeltaTime);
        Quaternion targetRot = Quaternion.Euler(0f, _holderYaw, 0f) * Quaternion.Euler(carryRotation);

        _rb.MovePosition(Vector3.Lerp(_rb.position, _target, t));
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, t));
    }

    // ============================================================
    // DRAG: spring with mass-limited lift
    // ============================================================

    private void PullTowardTarget()
    {
        Vector3 error = _target - _rb.position;

        float maxLift = dragLiftForce / Mathf.Max(_rb.mass, 0.01f);
        bool canLift = maxLift > -Physics.gravity.y;

        // If it can't be lifted, height doesn't count (looking up shouldn't break the grip).
        Vector3 distError = canLift ? error : Vector3.ProjectOnPlane(error, Vector3.up);
        if (distError.magnitude > dragMaxDistance)
        {
            ServerRelease();
            return;
        }

        Vector3 accel = error * dragSpring - _rb.linearVelocity * dragDamping;
        accel = Vector3.ClampMagnitude(accel, dragMaxAcceleration);
        if (accel.y > maxLift) accel.y = maxLift;

        _rb.AddForce(accel, ForceMode.Acceleration);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    public bool ServerTryGrab(ulong holderClientId, Vector3 target, float holderYaw, Mode mode)
    {
        if (!IsServer || mode == Mode.Released || IsGrabbed) return false;

        HolderClientId.Value = holderClientId;
        GrabMode.Value = mode;
        _target = target;
        _holderYaw = holderYaw;

        if (mode == Mode.Carried)
        {
            _rb.isKinematic = true;
        }
        else
        {
            _rb.linearDamping = dragLinearDamping;
            _rb.angularDamping = dragAngularDamping;
            _rb.WakeUp();
        }
        return true;
    }

    /// <summary>Called repeatedly by the holder with where they want the object.</summary>
    public void ServerUpdateTarget(ulong holderClientId, Vector3 target, float holderYaw)
    {
        if (!IsServer || HolderClientId.Value != holderClientId) return;
        _target = target;
        _holderYaw = holderYaw;
    }

    public void ServerRelease()
    {
        if (!IsServer) return;

        HolderClientId.Value = ulong.MaxValue;
        GrabMode.Value = Mode.Released;

        _rb.isKinematic = false;
        _rb.linearDamping = _defaultLinearDamping;
        _rb.angularDamping = _defaultAngularDamping;
        _rb.WakeUp();
    }

    public void ServerThrow(ulong holderClientId, Vector3 impulse)
    {
        if (!IsServer || HolderClientId.Value != holderClientId) return;

        ServerRelease();
        _rb.AddForce(impulse, ForceMode.Impulse);
    }
}