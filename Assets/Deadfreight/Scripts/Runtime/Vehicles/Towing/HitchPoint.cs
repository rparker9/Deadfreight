using UnityEngine;

/// <summary>
/// Where something can be towed from: put on an empty transform at the back of the
/// towing vehicle (e.g. a tow hook). A trailer's VehicleHitch attaches to it.
/// </summary>
public sealed class HitchPoint : MonoBehaviour
{
    /// <summary>The vehicle this hitch belongs to.</summary>
    public Vehicle Vehicle => _vehicle != null ? _vehicle : (_vehicle = GetComponentInParent<Vehicle>());

    private Vehicle _vehicle;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.6f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, 0.08f);
        Gizmos.DrawLine(transform.position, transform.position - transform.forward * 0.3f);
    }
}