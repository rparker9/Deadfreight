using Unity.Netcode;
using UnityEngine;

/// <summary>
/// OWNER-ONLY: Moves the local player with a CharacterController.
/// The NetworkTransform (Authority Mode = Owner) copies the result to everyone else,
/// so there is no prediction or reconciliation to manage.
/// Also publishes movement/aim values to PlayerState so remote players can animate.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerMotor : NetworkBehaviour
{
    [SerializeField] private MovementSettings settings;

    private CharacterController _cc;
    private PlayerState _state;

    private Vector3 _horizVel;
    private float _verticalVel;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _state = GetComponent<PlayerState>();
    }

    /// <summary>
    /// Called once per frame by PlayerInputController with this frame's input.
    /// </summary>
    public void Move(Vector2 move, float yaw, float pitch, bool jump, bool sprint, bool crouch, float dt)
    {
        if (!IsOwner || settings == null || dt <= 0f)
            return;

        // --- Horizontal ---
        move = Vector2.ClampMagnitude(move, 1f);
        bool hasInput = move.sqrMagnitude > 0.0001f;
        if (!hasInput)
            move = Vector2.zero;

        float targetSpeed = crouch ? settings.crouchSpeed
                          : sprint ? settings.sprintSpeed
                          : settings.walkSpeed;

        Vector3 desiredVel = Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y) * targetSpeed;

        bool grounded = _cc.isGrounded;
        float accel = grounded
            ? (hasInput ? settings.groundAccel : settings.groundDecel)
            : (hasInput ? settings.airAccel : settings.airDecel);

        _horizVel = Vector3.MoveTowards(_horizVel, desiredVel, accel * dt);

        // --- Vertical ---
        if (grounded)
        {
            if (_verticalVel < 0f)
                _verticalVel = -1f; // small downward push keeps isGrounded reliable

            if (jump)
                _verticalVel = settings.jumpSpeed;
        }

        _verticalVel += settings.gravity * dt;

        // --- Apply ---
        _cc.Move((_horizVel + Vector3.up * _verticalVel) * dt);

        // --- Publish for remote animation / IK / server-side grab checks ---
        float horizSpeed = _horizVel.magnitude;

        _state.MoveInput.Value = move;
        _state.Speed.Value = horizSpeed < 0.01f ? 0f : horizSpeed;
        _state.IsGrounded.Value = _cc.isGrounded;
        _state.IsJumping.Value = _verticalVel > 1f;
        _state.IsCrouching.Value = crouch;
        _state.IsSprinting.Value = sprint;
        _state.AimYaw.Value = yaw;
        _state.AimPitch.Value = pitch;
    }
}
