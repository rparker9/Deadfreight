using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Deadfreight.Core;
using Deadfreight.Player;

namespace Deadfreight.Combat
{
    /// <summary>
    /// OWNER-ONLY, PRESENTATION ONLY: the gun in front of your camera. Fully procedural:
    /// hip/ADS position, kick, sway, bob, and a lowered pose while reloading or sprinting.
    /// Reads everything from PlayerWeapon; nothing here affects where shots go.
    ///
    /// The gun is drawn by its own overlay camera that sits at the WORLD ORIGIN and copies the main
    /// camera's rotation and field of view. Far from the origin (levels are kilometres wide), 32-bit
    /// positions round to about a millimetre, which makes a gun 40 cm from your eye shimmer. At the
    /// origin it's exact. It also draws on top of the world, so the gun never clips into walls.
    ///
    /// The gun is a child of that camera. Its root is the handle (+Z down the barrel), placed at the
    /// weapon's Hip Position, and moved so the Sight is on the camera's centre line when aiming.
    /// Without a "ViewModel" layer it falls back to being a plain child of the main camera.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWeapon))]
    public sealed class WeaponViewFirstPerson : NetworkBehaviour
    {
        private const float LowerBlendTime = 0.2f;   // s, into and out of the lowered pose

        [SerializeField] private PlayerCameraController cameraController;

        [Header("View Model Camera")]
        [Tooltip("Layer the first-person gun is drawn on. Add it in Tags & Layers; the main camera stops drawing it.")]
        [SerializeField] private string viewModelLayer = "ViewModel";
        [Unit("m")][SerializeField] private float viewModelNearClip = 0.01f;

        [Header("Bob (per gait, blended by speed)")]
        [Tooltip("Walking. Below walk speed the bob shrinks toward nothing at a standstill.")]
        [SerializeField] private BobGait walkBob = new() { stepsPerSecond = 1.8f, vertical = 0.010f, horizontal = 0.008f, roll = 0.5f };
        [SerializeField] private BobGait runBob = new() { stepsPerSecond = 2.6f, vertical = 0.018f, horizontal = 0.014f, roll = 1f };
        [SerializeField] private BobGait sprintBob = new() { stepsPerSecond = 3.0f, vertical = 0.025f, horizontal = 0.022f, roll = 2f };
        [Tooltip("How fast the bob fades in/out when you start, stop, land or jump (per second)")]
        [SerializeField] private float bobFadeSpeed = 4f;

        /// <summary>
        /// One gait's bob. Faster movement mostly means a bigger bob, not a much faster one:
        /// stride length grows with speed far more than step rate does.
        /// </summary>
        [System.Serializable]
        public struct BobGait
        {
            [Tooltip("Footsteps per second (one dip per step, one side-to-side sway per two steps)")]
            public float stepsPerSecond;
            [Tooltip("Up/down")]
            [Unit("m")] public float vertical;
            [Tooltip("Side to side")]
            [Unit("m")] public float horizontal;
            [Tooltip("Tilt with each step")]
            [Unit("°")] public float roll;

            public static BobGait Lerp(BobGait a, BobGait b, float t) => new()
            {
                stepsPerSecond = Mathf.Lerp(a.stepsPerSecond, b.stepsPerSecond, t),
                vertical = Mathf.Lerp(a.vertical, b.vertical, t),
                horizontal = Mathf.Lerp(a.horizontal, b.horizontal, t),
                roll = Mathf.Lerp(a.roll, b.roll, t),
            };
        }

        private PlayerWeapon _weapon;
        private PlayerState _state;
        private MovementSettings _movement;
        private Camera _camera;          // main camera (your view of the world)
        private Camera _viewCamera;      // draws the gun, at the world origin
        private Transform _muzzleWorld;  // where the gun's muzzle appears in the world (for tracers)
        private int _viewLayer = -1;
        private WeaponModel _model;
        private Vector3 _sightOffset;
        private float _baseFov;

        private readonly WeaponKick _kick = new();
        private float _ads;        // 0..1
        private float _lowered;    // 0..1
        private Vector2 _sway;
        private Quaternion _lastViewRotation;
        private bool _hasLastView;
        private float _bobPhase;
        private float _bobWeight;

        // ============================================================
        // LIFECYCLE
        // ============================================================

        private void Awake()
        {
            _weapon = GetComponent<PlayerWeapon>();
            _state = GetComponent<PlayerState>();
            if (TryGetComponent(out PlayerMotor motor)) _movement = motor.Settings;
            if (cameraController == null)
                cameraController = GetComponent<PlayerCameraController>();
        }

        public override void OnNetworkSpawn()
        {
            WeaponDefinition def = _weapon.Weapon;
            _camera = cameraController != null ? cameraController.Camera : null;

            if (!IsOwner || def == null || def.modelPrefab == null || _camera == null)
            {
                enabled = false;
                return;
            }

            _baseFov = _camera.fieldOfView;
            _viewLayer = LayerMask.NameToLayer(viewModelLayer);
            if (_viewLayer < 0)
                Debug.LogWarning($"[WeaponViewFirstPerson] No '{viewModelLayer}' layer; the gun will be drawn by the main " +
                                 "camera and may shimmer far from the world origin.", this);
            else
                CreateViewCamera();

            Transform parent = _viewCamera != null ? _viewCamera.transform : _camera.transform;
            _model = WeaponModel.Spawn(def.modelPrefab, parent, castShadows: false);
            _sightOffset = _model.SightOffset;

            if (_viewCamera != null)
            {
                foreach (var t in _model.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = _viewLayer;
                // The gun really sits at the origin, so the world's shadows there would be wrong on it.
                foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
                    r.receiveShadows = false;

                // A point under the MAIN camera that mirrors the muzzle, so tracers start from where you see it.
                _muzzleWorld = new GameObject("View Muzzle (world)").transform;
                _muzzleWorld.SetParent(_camera.transform, false);
            }

            _weapon.SetModelMuzzle(_model.Muzzle, firstPerson: true, _muzzleWorld);
            _weapon.Fired += OnFired;
        }

        public override void OnNetworkDespawn()
        {
            if (_model == null) return;

            _weapon.Fired -= OnFired;
            if (_camera != null) _camera.fieldOfView = _baseFov;
            Destroy(_model.gameObject);
            if (_muzzleWorld != null) Destroy(_muzzleWorld.gameObject);
            DestroyViewCamera();
        }

        private void LateUpdate()
        {
            if (_model == null) return;
            WeaponDefinition def = _weapon.Weapon;
            float dt = Time.deltaTime;

            bool armed = _weapon.IsArmed;
            _model.SetVisible(armed);

            _ads = Mathf.MoveTowards(_ads, armed && _weapon.IsAiming ? 1f : 0f, dt / def.adsTime);
            bool lowered = _weapon.IsReloading || _state.IsSprinting.Value;
            _lowered = Mathf.MoveTowards(_lowered, lowered ? 1f : 0f, dt / LowerBlendTime);
            _kick.Update(def, dt);

            if (armed) Pose(def, dt);
            else _hasLastView = false;

            _camera.fieldOfView = _baseFov * Mathf.Lerp(1f, def.adsFovMultiplier, Mathf.SmoothStep(0f, 1f, _ads));
            SyncViewCamera();
        }

        private void OnFired() => _kick.Add(_weapon.Weapon);

        // ============================================================
        // VIEW MODEL CAMERA
        // ============================================================

        /// <summary>An overlay camera at the world origin that only draws the gun, stacked on the main camera.</summary>
        private void CreateViewCamera()
        {
            var go = new GameObject("ViewModel Camera");
            go.transform.SetPositionAndRotation(Vector3.zero, _camera.transform.rotation);

            _viewCamera = go.AddComponent<Camera>();
            _viewCamera.cullingMask = 1 << _viewLayer;
            _viewCamera.nearClipPlane = viewModelNearClip;
            _viewCamera.farClipPlane = 5f;
            _viewCamera.fieldOfView = _camera.fieldOfView;

            var mainData = _camera.GetUniversalAdditionalCameraData();
            var viewData = _viewCamera.GetUniversalAdditionalCameraData();
            viewData.renderType = CameraRenderType.Overlay;
            viewData.renderShadows = false;
            viewData.renderPostProcessing = mainData.renderPostProcessing;
            mainData.cameraStack.Add(_viewCamera);

            // The main camera stops drawing the gun layer.
            _camera.cullingMask &= ~(1 << _viewLayer);
        }

        private void DestroyViewCamera()
        {
            if (_viewCamera == null) return;
            if (_camera != null)
            {
                _camera.GetUniversalAdditionalCameraData().cameraStack.Remove(_viewCamera);
                _camera.cullingMask |= 1 << _viewLayer;
            }
            Destroy(_viewCamera.gameObject);
        }

        /// <summary>
        /// Runs after the main camera is placed (execution order 50): copy its rotation and FOV, stay at
        /// the origin, and move the world muzzle point to match the gun's muzzle.
        /// </summary>
        private void SyncViewCamera()
        {
            if (_viewCamera == null) return;

            _viewCamera.transform.SetPositionAndRotation(Vector3.zero, _camera.transform.rotation);
            _viewCamera.fieldOfView = _camera.fieldOfView;

            if (_muzzleWorld != null && _model.Muzzle != null)
            {
                Transform view = _viewCamera.transform;
                _muzzleWorld.SetLocalPositionAndRotation(
                    view.InverseTransformPoint(_model.Muzzle.position),
                    Quaternion.Inverse(view.rotation) * _model.Muzzle.rotation);
            }
        }

        // ============================================================
        // POSE
        // ============================================================

        private void Pose(WeaponDefinition def, float dt)
        {
            float ads = Mathf.SmoothStep(0f, 1f, _ads);
            float lowered = Mathf.SmoothStep(0f, 1f, _lowered);
            float steady = Mathf.Lerp(1f, def.adsSteadiness, ads);

            // Sway: the gun lags behind how fast the view turns (mouse, or the vehicle turning).
            Quaternion viewRotation = _camera.transform.rotation;
            Vector2 turnRate = Vector2.zero;
            if (_hasLastView && dt > 0f)
            {
                Vector3 d = (Quaternion.Inverse(_lastViewRotation) * viewRotation).eulerAngles;
                turnRate = new Vector2(Mathf.DeltaAngle(0f, d.x), Mathf.DeltaAngle(0f, d.y)) / dt;
            }
            _lastViewRotation = viewRotation;
            _hasLastView = true;

            Vector2 swayTarget = Vector2.ClampMagnitude(-turnRate * def.swayAmount, def.swayMax);
            _sway = Vector2.Lerp(_sway, swayTarget, 1f - Mathf.Exp(-def.swaySmoothing * dt));

            // Bob: pick the walk/run/sprint bob for the current speed.
            float roll = UpdateBob(def, dt, out Vector3 bob);

            // Hip -> ADS: aiming puts the sight on the camera's centre line, adsDistance ahead.
            Vector3 adsPosition = new Vector3(0f, 0f, def.adsDistance) - _sightOffset;
            Vector3 position = Vector3.Lerp(def.hipPosition, adsPosition, ads)
                             + bob * steady
                             + Vector3.back * _kick.Back
                             + Vector3.down * (def.lowerDrop * lowered);

            Quaternion rotation = Quaternion.Euler(def.lowerAngle * lowered - _kick.Up, 0f, 0f)
                                * Quaternion.Euler(_sway.x * steady, _sway.y * steady, roll * steady);

            _model.transform.SetLocalPositionAndRotation(position, rotation);
        }

        /// <summary>
        /// Footstep bob for the current gait. Returns the roll (degrees) and outputs the position offset.
        /// Shape: one dip per footstep, one side-to-side sway (and tilt) per two steps.
        /// </summary>
        private float UpdateBob(WeaponDefinition def, float dt, out Vector3 offset)
        {
            float speed = _state.Speed.Value;
            float gait = _movement != null ? _movement.Gait(speed) : Mathf.Clamp(speed / 3f, 0f, 3f);

            BobGait bob = gait <= 1f ? walkBob
                        : gait <= 2f ? BobGait.Lerp(walkBob, runBob, gait - 1f)
                        : BobGait.Lerp(runBob, sprintBob, gait - 2f);

            // Only on the ground and on foot; below walk speed it shrinks toward nothing.
            bool onFoot = _state.IsGrounded.Value && !_state.IsSeated;
            _bobWeight = Mathf.MoveTowards(_bobWeight, onFoot ? Mathf.Clamp01(gait) : 0f, bobFadeSpeed * dt);

            // Half a cycle (pi) per footstep.
            if (gait > 0.05f)
                _bobPhase = Mathf.Repeat(_bobPhase + bob.stepsPerSecond * Mathf.PI * dt, 2f * Mathf.PI);

            float scale = def.bobScale * _bobWeight;
            float side = Mathf.Sin(_bobPhase);
            offset = new Vector3(side * bob.horizontal, -Mathf.Abs(Mathf.Cos(_bobPhase)) * bob.vertical, 0f) * scale;
            return side * bob.roll * scale;
        }
    }
}