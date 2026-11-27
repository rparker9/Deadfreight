using Unity.Netcode;
using UnityEngine;

namespace Deadfreight.Player
{
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
        [Tooltip("Seated, first person: seconds to ease the seat's rotation into the view. Filters out the vehicle's " +
                 "very fast physics jitter while keeping slower bumps and turns. 0 = follow the seat exactly.")]
        [SerializeField, Min(0f)] private float seatViewSmoothing = 0.03f;

        private const string FallbackCameraTag = "FallbackCamera";

        private float _yaw;
        private float _pitch;
        private Vector2 _smoothedLookDelta;

        private GameObject _fallbackCamera;

        // Eye height: the view rig's height above the player's root (captured on first use)
        private float _standingEyeHeight;
        private bool _eyeHeightCaptured;

        // Vehicle seat: look is relative to this rotation (eased, see seatViewSmoothing), with a yaw limit
        private Quaternion? _reference;
        private float _yawLimit = 180f;

        [Header("Recoil")]
        [Tooltip("How fast the view moves into a kick. Higher = punchier.")]
        [SerializeField] private float recoilSnappiness = 25f;
        [Tooltip("How fast the kick settles back to where you were aiming. 0 = never (kick is permanent).")]
        [SerializeField] private float recoilReturnSpeed = 8f;

        // Recoil is an offset on top of _yaw/_pitch, so it can ease in and settle back.
        // x = pitch, y = yaw (degrees). Target is where the kick wants to be; current eases toward it.
        private Vector2 _recoilTarget;
        private Vector2 _recoilCurrent;

        [Header("Third Person")]
        [SerializeField] private float thirdPersonCollisionRadius = 0.25f;
        [SerializeField] private LayerMask thirdPersonCollisionMask = ~0;

        [Tooltip("Seconds to blend between first and third person when getting in or out of a seat")]
        [SerializeField] private float viewBlendTime = 0.35f;

        private bool _thirdPerson;
        private Vector3 _firstPersonLocalPos;
        private Quaternion _firstPersonLocalRot;

        // Third-person pose for this frame (set by SetThirdPerson every LateUpdate)
        private Vector3 _thirdPersonPos;
        private Quaternion _thirdPersonRot;

        // View blend: from the camera pose at the moment of switching, toward the live target pose
        private float _blendProgress = 1f;   // 0 = just switched, 1 = done
        private Vector3 _blendFromPos;
        private Quaternion _blendFromRot;
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

            // Yaw: world-space horizontal rotation (or relative to the seat), plus recoil
            if (yawPivot != null)
            {
                Quaternion baseRotation = _reference ?? Quaternion.identity;
                yawPivot.rotation = baseRotation * Quaternion.Euler(0f, _yaw + _recoilCurrent.y, 0f);
            }

            // Pitch: Local-space vertical rotation, plus recoil (kept inside the pitch limits)
            if (pitchPivot != null)
            {
                float pitch = Mathf.Clamp(_pitch + _recoilCurrent.x, pitchMin, pitchMax);
                pitchPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        // ============================================================
        // PUBLIC UTILITY API
        // ============================================================

        /// <summary>Turn the view by a yaw amount (e.g. when the ground you stand on turns).</summary>
        public void AddYaw(float degrees)
        {
            if (!IsOwner) return;
            _yaw = Mathf.Repeat(_yaw + degrees, 360f);
            UpdateTransforms();
        }

        /// <summary>
        /// Raise or lower the view rig (eyes) above the player's root, e.g. while seated.
        /// Null = back to the prefab's standing height. Not owner-only: the rig exists on every machine and
        /// shots start from it (PlayerWeapon's eye), so everyone must agree where the eyes are.
        /// </summary>
        public void SetEyeHeight(float? height)
        {
            if (yawPivot == null) return;
            if (!_eyeHeightCaptured)
            {
                _standingEyeHeight = yawPivot.localPosition.y;
                _eyeHeightCaptured = true;
            }

            Vector3 p = yawPivot.localPosition;
            p.y = height ?? _standingEyeHeight;
            yawPivot.localPosition = p;
        }

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

            // Ease toward the seat's rotation instead of copying it, so the vehicle's tiny high-frequency
            // physics jitter doesn't shake the view. Snaps on entering/leaving. Expects one call per frame
            // (PlayerSeating calls it every LateUpdate while seated).
            if (reference.HasValue && _reference.HasValue && seatViewSmoothing > 0f)
            {
                float t = 1f - Mathf.Exp(-Time.deltaTime / seatViewSmoothing);
                _reference = Quaternion.Slerp(_reference.Value, reference.Value, t);
            }
            else
            {
                _reference = reference;
            }
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

            if (!_thirdPerson)
            {
                BeginViewBlend();
                _thirdPerson = true;
            }

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

            // Just store it: LateUpdate places the camera (and blends into it after a switch).
            _thirdPersonPos = focus - dir * dist;
            _thirdPersonRot = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>Back to the normal first-person camera position.</summary>
        public void ClearThirdPerson()
        {
            if (!_thirdPerson || playerCamera == null) return;
            BeginViewBlend();
            _thirdPerson = false;
        }

        public bool IsThirdPerson => _thirdPerson;

        // ============================================================
        // CAMERA PLACEMENT (owner, every frame)
        // ============================================================

        /// <summary>
        /// Places the camera once per frame, after the seat and the character have moved
        /// (PlayerSeating runs earlier in LateUpdate). Both target poses are recomputed live
        /// every frame, so a blend keeps tracking a moving vehicle and your mouse.
        /// </summary>
        private void LateUpdate()
        {
            if (!IsOwner || playerCamera == null) return;

            UpdateRecoil(Time.deltaTime);

            Transform cam = playerCamera.transform;
            bool blending = _blendProgress < 1f;

            // First person, settled: just sit at the normal local pose under the pitch pivot.
            if (!_thirdPerson && !blending)
            {
                cam.localPosition = _firstPersonLocalPos;
                cam.localRotation = _firstPersonLocalRot;
                return;
            }

            // Where the camera should be this frame.
            Vector3 targetPos;
            Quaternion targetRot;
            if (_thirdPerson)
            {
                targetPos = _thirdPersonPos;
                targetRot = _thirdPersonRot;
            }
            else
            {
                Transform parent = cam.parent;
                targetPos = parent.TransformPoint(_firstPersonLocalPos);
                targetRot = parent.rotation * _firstPersonLocalRot;
            }

            if (blending)
            {
                _blendProgress = viewBlendTime > 0f ? Mathf.Min(1f, _blendProgress + Time.deltaTime / viewBlendTime) : 1f;
                float t = Mathf.SmoothStep(0f, 1f, _blendProgress); // ease in and out
                targetPos = Vector3.Lerp(_blendFromPos, targetPos, t);
                targetRot = Quaternion.Slerp(_blendFromRot, targetRot, t);
            }

            cam.SetPositionAndRotation(targetPos, targetRot);
        }

        /// <summary>Start blending from wherever the camera is right now.</summary>
        private void BeginViewBlend()
        {
            _blendFromPos = playerCamera.transform.position;
            _blendFromRot = playerCamera.transform.rotation;
            _blendProgress = 0f;
        }

        /// <summary>
        /// Kick the view (weapon recoil). Negative pitch = up. The kick eases in over a few frames
        /// and settles back to where you were aiming (see UpdateRecoil).
        /// </summary>
        public void AddRecoil(float pitchRecoil, float yawRecoil)
        {
            if (!IsOwner) return;
            _recoilTarget += new Vector2(pitchRecoil, yawRecoil);
        }

        /// <summary>Target decays toward zero; current chases target. Both frame-rate independent.</summary>
        private void UpdateRecoil(float dt)
        {
            if (_recoilTarget == Vector2.zero && _recoilCurrent == Vector2.zero) return;

            if (recoilReturnSpeed > 0f)
                _recoilTarget *= Mathf.Exp(-recoilReturnSpeed * dt);

            _recoilCurrent = Vector2.Lerp(_recoilCurrent, _recoilTarget, 1f - Mathf.Exp(-recoilSnappiness * dt));

            // Snap to rest once it's imperceptible, so this stops running.
            if (_recoilTarget.sqrMagnitude < 1e-6f && _recoilCurrent.sqrMagnitude < 1e-6f)
                _recoilTarget = _recoilCurrent = Vector2.zero;

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
}