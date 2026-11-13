using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The player's hands: finds the Interactable you're looking at, shows its prompt,
/// and asks the server to use it (E). Also carries a held Pickup: E drops, Q throws.
///
/// One path for everything: picking up is just the Pickup's interaction.
/// Input comes from PlayerInputController; the HUD reads ActionPrompt / CanThrow.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerInteractor : NetworkBehaviour
{
    [Header("Reach")]
    [SerializeField] private float maxReachDistance = 3.0f;
    [SerializeField] private LayerMask interactMask = ~0;

    [Header("Carry")]
    [SerializeField] private float carryDistance = 1.8f;
    [SerializeField] private float carryUpdateInterval = 1f / 30f;
    [Tooltip("Pull the held object in when something is between it and the camera")]
    [SerializeField] private bool clampToObstacles = true;
    [SerializeField] private float obstaclePadding = 0.15f;
    [Tooltip("Stop the held object colliding with its holder")]
    [SerializeField] private bool ignoreHeldCollision = true;

    [Header("Throw")]
    [SerializeField] private float throwImpulse = 6.5f;

    [Header("References")]
    [SerializeField] private PlayerCameraController cameraController;

    private const float EyeHeight = 1.6f;

    // ---- For the HUD (owner) ----
    public string ActionPrompt { get; private set; }
    public bool CanThrow => _held != null;

    // ---- Owner ----
    private Interactable _lookedAt;
    private float _nextCarryUpdate;

    // ---- All machines ----
    private PlayerState _state;
    private Pickup _held;                 // what we're holding (from PlayerState.HeldObject)
    private Pickup _ignoringCollisionWith;

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
        _state.HeldObject.OnValueChanged += OnHeldObjectChanged;
        OnHeldObjectChanged(default, _state.HeldObject.Value);
    }

    public override void OnNetworkDespawn()
    {
        _state.HeldObject.OnValueChanged -= OnHeldObjectChanged;
        if (IsServer) ServerDrop();
    }

    private void Update()
    {
        if (!IsOwner || cameraController == null || cameraController.Camera == null) return;

        _lookedAt = _state.IsSeated ? null : FindLookedAt();
        ActionPrompt = BuildPrompt();

        if (_held != null && Time.time >= _nextCarryUpdate)
        {
            _nextCarryUpdate = Time.time + carryUpdateInterval;
            UpdateCarryTargetServerRpc(ComputeCarryTarget());
        }
    }

    // ============================================================
    // INPUT (owner, called by PlayerInputController)
    // ============================================================

    /// <summary>E: use what you're looking at, otherwise drop what you're holding.</summary>
    public void RequestPrimaryAction()
    {
        if (!IsOwner) return;
        var target = _lookedAt != null ? new NetworkBehaviourReference(_lookedAt) : default;
        PrimaryActionServerRpc(target);
    }

    /// <summary>Q: throw what you're holding.</summary>
    public void RequestThrow()
    {
        if (IsOwner && _held != null)
            ThrowServerRpc();
    }

    // ============================================================
    // LOOK TARGET & PROMPT (owner)
    // ============================================================

    private Interactable FindLookedAt()
    {
        Transform cam = cameraController.Camera.transform;
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, maxReachDistance,
                             interactMask, QueryTriggerInteraction.Ignore))
            return null;

        // Interactables can sit on the hit collider or any parent (e.g. the vehicle root).
        Interactable best = null;
        foreach (var candidate in hit.collider.GetComponentsInParent<Interactable>())
        {
            if (!IsUsable(candidate) || !candidate.CanInteractLocal(this)) continue;
            if (best == null || candidate.Priority > best.Priority)
                best = candidate;
        }
        return best;
    }

    /// <summary>Hands full: you can still use things, but not pick up a second object.</summary>
    private bool IsUsable(Interactable interactable)
        => interactable != null && !(_held != null && interactable is Pickup);

    private string BuildPrompt()
    {
        if (_state.IsSeated) return "Get out";
        if (_lookedAt != null) return _lookedAt.Verb;
        if (_held != null) return "Drop";
        return null;
    }

    // ============================================================
    // CARRY TARGET (owner)
    // ============================================================

    private Vector3 ComputeCarryTarget()
    {
        Transform cam = cameraController.Camera.transform;
        float dist = carryDistance;

        if (clampToObstacles)
        {
            var hits = Physics.RaycastAll(cam.position, cam.forward, carryDistance, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (_held != null && h.collider.transform.IsChildOf(_held.transform)) continue; // the held object
                if (h.collider.transform.IsChildOf(transform)) continue;                       // ourselves
                dist = Mathf.Min(dist, Mathf.Max(0.25f, h.distance - obstaclePadding));
            }
        }

        return cam.position + cam.forward * dist;
    }

    // ============================================================
    // HELD OBJECT (all machines)
    // ============================================================

    private void OnHeldObjectChanged(NetworkObjectReference prev, NetworkObjectReference curr)
    {
        _held = curr.TryGet(out NetworkObject obj) && obj != null ? obj.GetComponent<Pickup>() : null;

        if (!ignoreHeldCollision) return;

        if (_ignoringCollisionWith != null)
            SetCollisionWithHeld(_ignoringCollisionWith, ignore: false);

        _ignoringCollisionWith = _held;

        if (_held != null)
            SetCollisionWithHeld(_held, ignore: true);
    }

    private void SetCollisionWithHeld(Pickup held, bool ignore)
    {
        var mine = GetComponentsInChildren<Collider>(true);
        var theirs = held.GetComponentsInChildren<Collider>(true);

        foreach (var a in mine)
        {
            if (a == null || a.isTrigger) continue;
            foreach (var b in theirs)
                if (b != null && !b.isTrigger)
                    Physics.IgnoreCollision(a, b, ignore);
        }
    }

    // ============================================================
    // RPCs (server)
    // ============================================================

    [Rpc(SendTo.Server)]
    private void PrimaryActionServerRpc(NetworkBehaviourReference targetRef)
    {
        if (targetRef.TryGet(out Interactable target) && IsUsable(target) && ServerCanReach(target.NetworkObject))
        {
            target.ServerInteract(this, OwnerClientId);
            return;
        }

        ServerDrop(); // nothing usable in view: E lets go
    }

    [Rpc(SendTo.Server)]
    private void ThrowServerRpc()
    {
        if (!TryGetHeldServer(out Pickup held)) return;

        held.ServerThrow(OwnerClientId, _state.AimDirection.normalized * throwImpulse);
        _state.ClearHeldObjectServer();
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void UpdateCarryTargetServerRpc(Vector3 target)
    {
        if (TryGetHeldServer(out Pickup held))
            held.ServerUpdateTarget(OwnerClientId, target, _state.AimYaw.Value);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    /// <summary>Called by Pickup when this player uses it.</summary>
    public void ServerPickUp(Pickup pickup)
    {
        if (!IsServer || pickup == null) return;
        if (_state.HeldObject.Value.TryGet(out _)) return; // hands full

        Vector3 target = ServerEyePosition() + _state.AimDirection.normalized * carryDistance;

        if (pickup.ServerTryHold(OwnerClientId, target, _state.AimYaw.Value))
            _state.SetHeldObjectServer(pickup.NetworkObject);
    }

    /// <summary>Let go of whatever this player holds (e.g. when getting into a seat).</summary>
    public void ServerDrop()
    {
        if (!IsServer) return;

        if (TryGetHeldServer(out Pickup held))
            held.ServerRelease();

        _state.ClearHeldObjectServer();
    }

    /// <summary>The held Pickup, if we still hold it. Clears stale state otherwise.</summary>
    private bool TryGetHeldServer(out Pickup held)
    {
        held = null;
        if (!_state.HeldObject.Value.TryGet(out NetworkObject obj) || obj == null) return false;

        held = obj.GetComponent<Pickup>();
        if (held != null && held.HolderClientId.Value == OwnerClientId) return true;

        _state.ClearHeldObjectServer(); // the object was released some other way
        held = null;
        return false;
    }

    private bool ServerCanReach(NetworkObject target)
    {
        if (target == null) return false;

        Vector3 eye = ServerEyePosition();

        // Closest point on any of the target's colliders (vehicles keep theirs on children).
        Vector3 point = target.transform.position;
        float bestSqr = float.MaxValue;
        foreach (var col in target.GetComponentsInChildren<Collider>())
        {
            if (col.isTrigger || !col.enabled) continue;
            Vector3 p = col.ClosestPoint(eye);
            float d = (p - eye).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; point = p; }
        }

        Vector3 toTarget = point - eye;
        float dist = toTarget.magnitude;
        if (dist > maxReachDistance) return false;
        if (dist < 0.001f) return true;

        // Line of sight: the first thing hit (ignoring us and what we hold) must belong to the target.
        var hits = Physics.RaycastAll(eye, toTarget / dist, dist + 0.05f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            Transform t = hit.collider.transform;
            if (t.IsChildOf(transform)) continue;
            if (_held != null && t.IsChildOf(_held.transform)) continue;
            return hit.collider.GetComponentInParent<NetworkObject>() == target;
        }
        return false;
    }

    private Vector3 ServerEyePosition() => transform.position + Vector3.up * EyeHeight;
}
