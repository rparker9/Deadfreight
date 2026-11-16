using KinematicCharacterController;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// OWNER-ONLY: Moves the local player with Kinematic Character Controller (KCC).
///
/// KCC handles the collision solving (sliding along walls, steps, slopes, ground snapping)
/// in FixedUpdate. This class answers its questions: "what velocity now?" (UpdateVelocity)
/// and "can I collide with this?" (IsColliderValidForCollisions).
///
/// - Runs only on the owner. On other machines the motor stays off and the capsule is just
///   a collider; the NetworkTransform (Authority Mode = Owner) moves it.
/// - Never pushes rigidbodies (KCC's Interactive Rigidbody Handling must be OFF): vehicles
///   and pickups block the player like walls.
/// - Rides whatever rigidbody it stands on (wagon, trailer) by following how that body
///   actually moved, so it works on clients where vehicles are kinematic copies.
/// - Publishes movement/aim to PlayerState so remote players can animate.
/// </summary>
[RequireComponent(typeof(KinematicCharacterMotor))]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerMotor : NetworkBehaviour, ICharacterController
{
    [SerializeField] private MovementSettings settings;

    [Tooltip("Turns the view when the ground you stand on turns (e.g. a wagon taking a corner)")]
    [SerializeField] private bool turnWithPlatform = true;

    private KinematicCharacterMotor _motor;
    private PlayerState _state;
    private PlayerCameraController _camera;

    // Latest input (set every frame by PlayerInputController, used every physics step)
    private Vector2 _move;
    private float _yaw;
    private bool _jump, _sprint, _crouch;

    // Moving ground (platform) we're standing on
    private Rigidbody _platform;
    private Vector3 _platformLocalPos;   // our position in the platform's space after the last step
    private float _platformYaw;          // platform heading last step
    private Vector3 _platformVelocity;   // the part of our velocity that came from the platform

    // Held object: never collide with what we carry
    private Transform _held;

    /// <summary>False while seated (PlayerSeating turns the motor off).</summary>
    public bool IsActive => _motor != null && _motor.enabled;

    private void Awake()
    {
        _motor = GetComponent<KinematicCharacterMotor>();
        _state = GetComponent<PlayerState>();
        _camera = GetComponent<PlayerCameraController>();

        _motor.CharacterController = this;
        _motor.InteractiveRigidbodyHandling = false; // we never push rigidbodies; see class summary
    }

    public override void OnNetworkSpawn()
    {
        _state.HeldObject.OnValueChanged += OnHeldChanged;
        OnHeldChanged(default, _state.HeldObject.Value);

        if (IsOwner)
        {
            _motor.SetPosition(transform.position); // start exactly where we spawned
            _motor.enabled = true;
        }
        else
        {
            _motor.enabled = false; // remote players: the network moves us; the capsule just collides
        }
    }

    public override void OnNetworkDespawn()
    {
        _state.HeldObject.OnValueChanged -= OnHeldChanged;
    }

    private void OnHeldChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        _held = curr.TryGet(out NetworkObject obj) && obj != null ? obj.transform : null;
    }

    // ============================================================
    // INPUT (owner, every frame from PlayerInputController)
    // ============================================================

    /// <summary>Store this frame's input; KCC applies it in the next physics step. Also publishes state.</summary>
    public void Move(Vector2 move, float yaw, float pitch, bool jump, bool sprint, bool crouch, float dt)
    {
        if (!IsOwner || settings == null) return;

        _move = Vector2.ClampMagnitude(move, 1f);
        _yaw = yaw;
        _jump = jump;
        _sprint = sprint;
        _crouch = crouch;

        PublishState(pitch);
    }

    private void PublishState(float pitch)
    {
        bool active = IsActive;
        Vector3 selfVelocity = active ? _motor.BaseVelocity - _platformVelocity : Vector3.zero;
        float horizSpeed = Vector3.ProjectOnPlane(selfVelocity, Vector3.up).magnitude;

        _state.MoveInput.Value = active ? _move : Vector2.zero;
        _state.Speed.Value = horizSpeed < 0.01f ? 0f : horizSpeed;
        _state.IsGrounded.Value = !active || _motor.GroundingStatus.IsStableOnGround;
        _state.IsJumping.Value = active && Vector3.Dot(selfVelocity, Vector3.up) > 1f;
        _state.IsCrouching.Value = active && _crouch;
        _state.IsSprinting.Value = active && _sprint;
        _state.AimYaw.Value = _yaw;
        _state.AimPitch.Value = pitch;
    }

    // ============================================================
    // KCC CALLBACKS (owner, physics step)
    // ============================================================

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        if (settings == null || deltaTime <= 0f) return;

        Vector3 up = _motor.CharacterUp;
        bool grounded = _motor.GroundingStatus.IsStableOnGround;

        // Our own velocity = what KCC left us last step, minus what the platform added.
        Vector3 self = currentVelocity - _platformVelocity;
        Vector3 horizontal = Vector3.ProjectOnPlane(self, up);
        float vertical = Vector3.Dot(self, up);

        // ---- Horizontal: accelerate toward the input direction ----
        bool hasInput = _move.sqrMagnitude > 0.0001f;
        float targetSpeed = _crouch ? settings.crouchSpeed : _sprint ? settings.sprintSpeed : settings.walkSpeed;
        Vector3 desired = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(_move.x, 0f, _move.y) * targetSpeed;

        float accel = grounded
            ? (hasInput ? settings.groundAccel : settings.groundDecel)
            : (hasInput ? settings.airAccel : settings.airDecel);
        horizontal = Vector3.MoveTowards(horizontal, desired, accel * deltaTime);

        // ---- Vertical ----
        if (grounded)
        {
            vertical = 0f; // KCC keeps us snapped to the ground
            if (_jump)
            {
                vertical = settings.jumpSpeed;
                _motor.ForceUnground(); // stop ground snapping from cancelling the jump
                grounded = false;
            }
        }
        else
        {
            vertical += settings.gravity * deltaTime;
        }

        // On the ground, follow the slope instead of walking into it.
        if (grounded && horizontal.sqrMagnitude > 0f)
            horizontal = _motor.GetDirectionTangentToSurface(horizontal, _motor.GroundingStatus.GroundNormal) * horizontal.magnitude;

        // ---- Moving ground ----
        _platformVelocity = ComputePlatformVelocity(deltaTime);

        currentVelocity = horizontal + up * vertical + _platformVelocity;
    }

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        // The root never rotates: the camera's yaw pivot turns the view, and
        // PlayerAnimator turns the body model. Keep the capsule upright.
        currentRotation = Quaternion.identity;
    }

    public void AfterCharacterUpdate(float deltaTime)
    {
        // Remember where we ended up on the platform, so next step we can follow it.
        if (_platform != null)
            _platformLocalPos = ToPlatformSpace(_motor.TransientPosition);
    }

    public bool IsColliderValidForCollisions(Collider coll)
    {
        // Walk through what we're carrying (it's held in front of us).
        if (_held != null && coll.transform.IsChildOf(_held)) return false;
        return true;
    }

    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport report)
    {
        // Hit a ceiling while going up: stop rising.
        if (!report.IsStable && Vector3.Dot(hitNormal, _motor.CharacterUp) < -0.5f)
        {
            Vector3 v = _motor.BaseVelocity;
            float up = Vector3.Dot(v - _platformVelocity, _motor.CharacterUp);
            if (up > 0f) _motor.BaseVelocity = v - _motor.CharacterUp * up;
        }
    }

    // Unused KCC callbacks
    public void BeforeCharacterUpdate(float deltaTime) { }
    public void PostGroundingUpdate(float deltaTime) { }
    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport report) { }
    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint,
        Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport report)
    { }
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }

    // ============================================================
    // MOVING GROUND
    // ============================================================

    /// <summary>
    /// Velocity that keeps us at the same spot on the rigidbody we stand on.
    /// Position-based (not the body's velocity), so it also works on clients where
    /// vehicles are kinematic copies moved by the network.
    /// </summary>
    private Vector3 ComputePlatformVelocity(float dt)
    {
        Rigidbody ground = _motor.GroundingStatus.IsStableOnGround && _motor.GroundingStatus.GroundCollider != null
            ? _motor.GroundingStatus.GroundCollider.attachedRigidbody
            : null;

        if (ground != _platform)
        {
            // Leaving a platform: the platform velocity stays in our own velocity (we keep momentum).
            // Landing on one: start following it from where we are.
            _platform = ground;
            if (_platform != null)
            {
                _platformLocalPos = ToPlatformSpace(_motor.TransientPosition);
                _platformYaw = PlatformRotation.eulerAngles.y;
            }
            return Vector3.zero;
        }

        if (_platform == null) return Vector3.zero;

        // Turn the view with the platform.
        float yaw = PlatformRotation.eulerAngles.y;
        float yawDelta = Mathf.DeltaAngle(_platformYaw, yaw);
        _platformYaw = yaw;
        if (turnWithPlatform && _camera != null && Mathf.Abs(yawDelta) > 0.001f)
            _camera.AddYaw(yawDelta);

        Vector3 target = FromPlatformSpace(_platformLocalPos);
        return (target - _motor.TransientPosition) / dt;
    }

    // Simulated bodies (host): the physics pose. Kinematic copies (clients): the transform,
    // which is what the network moves.
    private Vector3 PlatformPosition => _platform.isKinematic ? _platform.transform.position : _platform.position;
    private Quaternion PlatformRotation => _platform.isKinematic ? _platform.transform.rotation : _platform.rotation;

    private Vector3 ToPlatformSpace(Vector3 world) =>
        Quaternion.Inverse(PlatformRotation) * (world - PlatformPosition);

    private Vector3 FromPlatformSpace(Vector3 local) =>
        PlatformPosition + PlatformRotation * local;
}