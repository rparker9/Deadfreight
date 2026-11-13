using UnityEngine;

/// <summary>
/// A seat on a vehicle. Place on an empty transform:
///   - Position = where the seated player's FEET (player root) go
///   - +Z (forward) = the way the player faces
/// Listed in VehicleSeating on the vehicle root.
/// Contains no MonoBehavior lifecycle methods other than DrawGizmos for debugging.
/// </summary>
public sealed class VehicleSeat : MonoBehaviour
{
    public enum SeatRole 
    { 
        Driver, 
        Passenger 
    }
    public enum SteerMode
    {
        /// <summary>
        /// Halo-style: the vehicle steers toward where the camera looks. W/S = throttle.
        /// </summary>
        Camera,
        /// <summary>
        /// A/D steer directly.
        /// </summary>
        Keys
    }

    [SerializeField] private SeatRole role = SeatRole.Passenger;

    [Tooltip("Where the player is placed when they get out. Defaults to the seat itself.")]
    [SerializeField] private Transform exitPoint;

    [Header("First-Person Camera")]
    [Tooltip("How far the seated player can turn their head left/right from the seat's forward (degrees)")]
    [Range(10f, 180f)][SerializeField] private float yawLimit = 120f;

    [Tooltip("Camera pitches and rolls with the vehicle. Off = only follows its heading (gentler). First person only.")]
    [SerializeField] private bool inheritTilt = true;

    [Header("Third Person (Halo-style)")]
    [Tooltip("Orbit camera around the vehicle instead of looking from the seat")]
    [SerializeField] private bool thirdPerson = true;
    [Tooltip("Point the camera orbits around. Empty = vehicle root, raised by Camera Height.")]
    [SerializeField] private Transform cameraFocus;
    [SerializeField] private float cameraHeight = 2f;
    [SerializeField] private float cameraDistance = 6f;

    [Header("Driving")]
    [SerializeField] private SteerMode steerMode = SteerMode.Camera;
    [Tooltip("Camera mode: how far (degrees) the camera must be off the vehicle's heading for full lock")]
    [SerializeField] private float cameraSteerFullAngle = 30f;

    // Public Accessors
    public SeatRole Role => role;
    public Transform ExitPoint => exitPoint != null ? exitPoint : transform;
    public float YawLimit => yawLimit;
    public bool ThirdPerson => thirdPerson;
    public float CameraDistance => cameraDistance;
    public SteerMode Steering => steerMode;
    public float CameraSteerFullAngle => cameraSteerFullAngle;

    /// <summary>
    /// The vehicle this seat belongs to (root with VehicleSeating).
    /// </summary>
    public Transform Vehicle => _vehicle != null ? _vehicle : (_vehicle = FindVehicleRoot());

    /// <summary>World point the third-person camera orbits.</summary>
    public Vector3 CameraFocusPoint => cameraFocus != null
        ? cameraFocus.position
        : Vehicle.position + Vector3.up * cameraHeight;

    private Transform _vehicle;

    private Transform FindVehicleRoot()
    {
        var seats = GetComponentInParent<VehicleSeating>();
        return seats != null ? seats.transform : transform.root;
    }

    /// <summary>
    /// Rotation the seated player's view is relative to.
    /// </summary>
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