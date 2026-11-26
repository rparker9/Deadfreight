using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Presentation only: drives the character Animator and body rotation from PlayerState.
///
/// - Remote players (everyone you see): full locomotion, every frame.
/// - Your own player: body hidden (first person), except in a third-person vehicle seat,
///   where it's shown sitting and facing the seat.
///
/// Everything is read from synced state every frame, so late joiners are correct immediately.
/// Missing optional Animator parameters are skipped quietly.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerAnimator : NetworkBehaviour
{
    [Header("Character Model")]
    [Tooltip("Character visual root: rotates to face the aim direction (or the seat)")]
    [SerializeField] private Transform characterModelRoot;

    [Tooltip("Body renderers, hidden for the owner in first person")]
    [SerializeField] private Renderer[] characterRenderers;

    [Header("Animator Parameters")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isJumpingParam = "IsJumping";
    [SerializeField] private string isCrouchingParam = "IsCrouching";
    [SerializeField] private string isSprintingParam = "IsSprinting";
    [Tooltip("Int: 0 = hands free, 1 = carrying")]
    [SerializeField] private string holdPoseParam = "HoldPose";
    [Tooltip("Bool: true while in a vehicle seat")]
    [SerializeField] private string isSeatedParam = "IsSeated";

    [Tooltip("Float: clip playback rate, so feet match the ground (use as the Locomotion state's Speed Multiplier)")]
    [SerializeField] private string locomotionSpeedParam = "LocomotionSpeed";

    [Header("Locomotion")]
    [Tooltip("Walk, run and sprint speeds that map to blend 1, 2 and 3. Empty = taken from PlayerMotor.")]
    [SerializeField] private MovementSettings movementSettings;
    [Tooltip("How fast the walk clips travel. Read it from Walk01_Forward [RM]: Inspector > Animation > Average Velocity (z).")]
    [Unit("m/s")] [SerializeField] private float walkClipSpeed = 1.5f;
    [Tooltip("How fast the run clips travel (Run01_Forward [RM], same place)")]
    [Unit("m/s")] [SerializeField] private float runClipSpeed = 4.0f;
    [Tooltip("How fast the sprint clips travel (Sprint01_Forward [RM], same place)")]
    [Unit("m/s")] [SerializeField] private float sprintClipSpeed = 6.5f;
    [Tooltip("Limits on the playback rate. Hitting a limit means your movement speed and the clip are far apart.")]
    [SerializeField] private Vector2 playbackRateRange = new(0.6f, 1.6f);

    [Header("Tuning")]
    [Tooltip("Smooth gait changes (seconds, 0 = instant)")]
    [SerializeField] private float speedSmoothTime = 0.1f;
    [Tooltip("Smooth the blend position (seconds, 0 = instant)")]
    [SerializeField] private float inputDampTime = 0.1f;
    [Tooltip("Body turn speed (degrees/second, 0 = instant)")]
    [SerializeField] private float bodyRotationSpeed = 720f;

    private const float Epsilon = 0.001f;

    private Animator _animator;
    private PlayerState _state;
    private PlayerSeating _seating;

    // Parameter hashes (0 = the Animator doesn't have it; skipped)
    private int _moveSpeed, _moveX, _moveY;
    private int _isGrounded, _isJumping, _isCrouching, _isSprinting;
    private int _holdPose, _isSeated;
    private int _locomotionSpeed;

    private float _gait;            // 0 idle, 1 walk, 2 run, 3 sprint (smoothed)
    private float _gaitVelocity;
    private bool _ownerBodyVisible;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _state = GetComponent<PlayerState>();
        _seating = GetComponent<PlayerSeating>();

        if (_animator == null)
        {
            Debug.LogError($"[PlayerAnimator] No Animator under {name}.", this);
            return;
        }

        // Locomotion is required; the rest is optional.
        _moveSpeed = Param(moveSpeedParam);
        _moveX = Param(moveXParam, required: true);
        _moveY = Param(moveYParam, required: true);
        _isGrounded = Param(isGroundedParam);
        _isJumping = Param(isJumpingParam);
        _isCrouching = Param(isCrouchingParam);
        _isSprinting = Param(isSprintingParam);
        _holdPose = Param(holdPoseParam);
        _isSeated = Param(isSeatedParam);
        _locomotionSpeed = Param(locomotionSpeedParam);

        if (movementSettings == null && TryGetComponent(out PlayerMotor motor))
            movementSettings = motor.Settings;
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            SetBodyVisible(false); // first person
            return;
        }

        // Start from the current state with no smoothing (correct for late joiners).
        UpdateRemote(instant: true);
    }

    private void LateUpdate()
    {
        if (!IsSpawned || _animator == null) return;

        if (IsOwner) UpdateOwner();
        else UpdateRemote(instant: false);
    }

    // ============================================================
    // MODES
    // ============================================================

    /// <summary>Everyone else: full locomotion from synced state.</summary>
    private void UpdateRemote(bool instant)
    {
        SetLocomotion(instant);

        SetBool(_isGrounded, _state.IsGrounded.Value);
        SetBool(_isJumping, _state.IsJumping.Value);
        SetBool(_isCrouching, _state.IsCrouching.Value);
        SetBool(_isSprinting, _state.IsSprinting.Value);

        ApplyPoses();
        ApplyBodyRotation(instant);
    }

    /// <summary>Our own player: only visible (sitting) in a third-person seat.</summary>
    private void UpdateOwner()
    {
        VehicleSeat seat = _seating != null ? _seating.CurrentSeat : null;
        bool show = seat != null && seat.ThirdPerson;

        if (show != _ownerBodyVisible)
            SetBodyVisible(show);

        if (!show) return;

        ApplyPoses();
        ApplyBodyRotation(instant: true);
    }

    // ============================================================
    // PIECES
    // ============================================================

    /// <summary>
    /// The locomotion blend tree is rings around idle: walk at radius 1, run at 2, sprint at 3 (front only).
    /// MoveX/MoveY = movement direction relative to the BODY, scaled by gait (0 idle, 1 walk, 2 run, 3 sprint).
    /// MoveSpeed = gait alone (for transitions). LocomotionSpeed = playback rate so feet don't slide.
    /// </summary>
    private void SetLocomotion(bool instant)
    {
        float speed = _state.Speed.Value;
        float target = GaitFromSpeed(speed);

        if (instant || speedSmoothTime <= 0f)
        {
            _gait = target;
            _gaitVelocity = 0f;
        }
        else
        {
            _gait = Mathf.SmoothDamp(_gait, target, ref _gaitVelocity, speedSmoothTime);
        }

        // Snap tiny values to 0 so the blend tree truly idles.
        if (_gait < Epsilon)
        {
            _gait = 0f;
            _gaitVelocity = 0f;
        }

        Vector2 blend = BodyRelativeDirection() * _gait;

        if (instant || inputDampTime <= 0f)
        {
            _animator.SetFloat(_moveX, blend.x);
            _animator.SetFloat(_moveY, blend.y);
        }
        else
        {
            _animator.SetFloat(_moveX, blend.x, inputDampTime, Time.deltaTime);
            _animator.SetFloat(_moveY, blend.y, inputDampTime, Time.deltaTime);
        }

        if (_moveSpeed != 0) _animator.SetFloat(_moveSpeed, _gait);
        if (_locomotionSpeed != 0) _animator.SetFloat(_locomotionSpeed, PlaybackRate(speed, _gait));
    }

    /// <summary>
    /// Move input is relative to the aim yaw, but the body turns toward the aim over a few frames.
    /// Re-express it relative to where the body actually faces, so strafes stay correct while turning.
    /// </summary>
    private Vector2 BodyRelativeDirection()
    {
        Vector2 input = _state.MoveInput.Value;
        if (input.sqrMagnitude < Epsilon * Epsilon) return Vector2.zero;
        if (characterModelRoot == null) return input.normalized;

        Vector3 world = Quaternion.Euler(0f, _state.AimYaw.Value, 0f) * new Vector3(input.x, 0f, input.y);
        Vector3 local = Quaternion.Inverse(characterModelRoot.rotation) * world;
        return new Vector2(local.x, local.z).normalized;
    }

    /// <summary>0 at rest, 1 at walk speed, 2 at run speed, 3 at sprint speed (linear in between).</summary>
    private float GaitFromSpeed(float speed)
    {
        float walk = movementSettings != null ? movementSettings.walkSpeed : 3f;
        float run = movementSettings != null ? movementSettings.runSpeed : 5.5f;
        float sprint = movementSettings != null ? movementSettings.sprintSpeed : 8f;
        walk = Mathf.Max(walk, 0.01f);
        run = Mathf.Max(run, walk + 0.01f);
        sprint = Mathf.Max(sprint, run + 0.01f);

        if (speed <= walk) return speed / walk;
        if (speed <= run) return 1f + (speed - walk) / (run - walk);
        return Mathf.Min(3f, 2f + (speed - run) / (sprint - run));
    }

    /// <summary>
    /// Actual speed / the speed the blended clips travel at, so feet match the ground.
    /// Below gait 1 the tree blends walk with idle, so the clips' speed scales down with gait.
    /// </summary>
    private float PlaybackRate(float speed, float gait)
    {
        if (gait < 0.1f) return 1f;

        float clipSpeed;
        if (gait <= 1f) clipSpeed = walkClipSpeed * gait;
        else if (gait <= 2f) clipSpeed = Mathf.Lerp(walkClipSpeed, runClipSpeed, gait - 1f);
        else clipSpeed = Mathf.Lerp(runClipSpeed, sprintClipSpeed, gait - 2f);

        if (clipSpeed < 0.01f) return 1f;
        return Mathf.Clamp(speed / clipSpeed, playbackRateRange.x, playbackRateRange.y);
    }

    /// <summary>
    /// Upper-body pose (carrying) and sitting.
    /// </summary>
    private void ApplyPoses()
    {
        bool holding = _state.HeldObject.Value.TryGet(out NetworkObject held) && held != null;
        bool seated = _seating != null && _seating.IsSeated;

        if (_holdPose != 0) _animator.SetInteger(_holdPose, holding ? 1 : 0);
        SetBool(_isSeated, seated);
    }

    /// <summary>
    /// Face the seat when seated (no lag), otherwise turn toward the aim direction.
    /// </summary>
    private void ApplyBodyRotation(bool instant)
    {
        if (characterModelRoot == null) return;

        VehicleSeat seat = _seating != null ? _seating.CurrentSeat : null;
        Quaternion target = seat != null ? seat.transform.rotation : _state.BodyRotation;

        if (seat != null || instant || bodyRotationSpeed <= 0f)
            characterModelRoot.rotation = target;
        else
            characterModelRoot.rotation = Quaternion.RotateTowards(
                characterModelRoot.rotation, target, bodyRotationSpeed * Time.deltaTime);
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private void SetBodyVisible(bool visible)
    {
        _ownerBodyVisible = visible;
        if (characterRenderers == null) return;

        foreach (var r in characterRenderers)
            if (r != null) r.enabled = visible;
    }

    private void SetBool(int hash, bool value)
    {
        if (hash != 0) _animator.SetBool(hash, value);
    }

    /// <summary>Hash of a parameter the Animator has, or 0 (skipped) if it doesn't.</summary>
    private int Param(string paramName, bool required = false)
    {
        foreach (var p in _animator.parameters)
            if (p.name == paramName) return p.nameHash;

        if (required) Debug.LogError($"[PlayerAnimator] Animator is missing required parameter '{paramName}'.", this);
        else Debug.LogWarning($"[PlayerAnimator] Animator has no '{paramName}' parameter; it will be skipped.", this);
        return 0;
    }
}