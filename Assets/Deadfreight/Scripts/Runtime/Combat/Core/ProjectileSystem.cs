using System.Collections.Generic;
using UnityEngine;

namespace Deadfreight.Combat
{
    /// <summary>
    /// Simulates projectiles as plain data (no NetworkObjects, no Rigidbodies).
    ///
    /// Every machine runs its own copies:
    ///  - Authoritative (server only): invisible, deals damage.
    ///  - Cosmetic (every client, host included): visible, only plays impact FX.
    /// Both start from the same origin and direction, so they fly the same arc. They can disagree
    /// slightly against moving targets, which only affects FX, never damage.
    /// </summary>
    public sealed class ProjectileSystem : MonoBehaviour
    {
        private static ProjectileSystem _instance;
        public static ProjectileSystem Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[ProjectileSystem]");
                    DontDestroyOnLoad(go);
                    _instance = go.AddComponent<ProjectileSystem>();
                }
                return _instance;
            }
        }

        // How long the visual takes to slide from the muzzle onto the true (eye-line) path.
        private const float VisualConvergeTime = 0.15f;

        private sealed class Projectile
        {
            public WeaponDefinition Def;
            public Vector3 Position;
            public Vector3 Velocity;
            public float Age;
            public int Mask;
            public Transform IgnoreShooter;
            public Transform IgnoreVehicle;
            public ulong AttackerClientId;
            public bool Authoritative;
            public Transform Visual;
            public Vector3 VisualOffset;
        }

        private readonly List<Projectile> _active = new();
        private readonly Collider[] _overlap = new Collider[64];
        private readonly HashSet<Health> _damaged = new();

        public void Spawn(WeaponDefinition def, Vector3 origin, Vector3 direction, int mask,
                          Transform ignoreShooter, Transform ignoreVehicle, ulong attackerClientId, bool authoritative,
                          float catchUp, Vector3 visualStart)
        {
            var p = new Projectile
            {
                Def = def,
                Position = origin,
                Velocity = direction.normalized * def.projectileSpeed,
                Mask = mask,
                IgnoreShooter = ignoreShooter,
                IgnoreVehicle = ignoreVehicle,
                AttackerClientId = attackerClientId,
                Authoritative = authoritative,
                VisualOffset = visualStart - origin,
            };

            if (!authoritative && def.projectileVisual != null)
                p.Visual = Instantiate(def.projectileVisual, visualStart, Quaternion.LookRotation(p.Velocity)).transform;

            // Fast-forward to make up for network delay, so this copy lines up with the shooter's.
            if (catchUp > 0f && Step(p, catchUp))
            {
                Kill(p);
                return;
            }

            _active.Add(p);
            UpdateVisual(p);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                Projectile p = _active[i];
                if (Step(p, dt))
                {
                    Kill(p);
                    _active.RemoveAt(i);
                }
                else
                {
                    UpdateVisual(p);
                }
            }
        }

        /// <summary>Advances one step. Returns true when the projectile is finished (hit or expired).</summary>
        private bool Step(Projectile p, float dt)
        {
            Vector3 gravity = Physics.gravity * p.Def.gravityScale;
            Vector3 next = p.Position + p.Velocity * dt + 0.5f * dt * dt * gravity;
            Vector3 delta = next - p.Position;
            float length = delta.magnitude;

            if (length > 0f &&
                ShotQuery.Cast(p.Position, delta / length, p.Def.projectileRadius, length, p.Mask,
                                p.IgnoreShooter, p.IgnoreVehicle, out RaycastHit hit))
            {
                p.Position = hit.point;
                Impact(p, hit);
                return true;
            }

            p.Position = next;
            p.Velocity += gravity * dt;
            p.Age += dt;
            return p.Age >= p.Def.lifetime;
        }

        private void Impact(Projectile p, RaycastHit hit)
        {
            if (p.Authoritative)
            {
                ApplyDamage(p, hit);
            }
            else if (p.Def.impactPrefab != null)
            {
                Destroy(Instantiate(p.Def.impactPrefab, hit.point, Quaternion.LookRotation(hit.normal)), 3f);
            }
        }

        private void ApplyDamage(Projectile p, RaycastHit hit)
        {
            Vector3 direction = p.Velocity.normalized;

            if (p.Def.explosionRadius <= 0f)
            {
                if (hit.collider.TryGetComponent(out Hitbox hitbox) && hitbox.Owner != null && hitbox.Owner.DamagedByPlayers)
                {
                    hitbox.Owner.ApplyDamage(new DamageInfo
                    {
                        Amount = p.Def.damage * hitbox.Multiplier,
                        Point = hit.point,
                        Direction = direction,
                        AttackerClientId = p.AttackerClientId,
                    });
                }
                return;
            }

            // Explosion: each Health is damaged once, by its closest hitbox. No multipliers.
            float radius = p.Def.explosionRadius;
            int count = Physics.OverlapSphereNonAlloc(hit.point, radius, _overlap, p.Mask, QueryTriggerInteraction.Ignore);
            _damaged.Clear();

            for (int i = 0; i < count; i++)
            {
                if (!_overlap[i].TryGetComponent(out Hitbox hitbox) || hitbox.Owner == null) continue;
                Health health = hitbox.Owner;
                if (!health.DamagedByPlayers || !_damaged.Add(health)) continue;

                float distance = Vector3.Distance(hit.point, _overlap[i].ClosestPoint(hit.point));
                float scale = 1f - Mathf.Clamp01(distance / radius);
                health.ApplyDamage(new DamageInfo
                {
                    Amount = p.Def.damage * scale,
                    Point = hit.point,
                    Direction = (health.transform.position - hit.point).normalized,
                    AttackerClientId = p.AttackerClientId,
                });
            }
        }

        private void UpdateVisual(Projectile p)
        {
            if (p.Visual == null) return;
            float offsetScale = 1f - Mathf.Clamp01(p.Age / VisualConvergeTime);
            p.Visual.position = p.Position + p.VisualOffset * offsetScale;
            if (p.Velocity.sqrMagnitude > 0.0001f)
                p.Visual.rotation = Quaternion.LookRotation(p.Velocity);
        }

        private static void Kill(Projectile p)
        {
            if (p.Visual != null) Destroy(p.Visual.gameObject);
        }
    }
}
