using Unity.Netcode;
using UnityEngine;

/// <summary>
/// OWNER-ONLY: First-person camera controller.
/// Handles mouse look input and camera positioning.
/// Only enabled for the local player.
/// </summary>
public sealed class PlayerCameraController : NetworkBehaviour
{
    [Header("Camera")]
    [SerializeField] private Camera playerCamera;

    [Header("View Rig")]
    [Tooltip("Yaw pivot (rotates horizontally in world space)")]
    [SerializeField] private Transform yawPivot;

    [Tooltip("Pitch pivot (rotates vertically in local space, child of yaw)")]
    [SerializeField] private Transform pitchPivot;

    [Header("Sensitivity")]
    [SerializeField] private float lookSensitivityX = 0.10f;
    [SerializeField] private float lookSensitivityY = 0.10f;

    [Header("Pitch Limits")]
    [SerializeField] private float pitchMin = -80f;
    [SerializeField] private float pitchMax = 80f;

    [Header("Smoothing")]
    [Tooltip("Higher = smoother camera. 0 = instant. Try 15-25 for realistic feel.")]
    [SerializeField] private float lookSmoothing = 20f;

    private const string FallbackCameraTag = "FallbackCamera";

    private float _yaw;
    private float _pitch;
    private Vector2 _smoothedLookDelta;

    private GameObject _fallbackCamera;

    // Public API for other systems (input, motor)
    public float YawDegrees => _yaw;
    public float PitchDegrees => _pitch;
    public Camera Camera => playerCamera;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    public override void OnNetworkSpawn()
    {
        // Only enable for owner
        if (!IsOwner)
        {
            // Disable camera for non-owners
            if (playerCamera != null)
                playerCamera.gameObject.SetActive(false);

            enabled = false;
            return;
        }

        // Owner initialization
        InitializeOwnerCamera();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            CleanupOwnerCamera();
        }
    }

    private void InitializeOwnerCamera()
    {
        // Enable player camera
        if (playerCamera != null)
            playerCamera.gameObject.SetActive(true);

        // Find and disable fallback camera (usually scene camera)
        _fallbackCamera = GameObject.FindGameObjectWithTag(FallbackCameraTag);
        if (_fallbackCamera != null)
            _fallbackCamera.SetActive(false);

        // Initialize yaw/pitch from current transform state
        if (yawPivot != null)
            _yaw = yawPivot.eulerAngles.y;

        if (pitchPivot != null)
            _pitch = pitchPivot.localEulerAngles.x;

        // Normalize pitch to -180 to 180 range
        if (_pitch > 180f)
            _pitch -= 360f;

        // Lock cursor for FPS gameplay
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void CleanupOwnerCamera()
    {
        // Re-enable fallback camera
        if (_fallbackCamera == null)
            _fallbackCamera = GameObject.FindGameObjectWithTag(FallbackCameraTag);

        if (_fallbackCamera != null)
            _fallbackCamera.SetActive(true);

        // Restore cursor
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // ============================================================
    // INPUT API (called by PlayerInputController)
    // ============================================================

    /// <summary>
    /// Apply look input from PlayerInputController.
    /// Should be called every frame with raw look delta from Input System.
    /// </summary>
    /// <param name="lookDelta">Raw look input (e.g., mouse delta)</param>
    /// <param name="deltaTime">Time.deltaTime for frame-rate independent smoothing</param>
    public void ApplyLook(Vector2 lookDelta, float deltaTime)
    {
        if (!IsOwner) return;

        // Apply sensitivity
        lookDelta = new Vector2(
            lookDelta.x * lookSensitivityX,
            lookDelta.y * lookSensitivityY);

        // Smooth the look delta (exponential smoothing)
        if (lookSmoothing > 0f)
        {
            float smoothFactor = 1f - Mathf.Exp(-lookSmoothing * deltaTime);
            _smoothedLookDelta = Vector2.Lerp(_smoothedLookDelta, lookDelta, smoothFactor);
        }
        else
        {
            _smoothedLookDelta = lookDelta;
        }

        // Update yaw (horizontal rotation)
        _yaw += _smoothedLookDelta.x;

        // Normalize yaw to 0-360 range
        if (_yaw >= 360f || _yaw <= -360f)
            _yaw = Mathf.Repeat(_yaw, 360f);

        // Update pitch (vertical rotation)
        _pitch -= _smoothedLookDelta.y; // Subtract for natural mouse feel
        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

        // Apply rotations to transforms
        UpdateTransforms();
    }

    private void UpdateTransforms()
    {
        // Yaw: World-space horizontal rotation
        if (yawPivot != null)
        {
            yawPivot.rotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        // Pitch: Local-space vertical rotation
        if (pitchPivot != null)
        {
            pitchPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }
    }

    // ============================================================
    // PUBLIC UTILITY API
    // ============================================================

    /// <summary>
    /// Manually set camera angles (useful for cutscenes, respawn, etc.)
    /// </summary>
    public void SetAngles(float yaw, float pitch)
    {
        if (!IsOwner) return;

        _yaw = yaw;
        _pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        _smoothedLookDelta = Vector2.zero;

        UpdateTransforms();
    }

    /// <summary>
    /// Add recoil/camera shake (useful for weapon feedback)
    /// </summary>
    public void AddRecoil(float pitchRecoil, float yawRecoil)
    {
        if (!IsOwner) return;

        _pitch += pitchRecoil;
        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

        _yaw += yawRecoil;

        UpdateTransforms();
    }

    /// <summary>
    /// Get forward direction (for raycasts, projectile spawning, etc.)
    /// </summary>
    public Vector3 GetLookDirection()
    {
        if (pitchPivot != null)
            return pitchPivot.forward;

        if (yawPivot != null)
            return yawPivot.forward;

        return transform.forward;
    }

    /// <summary>
    /// Get camera position (for audio listener positioning, etc.)
    /// </summary>
    public Vector3 GetCameraPosition()
    {
        if (playerCamera != null)
            return playerCamera.transform.position;

        if (pitchPivot != null)
            return pitchPivot.position;

        if (yawPivot != null)
            return yawPivot.position;

        return transform.position;
    }

    /// <summary>
    /// Temporarily enable/disable camera (for UI, cutscenes, etc.)
    /// </summary>
    public void SetCameraEnabled(bool enabled)
    {
        if (!IsOwner) return;

        if (playerCamera != null)
            playerCamera.enabled = enabled;
    }
}