using UnityEngine;

/// <summary>
/// The one cast used by aiming, hitscan, projectiles and the server's wall check:
/// the nearest hit that isn't part of the shooter or the vehicle they're seated in.
/// </summary>
public static class ShotQuery
{
    private static readonly RaycastHit[] Buffer = new RaycastHit[32];

    /// <param name="ignoreA">Usually the shooter's root.</param>
    /// <param name="ignoreB">Usually the vehicle the shooter is seated in (or null).</param>
    public static bool Cast(Vector3 origin, Vector3 direction, float radius, float distance, int layerMask,
                            Transform ignoreA, Transform ignoreB, out RaycastHit hit)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, Buffer, distance, layerMask, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, Buffer, distance, layerMask, QueryTriggerInteraction.Ignore);

        hit = default;
        float best = float.MaxValue;
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = Buffer[i];
            Transform t = h.collider.transform;
            if (ignoreA != null && t.IsChildOf(ignoreA)) continue;
            if (ignoreB != null && t.IsChildOf(ignoreB)) continue;
            if (h.distance >= best) continue;

            // A sphere cast that starts inside a collider reports distance 0 and point (0,0,0).
            if (h.distance <= 0f && h.point == Vector3.zero)
            {
                h.point = origin;
                h.normal = -direction;
            }

            best = h.distance;
            hit = h;
            found = true;
        }
        return found;
    }
}
