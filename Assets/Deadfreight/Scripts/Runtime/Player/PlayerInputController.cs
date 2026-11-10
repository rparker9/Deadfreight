using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// OWNER-ONLY: The single source of input for the local player.
/// Each frame: look -> camera, E/Q -> grab controller, movement -> motor.
/// </summary>
[RequireComponent(typeof(PlayerMotor))]
public sealed class PlayerInputController : NetworkBehaviour
{
    private PlayerControls _controls;
    private PlayerMotor _motor;
    private PlayerCameraController _camera;
    private PlayerGrabController _grab;
    private PlayerSeatController _seat;
    private UIRoot _ui;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _motor = GetComponent<PlayerMotor>();
        _camera = GetComponent<PlayerCameraController>();
        _grab = GetComponent<PlayerGrabController>();
        _seat = GetComponent<PlayerSeatController>();
        _ui = AppRoot.Instance != null ? AppRoot.Instance.GetComponent<UIRoot>() : null;

        _controls = new PlayerControls();
        _controls.Gameplay.Enable();
        _controls.UI.Enable();
        _controls.UI.Pause.performed += OnPausePressed;

        SetCursorLocked(true);
    }

    public override void OnNetworkDespawn() => CleanupInput();
    private void OnDisable() => CleanupInput();

    private void CleanupInput()
    {
        if (_controls == null) return;

        _controls.UI.Pause.performed -= OnPausePressed;
        _controls.Dispose(); // also disables all action maps
        _controls = null;
    }

    // ============================================================
    // PER-FRAME
    // ============================================================

    private void Update()
    {
        if (_controls == null) return;

        var g = _controls.Gameplay;
        bool paused = _ui != null && _ui.IsPaused;
        bool seated = _seat != null && _seat.IsSeated;

        if (!paused)
        {
            // Camera first so the grab raycast uses this frame's view.
            if (_camera != null)
                _camera.ApplyLook(g.Look.ReadValue<Vector2>(), Time.deltaTime);

            if (seated)
            {
                if (g.Interact.triggered) _seat.RequestExit();
            }
            else if (_grab != null)
            {
                if (g.Interact.triggered) _grab.RequestPrimaryAction();
                if (g.Throw.triggered) _grab.RequestThrow();
            }
        }

        // Seated: WASD drives (if driver), Space brakes. The motor just publishes aim.
        if (seated && _seat.IsDriving)
        {
            Vector2 drive = paused ? Vector2.zero : g.Move.ReadValue<Vector2>();
            _seat.SetDriverInput(drive.y, drive.x, !paused && g.Jump.IsPressed());
        }

        // Keep moving the motor while paused (with no input) so gravity
        // and deceleration still apply and remote players stop animating.
        bool canMove = !paused && !seated;
        Vector2 move = canMove ? g.Move.ReadValue<Vector2>() : Vector2.zero;
        bool jump = canMove && g.Jump.IsPressed();
        bool sprint = canMove && g.Sprint.IsPressed();
        bool crouch = canMove && g.Crouch.IsPressed();

        float yaw = _camera != null ? _camera.WorldYaw : transform.eulerAngles.y;
        float pitch = _camera != null ? _camera.PitchDegrees : 0f;

        _motor.Move(move, yaw, pitch, jump, sprint, crouch, Time.deltaTime);
    }

    // ============================================================
    // PAUSE
    // ============================================================

    private void OnPausePressed(InputAction.CallbackContext _)
    {
        if (_ui == null) return;

        _ui.TogglePause();
        SetCursorLocked(!_ui.IsPaused);
    }

    /// <summary>Used by PauseMenuScreen (Resume / Disconnect / Quit).</summary>
    public void SetPaused(bool paused)
    {
        if (!IsOwner || _ui == null) return;

        if (_ui.IsPaused != paused)
            _ui.TogglePause();

        SetCursorLocked(!paused);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
