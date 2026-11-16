using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// CLIENT-ONLY (Non-Owner): Makes remote players' heads look where they're aiming.
/// Arms are handled by animation poses (see PlayerAnimator), not IK.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerHeadLook : NetworkBehaviour
{
    [Header("Rig")]
    [Tooltip("Rig containing a Multi-Aim constraint on the head/neck that targets Head Aim Target")]
    [SerializeField] private Rig lookRig;

    [Header("Targets")]
    [SerializeField] private Transform viewPosition;
    [SerializeField] private Transform headAimTarget;

    [Header("Settings")]
    [SerializeField] private float aimDistance = 10f;

    [Tooltip("How fast the head aim target moves toward the synced aim (degrees/second, 0 = instant)")]
    [SerializeField] private float aimSmoothSpeed = 15f;

    [Tooltip("How fast head look fades in/out when getting in or out of a seat (per second)")]
    [SerializeField] private float blendSpeed = 4f;

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

        // Anyone who joins while a player is already seated starts with head look off:
        if (lookRig != null) lookRig.weight = _state.IsSeated ? 0f : 1f; // late joiners: already seated
        _pitch = _state.AimPitch.Value;
        _yaw = _state.AimYaw.Value;
    }

    private void LateUpdate()
    {
        if (viewPosition == null || headAimTarget == null) return;

        // Seated: fade the head look out and leave the head on the animation.
        float targetWeight = _state.IsSeated ? 0f : 1f;
        if (lookRig != null)
            lookRig.weight = Mathf.MoveTowards(lookRig.weight, targetWeight, blendSpeed * Time.deltaTime);

        // Smooth toward the synced aim (DeltaAngle handles 359° -> 1° wrapping).
        float t = aimSmoothSpeed > 0f ? 1f - Mathf.Exp(-aimSmoothSpeed * Time.deltaTime) : 1f;
        _pitch = Mathf.Lerp(_pitch, _state.AimPitch.Value, t);
        _yaw += Mathf.DeltaAngle(_yaw, _state.AimYaw.Value) * t;

        Vector3 dir = Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;
        headAimTarget.position = viewPosition.position + dir * aimDistance;
    }
}