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

    [Header("Tuning")]
    [Tooltip("Speed (m/s) that counts as a walk. MoveSpeed = speed / this: 0 idle, 1 walk, up to 2 run.")]
    [SerializeField] private float walkSpeedReference = 4.5f;
    [Tooltip("Smooth speed changes (seconds, 0 = instant)")]
    [SerializeField] private float speedSmoothTime = 0.1f;
    [Tooltip("Smooth movement direction (seconds, 0 = instant)")]
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

    private float _speed;
    private float _speedVelocity;
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
        _moveSpeed = Param(moveSpeedParam, required: true);
        _moveX = Param(moveXParam, required: true);
        _moveY = Param(moveYParam, required: true);
        _isGrounded = Param(isGroundedParam);
        _isJumping = Param(isJumpingParam);
        _isCrouching = Param(isCrouchingParam);
        _isSprinting = Param(isSprintingParam);
        _holdPose = Param(holdPoseParam);
        _isSeated = Param(isSeatedParam);
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
        SetMoveDirection(_state.MoveInput.Value, instant);
        SetSpeed(_state.Speed.Value, instant);

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

    /// <summary>Direction only (normalized) for a 2D directional blend tree.</summary>
    private void SetMoveDirection(Vector2 input, bool instant)
    {
        if (_moveX == 0 || _moveY == 0) return;
        Vector2 dir = input.sqrMagnitude > Epsilon * Epsilon ? input.normalized : Vector2.zero;

        if (instant || inputDampTime <= 0f)
        {
            _animator.SetFloat(_moveX, dir.x);
            _animator.SetFloat(_moveY, dir.y);
        }
        else
        {
            _animator.SetFloat(_moveX, dir.x, inputDampTime, Time.deltaTime);
            _animator.SetFloat(_moveY, dir.y, inputDampTime, Time.deltaTime);
        }
    }

    /// <summary>Speed as a fraction of walking speed: 0 idle, 1 walk, up to 2 run.</summary>
    private void SetSpeed(float metersPerSecond, bool instant)
    {
        if (_moveSpeed == 0) return;
        float target = Mathf.Clamp(metersPerSecond / Mathf.Max(walkSpeedReference, 0.01f), 0f, 2f);

        if (instant || speedSmoothTime <= 0f)
        {
            _speed = target;
            _speedVelocity = 0f;
        }
        else
        {
            _speed = Mathf.SmoothDamp(_speed, target, ref _speedVelocity, speedSmoothTime);
        }

        // Snap tiny values to 0 so the blend tree truly idles.
        if (_speed < Epsilon)
        {
            _speed = 0f;
            _speedVelocity = 0f;
        }

        _animator.SetFloat(_moveSpeed, _speed);
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