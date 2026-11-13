using Unity.Netcode;
using UnityEngine;

/// <summary>
/// An object a player can pick up (E), carry in front of them, drop (E) and throw (Q).
/// SERVER-AUTHORITATIVE: while held, the server moves it kinematically to the holder's
/// hold point at a fixed angle; physics returns when it's dropped or thrown.
/// Clients keep the Rigidbody kinematic; NetworkTransform (Server authority) replicates it.
///
/// Requires: NetworkObject + Rigidbody + Collider + NetworkTransform
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public sealed class Pickup : Interactable
{
    [Header("Carry")]
    [Tooltip("How quickly the object catches up to the hold point (higher = snappier)")]
    [SerializeField] private float carryFollowSpeed = 20f;

    [Tooltip("Rotation while carried, relative to the holder's facing (degrees)")]
    [SerializeField] private Vector3 carryRotation = Vector3.zero;

    /// <summary>Who is holding this (ulong.MaxValue = nobody).</summary>
    public readonly NetworkVariable<ulong> HolderClientId = new(
        NoHolder, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool IsHeld => HolderClientId.Value != NoHolder;

    protected override string DefaultVerb => "Pick up";

    private const ulong NoHolder = ulong.MaxValue;

    private Rigidbody _rb;

    // Server-only
    private Vector3 _target;
    private float _holderYaw;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        // Only the server simulates; clients just display the replicated transform.
        _rb.isKinematic = !IsServer || IsHeld;
    }

    private void FixedUpdate()
    {
        if (!IsServer || !IsHeld) return;

        float t = 1f - Mathf.Exp(-carryFollowSpeed * Time.fixedDeltaTime);
        Quaternion targetRot = Quaternion.Euler(0f, _holderYaw, 0f) * Quaternion.Euler(carryRotation);

        _rb.MovePosition(Vector3.Lerp(_rb.position, _target, t));
        _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, t));
    }

    // ============================================================
    // INTERACTABLE
    // ============================================================

    public override bool CanInteractLocal(PlayerInteractor player) => !IsHeld;

    public override bool CanInteractServer(PlayerInteractor player, ulong clientId) => !IsHeld;

    protected override void OnServerInteract(PlayerInteractor player, ulong clientId)
    {
        player.ServerPickUp(this);
    }

    // ============================================================
    // SERVER API (used by PlayerInteractor)
    // ============================================================

    public bool ServerTryHold(ulong holderClientId, Vector3 target, float holderYaw)
    {
        if (!IsServer || IsHeld) return false;

        HolderClientId.Value = holderClientId;
        _target = target;
        _holderYaw = holderYaw;
        _rb.isKinematic = true;
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

        HolderClientId.Value = NoHolder;
        _rb.isKinematic = false;
        _rb.WakeUp();
    }

    public void ServerThrow(ulong holderClientId, Vector3 impulse)
    {
        if (!IsServer || HolderClientId.Value != holderClientId) return;

        ServerRelease();
        _rb.AddForce(impulse, ForceMode.Impulse);
    }
}
