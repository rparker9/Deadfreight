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

    // Vehicle seat: look is relative to this rotation, with a yaw limit
    private Quaternion? _reference;
    private float _yawLimit = 180f;

    [Header("Third Person")]
    [SerializeField] private float thirdPersonCollisionRadius = 0.25f;
    [SerializeField] private LayerMask thirdPersonCollisionMask = ~0;

    private bool _thirdPerson;
    private Vector3 _firstPersonLocalPos;
    private Quaternion _firstPersonLocalRot;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    // Public API for other systems (input, motor)
    public float YawDegrees => _yaw;
    public float PitchDegrees => _pitch;
    /// <summary>Actual world-space heading of the view (differs from YawDegrees while seated).</summary>
    public float WorldYaw => yawPivot != null ? yawPivot.eulerAngles.y : _yaw;
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
        {
            playerCamera.gameObject.SetActive(true);
            _firstPersonLocalPos = playerCamera.transform.localPosition;
            _firstPersonLocalRot = playerCamera.transform.localRotation;
        }

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
        // Seated: yaw is relative to the seat and limited to its range.
        if (_reference.HasValue && _yawLimit < 180f)
            _yaw = Mathf.Clamp(Mathf.DeltaAngle(0f, _yaw), -_yawLimit, _yawLimit);

        // Yaw: world-space horizontal rotation (or relative to the seat)
        if (yawPivot != null)
        {
            Quaternion baseRotation = _reference ?? Quaternion.identity;
            yawPivot.rotation = baseRotation * Quaternion.Euler(0f, _yaw, 0f);
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
    /// Make the view relative to a rotation (a vehicle seat), or pass null to return to world space.
    /// Entering faces the seat's forward; leaving keeps the current world heading.
    /// </summary>
    public void SetReference(Quaternion? reference, float yawLimit = 180f)
    {
        if (!IsOwner) return;

        if (reference.HasValue && !_reference.HasValue)
            _yaw = 0f;                 // getting in: face forward
        else if (!reference.HasValue && _reference.HasValue)
            _yaw = WorldYaw;           // getting out: keep looking the same way

        _reference = reference;
        _yawLimit = yawLimit;
        UpdateTransforms();
    }

    /// <summary>
    /// Third person: place the camera behind the focus point along the current look direction,
    /// pulled in if something is in the way. Call every LateUpdate while active.
    /// </summary>
    public void SetThirdPerson(Vector3 focus, float distance, Rigidbody ignore = null)
    {
        if (!IsOwner || playerCamera == null) return;
        _thirdPerson = true;

        Vector3 dir = GetLookDirection();
        float dist = distance;

        int count = Physics.SphereCastNonAlloc(focus, thirdPersonCollisionRadius, -dir, _hits, distance,
                                               thirdPersonCollisionMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var h = _hits[i];
            if (h.distance <= 0f) continue;                       // overlapping at start (e.g. inside the vehicle)
            if (ignore != null && h.rigidbody == ignore) continue; // don't collide with our own vehicle
            if (h.collider.transform.IsChildOf(transform)) continue; // or ourselves
            if (h.distance < dist) dist = h.distance;
        }

        dist = Mathf.Max(0.5f, dist - 0.1f);
        playerCamera.transform.SetPositionAndRotation(focus - dir * dist, Quaternion.LookRotation(dir, Vector3.up));
    }

    /// <summary>Back to the normal first-person camera position.</summary>
    public void ClearThirdPerson()
    {
        if (!_thirdPerson || playerCamera == null) return;
        _thirdPerson = false;
        playerCamera.transform.localPosition = _firstPersonLocalPos;
        playerCamera.transform.localRotation = _firstPersonLocalRot;
    }

    public bool IsThirdPerson => _thirdPerson;

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