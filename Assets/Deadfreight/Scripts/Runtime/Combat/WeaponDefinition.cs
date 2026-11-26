using UnityEngine;

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

    [Header("Firing")]
    public FireKind kind = FireKind.Hitscan;
    public bool automatic = false;
    [Unit("s")] [Min(0.01f)] public float fireInterval = 0.25f;
    [Tooltip("Rays or projectiles per trigger pull (shotgun > 1)")]
    [Min(1)] public int pelletsPerShot = 1;
    [Tooltip("Half-angle of the spread cone")]
    [Unit("°")] [Min(0f)] public float spreadDegrees = 0.5f;

    [Header("Recoil (owner camera kick)")]
    [Unit("°")] public float recoilPitch = 1.5f;
    [Tooltip("Random left/right kick, up to this much either way")]
    [Unit("°")] public float recoilYaw = 0.5f;

    [Header("Ammo")]
    [Min(1)] public int magazineSize = 8;
    [Unit("s")] [Min(0f)] public float reloadTime = 1.5f;

    [Header("Damage")]
    [Min(0f)] public float damage = 25f;
    [Tooltip("Hitscan max distance; also how far aiming looks")]
    [Unit("m")] [Min(0.1f)] public float range = 100f;
    [Tooltip("Hitscan only. X = distance / range (0..1), Y = damage multiplier")]
    public AnimationCurve falloff = AnimationCurve.Constant(0f, 1f, 1f);

    [Header("Projectile")]
    [Unit("m/s")] [Min(0.1f)] public float projectileSpeed = 60f;
    [Tooltip("Multiplier on Physics.gravity. 0 = straight line")]
    public float gravityScale = 1f;
    [Tooltip("0 = raycast, > 0 = sphere cast")]
    [Unit("m")] [Min(0f)] public float projectileRadius = 0.05f;
    [Unit("s")] [Min(0.1f)] public float lifetime = 5f;
    [Tooltip("> 0 makes impacts explode, with damage falling off linearly to the edge")]
    [Unit("m")] [Min(0f)] public float explosionRadius = 0f;
    public GameObject projectileVisual;

    [Header("FX (cosmetic, local only)")]
    public Tracer tracerPrefab;
    public GameObject muzzleFlashPrefab;
    public GameObject impactPrefab;

    public float DamageAt(float distance) => damage * falloff.Evaluate(Mathf.Clamp01(distance / range));
}
