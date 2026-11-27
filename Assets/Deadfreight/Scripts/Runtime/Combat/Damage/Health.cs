using System;
using Unity.Netcode;
using UnityEngine;

namespace Deadfreight.Combat
{
    public struct DamageInfo
    {
        public float Amount;
        public Vector3 Point;
        public Vector3 Direction;
        public ulong AttackerClientId;
    }

    /// <summary>
    /// SERVER-AUTHORITATIVE health. Put it on the same GameObject as the NetworkObject.
    /// Hitboxes are collected from children in Awake. Their order is the hierarchy order, which is
    /// the same on every machine for the same prefab, so an index is safe to send in an RPC.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Health : NetworkBehaviour
    {
        [SerializeField, Min(1f)] private float maxHealth = 100f;
        [Tooltip("Turn off on the Player prefab to disable friendly fire")]
        [SerializeField] private bool damagedByPlayers = true;

        private readonly NetworkVariable<float> _current = new(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private Hitbox[] _hitboxes;

        public float Current => _current.Value;
        public float Max => maxHealth;
        public bool IsDead => IsSpawned && _current.Value <= 0f;
        public bool DamagedByPlayers => damagedByPlayers;

        /// <summary>Server only.</summary>
        public event Action<DamageInfo> Damaged;
        /// <summary>All machines (driven by the NetworkVariable).</summary>
        public event Action Died;

        // ============================================================
        // LIFECYCLE
        // ============================================================

        private void Awake()
        {
            _hitboxes = GetComponentsInChildren<Hitbox>(true);
            for (int i = 0; i < _hitboxes.Length; i++)
                _hitboxes[i].Bind(this, i);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer) _current.Value = maxHealth;
            _current.OnValueChanged += OnHealthChanged;
        }

        public override void OnNetworkDespawn() => _current.OnValueChanged -= OnHealthChanged;

        private void OnHealthChanged(float previous, float current)
        {
            if (previous > 0f && current <= 0f) Died?.Invoke();
        }

        // ============================================================
        // API
        // ============================================================

        public bool TryGetHitbox(int index, out Hitbox hitbox)
        {
            hitbox = index >= 0 && index < _hitboxes.Length ? _hitboxes[index] : null;
            return hitbox != null;
        }

        /// <summary>Server only; ignored anywhere else.</summary>
        public void ApplyDamage(in DamageInfo info)
        {
            if (!IsServer || IsDead || info.Amount <= 0f) return;
            _current.Value = Mathf.Max(0f, _current.Value - info.Amount);
            Damaged?.Invoke(info);
        }
    }
}
