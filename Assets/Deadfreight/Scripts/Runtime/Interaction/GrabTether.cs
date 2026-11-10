using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Draws a rope/hook line from the player's hand to whatever they're DRAGGING.
/// Runs on every client (including the owner), purely visual.
/// Put on the Player root next to a LineRenderer.
/// </summary>
[RequireComponent(typeof(PlayerState))]
[RequireComponent(typeof(LineRenderer))]
public sealed class GrabTether : NetworkBehaviour
{
    [Tooltip("Where the rope starts: the right hand bone for others, a point near the camera for yourself")]
    [SerializeField] private Transform handAnchor;
    [SerializeField] private Transform ownerAnchor;

    private PlayerState _state;
    private LineRenderer _line;
    private NetworkGrabbable _dragged;
    private Collider _draggedCollider;

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        _line = GetComponent<LineRenderer>();
        _line.positionCount = 2;
        _line.enabled = false;
    }

    public override void OnNetworkSpawn()
    {
        _state.GrabbedObject.OnValueChanged += OnGrabbedChanged;
        OnGrabbedChanged(default, _state.GrabbedObject.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.GrabbedObject.OnValueChanged -= OnGrabbedChanged;
    }

    private void OnGrabbedChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        _dragged = null;
        _draggedCollider = null;

        if (curr.TryGet(out NetworkObject obj) && obj != null)
        {
            _dragged = obj.GetComponent<NetworkGrabbable>();
            _draggedCollider = obj.GetComponentInChildren<Collider>();
        }
    }

    private void LateUpdate()
    {
        Transform anchor = IsOwner && ownerAnchor != null ? ownerAnchor : handAnchor;

        bool show = _dragged != null
                 && _dragged.GrabMode.Value == NetworkGrabbable.Mode.Dragged
                 && anchor != null;

        _line.enabled = show;
        if (!show) return;

        Vector3 start = anchor.position;
        Vector3 end = _draggedCollider != null
            ? _draggedCollider.ClosestPoint(start)   // hook onto the nearest edge
            : _dragged.transform.position;

        _line.SetPosition(0, start);
        _line.SetPosition(1, end);
    }
}