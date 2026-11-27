using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Deadfreight.Player;
using Deadfreight.Core;

namespace Deadfreight.Combat
{
    /// <summary>
    /// PRESENTATION ONLY: the gun on other players' bodies (off for your own player, whose body is hidden).
    /// Reads everything from PlayerWeapon; nothing here affects where shots go.
    ///
    ///  - The gun sits in Hand Socket (an empty under the right hand bone, placed by eye in the prefab).
    ///  - The Animator's "Upper Body" layer plays hold / aim / reload clips for the weapon's hold type.
    ///  - The left hand is IK'd to the gun's LeftHandGrip (off while reloading, so the reload clip shows).
    ///  - After animation, the spine bends toward the synced aim, plus kick. The head is put back
    ///    afterwards, so head look isn't doubled.
    ///
    /// Everything is set up in Awake from prefab references: no runtime bone lookups, no grip maths.
    /// </summary>
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerWeapon))]
    public sealed class WeaponViewThirdPerson : NetworkBehaviour
    {
        [Header("Body")]
        [SerializeField] private Animator animator;
        [Tooltip("Empty child of the right hand bone. The gun's root (its handle) goes exactly here.")]
        [SerializeField] private Transform handSocket;
        [Tooltip("Spine bones to bend toward the aim, hips to chest (e.g. Spine, Spine1, Spine2)")]
        [SerializeField] private Transform[] spineBones;
        [SerializeField] private Transform head;

        [Header("Left Hand")]
        [Tooltip("Two Bone IK on the left arm, targeting Left Hand Target")]
        [SerializeField] private TwoBoneIKConstraint leftHandIK;
        [Tooltip("The left arm IK target. Make it a child of Hand Socket.")]
        [SerializeField] private Transform leftHandTarget;

        [Header("Aim")]
        [Tooltip("How far ahead of the eye the barrel is aimed. Other machines don't know what the player " +
                 "is actually looking at, so this is a fixed distance along their aim.")]
        [Unit("m")] [SerializeField] private float aimDistance = 20f;
        [Tooltip("How far the aim can turn left/right from the hips (a seated passenger looking sideways)")]
        [Unit("°")] [SerializeField] private float maxBodyYaw = 80f;
        [Tooltip("How much of the way the gun turns to the aim while holding (not aiming down sights). 1 = all the way.")]
        [Range(0f, 1f)] [SerializeField] private float holdAimFollow = 0f;
        [Tooltip("Largest turn the spine will make to line the gun up, so odd poses never fold the body")]
        [Unit("°")] [SerializeField] private float maxCorrection = 75f;
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

        private PlayerWeapon _weapon;
        private PlayerState _state;
        private WeaponModel _model;

        private int _layer = -1;
        private int _weaponType, _isAiming, _isReloading, _reloadSpeed;

        private readonly WeaponKick _kick = new();
        private float _weight;   // upper-body layer weight, 0..1
        private float _ads;      // 0..1
        private float _pitch;
        private float _yaw;

        // ============================================================
        // LIFECYCLE
        // ============================================================

        private void Awake()
        {
            _weapon = GetComponent<PlayerWeapon>();
            _state = GetComponent<PlayerState>();
            if (animator == null) animator = GetComponentInChildren<Animator>();

            if (animator != null)
            {
                _layer = animator.GetLayerIndex(upperBodyLayer);
                _weaponType = Param(weaponTypeParam);
                _isAiming = Param(isAimingParam);
                _isReloading = Param(isReloadingParam);
                _reloadSpeed = Param(reloadSpeedParam);
            }

            WeaponDefinition def = _weapon.Weapon;
            if (def == null || def.modelPrefab == null || handSocket == null) return;

            _model = WeaponModel.Spawn(def.modelPrefab, handSocket, castShadows: true);
            _model.SetVisible(false);
            _weapon.SetModelMuzzle(_model.Muzzle, firstPerson: false);

            // The gun's root is at the socket, so the left grip's pose relative to the socket never changes.
            if (leftHandTarget != null && _model.LeftHandGrip != null)
                leftHandTarget.SetPositionAndRotation(_model.LeftHandGrip.position, _model.LeftHandGrip.rotation);
        }

        public override void OnNetworkSpawn()
        {
            SetWeights(0f);

            // Your own body is hidden (first person), so this view is only for other players.
            if (IsOwner || _model == null)
            {
                enabled = false;
                return;
            }

            _pitch = _state.AimPitch.Value;
            _yaw = _state.AimYaw.Value;
            _weapon.Fired += OnFired;
        }

        public override void OnNetworkDespawn()
        {
            if (!IsOwner && _model != null) _weapon.Fired -= OnFired;
        }

        private void LateUpdate()
        {
            WeaponDefinition def = _weapon.Weapon;
            float dt = Time.deltaTime;

            bool armed = _weapon.IsArmed;
            bool aiming = armed && _weapon.IsAiming;
            bool reloading = _weapon.IsReloadingShown;
            _model.SetVisible(armed);

            _weight = Mathf.MoveTowards(_weight, armed ? 1f : 0f, blendSpeed * dt);
            _ads = Mathf.MoveTowards(_ads, aiming ? 1f : 0f, dt / def.adsTime);
            _kick.Update(def, dt);

            // Animator: which clips, and how much of them.
            if (_layer >= 0) animator.SetLayerWeight(_layer, _weight);
            if (_weaponType != 0) animator.SetInteger(_weaponType, (int)def.holdType);
            if (_isAiming != 0) animator.SetBool(_isAiming, aiming);
            if (_isReloading != 0) animator.SetBool(_isReloading, reloading);
            if (_reloadSpeed != 0) animator.SetFloat(_reloadSpeed, def.bodyReloadClipLength / Mathf.Max(def.reloadTime, 0.1f));

            // Left hand on the fore-end, except while reloading (the clip moves it to the magazine).
            if (leftHandIK != null)
            {
                bool wantIK = armed && _model.LeftHandGrip != null && !reloading;
                leftHandIK.weight = Mathf.MoveTowards(leftHandIK.weight, wantIK ? _weight : 0f, blendSpeed * dt);
            }

            AimSpine(def, dt);
        }

        private void OnFired() => _kick.Add(_weapon.Weapon);

        // ============================================================
        // AIM
        // ============================================================

        /// <summary>
        /// Point the gun at the aim, whatever the clip's stance: measure where the barrel actually points
        /// after animation, then turn the spine by exactly the difference. The target is the same line
        /// the hitscan uses (from the eye along the synced aim), aimDistance ahead.
        /// Runs after animation and IK, so the arms, gun and left hand all move with the chest.
        /// </summary>
        private void AimSpine(WeaponDefinition def, float dt)
        {
            if (spineBones == null || spineBones.Length == 0 || _weight <= 0f) return;

            // Smooth toward the synced aim, the same way PlayerHeadLook does.
            float t = aimSmoothSpeed > 0f ? 1f - Mathf.Exp(-aimSmoothSpeed * dt) : 1f;
            _pitch = Mathf.Lerp(_pitch, _state.AimPitch.Value, t);
            _yaw += Mathf.DeltaAngle(_yaw, _state.AimYaw.Value) * t;

            // Aim direction, with yaw limited relative to the hips.
            Transform body = animator.transform;
            float bodyYaw = body.eulerAngles.y;
            float yaw = bodyYaw + Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, _yaw), -maxBodyYaw, maxBodyYaw);
            Vector3 aimDir = Quaternion.Euler(_pitch + def.bodyAimCorrection.x, yaw + def.bodyAimCorrection.y, 0f) * Vector3.forward;
            Vector3 target = _weapon.EyePosition + aimDir * aimDistance;

            float follow = _weight * Mathf.Lerp(holdAimFollow, 1f, Mathf.SmoothStep(0f, 1f, _ads));
            Quaternion headBefore = head != null ? head.rotation : Quaternion.identity;

            // Turning the spine also moves the muzzle, so a second pass cleans up what's left (only when
            // following fully; a partial follow is meant to stay partial).
            Transform gun = _model.transform;
            Transform muzzle = _model.Muzzle != null ? _model.Muzzle : gun;
            int passes = follow > 0.99f ? 2 : 1;
            for (int i = 0; i < passes; i++)
            {
                Vector3 want = target - muzzle.position;
                if (want.sqrMagnitude < 0.01f) break;

                // The gun's +Z is the barrel (WeaponModel convention).
                Quaternion.FromToRotation(gun.forward, want.normalized).ToAngleAxis(out float angle, out Vector3 axis);
                if (angle > 180f) angle -= 360f;
                if (Mathf.Abs(angle) < 0.01f) break;

                angle = Mathf.Clamp(angle, -maxCorrection, maxCorrection) * follow;
                TurnSpine(Quaternion.AngleAxis(angle, axis));
            }

            // Kick tips the chest up.
            if (_kick.Up > 0f)
                TurnSpine(Quaternion.AngleAxis(-_kick.Up * _weight, body.right));

            // Keep the head where head look put it.
            if (head != null) head.rotation = headBefore;
        }

        /// <summary>
        /// Spread a world-space turn evenly over the spine. Each bone turns by a share, and each share
        /// also carries the bones above it, so the chest (and the gun) end up turned by the whole amount.
        /// </summary>
        private void TurnSpine(Quaternion turn)
        {
            Quaternion step = Quaternion.Slerp(Quaternion.identity, turn, 1f / spineBones.Length);
            foreach (Transform bone in spineBones)
                if (bone != null) bone.rotation = step * bone.rotation;
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private void SetWeights(float weight)
        {
            if (_layer >= 0) animator.SetLayerWeight(_layer, weight);
            if (leftHandIK != null) leftHandIK.weight = weight;
        }

        /// <summary>Hash of a parameter the Animator has, or 0 (skipped) if it doesn't.</summary>
        private int Param(string paramName)
        {
            if (animator.runtimeAnimatorController == null) return 0;
            foreach (var p in animator.parameters)
                if (p.name == paramName) return p.nameHash;

            Debug.LogWarning($"[WeaponViewThirdPerson] Animator has no '{paramName}' parameter; it will be skipped.", this);
            return 0;
        }
    }
}
