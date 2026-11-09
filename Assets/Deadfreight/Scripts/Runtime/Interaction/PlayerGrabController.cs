using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles player grabbing, carrying, dropping, and throwing physics objects.
/// Also supports optional "use" interactions via Interactable components.
/// Input is handled by PlayerInputController.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerGrabController : NetworkBehaviour
{
    [Header("Raycast")]
    [SerializeField] private float maxReachDistance = 3.0f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Carry Settings")]
    [SerializeField] private float carryDistance = 1.8f;
    [SerializeField] private float dragDistance = 2.2f;
    [SerializeField] private float dragMassThreshold = 12f;
    [SerializeField] private float carryUpdateInterval = 1f / 30f;
    [SerializeField] private bool clampToObstacles = true;
    [SerializeField] private float obstaclePadding = 0.15f;

    [Header("Throw")]
    [SerializeField] private float throwImpulse = 6.5f;

    [Header("References")]
    [SerializeField] private PlayerCameraController cameraController;

    [Header("Collision")]
    [SerializeField] private bool ignoreHeldCollision = true;

    // Simple public state for UI
    public NetworkObject LookedAtObject { get; private set; }
    public NetworkGrabbable LookedAtGrabbable { get; private set; }
    public string ActionPrompt { get; private set; }
    public bool CanThrow => _localHeldObject != null;

    // Internal state
    private PlayerState _state;
    private NetworkObject _localHeldObject;
    private NetworkObject _heldObjectIgnoringCollision;
    private float _nextCarryUpdateTime;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        if (cameraController == null)
            cameraController = GetComponent<PlayerCameraController>();
    }

    public override void OnNetworkSpawn()
    {
        if (_state != null)
            _state.GrabbedObject.OnValueChanged += OnGrabbedObjectChanged;

        OnGrabbedObjectChanged(default, _state?.GrabbedObject.Value ?? default);
    }

    public override void OnNetworkDespawn()
    {
        if (_state != null)
            _state.GrabbedObject.OnValueChanged -= OnGrabbedObjectChanged;

        if (IsServer)
            ServerDrop();
    }

    // ============================================================
    // UPDATE
    // ============================================================

    private void Update()
    {
        if (!IsOwner) return;
        if (cameraController?.Camera == null) return;

        UpdateLookTarget();

        // Send carry target updates
        if (_localHeldObject != null)
            UpdateCarryTargetPositionIfDue();
    }

    // ============================================================
    // PUBLIC INPUT API (called by PlayerInputController)
    // ============================================================

    /// <summary>
    /// Called by PlayerInputController when interact key is pressed.
    /// Handles Interact → Drop → Grab priority.
    /// </summary>
    public void RequestPrimaryAction()
    {
        if (!IsOwner) return;

        Debug.Log("RequestPrimaryAction called");
        var target = LookedAtObject != null ? new NetworkObjectReference(LookedAtObject) : default;
        RequestPrimaryActionServerRpc(target);
    }

    /// <summary>
    /// Called by PlayerInputController when throw key is pressed.
    /// </summary>
    public void RequestThrow()
    {
        if (!IsOwner) return;
        if (_localHeldObject == null) return;

        RequestThrowServerRpc();
    }

    // ============================================================
    // LOOK TARGET & ACTION PROMPT
    // ============================================================

    private void UpdateLookTarget()
    {
        var cam = cameraController.Camera;
        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;

        NetworkObject newLookedAtObject = null;
        NetworkGrabbable newLookedAtGrabbable = null;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, maxReachDistance, interactMask, QueryTriggerInteraction.Ignore))
        {
            newLookedAtGrabbable = hit.collider.GetComponent<NetworkGrabbable>();
            newLookedAtObject = hit.collider.GetComponent<NetworkObject>();
        }

        bool changed = newLookedAtObject != LookedAtObject || newLookedAtGrabbable != LookedAtGrabbable;
        LookedAtObject = newLookedAtObject;
        LookedAtGrabbable = newLookedAtGrabbable;

        if (changed)
            UpdateActionPrompt();
    }

    private void UpdateActionPrompt()
    {
        // Priority: Interact > Drop > Grab
        if (LookedAtObject != null)
        {
            var interactable = FindBestInteractable(LookedAtObject);
            if (interactable != null)
            {
                ActionPrompt = interactable.Verb;
                return;
            }
        }

        if (_localHeldObject != null)
        {
            ActionPrompt = "Drop";
            return;
        }

        if (LookedAtGrabbable != null)
        {
            ActionPrompt = "Grab";
            return;
        }

        ActionPrompt = null;
    }

    private Interactable FindBestInteractable(NetworkObject target)
    {
        if (target == null) return null;

        var interactables = target.GetComponents<Interactable>();
        Interactable best = null;
        int bestPriority = int.MinValue;

        foreach (var interactable in interactables)
        {
            if (interactable != null && interactable.CanInteractLocal(this) && interactable.Priority > bestPriority)
            {
                best = interactable;
                bestPriority = interactable.Priority;
            }
        }

        return best;
    }

    // ============================================================
    // CARRY TARGET COMPUTATION
    // ============================================================

    private void UpdateCarryTargetPositionIfDue()
    {
        if (Time.time < _nextCarryUpdateTime) return;
        _nextCarryUpdateTime = Time.time + carryUpdateInterval;

        var cam = cameraController.Camera;
        Vector3 origin = cam.transform.position;
        Vector3 dir = cam.transform.forward;

        float desiredDist = GetDesiredCarryDistance();
        Vector3 target = ComputeCarryTarget(origin, dir, desiredDist);

        UpdateCarryTargetServerRpc(target);
    }

    private float GetDesiredCarryDistance()
    {
        if (_localHeldObject != null)
        {
            var grabbable = _localHeldObject.GetComponent<NetworkGrabbable>();
            if (grabbable?.GrabMode.Value == NetworkGrabbable.Mode.Dragged)
                return dragDistance;
        }
        return carryDistance;
    }

    private Vector3 ComputeCarryTarget(Vector3 cameraPos, Vector3 cameraForward, float desiredDistance)
    {
        if (!clampToObstacles)
            return cameraPos + cameraForward * desiredDistance;

        Collider[] heldCols = _localHeldObject?.GetComponents<Collider>();
        float dist = desiredDistance;

        var hits = Physics.RaycastAll(cameraPos, cameraForward, desiredDistance, ~0, QueryTriggerInteraction.Ignore);
        if (hits.Length > 0)
        {
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                // Skip held object colliders
                bool isHeldCollider = false;
                if (heldCols != null)
                {
                    foreach (var col in heldCols)
                    {
                        if (col == h.collider)
                        {
                            isHeldCollider = true;
                            break;
                        }
                    }
                }

                if (!isHeldCollider)
                {
                    dist = Mathf.Max(0.25f, h.distance - obstaclePadding);
                    break;
                }
            }
        }

        return cameraPos + cameraForward * dist;
    }

    // ============================================================
    // GRABBED OBJECT COLLISION MANAGEMENT
    // ============================================================

    /// <summary>
    /// Handles updates to the currently grabbed object, managing collision states and updating related prompts as
    /// necessary.
    /// </summary>
    /// <remarks>This method ensures that collision settings between the player and grabbed objects are
    /// correctly managed when the grabbed object changes. It also updates the action prompt to reflect the current
    /// interaction state.</remarks>
    /// <param name="prev">A reference to the previously grabbed network object. Used to restore collision settings if applicable.</param>
    /// <param name="curr">A reference to the newly grabbed network object. Used to update collision management and action prompts.</param>
    private void OnGrabbedObjectChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        // Restore collision with previous object
        if (ignoreHeldCollision && _heldObjectIgnoringCollision != null)
        {
            SetCollisionBetweenPlayerAndGrabbed(_heldObjectIgnoringCollision, false);
            _heldObjectIgnoringCollision = null;
        }

        // Update local reference
        _localHeldObject = null;
        if (IsClient && curr.TryGet(out var netObj))
            _localHeldObject = netObj;

        // Ignore collision with new object
        if (ignoreHeldCollision && curr.TryGet(out var newHeld))
        {
            _heldObjectIgnoringCollision = newHeld;
            SetCollisionBetweenPlayerAndGrabbed(_heldObjectIgnoringCollision, true);
        }

        UpdateActionPrompt();
    }

    private void SetCollisionBetweenPlayerAndGrabbed(NetworkObject grabbed, bool ignore)
    {
        if (grabbed == null) return;

        var playerCols = GetComponentsInChildren<Collider>(true);
        var grabbedCols = grabbed.GetComponentsInChildren<Collider>(true);

        foreach (var pc in playerCols)
        {
            if (pc == null || pc.isTrigger) continue;

            foreach (var gc in grabbedCols)
            {
                if (gc == null || gc.isTrigger) continue;
                Physics.IgnoreCollision(pc, gc, ignore);
            }
        }
    }

    // ============================================================
    // RPC METHODS (SERVER)
    // ============================================================

    [Rpc(SendTo.Server)]
    private void RequestPrimaryActionServerRpc(NetworkObjectReference targetObj)
    {
        if (!IsServer || _state == null) return;

        NetworkObject target = null;
        targetObj.TryGet(out target);

        // Priority: Interact > Drop > Grab
        if (target != null && ServerCanReachTarget(target))
        {
            if (ServerTryInteract(target))
                return;
        }

        if (_state.GrabbedObject.Value.TryGet(out _))
        {
            ServerDrop();
            return;
        }

        if (target != null)
            ServerTryGrab(target);
    }

    [Rpc(SendTo.Server)]
    private void RequestThrowServerRpc()
    {
        if (!IsServer) return;
        ServerThrow();
    }

    [Rpc(SendTo.Server)]
    private void UpdateCarryTargetServerRpc(Vector3 target)
    {
        if (!IsServer || _state == null) return;

        if (!_state.GrabbedObject.Value.TryGet(out var grabbedObj) || grabbedObj == null)
            return;

        var grabbable = grabbedObj.GetComponent<NetworkGrabbable>();
        grabbable?.ServerUpdateCarryTarget(OwnerClientId, target);
    }

    // ============================================================
    // SERVER LOGIC
    // ============================================================

    private bool ServerTryInteract(NetworkObject target)
    {
        var interactables = target.GetComponents<Interactable>();
        Interactable best = null;
        int bestPriority = int.MinValue;

        foreach (var interactable in interactables)
        {
            if (interactable != null && interactable.Priority > bestPriority)
            {
                best = interactable;
                bestPriority = interactable.Priority;
            }
        }

        if (best == null) return false;

        best.ServerInteract(this, OwnerClientId);
        return true;
    }

    private void ServerTryGrab(NetworkObject target)
    {
        var grabbable = target.GetComponent<NetworkGrabbable>();
        if (grabbable == null || grabbable.IsGrabbed) return;
        if (!ServerCanReachTarget(target)) return;

        var mode = grabbable.Mass >= dragMassThreshold
            ? NetworkGrabbable.Mode.Dragged
            : NetworkGrabbable.Mode.Carried;

        Vector3 eye = ServerEyePosition();
        Vector3 dir = _state.AimDirection.normalized;
        float desiredDist = mode == NetworkGrabbable.Mode.Dragged ? dragDistance : carryDistance;
        Vector3 initialTarget = eye + dir * desiredDist;

        if (grabbable.ServerTryGrab(OwnerClientId, initialTarget, mode))
            _state.SetGrabbedObjectServer(target);
    }

    private void ServerDrop()
    {
        if (_state == null) return;
        if (!_state.GrabbedObject.Value.TryGet(out var grabbedObj) || grabbedObj == null) return;

        var grabbable = grabbedObj.GetComponent<NetworkGrabbable>();
        grabbable?.ServerRelease();

        _state.ClearGrabbedObjectServer();
    }

    private void ServerThrow()
    {
        if (_state == null) return;
        if (!_state.GrabbedObject.Value.TryGet(out var grabbedObj) || grabbedObj == null) return;

        var grabbable = grabbedObj.GetComponent<NetworkGrabbable>();
        if (grabbable == null)
        {
            _state.ClearGrabbedObjectServer();
            return;
        }

        Vector3 impulse = _state.AimDirection.normalized * throwImpulse;
        grabbable.ServerThrow(OwnerClientId, impulse);
        _state.ClearGrabbedObjectServer();
    }

    private bool ServerCanReachTarget(NetworkObject expected)
    {
        if (expected == null) return false;

        Vector3 eye = ServerEyePosition();
        Vector3 targetPoint = expected.transform.position;

        var col = expected.GetComponent<Collider>();
        if (col != null)
            targetPoint = col.ClosestPoint(eye);

        Vector3 toTarget = targetPoint - eye;
        float dist = toTarget.magnitude;

        if (dist > maxReachDistance) return false;
        if (dist < 0.001f) return true;

        Vector3 dir = toTarget / dist;

        if (Physics.Raycast(eye, dir, out RaycastHit hit, dist + 0.05f, ~0, QueryTriggerInteraction.Ignore))
        {
            var hitNetObj = hit.collider.GetComponentInParent<NetworkObject>();
            return hitNetObj == expected;
        }

        return false;
    }

    private Vector3 ServerEyePosition()
    {
        return transform.position + Vector3.up * 1.6f;
    }
}