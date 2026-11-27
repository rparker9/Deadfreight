using UnityEngine;
using Deadfreight.Core;

namespace Deadfreight.Combat
{
    /// <summary>
    /// Static data for one weapon. Every machine has the same asset, so none of this is networked.
    /// </summary>
    [CreateAssetMenu(menuName = "Deadfreight/Weapon Definition")]
    public sealed class WeaponDefinition : ScriptableObject
    {
        public enum FireKind
        {
            Hitscan,
            Projectile
        }

        /// <summary>Which set of third-person body animations this weapon uses (Animator WeaponType: 0, 1).</summary>
        public enum HoldType
        {
            Rifle,
            Pistol
        }

        [Header("Firing")]
        public FireKind kind = FireKind.Hitscan;
        public bool automatic = false;
        [Unit("s")][Min(0.01f)] public float fireInterval = 0.25f;
        [Tooltip("Rays or projectiles per trigger pull (shotgun > 1)")]
        [Min(1)] public int pelletsPerShot = 1;
        [Tooltip("Half-angle of the spread cone")]
        [Unit("°")][Min(0f)] public float spreadDegrees = 0.5f;

        [Header("Recoil (owner camera kick)")]
        [Unit("°")] public float recoilPitch = 1.5f;
        [Tooltip("Random left/right kick, up to this much either way")]
        [Unit("°")] public float recoilYaw = 0.5f;

        [Header("Ammo")]
        [Min(1)] public int magazineSize = 8;
        [Unit("s")][Min(0f)] public float reloadTime = 1.5f;

        [Header("Damage")]
        [Min(0f)] public float damage = 25f;
        [Tooltip("Hitscan max distance; also how far aiming looks")]
        [Unit("m")][Min(0.1f)] public float range = 100f;
        [Tooltip("Hitscan only. X = distance / range (0..1), Y = damage multiplier")]
        public AnimationCurve falloff = AnimationCurve.Constant(0f, 1f, 1f);

        [Header("Projectile")]
        [Unit("m/s")][Min(0.1f)] public float projectileSpeed = 60f;
        [Tooltip("Multiplier on Physics.gravity. 0 = straight line")]
        public float gravityScale = 1f;
        [Tooltip("0 = raycast, > 0 = sphere cast")]
        [Unit("m")][Min(0f)] public float projectileRadius = 0.05f;
        [Unit("s")][Min(0.1f)] public float lifetime = 5f;
        [Tooltip("> 0 makes impacts explode, with damage falling off linearly to the edge")]
        [Unit("m")][Min(0f)] public float explosionRadius = 0f;
        public GameObject projectileVisual;

        [Header("View: Model")]
        [Tooltip("Gun prefab with WeaponModel on its root (root = the handle, +Z down the barrel). " +
                 "Spawned in front of your camera, and in Hand Socket on other players' bodies.")]
        public WeaponModel modelPrefab;

        [Header("View: First Person")]
        [Tooltip("Where the gun's handle sits relative to the camera when not aiming (right, up, forward)")]
        [Unit("m")] public Vector3 hipPosition = new(0.18f, -0.16f, 0.4f);
        [Tooltip("How far in front of the eye the sight sits when aiming down sights")]
        [Unit("m")][Min(0.05f)] public float adsDistance = 0.25f;
        [Unit("s")][Min(0.01f)] public float adsTime = 0.18f;
        [Tooltip("Camera field of view while aiming, as a fraction of normal")]
        [Range(0.3f, 1f)] public float adsFovMultiplier = 0.8f;
        [Tooltip("Sway and bob while aiming, as a fraction of normal")]
        [Range(0f, 1f)] public float adsSteadiness = 0.3f;

        [Header("View: Third Person (body animation)")]
        [Tooltip("Which body animations to use")]
        public HoldType holdType = HoldType.Rifle;
        [Tooltip("Length of this hold type's reload clip. The clip is sped up or slowed to fit Reload Time.")]
        [Unit("s")][Min(0.1f)] public float bodyReloadClipLength = 2.5f;
        [Tooltip("Small extra offset on the body's aim (pitch down, yaw right). Normally 0: the body lines the barrel up on its own.")]
        [Unit("°")] public Vector2 bodyAimCorrection = Vector2.zero;

        [Header("View: Kick")]
        [Tooltip("First person: gun slides back")]
        [Unit("m")] public float kickBack = 0.04f;
        [Tooltip("First person: gun tips up. Third person: the upper body tips up by this much.")]
        [Unit("°")] public float kickUp = 4f;
        [Tooltip("How fast the gun moves into a kick")]
        public float kickSnappiness = 30f;
        [Tooltip("How fast the gun settles back")]
        public float kickReturnSpeed = 10f;

        [Header("View: Sway and Bob (first person)")]
        [Tooltip("Degrees of lag per degree/second of turning")]
        public float swayAmount = 0.015f;
        [Unit("°")] public float swayMax = 4f;
        public float swaySmoothing = 10f;
        [Tooltip("How much this gun bobs, as a multiple of the walk/run/sprint bob on WeaponViewFirstPerson " +
                 "(heavier guns a bit more)")]
        [Min(0f)] public float bobScale = 1f;

        [Header("View: Lowered (first person reload and sprint)")]
        [Tooltip("The gun tips down and drops while reloading or sprinting (there are no first-person arms to animate)")]
        [Unit("°")] public float lowerAngle = 35f;
        [Unit("m")] public float lowerDrop = 0.12f;

        [Header("FX (cosmetic, local only)")]
        public Tracer tracerPrefab;
        public GameObject muzzleFlashPrefab;
        public GameObject impactPrefab;

        public float DamageAt(float distance) => damage * falloff.Evaluate(Mathf.Clamp01(distance / range));
    }
}