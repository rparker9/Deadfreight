using UnityEngine;

/// <summary>
/// Marks a collider as damageable and routes hits to the Health above it.
/// Put hitbox colliders on the Hitbox layer. Use a multiplier above 1 on the head for headshots.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class Hitbox : MonoBehaviour
{
    [SerializeField, Min(0f)] private float damageMultiplier = 1f;

    public Health Owner { get; private set; }
    public int Index { get; private set; }
    public Collider Collider { get; private set; }
    public float Multiplier => damageMultiplier;

    internal void Bind(Health owner, int index)
    {
        Owner = owner;
        Index = index;
        Collider = GetComponent<Collider>();
    }
}
