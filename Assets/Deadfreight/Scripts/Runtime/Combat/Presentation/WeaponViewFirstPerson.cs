using Unity.Netcode;
using UnityEngine;
using Deadfreight.Core;
using Deadfreight.Player;

namespace Deadfreight.Combat
{
    /// <summary>
    /// OWNER-ONLY, PRESENTATION ONLY: the gun in front of your camera. Fully procedural:
    /// hip/ADS position, kick, sway, bob, and a lowered pose while reloading or sprinting.
    /// Reads everything from PlayerWeapon; nothing here affects where shots go.
    ///
    /// The gun is a child of the camera. Its root is the handle (+Z down the barrel), placed at the
    /// weapon's Hip Position, and moved so the Sight is on the camera's centre line when aiming.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWeapon))]
    public sealed class WeaponViewFirstPerson : NetworkBehaviour
    {
        private const float LowerBlendTime = 0.2f;   // s, into and out of the lowered pose

        [SerializeField] private PlayerCameraController cameraController;

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
        private Camera _camera;
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

            _model = WeaponModel.Spawn(def.modelPrefab, _camera.transform, castShadows: false);
            _sightOffset = _model.SightOffset;
            _baseFov = _camera.fieldOfView;
            _weapon.SetModelMuzzle(_model.Muzzle, firstPerson: true);
            _weapon.Fired += OnFired;
        }

        public override void OnNetworkDespawn()
        {
            if (_model == null) return;

            _weapon.Fired -= OnFired;
            if (_camera != null) _camera.fieldOfView = _baseFov;
            Destroy(_model.gameObject);
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
        }

        private void OnFired() => _kick.Add(_weapon.Weapon);

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