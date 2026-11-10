using Unity.Netcode;
using UnityEngine;

/// <summary>
/// CLIENT-ONLY (Non-Owner): Drives animator based on PlayerState.
/// This component only runs for REMOTE players (what others see).
/// Owner uses separate FPS arm rig.
/// </summary>
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerAnimationClient : NetworkBehaviour
{
    [Header("Character Model")]
    [Tooltip("Character visual root - rotates to match body rotation")]
    [SerializeField] private Transform characterModelRoot;

    [Header("Animator Parameters")]
    [SerializeField] private string moveSpeedParam = "MoveSpeed";
    [SerializeField] private string moveXParam = "MoveX";
    [SerializeField] private string moveYParam = "MoveY";
    [SerializeField] private string isGroundedParam = "IsGrounded";
    [SerializeField] private string isJumpingParam = "IsJumping";
    [SerializeField] private string isCrouchingParam = "IsCrouching";
    [SerializeField] private string isSprintingParam = "IsSprinting";
    [Tooltip("Int: 0 = hands free, 1 = carrying, 2 = dragging. Drives the upper-body pose layer.")]
    [SerializeField] private string holdPoseParam = "HoldPose";
    [Tooltip("Bool: true while in a vehicle seat")]
    [SerializeField] private string isSeatedParam = "IsSeated";

    [Header("Animation Speed Mapping")]
    [Tooltip("How to map actual speed to animation speed")]
    [SerializeField] private float walkSpeedReference = 4.5f;

    [Header("Smoothing")]
    [Tooltip("Smooth speed changes (0 = instant)")]
    [SerializeField] private float speedSmoothTime = 0.1f;

    [Tooltip("Smooth directional input (0 = instant)")]
    [SerializeField] private float inputDampTime = 0.1f;

    [Tooltip("Body rotation speed (0 = instant)")]
    [SerializeField] private float bodyRotationSpeed = 720f;

    [Header("Visibility")]
    [Tooltip("Renderers to hide for owner (FPS view)")]
    [SerializeField] private Renderer[] characterRenderers;

    private Animator _animator;
    private PlayerState _state;
    private PlayerSeatController _seat;
    private int _isSeatedHash;
    private bool _hasIsSeated;

    // Cached parameter hashes
    private int _moveSpeedHash;
    private int _moveXHash;
    private int _moveYHash;
    private int _isGroundedHash;
    private int _isJumpingHash;
    private int _isCrouchingHash;
    private int _isSprintingHash;
    private int _holdPoseHash;
    private bool _hasHoldPose;

    // Smoothed values
    private float _currentSpeed;
    private float _speedVelocity;

    // Target inputs
    private Vector2 _targetMoveInput;
    private const float AnimatorFloatEpsilon = 0.001f;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _animator = GetComponentInChildren<Animator>();
        _state = GetComponent<PlayerState>();
        _seat = GetComponent<PlayerSeatController>();

        // Verify animator found
        if (_animator == null)
        {
            Debug.LogError($"[PlayerAnimationClient] Animator not found on {gameObject.name}! Make sure Animator component is on a child GameObject.");
        }
        else
        {
            // Verify parameters exist
            VerifyAnimatorParameters();
        }

        // Cache parameter hashes
        _moveSpeedHash = Animator.StringToHash(moveSpeedParam);
        _moveXHash = Animator.StringToHash(moveXParam);
        _moveYHash = Animator.StringToHash(moveYParam);
        _isGroundedHash = Animator.StringToHash(isGroundedParam);
        _isJumpingHash = Animator.StringToHash(isJumpingParam);
        _isCrouchingHash = Animator.StringToHash(isCrouchingParam);
        _isSprintingHash = Animator.StringToHash(isSprintingParam);
        _holdPoseHash = Animator.StringToHash(holdPoseParam);
        _hasHoldPose = HasParameter(holdPoseParam);
        _isSeatedHash = Animator.StringToHash(isSeatedParam);
        _hasIsSeated = HasParameter(isSeatedParam);
    }

    private void VerifyAnimatorParameters()
    {
        if (_animator == null) return;

        bool hasSpeed = HasParameter(moveSpeedParam);
        bool hasMoveX = HasParameter(moveXParam);
        bool hasMoveY = HasParameter(moveYParam);
        bool hasGrounded = HasParameter(isGroundedParam);
        bool hasJumping = HasParameter(isJumpingParam);
        bool hasCrouching = HasParameter(isCrouchingParam);
        bool hasSprinting = HasParameter(isSprintingParam);

        if (!hasSpeed) Debug.LogError($"[PlayerAnimationClient] Animator missing parameter: {moveSpeedParam}");
        if (!hasMoveX) Debug.LogError($"[PlayerAnimationClient] Animator missing parameter: {moveXParam}");
        if (!hasMoveY) Debug.LogError($"[PlayerAnimationClient] Animator missing parameter: {moveYParam}");
        if (!hasGrounded) Debug.LogWarning($"[PlayerAnimationClient] Animator missing parameter: {isGroundedParam}");
        if (!hasJumping) Debug.LogWarning($"[PlayerAnimationClient] Animator missing parameter: {isJumpingParam}");
        if (!hasCrouching) Debug.LogWarning($"[PlayerAnimationClient] Animator missing parameter: {isCrouchingParam}");
        if (!hasSprinting) Debug.LogWarning($"[PlayerAnimationClient] Animator missing parameter: {isSprintingParam}");
    }

    private bool HasParameter(string paramName)
    {
        if (_animator == null) return false;

        foreach (var param in _animator.parameters)
        {
            if (param.name == paramName)
                return true;
        }
        return false;
    }

    public override void OnNetworkSpawn()
    {
        // Owner: hide character model (first person). Stays enabled only to show it
        // again while in a third-person vehicle seat (see LateUpdate).
        if (IsOwner)
        {
            SetRenderersVisible(false);
            _ownerBodyVisible = false;
            return;
        }

        // Non-owner: subscribe to state changes
        _state.MoveInput.OnValueChanged += OnMoveInputChanged;
        _state.IsGrounded.OnValueChanged += OnGroundedChanged;
        _state.IsJumping.OnValueChanged += OnJumpingChanged;
        _state.IsCrouching.OnValueChanged += OnCrouchingChanged;
        _state.IsSprinting.OnValueChanged += OnSprintingChanged;

        // Initial sync
        UpdateAllParameters();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner)
        {
            _state.MoveInput.OnValueChanged -= OnMoveInputChanged;
            _state.IsGrounded.OnValueChanged -= OnGroundedChanged;
            _state.IsJumping.OnValueChanged -= OnJumpingChanged;
            _state.IsCrouching.OnValueChanged -= OnCrouchingChanged;
            _state.IsSprinting.OnValueChanged -= OnSprintingChanged;
        }
    }

    // ============================================================
    // STATE CHANGE CALLBACKS
    // ============================================================

    private void OnMoveInputChanged(Vector2 prev, Vector2 curr)
    {
        if (_animator == null) return;

        _targetMoveInput = curr;
    }

    private void OnGroundedChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isGroundedHash, curr);
    }

    private void OnJumpingChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isJumpingHash, curr);
    }

    private void OnCrouchingChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isCrouchingHash, curr);
    }

    private void OnSprintingChanged(bool prev, bool curr)
    {
        if (_animator != null)
            _animator.SetBool(_isSprintingHash, curr);
    }

    // ============================================================
    // UPDATE
    // ============================================================

    private bool _ownerBodyVisible;

    private void LateUpdate()
    {
        if (IsOwner)
        {
            UpdateOwnerThirdPerson();
            return;
        }

        UpdateMoveInput();

        // Smooth speed for blend tree
        UpdateSpeed();

        // Rotate character model to match body rotation
        UpdateBodyRotation();

        // Upper-body pose for holding things, and sitting
        UpdateHoldPose();
        UpdateSeated();
    }

    private void UpdateHoldPose()
    {
        if (_animator == null || !_hasHoldPose) return;

        int pose = 0;
        if (_state.GrabbedObject.Value.TryGet(out NetworkObject held) && held != null
            && held.TryGetComponent(out NetworkGrabbable grabbable))
        {
            pose = grabbable.GrabMode.Value == NetworkGrabbable.Mode.Dragged ? 2 : 1;
        }

        _animator.SetInteger(_holdPoseHash, pose);
    }

    /// <summary>Owner: show our own body (seated) while the camera is in third person.</summary>
    private void UpdateOwnerThirdPerson()
    {
        bool show = _seat != null && _seat.CurrentSeat != null && _seat.CurrentSeat.ThirdPerson;

        if (show != _ownerBodyVisible)
        {
            _ownerBodyVisible = show;
            SetRenderersVisible(show);
        }

        if (show)
        {
            UpdateBodyRotation();
            UpdateSeated();
        }
    }

    private void UpdateSeated()
    {
        if (_animator == null || !_hasIsSeated) return;
        _animator.SetBool(_isSeatedHash, _seat != null && _seat.IsSeated);
    }

    private void UpdateMoveInput()
    {
        if (_animator == null) return;

        Vector2 v = _targetMoveInput;

        // Snap tiny drift
        if (Mathf.Abs(v.x) < AnimatorFloatEpsilon) v.x = 0f;
        if (Mathf.Abs(v.y) < AnimatorFloatEpsilon) v.y = 0f;

        // Direction-only for 2D Freeform Directional
        if (v.sqrMagnitude > AnimatorFloatEpsilon * AnimatorFloatEpsilon)
            v = v.normalized;
        else
            v = Vector2.zero;

        if (inputDampTime > 0f)
        {
            _animator.SetFloat(_moveXHash, v.x, inputDampTime, Time.deltaTime);
            _animator.SetFloat(_moveYHash, v.y, inputDampTime, Time.deltaTime);
        }
        else
        {
            _animator.SetFloat(_moveXHash, v.x);
            _animator.SetFloat(_moveYHash, v.y);
        }
    }

    private void UpdateSpeed()
    {
        if (_animator == null) return;

        // Get actual speed from state
        float actualSpeed = _state.Speed.Value;

        // Simple normalized speed (you can adjust this based on your blend tree)
        // For most blend trees: 0 = idle, 1 = walk, >1 = run
        float normalizedSpeed = actualSpeed / walkSpeedReference;

        // Clamp to reasonable range
        normalizedSpeed = Mathf.Clamp(normalizedSpeed, 0f, 2f);

        // Smooth transition
        if (speedSmoothTime > 0f)
        {
            _currentSpeed = Mathf.SmoothDamp(
                _currentSpeed,
                normalizedSpeed,
                ref _speedVelocity,
                speedSmoothTime);
        }
        else
        {
            _currentSpeed = normalizedSpeed;
        }

        // IMPORTANT: snap tiny values to 0 so blend trees truly idle.
        if (Mathf.Abs(_currentSpeed) < AnimatorFloatEpsilon)
        {
            _currentSpeed = 0f;
            _speedVelocity = 0f; // prevent tiny residual velocity from reintroducing drift
        }

        _animator.SetFloat(_moveSpeedHash, _currentSpeed);
    }

    private void UpdateBodyRotation()
    {
        if (characterModelRoot == null)
        {
            return;
        }

        // Seated: face the seat. Otherwise: face where the player is aiming.
        Quaternion targetRotation = _seat != null && _seat.CurrentSeat != null
            ? _seat.CurrentSeat.transform.rotation
            : _state.BodyRotation;

        bool seated = _seat != null && _seat.CurrentSeat != null;

        if (bodyRotationSpeed > 0f && !seated)   // seated: stick to the seat, no lag
        {
            characterModelRoot.rotation = Quaternion.RotateTowards(
                characterModelRoot.rotation,
                targetRotation,
                bodyRotationSpeed * Time.deltaTime);
        }
        else
        {
            characterModelRoot.rotation = targetRotation;
        }
    }

    private void UpdateAllParameters()
    {
        if (_animator == null) return;

        Vector2 moveInput = _state.MoveInput.Value;
        _targetMoveInput = moveInput;

        if (Mathf.Abs(moveInput.x) < AnimatorFloatEpsilon) moveInput.x = 0f;
        if (Mathf.Abs(moveInput.y) < AnimatorFloatEpsilon) moveInput.y = 0f;

        if (moveInput.sqrMagnitude > AnimatorFloatEpsilon * AnimatorFloatEpsilon)
            moveInput = moveInput.normalized;
        else
            moveInput = Vector2.zero;

        _animator.SetFloat(_moveXHash, moveInput.x);
        _animator.SetFloat(_moveYHash, moveInput.y);

        // Initialize speed (instant, no smoothing)
        float actualSpeed = _state.Speed.Value;
        float normalizedSpeed = actualSpeed / walkSpeedReference;
        normalizedSpeed = Mathf.Clamp(normalizedSpeed, 0f, 2f);

        _currentSpeed = normalizedSpeed;
        _speedVelocity = 0f;

        if (Mathf.Abs(_currentSpeed) < AnimatorFloatEpsilon)
            _currentSpeed = 0f;

        _animator.SetFloat(_moveSpeedHash, _currentSpeed);

        // Initialize body rotation
        if (characterModelRoot != null)
            characterModelRoot.rotation = _state.BodyRotation;
    }

    // ============================================================
    // VISIBILITY
    // ============================================================

    private void SetRenderersVisible(bool visible)
    {
        if (characterRenderers == null) return;

        foreach (var renderer in characterRenderers)
        {
            if (renderer != null)
                renderer.enabled = visible;
        }
    }

    public void SetCharacterVisible(bool visible)
    {
        SetRenderersVisible(visible);
    }
}