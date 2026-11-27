using System;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;
using Deadfreight.Player;
using Deadfreight.Vehicles;
using Deadfreight.Core;

namespace Deadfreight.Combat
{
    /// <summary>One hitscan result: client -> server (for damage) and server -> others (for FX).</summary>
    public struct ShotHit : INetworkSerializable
    {
        public Vector3 Point;          // impact point, or the max-range end point on a miss
        public Vector3 Normal;
        public bool HitSurface;
        public bool HitTarget;         // hit a Hitbox
        public NetworkObjectReference Target;
        public byte HitboxIndex;

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Point);
            s.SerializeValue(ref Normal);
            s.SerializeValue(ref HitSurface);
            s.SerializeValue(ref HitTarget);
            if (HitTarget)
            {
                s.SerializeValue(ref Target);
                s.SerializeValue(ref HitboxIndex);
            }
        }
    }

    /// <summary>
    /// The player's gun. Lives on the Player prefab; input comes from PlayerInputController.
    ///
    /// Owner:  trigger -> predicted ammo/cooldown -> trace or spawn locally (instant feedback) -> RPC to server.
    /// Server: checks sender, seat, origin, fire rate, ammo -> validates and applies damage -> RPC to others for FX.
    ///
    /// Hitscan is "client-reported, server-validated", the same idea as PlayerInteractor's reach check:
    /// the shooter says what it hit and the server checks the claim is plausible (near the hitbox,
    /// no wall in the way). It trusts clients more than a competitive shooter would, which is fine for co-op.
    ///
    /// Shooting is first person only (passenger seats are first person; drivers can't shoot).
    /// While seated, every cast ignores the vehicle you're in, since your eye is inside it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerState))]
    public sealed class PlayerWeapon : NetworkBehaviour
    {
        private const float MaxCatchUp = 0.25f;      // s, cap on projectile fast-forward
        private const float DefaultEyeHeight = 1.6f; // matches PlayerInteractor, used if no eye is set

        [SerializeField] private WeaponDefinition weapon;

        [Header("References")]
        [Tooltip("Shot origin. Use the camera rig's Pitch Pivot (it exists on every machine).")]
        [SerializeField] private Transform eye;
        [SerializeField] private PlayerCameraController cameraController;

        [Header("Layers")]
        [Tooltip("What shots can hit: world + Hitbox layer")]
        [SerializeField] private LayerMask hitMask = ~0;
        [Tooltip("Static world only, NOT the Hitbox layer. The server rejects hits that pass through these.")]
        [SerializeField] private LayerMask lineOfSightBlockers = 1;

        [Header("Server Validation")]
        [Tooltip("Allowed gap between the reported origin and the server's view of the eye. " +
                 "Needs headroom for riding a moving vehicle (15 m/s * ~0.15 s ≈ 2.3 m).")]
        [Unit("m")][SerializeField, Min(0f)] private float originTolerance = 3f;
        [Tooltip("Allowed gap between a claimed hit point and the hitbox's position on the server")]
        [Unit("m")][SerializeField, Min(0f)] private float hitTolerance = 1.5f;
        [Tooltip("Extra shots let through at once, for packets that arrive bunched together")]
        [SerializeField, Min(0)] private int burstAllowance = 2;

        // ---- All machines ----
        private PlayerState _state;
        private PlayerSeating _seating;

        // Muzzles of the spawned gun models (set by the views): yours in first person, the body's for others.
        private Transform _viewMuzzle;
        private Transform _bodyMuzzle;

        // ---- Synced presentation state (owner writes; the views read it) ----
        private readonly NetworkVariable<bool> _aiming = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        private readonly NetworkVariable<bool> _reloading = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // ---- Owner (predicted) ----
        private int _ammo;
        private float _nextFireTime;
        private float _reloadEndTime = -1f;
        private bool _triggerHeld;
        private bool _semiAutoLatched;

        // ---- Server ----
        private int _serverAmmo;
        private float _fireTokens;
        private float _lastTokenTime;
        private float _serverReloadEnd = -1f;

        /// <summary>A shot was fired: on the owner when it fires, on everyone else when the server relays it. For kick/FX.</summary>
        public event Action Fired;

        // ---- For the HUD (owner) ----
        public WeaponDefinition Weapon => weapon;
        public int Ammo => _ammo;
        public bool IsReloading => _reloadEndTime >= 0f;

        // ---- For the views (all machines) ----
        /// <summary>Aiming down sights (synced).</summary>
        public bool IsAiming => _aiming.Value;
        /// <summary>Reloading as everyone sees it: exact on the owner, synced for everyone else.</summary>
        public bool IsReloadingShown => IsOwner ? IsReloading : _reloading.Value;

        /// <summary>Gun out: has a weapon, hands free, and not driving. Works on every machine.</summary>
        public bool IsArmed
        {
            get
            {
                if (weapon == null) return false;
                if (_state.HeldObject.Value.TryGet(out NetworkObject _)) return false;
                if (_seating != null && _seating.IsDriving) return false;
                return true;
            }
        }

        // ============================================================
        // LIFECYCLE
        // ============================================================

        private void Awake()
        {
            _state = GetComponent<PlayerState>();
            _seating = GetComponent<PlayerSeating>();
            if (cameraController == null)
                cameraController = GetComponent<PlayerCameraController>();
        }

        public override void OnNetworkSpawn()
        {
            if (weapon == null) return;
            _ammo = weapon.magazineSize;

            if (IsServer)
            {
                _serverAmmo = weapon.magazineSize;
                _fireTokens = 1 + burstAllowance;
                _lastTokenTime = Time.time;
            }
        }

        private void Update()
        {
            if (!IsOwner || weapon == null) return;
            PublishViewState();

            if (IsReloading && Time.time >= _reloadEndTime)
            {
                _ammo = weapon.magazineSize;
                _reloadEndTime = -1f;
            }

            if (!_triggerHeld)
            {
                _semiAutoLatched = false;
                return;
            }
            if (_semiAutoLatched || IsReloading || Time.time < _nextFireTime) return;

            if (_ammo <= 0)
            {
                Reload();
                return;
            }

            if (!weapon.automatic) _semiAutoLatched = true;
            _nextFireTime = Time.time + weapon.fireInterval;
            _ammo--;
            FireLocal();
        }

        // ============================================================
        // INPUT (owner, called by PlayerInputController)
        // ============================================================

        public void SetTrigger(bool held) => _triggerHeld = held;

        /// <summary>Aim down sights (only while armed).</summary>
        public void SetAiming(bool aiming)
        {
            if (!IsOwner) return;
            bool value = aiming && IsArmed;
            if (_aiming.Value != value) _aiming.Value = value;
        }

        /// <summary>Called by the views: the spawned gun model's muzzle (first person for you, body for others).</summary>
        public void SetModelMuzzle(Transform modelMuzzle, bool firstPerson)
        {
            if (modelMuzzle == null) return;
            if (firstPerson) _viewMuzzle = modelMuzzle;
            else _bodyMuzzle = modelMuzzle;
        }

        public void Reload()
        {
            if (!IsOwner || weapon == null || IsReloading || _ammo >= weapon.magazineSize) return;
            _reloadEndTime = Time.time + weapon.reloadTime;
            ReloadServerRpc();
        }

        // ============================================================
        // FIRING (owner)
        // ============================================================

        private void FireLocal()
        {
            Vector3 origin = EyePosition;
            Vector3 baseDir = (GetAimPoint() - origin).normalized;
            Transform vehicle = SeatedVehicle();
            int count = weapon.pelletsPerShot;

            if (weapon.kind == WeaponDefinition.FireKind.Hitscan)
            {
                var hits = new ShotHit[count];
                for (int i = 0; i < count; i++)
                    hits[i] = TraceHitscan(origin, ApplySpread(baseDir), vehicle);

                PlayShotFx(hits);
                FireHitscanServerRpc(origin, hits);
            }
            else
            {
                var dirs = new Vector3[count];
                Vector3 visualStart = MuzzlePosition;
                for (int i = 0; i < count; i++)
                {
                    dirs[i] = ApplySpread(baseDir);
                    ProjectileSystem.Instance.Spawn(weapon, origin, dirs[i], hitMask, transform, vehicle,
                                                    OwnerClientId, authoritative: false, catchUp: 0f, visualStart);
                }
                PlayMuzzleFlash();
                FireProjectileServerRpc(origin, dirs);
            }

            Fired?.Invoke();

            if (cameraController != null)
                cameraController.AddRecoil(-weapon.recoilPitch, Random.Range(-weapon.recoilYaw, weapon.recoilYaw));
        }

        /// <summary>
        /// The point under the crosshair. Shots go from the eye to this point, so they still land
        /// on the crosshair if the camera is ever offset from the eye.
        /// </summary>
        private Vector3 GetAimPoint()
        {
            Transform view = cameraController != null && cameraController.Camera != null
                ? cameraController.Camera.transform
                : eye != null ? eye : transform;
            Vector3 forward = view.forward;

            if (ShotQuery.Cast(view.position, forward, 0f, weapon.range, hitMask, transform, SeatedVehicle(), out RaycastHit hit) &&
                Vector3.Dot(hit.point - EyePosition, forward) > 0.5f)   // ignore anything between the camera and the eye
                return hit.point;

            return view.position + forward * weapon.range;
        }

        private Vector3 ApplySpread(Vector3 direction)
        {
            if (weapon.spreadDegrees <= 0f) return direction;
            Vector2 r = Random.insideUnitCircle * Mathf.Tan(weapon.spreadDegrees * Mathf.Deg2Rad);
            return (Quaternion.LookRotation(direction) * new Vector3(r.x, r.y, 1f)).normalized;
        }

        private ShotHit TraceHitscan(Vector3 origin, Vector3 direction, Transform vehicle)
        {
            var shot = new ShotHit { Point = origin + direction * weapon.range, Normal = -direction };
            if (!ShotQuery.Cast(origin, direction, 0f, weapon.range, hitMask, transform, vehicle, out RaycastHit hit))
                return shot;

            shot.HitSurface = true;
            shot.Point = hit.point;
            shot.Normal = hit.normal;

            if (hit.collider.TryGetComponent(out Hitbox hitbox) && hitbox.Owner != null && hitbox.Owner.NetworkObject != null)
            {
                shot.HitTarget = true;
                shot.Target = hitbox.Owner.NetworkObject;
                shot.HitboxIndex = (byte)hitbox.Index;
            }
            return shot;
        }

        // ============================================================
        // RPCs (server)
        // ============================================================

        [Rpc(SendTo.Server)]
        private void FireHitscanServerRpc(Vector3 origin, ShotHit[] hits, RpcParams rpcParams = default)
        {
            if (!ServerValidateShot(rpcParams.Receive.SenderClientId, origin, hits?.Length ?? 0)) return;

            Transform vehicle = SeatedVehicle();
            for (int i = 0; i < hits.Length; i++)
                if (hits[i].HitTarget) ServerApplyHitscanDamage(origin, hits[i], vehicle);

            ShotFxRpc(hits);
        }

        [Rpc(SendTo.Server)]
        private void FireProjectileServerRpc(Vector3 origin, Vector3[] dirs, RpcParams rpcParams = default)
        {
            ulong sender = rpcParams.Receive.SenderClientId;
            if (!ServerValidateShot(sender, origin, dirs?.Length ?? 0)) return;

            // The shooter's copy started half a round trip ago: skip ahead to match it.
            float catchUp = Mathf.Min(HalfRttSeconds(sender), MaxCatchUp);
            Transform vehicle = SeatedVehicle();

            for (int i = 0; i < dirs.Length; i++)
            {
                if (!(dirs[i].sqrMagnitude >= 0.5f)) continue;   // not a real direction (also rejects NaN)
                ProjectileSystem.Instance.Spawn(weapon, origin, dirs[i], hitMask, transform, vehicle,
                                                OwnerClientId, authoritative: true, catchUp, origin);
            }

            SpawnProjectilesRpc(origin, dirs, catchUp);
        }

        [Rpc(SendTo.Server)]
        private void ReloadServerRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId || _serverReloadEnd >= 0f) return;
            // 10% leniency so jitter doesn't reject the first shot after a reload.
            _serverReloadEnd = Time.time + weapon.reloadTime * 0.9f;
        }

        // ============================================================
        // SERVER VALIDATION
        // ============================================================

        private bool ServerValidateShot(ulong sender, Vector3 origin, int pelletCount)
        {
            if (weapon == null || sender != OwnerClientId) return false;
            if (_seating != null && _seating.IsDriving) return false;
            if (pelletCount < 1 || pelletCount > weapon.pelletsPerShot) return false;

            // Written as !(a <= b) so NaN from a bad client fails the check.
            if (!((origin - EyePosition).sqrMagnitude <= originTolerance * originTolerance)) return false;

            // Finish a reload lazily, when the next shot arrives.
            if (_serverReloadEnd >= 0f && Time.time >= _serverReloadEnd)
            {
                _serverAmmo = weapon.magazineSize;
                _serverReloadEnd = -1f;
            }

            // Token bucket: refills at the weapon's fire rate, so bunched packets still pass.
            _fireTokens = Mathf.Min(1 + burstAllowance,
                                    _fireTokens + (Time.time - _lastTokenTime) / weapon.fireInterval);
            _lastTokenTime = Time.time;

            if (_serverReloadEnd >= 0f || _serverAmmo <= 0 || _fireTokens < 1f)
            {
                SyncAmmoRpc(_serverAmmo);
                return false;
            }

            _fireTokens -= 1f;
            _serverAmmo--;
            return true;
        }

        private void ServerApplyHitscanDamage(Vector3 origin, in ShotHit hit, Transform vehicle)
        {
            if (!hit.Target.TryGet(out NetworkObject target) || !target.TryGetComponent(out Health health)) return;
            if (!health.DamagedByPlayers || health.IsDead) return;
            if (!health.TryGetHitbox(hit.HitboxIndex, out Hitbox hitbox)) return;

            Vector3 toHit = hit.Point - origin;
            float distance = toHit.magnitude;
            if (!(distance >= 0.01f && distance <= weapon.range + hitTolerance)) return;   // also rejects NaN

            // The shooter sees targets slightly in the past, so allow some slack around the hitbox.
            Vector3 closest = hitbox.Collider.ClosestPoint(hit.Point);
            if (!((closest - hit.Point).sqrMagnitude <= hitTolerance * hitTolerance)) return;

            // No shooting through walls. Ignores us and our vehicle, since the eye is inside it.
            Vector3 direction = toHit / distance;
            if (ShotQuery.Cast(origin, direction, 0f, distance - 0.1f, lineOfSightBlockers, transform, vehicle, out _)) return;

            health.ApplyDamage(new DamageInfo
            {
                Amount = weapon.DamageAt(distance) * hitbox.Multiplier,
                Point = hit.Point,
                Direction = direction,
                AttackerClientId = OwnerClientId,
            });
        }

        // ============================================================
        // RPCs (server -> clients)
        // ============================================================

        // NotOwner = everyone except the shooter (the host included, if the host didn't fire).
        [Rpc(SendTo.NotOwner)]
        private void ShotFxRpc(ShotHit[] hits)
        {
            PlayShotFx(hits);
            Fired?.Invoke();
        }

        [Rpc(SendTo.NotOwner)]
        private void SpawnProjectilesRpc(Vector3 origin, Vector3[] dirs, float serverCatchUp)
        {
            float catchUp = Mathf.Min(serverCatchUp + (IsServer ? 0f : HalfRttSeconds(NetworkManager.ServerClientId)), MaxCatchUp);
            Vector3 visualStart = MuzzlePosition;
            Transform vehicle = SeatedVehicle();

            for (int i = 0; i < dirs.Length; i++)
                ProjectileSystem.Instance.Spawn(weapon, origin, dirs[i], hitMask, transform, vehicle,
                                                OwnerClientId, authoritative: false, catchUp, visualStart);

            PlayMuzzleFlash();
            Fired?.Invoke();
        }

        /// <summary>
        /// Corrects the owner's predicted ammo after the server rejected a shot.
        /// Can be off by one if more shots are in flight; the next reload resyncs it.
        /// </summary>
        [Rpc(SendTo.Owner)]
        private void SyncAmmoRpc(int serverAmmo) => _ammo = serverAmmo;

        // ============================================================
        // HELPERS
        // ============================================================

        /// <summary>Owner: keep the synced aiming/reloading in step (NetworkVariables only send on change).</summary>
        private void PublishViewState()
        {
            if (!IsArmed && _aiming.Value) _aiming.Value = false;
            if (_reloading.Value != IsReloading) _reloading.Value = IsReloading;
        }

        /// <summary>Where shots visually start: the first-person muzzle for the owner, the body's for everyone else.</summary>
        private Vector3 MuzzlePosition => MuzzleTransform != null ? MuzzleTransform.position : EyePosition;

        /// <summary>The muzzle point this machine shows shots from (null until a gun model has spawned).</summary>
        private Transform MuzzleTransform => IsOwner ? _viewMuzzle : _bodyMuzzle;

        /// <summary>Where shots start: the eye. Exists on every machine (the view uses it to aim the body's gun).</summary>
        public Vector3 EyePosition => eye != null ? eye.position : transform.position + Vector3.up * DefaultEyeHeight;

        /// <summary>The vehicle we're seated in, or null. Seats are server-written, so this works everywhere.</summary>
        private Transform SeatedVehicle()
        {
            if (!_state.IsSeated || !_state.SeatVehicle.Value.TryGet(out NetworkObject vehicle) || vehicle == null)
                return null;
            return vehicle.transform;
        }

        private void PlayShotFx(ShotHit[] hits)
        {
            PlayMuzzleFlash();

            Vector3 from = MuzzlePosition;
            for (int i = 0; i < hits.Length; i++)
            {
                if (weapon.tracerPrefab != null)
                    Instantiate(weapon.tracerPrefab).Init(from, hits[i].Point);

                if (hits[i].HitSurface && weapon.impactPrefab != null)
                    Destroy(Instantiate(weapon.impactPrefab, hits[i].Point, Quaternion.LookRotation(hits[i].Normal)), 3f);
            }
        }

        /// <summary>
        /// One flash per shot (not per pellet), parented to the muzzle so it points down the barrel
        /// and stays on the gun while it plays. Make the flash prefab face +Z.
        /// </summary>
        private void PlayMuzzleFlash()
        {
            if (weapon.muzzleFlashPrefab == null) return;

            Transform at = MuzzleTransform;
            GameObject flash = at != null
                ? Instantiate(weapon.muzzleFlashPrefab, at.position, at.rotation, at)
                : Instantiate(weapon.muzzleFlashPrefab, MuzzlePosition, Quaternion.LookRotation(transform.forward));
            Destroy(flash, 3f);
        }

        private float HalfRttSeconds(ulong clientId)
        {
            if (clientId == NetworkManager.LocalClientId) return 0f;
            return NetworkManager.NetworkConfig.NetworkTransport.GetCurrentRtt(clientId) * 0.0005f; // ms -> s, halved
        }
    }
}
