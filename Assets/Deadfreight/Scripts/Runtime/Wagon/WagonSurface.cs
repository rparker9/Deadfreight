using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional: put on ground colliders to change how wagon wheels behave on them.
/// No component = road (1, 1).
///
/// Suggested starting values:
///   Road / packed dirt   rolling 1     grip 1
///   Grass                rolling 2.5   grip 0.9
///   Gravel               rolling 3     grip 0.8
///   Mud                  rolling 8     grip 0.6   (heavy wagon + mud = stuck)
///   Ice / wet stone      rolling 0.7   grip 0.25
/// </summary>
public sealed class WagonSurface : MonoBehaviour
{
    [Tooltip("Multiplies each wheel's rolling resistance")]
    [SerializeField] private float rollingResistance = 1f;
    [Tooltip("Multiplies sideways grip and braking")]
    [SerializeField] private float grip = 1f;

    public float RollingResistance => rollingResistance;
    public float Grip => grip;

    // Collider -> surface lookup, cached so wheels don't call GetComponent every physics step.
    private static readonly Dictionary<Collider, WagonSurface> Cache = new();

    public static WagonSurface For(Collider col)
    {
        if (col == null) return null;
        if (!Cache.TryGetValue(col, out var surface))
        {
            surface = col.GetComponentInParent<WagonSurface>();
            Cache[col] = surface; // caches "none" too
        }
        return surface;
    }

    private void OnDestroy() => Cache.Clear();
}