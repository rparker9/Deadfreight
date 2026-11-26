using UnityEngine;

/// <summary>
/// Marks the points on a gun model that the view needs. Put it on the root of the gun prefab.
/// The root's +Z must point down the barrel and its scale must be 1.
/// Spawned colliders are disabled, so the gun never blocks anything.
/// </summary>
public sealed class WeaponModel : MonoBehaviour
{
    [Tooltip("Where your eye goes when aiming down sights (at the rear sight, on the barrel line)")]
    [SerializeField] private Transform sight;
    [Tooltip("End of the barrel: tracers and projectiles start here")]
    [SerializeField] private Transform muzzle;

    [Header("Third Person Hands")]
    [Tooltip("Third person: the gun is attached so this point sits exactly on the right hand bone, " +
             "with the same rotation. Rotate it until the gun sits right in the hand.")]
    [SerializeField] private Transform rightHandGrip;
    [Tooltip("Third person: the left hand is IK'd here (fore-end). Leave empty for one-handed guns.")]
    [SerializeField] private Transform leftHandGrip;

    public Transform Sight => sight;
    public Transform Muzzle => muzzle;
    public Transform RightHandGrip => rightHandGrip;
    public Transform LeftHandGrip => leftHandGrip;

    /// <summary>Gun root -> sight, in the gun's own space (unscaled metres).</summary>
    public Vector3 SightOffset => sight != null ? LocalPosition(sight) : Vector3.zero;

    /// <summary>A child's position in the gun's own space.</summary>
    public Vector3 LocalPosition(Transform point)
        => Quaternion.Inverse(transform.rotation) * (point.position - transform.position);

    /// <summary>A child's rotation in the gun's own space.</summary>
    public Quaternion LocalRotation(Transform point)
        => Quaternion.Inverse(transform.rotation) * point.rotation;
}
