using UnityEngine;

/// <summary>
/// A seat on a vehicle. Place on an empty transform:
///   - Position = where the seated player's FEET (player root) go
///   - +Z (forward) = the way the player faces
/// Listed in VehicleSeats on the vehicle root.
/// </summary>
public sealed class VehicleSeat : MonoBehaviour
{
    public enum SeatRole { Driver, Passenger }

    [SerializeField] private SeatRole role = SeatRole.Passenger;

    [Tooltip("Where the player is placed when they get out. Defaults to the seat itself.")]
    [SerializeField] private Transform exitPoint;

    [Tooltip("How far the seated player can turn their head left/right from the seat's forward (degrees)")]
    [Range(10f, 180f)] [SerializeField] private float yawLimit = 120f;

    [Tooltip("Camera pitches and rolls with the vehicle. Off = only follows its heading (gentler).")]
    [SerializeField] private bool inheritTilt = true;

    public SeatRole Role => role;
    public Transform ExitPoint => exitPoint != null ? exitPoint : transform;
    public float YawLimit => yawLimit;

    /// <summary>Rotation the seated player's view is relative to.</summary>
    public Quaternion ViewReference =>
        inheritTilt ? transform.rotation : Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

    private void OnDrawGizmos()
    {
        Gizmos.color = role == SeatRole.Driver ? Color.yellow : Color.cyan;
        Gizmos.DrawWireCube(transform.position + transform.up * 0.5f, new Vector3(0.4f, 1f, 0.4f));
        Gizmos.DrawLine(transform.position + transform.up * 0.5f, transform.position + transform.up * 0.5f + transform.forward * 0.6f);

        if (exitPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(exitPoint.position, 0.2f);
        }
    }
}
