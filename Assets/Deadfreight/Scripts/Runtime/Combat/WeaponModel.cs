using UnityEngine;

/// <summary>
/// Marks the points on a gun model. Put it on the root of the gun prefab.
///
/// Convention (both views rely on it):
///   - The root sits where the RIGHT HAND holds the gun (the handle).
///   - Root +Z points down the barrel, +Y up, scale 1. Rotate the mesh child to fit, never the root.
///   - The points below are direct children of the root.
/// Spawned colliders are disabled, so the gun never blocks anything.
/// </summary>
public sealed class WeaponModel : MonoBehaviour
{
    [Tooltip("Where your eye goes when aiming down sights (rear sight, on the barrel line)")]
    [SerializeField] private Transform sight;
    [Tooltip("End of the barrel: tracers and projectiles start here")]
    [SerializeField] private Transform muzzle;
    [Tooltip("Third person: the left hand is IK'd here (fore-end). Rotate it so the left palm wraps the gun. " +
             "Leave empty for one-handed guns.")]
    [SerializeField] private Transform leftHandGrip;

    public Transform Sight => sight;
    public Transform Muzzle => muzzle;
    public Transform LeftHandGrip => leftHandGrip;

    /// <summary>Root -> sight, in the gun's own space (metres).</summary>
    public Vector3 SightOffset => sight != null
        ? Quaternion.Inverse(transform.rotation) * (sight.position - transform.position)
        : Vector3.zero;

    /// <summary>Spawn a copy under a parent, zeroed, with colliders off.</summary>
    public static WeaponModel Spawn(WeaponModel prefab, Transform parent, bool castShadows)
    {
        WeaponModel model = Instantiate(prefab, parent, false);
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

        foreach (var col in model.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        if (!castShadows)
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return model;
    }

    public void SetVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }
}
