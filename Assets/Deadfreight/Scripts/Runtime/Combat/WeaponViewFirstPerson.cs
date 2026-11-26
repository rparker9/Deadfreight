using Unity.Netcode;
using UnityEngine;

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
    private const float BobFullSpeed = 4.5f;     // m/s, full bob

    [SerializeField] private PlayerCameraController cameraController;

    private PlayerWeapon _weapon;
    private PlayerState _state;
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

        // Bob: a figure-eight while walking on the ground, scaled by speed.
        float speed = _state.Speed.Value;
        bool walking = _state.IsGrounded.Value && !_state.IsSeated;
        _bobWeight = Mathf.MoveTowards(_bobWeight, walking ? Mathf.Clamp01(speed / BobFullSpeed) : 0f, 4f * dt);
        _bobPhase = Mathf.Repeat(_bobPhase + speed * def.bobFrequency * dt * 2f * Mathf.PI, 2f * Mathf.PI);
        Vector3 bob = new Vector3(Mathf.Sin(_bobPhase), -Mathf.Abs(Mathf.Cos(_bobPhase)), 0f) * (def.bobAmount * _bobWeight);

        // Hip -> ADS: aiming puts the sight on the camera's centre line, adsDistance ahead.
        Vector3 adsPosition = new Vector3(0f, 0f, def.adsDistance) - _sightOffset;
        Vector3 position = Vector3.Lerp(def.hipPosition, adsPosition, ads)
                         + bob * steady
                         + Vector3.back * _kick.Back
                         + Vector3.down * (def.lowerDrop * lowered);

        Quaternion rotation = Quaternion.Euler(def.lowerAngle * lowered - _kick.Up, 0f, 0f)
                            * Quaternion.Euler(_sway.x * steady, _sway.y * steady, 0f);

        _model.transform.SetLocalPositionAndRotation(position, rotation);
    }
}
