using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// PRESENTATION ONLY: shows the gun. Nothing here affects where shots go.
///
/// You (owner), first person, fully procedural: a gun model in front of the camera with
/// hip/ADS position, kick, sway, bob, and a lowered pose while reloading or sprinting.
///
/// Everyone else, third person, animation-driven:
///  - The Animator's "Upper Body" layer plays hold / aim / reload clips for the weapon's hold type.
///  - The gun is attached to the right hand bone, so it follows those clips.
///  - The left hand is IK'd to the gun's fore-end (off while reloading, so the reload clip shows).
///  - After animation, the spine is bent toward the synced aim (pitch, and yaw for seated passengers),
///    plus kick. The head is put back afterwards, so head look isn't doubled.
///
/// Synced: aiming and reloading (owner writes). Kick plays from PlayerWeapon.Fired on every machine.
/// Runs after PlayerSeating and PlayerAnimator have moved and turned the body (execution order).
/// </summary>
[DefaultExecutionOrder(50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerState))]
[RequireComponent(typeof(PlayerWeapon))]
public sealed class PlayerWeaponView : NetworkBehaviour
{
    private const float LowerBlendTime = 0.2f;   // s, into and out of the lowered pose
    private const float BobFullSpeed = 4.5f;     // m/s, full bob

    [Header("First Person")]
    [SerializeField] private PlayerCameraController cameraController;

    [Header("Third Person")]
    [Tooltip("Two Bone IK on the left arm, targeting Left Hand Target")]
    [SerializeField] private TwoBoneIKConstraint leftHandIK;
    [Tooltip("The left arm IK target. Must be a CHILD OF THE RIGHT HAND BONE, so it moves with the gun.")]
    [SerializeField] private Transform leftHandTarget;
    [Tooltip("How far the upper body can twist left/right from the hips (a seated passenger looking sideways)")]
    [Unit("°")] [SerializeField] private float maxBodyYaw = 80f;
    [Tooltip("How much of the aim pitch the body follows while holding (not aiming down sights)")]
    [Range(0f, 1f)] [SerializeField] private float holdPitchFollow = 0.5f;
    [Tooltip("How fast the body follows the synced aim (like head look)")]
    [SerializeField] private float aimSmoothSpeed = 15f;
    [Tooltip("How fast the upper-body layer and left-hand IK fade in/out (per second)")]
    [SerializeField] private float blendSpeed = 6f;

    [Header("Animator")]
    [SerializeField] private string upperBodyLayer = "Upper Body";
    [Tooltip("Int: 0 rifle, 1 pistol")]
    [SerializeField] private string weaponTypeParam = "WeaponType";
    [SerializeField] private string isAimingParam = "IsAiming";
    [SerializeField] private string isReloadingParam = "IsReloading";
    [Tooltip("Float: reload clip speed, so the clip lasts exactly Reload Time")]
    [SerializeField] private string reloadSpeedParam = "ReloadSpeed";

    private readonly NetworkVariable<bool> _aiming = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<bool> _reloading = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private PlayerState _state;
    private PlayerSeating _seating;
    private PlayerWeapon _weapon;
    private Animator _animator;

    private WeaponModel _viewModel;   // owner, in front of the camera
    private WeaponModel _bodyModel;   // everyone else, in the right hand
    private Vector3 _sightOffset;
    private float _baseFov;

    // Animator (0 = missing, skipped)
    private int _layer = -1;
    private int _weaponType, _isAiming, _isReloading, _reloadSpeed;

    // Body bones (third person)
    private readonly List<Transform> _spine = new();
    private Transform _head;

    // Blends (0..1)
    private float _ads;
    private float _lowered;
    private float _upperWeight;

    // Kick: x = back (m), y = up (degrees). Same target/current easing as camera recoil.
    private Vector2 _kickTarget;
    private Vector2 _kickCurrent;

    // First person only
    private Vector2 _sway;
    private Quaternion _lastViewRotation;
    private bool _hasLastView;
    private float _bobPhase;
    private float _bobWeight;

    // Third person: smoothed synced aim
    private float _pitch;
    private float _yaw;

    public bool IsAiming => _aiming.Value;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        _seating = GetComponent<PlayerSeating>();
        _weapon = GetComponent<PlayerWeapon>();
        _animator = GetComponentInChildren<Animator>();
        if (cameraController == null)
            cameraController = GetComponent<PlayerCameraController>();

        if (_animator != null)
        {
            _layer = _animator.GetLayerIndex(upperBodyLayer);
            _weaponType = Param(weaponTypeParam);
            _isAiming = Param(isAimingParam);
            _isReloading = Param(isReloadingParam);
            _reloadSpeed = Param(reloadSpeedParam);

            if (_animator.isHuman)
            {
                foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
                {
                    Transform t = _animator.GetBoneTransform(bone);
                    if (t != null) _spine.Add(t);
                }
                _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        _weapon.Fired += OnFired;
        _pitch = _state.AimPitch.Value;
        _yaw = _state.AimYaw.Value;
        if (leftHandIK != null) leftHandIK.weight = 0f;
        if (_layer >= 0) _animator.SetLayerWeight(_layer, 0f);

        WeaponDefinition def = _weapon.Weapon;
        if (def == null || def.modelPrefab == null) return;

        if (IsOwner)
        {
            Camera cam = cameraController != null ? cameraController.Camera : null;
            if (cam == null) return;

            _viewModel = Instantiate(def.modelPrefab, cam.transform, false);
            _viewModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            PrepareModel(_viewModel, castShadows: false);
            _sightOffset = _viewModel.SightOffset;
            _baseFov = cam.fieldOfView;
        }
        else
        {
            Transform hand = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            if (hand == null)
            {
                Debug.LogWarning($"[PlayerWeaponView] No humanoid right hand on {name}; third-person gun disabled.", this);
                return;
            }

            _bodyModel = Instantiate(def.modelPrefab, hand, false);
            PrepareModel(_bodyModel, castShadows: true);
            AttachToHand(_bodyModel, hand);
        }
    }

    public override void OnNetworkDespawn()
    {
        _weapon.Fired -= OnFired;

        if (IsOwner && cameraController != null && cameraController.Camera != null && _baseFov > 0f)
            cameraController.Camera.fieldOfView = _baseFov;

        if (_viewModel != null) Destroy(_viewModel.gameObject);
        if (_bodyModel != null) Destroy(_bodyModel.gameObject);
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        WeaponDefinition def = _weapon.Weapon;
        if (def == null) return;

        float dt = Time.deltaTime;
        bool armed = IsArmed();

        // Owner publishes its state; everyone reads it.
        if (IsOwner)
        {
            if (!armed && _aiming.Value) _aiming.Value = false;
            if (_reloading.Value != _weapon.IsReloading) _reloading.Value = _weapon.IsReloading;
        }

        bool aiming = armed && _aiming.Value;
        bool lowered = _reloading.Value || _state.IsSprinting.Value;

        _ads = Mathf.MoveTowards(_ads, aiming ? 1f : 0f, dt / def.adsTime);
        _lowered = Mathf.MoveTowards(_lowered, lowered ? 1f : 0f, dt / LowerBlendTime);
        UpdateKick(def, dt);

        if (_viewModel != null)
        {
            SetVisible(_viewModel, armed);
            if (armed) PoseFirstPerson(def, dt);
            else _hasLastView = false;
            UpdateFov(def);
        }

        if (_bodyModel != null)
        {
            SetVisible(_bodyModel, armed);
            UpdateThirdPerson(def, armed, aiming, dt);
        }
    }

    // ============================================================
    // INPUT (owner, called by PlayerInputController)
    // ============================================================

    public void SetAiming(bool aiming)
    {
        if (!IsOwner) return;
        bool value = aiming && IsArmed();
        if (_aiming.Value != value) _aiming.Value = value;
    }

    // ============================================================
    // API
    // ============================================================

    /// <summary>
    /// Where shots visually start on this machine: your view model's muzzle, or the body model's for others.
    /// False if there's no visible model (PlayerWeapon falls back to its own muzzle points).
    /// </summary>
    public bool TryGetMuzzle(out Vector3 position)
    {
        WeaponModel model = IsOwner ? _viewModel : _bodyModel;
        if (model != null && model.gameObject.activeSelf && model.Muzzle != null)
        {
            position = model.Muzzle.position;
            return true;
        }
        position = default;
        return false;
    }

    // ============================================================
    // FIRST PERSON (owner)
    // ============================================================

    private void PoseFirstPerson(WeaponDefinition def, float dt)
    {
        float ads = Mathf.SmoothStep(0f, 1f, _ads);
        float lowered = Mathf.SmoothStep(0f, 1f, _lowered);
        float steady = Mathf.Lerp(1f, def.adsSteadiness, ads);

        // Sway: the gun lags behind how fast the view is turning (mouse, or the vehicle turning).
        Quaternion viewRotation = _viewModel.transform.parent.rotation;
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
                         + Vector3.back * _kickCurrent.x
                         + Vector3.down * (def.lowerDrop * lowered);

        Quaternion rotation = Quaternion.Euler(def.lowerAngle * lowered - _kickCurrent.y, 0f, 0f)
                            * Quaternion.Euler(_sway.x * steady, _sway.y * steady, 0f);

        _viewModel.transform.SetLocalPositionAndRotation(position, rotation);
    }

    private void UpdateFov(WeaponDefinition def)
    {
        Camera cam = cameraController != null ? cameraController.Camera : null;
        if (cam == null || _baseFov <= 0f) return;
        cam.fieldOfView = _baseFov * Mathf.Lerp(1f, def.adsFovMultiplier, Mathf.SmoothStep(0f, 1f, _ads));
    }

    // ============================================================
    // THIRD PERSON (everyone else)
    // ============================================================

    private void UpdateThirdPerson(WeaponDefinition def, bool armed, bool aiming, float dt)
    {
        // Animator: which clips, and how much of them.
        _upperWeight = Mathf.MoveTowards(_upperWeight, armed ? 1f : 0f, blendSpeed * dt);
        if (_layer >= 0) _animator.SetLayerWeight(_layer, _upperWeight);
        if (_weaponType != 0) _animator.SetInteger(_weaponType, (int)def.holdType);
        if (_isAiming != 0) _animator.SetBool(_isAiming, aiming);
        if (_isReloading != 0) _animator.SetBool(_isReloading, _reloading.Value);
        if (_reloadSpeed != 0) _animator.SetFloat(_reloadSpeed, def.bodyReloadClipLength / Mathf.Max(def.reloadTime, 0.1f));

        // Left hand on the fore-end, except while reloading (the clip moves it to the magazine).
        if (leftHandIK != null)
        {
            bool wantIK = armed && _bodyModel.LeftHandGrip != null && !_reloading.Value;
            leftHandIK.weight = Mathf.MoveTowards(leftHandIK.weight, wantIK ? _upperWeight : 0f, blendSpeed * dt);
        }

        BendSpine(def, dt);
    }

    /// <summary>
    /// The aim clips point straight ahead from the hips. Bend the spine toward the synced aim:
    /// pitch always, yaw only as far as the body isn't already facing it (seated passengers).
    /// Runs after animation and IK, so the arms, gun and IK'd left hand all bend with the chest.
    /// </summary>
    private void BendSpine(WeaponDefinition def, float dt)
    {
        if (_spine.Count == 0 || _upperWeight <= 0f) return;

        // Smooth toward the synced aim, the same way PlayerHeadLook does.
        float t = aimSmoothSpeed > 0f ? 1f - Mathf.Exp(-aimSmoothSpeed * dt) : 1f;
        _pitch = Mathf.Lerp(_pitch, _state.AimPitch.Value, t);
        _yaw += Mathf.DeltaAngle(_yaw, _state.AimYaw.Value) * t;

        Transform body = _animator.transform;
        float follow = Mathf.Lerp(holdPitchFollow, 1f, Mathf.SmoothStep(0f, 1f, _ads));

        float pitch = (_pitch * follow + def.bodyAimCorrection.x - _kickCurrent.y) * _upperWeight;
        float yaw = (Mathf.Clamp(Mathf.DeltaAngle(body.eulerAngles.y, _yaw), -maxBodyYaw, maxBodyYaw)
                     + def.bodyAimCorrection.y) * _upperWeight;

        Quaternion headBefore = _head != null ? _head.rotation : Quaternion.identity;

        // Spread the bend evenly over the spine bones. Positive pitch = down (matches AimPitch).
        float n = _spine.Count;
        Quaternion step = Quaternion.AngleAxis(yaw / n, body.up) * Quaternion.AngleAxis(pitch / n, body.right);
        foreach (Transform bone in _spine)
            bone.rotation = step * bone.rotation;

        // Keep the head where head look put it (it already aims at the target).
        if (_head != null) _head.rotation = headBefore;
    }

    /// <summary>Attach the gun so its right-hand grip point sits exactly on the hand bone.</summary>
    private void AttachToHand(WeaponModel model, Transform hand)
    {
        Transform gun = model.transform;

        // Cancel the bone's scale (some rigs import bones scaled), so the gun keeps its real size.
        float handScale = Mathf.Max(hand.lossyScale.x, 0.0001f);
        gun.localScale = Vector3.one / handScale;

        if (model.RightHandGrip != null)
        {
            Quaternion gripRot = model.LocalRotation(model.RightHandGrip);
            Vector3 gripPos = model.LocalPosition(model.RightHandGrip);
            gun.rotation = hand.rotation * Quaternion.Inverse(gripRot);
            gun.position = hand.position - gun.rotation * gripPos;
        }
        else
        {
            gun.SetPositionAndRotation(hand.position, hand.rotation);
        }

        // The left-hand target lives under the right hand too, so this offset never changes.
        if (leftHandTarget != null && model.LeftHandGrip != null)
            leftHandTarget.SetPositionAndRotation(model.LeftHandGrip.position, model.LeftHandGrip.rotation);
    }

    // ============================================================
    // KICK (all machines)
    // ============================================================

    private void OnFired()
    {
        WeaponDefinition def = _weapon.Weapon;
        if (def != null) _kickTarget += new Vector2(def.kickBack, def.kickUp);
    }

    private void UpdateKick(WeaponDefinition def, float dt)
    {
        if (_kickTarget == Vector2.zero && _kickCurrent == Vector2.zero) return;

        _kickTarget *= Mathf.Exp(-def.kickReturnSpeed * dt);
        _kickCurrent = Vector2.Lerp(_kickCurrent, _kickTarget, 1f - Mathf.Exp(-def.kickSnappiness * dt));

        if (_kickTarget.sqrMagnitude < 1e-8f && _kickCurrent.sqrMagnitude < 1e-8f)
            _kickTarget = _kickCurrent = Vector2.zero;
    }

    // ============================================================
    // HELPERS
    // ============================================================

    /// <summary>Gun out: has a weapon, hands free, and not driving.</summary>
    private bool IsArmed()
    {
        if (_weapon.Weapon == null) return false;
        if (_state.HeldObject.Value.TryGet(out NetworkObject _)) return false;
        if (_seating != null && _seating.IsDriving) return false;
        return true;
    }

    private static void PrepareModel(WeaponModel model, bool castShadows)
    {
        foreach (var col in model.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        if (!castShadows)
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    private static void SetVisible(WeaponModel model, bool visible)
    {
        if (model.gameObject.activeSelf != visible)
            model.gameObject.SetActive(visible);
    }

    /// <summary>Hash of a parameter the Animator has, or 0 (skipped) if it doesn't.</summary>
    private int Param(string paramName)
    {
        foreach (var p in _animator.parameters)
            if (p.name == paramName) return p.nameHash;

        Debug.LogWarning($"[PlayerWeaponView] Animator has no '{paramName}' parameter; it will be skipped.", this);
        return 0;
    }
}
