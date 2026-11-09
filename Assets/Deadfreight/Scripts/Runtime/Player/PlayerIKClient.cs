using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// CLIENT-ONLY (Non-Owner): Drives IK rigs based on PlayerState.
/// Handles aim IK (spine/head) and hand IK (for grabbed objects).
/// Only runs for REMOTE players.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerIKClient : NetworkBehaviour
{
    [Header("Rig")]
    [SerializeField] private Rig aimRig;

    [Header("Aim IK")]
    [SerializeField] private Transform viewPosition;
    [SerializeField] private Transform aimTarget;
    [SerializeField] private Transform headAimTarget;

    [Header("Aim Settings")]
    [SerializeField] private float aimDistance = 10f;
    [SerializeField] private float aimHeightOffset = 0f;
    [SerializeField] private float aimSmoothSpeed = 15f;

    [Header("Hand IK")]
    [SerializeField] private Transform rightHandTarget;
    [SerializeField] private Transform leftHandTarget;
    [SerializeField] private bool useRightHandIK = true;
    [SerializeField] private bool useLeftHandIK = true;

    [Header("Hand Offsets")]
    [SerializeField] private Vector3 rightHandPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 leftHandPositionOffset = Vector3.zero;
    [SerializeField] private Vector3 rightHandRotationOffset = Vector3.zero;
    [SerializeField] private Vector3 leftHandRotationOffset = Vector3.zero;

    [Header("Smoothing")]
    [SerializeField] private float positionSmoothTime = 0.1f;
    [SerializeField] private float rotationSmoothSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool showDebugGizmos = false;

    private PlayerState _state;

    // Smoothed aim
    private float _currentPitch;
    private float _currentYaw;

    // Hand smoothing
    private Vector3 _rightHandVelocity;
    private Vector3 _leftHandVelocity;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _state = GetComponent<PlayerState>();

        if (aimRig == null)
            aimRig = GetComponentInChildren<Rig>();
    }

    public override void OnNetworkSpawn()
    {
        // Owner uses FPS arms, disable IK
        if (IsOwner)
        {
            if (aimRig != null) aimRig.weight = 0f;
            enabled = false;
            return;
        }

        // Enable IK for remote players
        if (aimRig != null) aimRig.weight = 1f;

        // Subscribe to grabbed object changes
        _state.GrabbedObject.OnValueChanged += OnGrabbedObjectChanged;

        // Initialize current angles
        _currentPitch = _state.AimPitch.Value;
        _currentYaw = _state.AimYaw.Value;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            _state.GrabbedObject.OnValueChanged -= OnGrabbedObjectChanged;
        }
    }

    private void OnGrabbedObjectChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        // Reset hand velocities when switching objects
        _rightHandVelocity = Vector3.zero;
        _leftHandVelocity = Vector3.zero;
    }

    // ============================================================
    // UPDATE
    // ============================================================

    private void LateUpdate()
    {
        if (IsOwner) return;

        UpdateAimIK();
        UpdateHandIK();
    }

    private void UpdateAimIK()
    {
        if (viewPosition == null || aimTarget == null) return;

        // Get target angles from state
        float targetYaw = _state.AimYaw.Value;
        float targetPitch = _state.AimPitch.Value;

        // Smooth both angles
        if (aimSmoothSpeed > 0f)
        {
            float t = 1f - Mathf.Exp(-aimSmoothSpeed * Time.deltaTime);
            _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, t);

            // Handle yaw wrapping (e.g., 359° -> 1° should interpolate through 360°, not backwards)
            float yawDelta = Mathf.DeltaAngle(_currentYaw, targetYaw);
            _currentYaw += yawDelta * t;

            // Normalize to 0-360
            if (_currentYaw < 0f) _currentYaw += 360f;
            if (_currentYaw >= 360f) _currentYaw -= 360f;
        }
        else
        {
            _currentPitch = targetPitch;
            _currentYaw = targetYaw;
        }

        // Calculate aim direction using ABSOLUTE angles (world space)
        // This ensures the target orbits correctly as the character rotates
        Quaternion aimRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
        Vector3 aimDir = aimRotation * Vector3.forward;

        // Position aim target at distance from view position
        Vector3 targetPos = viewPosition.position + aimDir * aimDistance;
        targetPos.y += aimHeightOffset;

        aimTarget.position = targetPos;

        if (headAimTarget != null)
            headAimTarget.position = targetPos;
    }

    private void UpdateHandIK()
    {
        // Get grabbed object's grip points
        HandGripPoints gripPoints = _state.GrabbedGripPoints;
        if (gripPoints == null)
        {
            // No object grabbed, hands return to idle pose
            return;
        }

        // Get both hand grips
        var grips = gripPoints.GetBothHandGrips();

        // Update right hand
        if (useRightHandIK && rightHandTarget != null && grips.right?.transform != null)
        {
            UpdateHandTarget(
                rightHandTarget,
                grips.right.transform,
                rightHandPositionOffset,
                rightHandRotationOffset,
                ref _rightHandVelocity);
        }

        // Update left hand
        if (useLeftHandIK && leftHandTarget != null && grips.left?.transform != null)
        {
            UpdateHandTarget(
                leftHandTarget,
                grips.left.transform,
                leftHandPositionOffset,
                leftHandRotationOffset,
                ref _leftHandVelocity);
        }
    }

    private void UpdateHandTarget(
        Transform handTarget,
        Transform gripPoint,
        Vector3 posOffset,
        Vector3 rotOffset,
        ref Vector3 velocity)
    {
        // Calculate target position and rotation
        Vector3 targetPos = gripPoint.position + gripPoint.TransformDirection(posOffset);
        Quaternion targetRot = gripPoint.rotation * Quaternion.Euler(rotOffset);

        // Smooth position
        if (positionSmoothTime > 0f)
        {
            handTarget.position = Vector3.SmoothDamp(
                handTarget.position,
                targetPos,
                ref velocity,
                positionSmoothTime);
        }
        else
        {
            handTarget.position = targetPos;
        }

        // Smooth rotation
        if (rotationSmoothSpeed > 0f)
        {
            handTarget.rotation = Quaternion.Slerp(
                handTarget.rotation,
                targetRot,
                Time.deltaTime * rotationSmoothSpeed);
        }
        else
        {
            handTarget.rotation = targetRot;
        }
    }

    // ============================================================
    // PUBLIC API
    // ============================================================

    public void SetHandIKEnabled(bool right, bool left)
    {
        useRightHandIK = right;
        useLeftHandIK = left;
    }

    public void SetRigWeight(float weight)
    {
        if (aimRig != null && !IsOwner)
            aimRig.weight = weight;
    }

    // ============================================================
    // DEBUG VISUALIZATION
    // ============================================================

    private void OnDrawGizmos()
    {
        if (!showDebugGizmos) return;
        if (!Application.isPlaying) return;
        if (IsOwner) return; // Only show for remote players

        // Draw view position
        if (viewPosition != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(viewPosition.position, 0.1f);
        }

        // Draw aim target
        if (aimTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(aimTarget.position, 0.15f);

            // Draw line from view to aim target
            if (viewPosition != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(viewPosition.position, aimTarget.position);
            }
        }

        // Draw hand targets
        if (rightHandTarget != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(rightHandTarget.position, 0.08f);
        }

        if (leftHandTarget != null)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawWireSphere(leftHandTarget.position, 0.08f);
        }
    }
} 