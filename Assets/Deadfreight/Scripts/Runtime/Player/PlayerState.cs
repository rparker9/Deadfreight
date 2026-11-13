using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synced player values that other machines need.
/// - Movement and aim: written by the OWNER (PlayerMotor), read by everyone.
/// - Held object and vehicle seat: written by the SERVER, read by everyone.
/// Remote clients read these to drive animation and head look.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerState : NetworkBehaviour
{
    private const NetworkVariableReadPermission Everyone = NetworkVariableReadPermission.Everyone;
    private const NetworkVariableWritePermission Owner = NetworkVariableWritePermission.Owner;
    private const NetworkVariableWritePermission Server = NetworkVariableWritePermission.Server;

    // ---- Movement (owner writes) ----
    public readonly NetworkVariable<Vector2> MoveInput = new(Vector2.zero, Everyone, Owner);
    public readonly NetworkVariable<float> Speed = new(0f, Everyone, Owner);
    public readonly NetworkVariable<bool> IsGrounded = new(true, Everyone, Owner);
    public readonly NetworkVariable<bool> IsJumping = new(false, Everyone, Owner);
    public readonly NetworkVariable<bool> IsCrouching = new(false, Everyone, Owner);
    public readonly NetworkVariable<bool> IsSprinting = new(false, Everyone, Owner);

    // ---- Aim (owner writes). Yaw also drives the remote body rotation. ----
    public readonly NetworkVariable<float> AimYaw = new(0f, Everyone, Owner);
    public readonly NetworkVariable<float> AimPitch = new(0f, Everyone, Owner);

    // ---- Held object (server writes; see PlayerInteractor) ----
    public readonly NetworkVariable<NetworkObjectReference> HeldObject = new(default, Everyone, Server);

    // ---- Vehicle seat (server writes; see VehicleSeating) ----
    public readonly NetworkVariable<NetworkObjectReference> SeatVehicle = new(default, Everyone, Server);
    public readonly NetworkVariable<int> SeatIndex = new(-1, Everyone, Server);

    // ---- Helpers ----
    public Vector3 AimDirection => Quaternion.Euler(AimPitch.Value, AimYaw.Value, 0f) * Vector3.forward;
    public Quaternion BodyRotation => Quaternion.Euler(0f, AimYaw.Value, 0f);
    public bool IsMoving => MoveInput.Value.sqrMagnitude > 0.01f;
    public bool IsSeated => SeatIndex.Value >= 0;

    // ---- Server-only API ----
    public void SetHeldObjectServer(NetworkObject heldObj)
    {
        if (!IsServer) return;
        HeldObject.Value = heldObj != null ? new NetworkObjectReference(heldObj) : default;
    }

    public void ClearHeldObjectServer()
    {
        if (!IsServer) return;
        HeldObject.Value = default;
    }

    public void SetSeatServer(NetworkObject vehicle, int seatIndex)
    {
        if (!IsServer) return;
        SeatVehicle.Value = new NetworkObjectReference(vehicle);
        SeatIndex.Value = seatIndex;
    }

    public void ClearSeatServer()
    {
        if (!IsServer) return;
        SeatIndex.Value = -1;
        SeatVehicle.Value = default;
    }
}
