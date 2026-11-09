using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Defines hand attachment points for IK animation.
/// Used by PlayerIKClient to animate remote players' hands on grabbed objects.
/// 
/// NOTE: This is VISUAL ONLY. Physics grabbing uses NetworkGrabbable.
/// </summary>
public class HandGripPoints : MonoBehaviour
{
    [System.Serializable]
    public class HandGrip
    {
        [Tooltip("Name of this grip (e.g., 'Handle', 'Top', 'Bottom')")]
        public string name = "Grip";

        [Tooltip("Transform defining hand position and rotation")]
        public Transform transform;

        [Tooltip("Which hand uses this grip")]
        public HandSide handSide = HandSide.Right;

        [Tooltip("Priority when multiple grips available (higher = preferred)")]
        public int priority = 0;
    }

    public enum HandSide
    {
        Right,
        Left,
        Both  // For two-handed objects
    }

    [Header("Grip Configuration")]
    [Tooltip("Hand grip points on this object")]
    public HandGrip[] handGrips = new HandGrip[2];

    [Header("Object Type")]
    [Tooltip("Does this object require two hands?")]
    public bool isTwoHanded = true;

    [Tooltip("Can be held with one hand if both aren't available?")]
    public bool canUseOneHand = false;

    [Header("Debug")]
    [Tooltip("Show grip gizmos in editor")]
    public bool showGizmos = true;

    [Tooltip("Size of grip gizmos")]
    public float gizmoSize = 0.05f;

    // ============================================================
    // EDITOR HELPERS
    // ============================================================

    private void OnValidate()
    {
        // Auto-create grip transforms if they don't exist
        for (int i = 0; i < handGrips.Length; i++)
        {
            if (handGrips[i] != null && handGrips[i].transform == null)
            {
                GameObject gripGO = new GameObject($"GripPoint_{i}");
                gripGO.transform.SetParent(transform);
                gripGO.transform.localPosition = Vector3.zero;
                gripGO.transform.localRotation = Quaternion.identity;
                handGrips[i].transform = gripGO.transform;
            }
        }
    }

    // ============================================================
    // GIZMOS
    // ============================================================

    private void OnDrawGizmos()
    {
        if (!showGizmos || handGrips == null) return;

        foreach (var grip in handGrips)
        {
            if (grip?.transform == null) continue;

            // Color by hand side
            Gizmos.color = grip.handSide == HandSide.Right ? Color.red :
                           grip.handSide == HandSide.Left ? Color.blue :
                           Color.green;

            // Draw sphere at grip position
            Gizmos.DrawWireSphere(grip.transform.position, gizmoSize);

            // Draw forward direction (palm facing)
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(grip.transform.position,
                           grip.transform.position + grip.transform.forward * gizmoSize * 3);

            // Draw up direction (fingers pointing)
            Gizmos.color = Color.green;
            Gizmos.DrawLine(grip.transform.position,
                           grip.transform.position + grip.transform.up * gizmoSize * 2);
        }
    }

    // ============================================================
    // PUBLIC API (for PlayerIKClient)
    // ============================================================

    /// <summary>
    /// Get the grip point for a specific hand.
    /// Returns highest priority grip matching the hand side.
    /// </summary>
    public HandGrip GetGripForHand(HandSide hand)
    {
        HandGrip bestGrip = null;
        int highestPriority = int.MinValue;

        foreach (var grip in handGrips)
        {
            if (grip?.transform == null) continue;

            // Check if this grip matches requested hand
            bool matches = grip.handSide == hand || grip.handSide == HandSide.Both;

            if (matches && grip.priority > highestPriority)
            {
                bestGrip = grip;
                highestPriority = grip.priority;
            }
        }

        return bestGrip;
    }

    /// <summary>
    /// Get both hand grips for two-handed objects.
    /// </summary>
    public (HandGrip right, HandGrip left) GetBothHandGrips()
    {
        HandGrip rightGrip = GetGripForHand(HandSide.Right);
        HandGrip leftGrip = GetGripForHand(HandSide.Left);

        return (rightGrip, leftGrip);
    }
}