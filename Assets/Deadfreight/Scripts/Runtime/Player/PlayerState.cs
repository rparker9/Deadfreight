using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Synced player values that other machines need.
/// - Movement and aim: written by the OWNER (PlayerMotor), read by everyone.
/// - Grabbed object: written by the SERVER (PlayerGrabController), read by everyone.
/// Remote clients read these to drive animation and IK.
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

    // ---- Equipment (server writes) ----
    public readonly NetworkVariable<NetworkObjectReference> GrabbedObject = new(default, Everyone, Server);

    // ---- Helpers ----
    public Vector3 AimDirection => Quaternion.Euler(AimPitch.Value, AimYaw.Value, 0f) * Vector3.forward;
    public Quaternion BodyRotation => Quaternion.Euler(0f, AimYaw.Value, 0f);
    public bool IsMoving => MoveInput.Value.sqrMagnitude > 0.01f;

    private HandGripPoints _cachedGripPoints;

    /// <summary>HandGripPoints of the currently grabbed object (for hand IK).</summary>
    public HandGripPoints GrabbedGripPoints
    {
        get
        {
            if (_cachedGripPoints == null && GrabbedObject.Value.TryGet(out NetworkObject netObj))
                _cachedGripPoints = netObj.GetComponent<HandGripPoints>();
            return _cachedGripPoints;
        }
    }

    public override void OnNetworkSpawn()
    {
        GrabbedObject.OnValueChanged += OnGrabbedObjectChanged;
    }

    public override void OnNetworkDespawn()
    {
        GrabbedObject.OnValueChanged -= OnGrabbedObjectChanged;
    }

    private void OnGrabbedObjectChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        _cachedGripPoints = null; // re-resolve on next read
    }

    // ---- Server-only API ----
    public void SetGrabbedObjectServer(NetworkObject grabbedObj)
    {
        if (!IsServer) return;
        GrabbedObject.Value = grabbedObj != null ? new NetworkObjectReference(grabbedObj) : default;
    }

    public void ClearGrabbedObjectServer()
    {
        if (!IsServer) return;
        GrabbedObject.Value = default;
    }
}
