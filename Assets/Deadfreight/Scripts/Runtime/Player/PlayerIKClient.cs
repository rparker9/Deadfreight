using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// CLIENT-ONLY (Non-Owner): Makes remote players' heads look where they're aiming.
/// Arms are handled by animation poses (see PlayerAnimationClient), not IK.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerIKClient : NetworkBehaviour
{
    [Header("Rig")]
    [Tooltip("Rig containing a Multi-Aim constraint on the head/neck that targets Head Aim Target")]
    [SerializeField] private Rig lookRig;

    [Header("Targets")]
    [SerializeField] private Transform viewPosition;
    [SerializeField] private Transform headAimTarget;

    [Header("Settings")]
    [SerializeField] private float aimDistance = 10f;
    [SerializeField] private float aimSmoothSpeed = 15f;

    private PlayerState _state;
    private float _pitch;
    private float _yaw;

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        if (lookRig == null)
            lookRig = GetComponentInChildren<Rig>();
    }

    public override void OnNetworkSpawn()
    {
        // Owner can't see their own body; no need to aim it.
        if (IsOwner)
        {
            if (lookRig != null) lookRig.weight = 0f;
            enabled = false;
            return;
        }

        if (lookRig != null) lookRig.weight = 1f;
        _pitch = _state.AimPitch.Value;
        _yaw = _state.AimYaw.Value;
    }

    private void LateUpdate()
    {
        if (viewPosition == null || headAimTarget == null) return;

        // Smooth toward the synced aim (DeltaAngle handles 359° -> 1° wrapping).
        float t = aimSmoothSpeed > 0f ? 1f - Mathf.Exp(-aimSmoothSpeed * Time.deltaTime) : 1f;
        _pitch = Mathf.Lerp(_pitch, _state.AimPitch.Value, t);
        _yaw += Mathf.DeltaAngle(_yaw, _state.AimYaw.Value) * t;

        Vector3 dir = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
        headAimTarget.position = viewPosition.position + dir * aimDistance;
    }
}