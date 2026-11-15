using System;
using UnityEngine;

/// <summary>
/// Lets this vehicle be towed. SERVER-AUTHORITATIVE.
/// Put on the ROOT of the towed vehicle (the trailer), next to its Vehicle.
///
/// Attaching joins this vehicle's Coupling (the tip of its drawbar) to a HitchPoint on the
/// towing vehicle with a physics joint: it pivots freely within limits and can break loose
/// under a hard enough jolt. The joint only exists on the server; clients just see both
/// vehicles move.
///
/// While attached this is the trailer's control source: it can pass the towing vehicle's
/// brake through, so the trailer brakes with it instead of shoving it.
/// </summary>
public sealed class VehicleHitch : VehicleModule, IVehicleControlSource
{
    [Header("Coupling")]
    [Tooltip("The tip of this vehicle's drawbar: the point that connects to the HitchPoint")]
    [SerializeField] private Transform coupling;
    [Tooltip("Attach to this hitch when the vehicle spawns (optional)")]
    [SerializeField] private HitchPoint attachOnSpawn;
    [Tooltip("If the coupling is further than this from the hitch when attaching, this vehicle is moved into place (m)")]
    [SerializeField] private float snapTolerance = 0.5f;

    [Header("Joint limits (degrees)")]
    [Tooltip("Left/right swing. Lower = can't jackknife as far.")]
    [SerializeField] private float yawLimit = 75f;
    [Tooltip("Up/down, for hills and bumps")]
    [SerializeField] private float pitchLimit = 25f;
    [Tooltip("Twist between the two vehicles")]
    [SerializeField] private float rollLimit = 10f;

    [Header("Breaking")]
    [Tooltip("Force (N) that snaps the coupling. 0 = unbreakable.")]
    [SerializeField] private float breakForce = 40000f;

    [Header("Brakes")]
    [Tooltip("This vehicle brakes when the towing vehicle brakes (on wheels with Has Brake)")]
    [SerializeField] private bool brakeWithTowVehicle = true;

    // ---- Events (SERVER only) ----
    public event Action<HitchPoint> Attached;
    /// <summary>Detached. True if the coupling broke rather than being released.</summary>
    public event Action<bool> Detached;

    public override int Order => VehicleModuleOrder.Hitch;

    public bool IsAttached => _joint != null;
    public HitchPoint AttachedTo { get; private set; }
    public Vehicle TowVehicle => AttachedTo != null ? AttachedTo.Vehicle : null;

    private ConfigurableJoint _joint;
    private bool _spawnAttachDone;

    // ============================================================
    // STEP (server)
    // ============================================================

    public override void Simulate(in VehicleControls controls, ref VehicleNetState state, float dt)
    {
        // Modules have no spawn callback; the first server step is the earliest safe moment.
        if (!_spawnAttachDone)
        {
            _spawnAttachDone = true;
            if (attachOnSpawn != null) ServerAttach(attachOnSpawn);
        }
    }

    /// <summary>IVehicleControlSource: no throttle or steering of our own, optionally the tow vehicle's brake.</summary>
    public VehicleControls GetControls()
    {
        float brake = brakeWithTowVehicle && TowVehicle != null ? TowVehicle.State.Brake : 0f;
        return new VehicleControls(0f, 0f, brake);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    public bool ServerAttach(HitchPoint hitch)
    {
        if (!Vehicle.IsServer || hitch == null || coupling == null) return false;

        Vehicle tow = hitch.Vehicle;
        if (tow == null || tow == Vehicle) return false;

        if (IsAttached) ServerDetach();

        // Line the coupling up with the hitch so the joint doesn't yank on creation.
        Vector3 offset = hitch.transform.position - coupling.position;
        if (offset.magnitude > snapTolerance)
        {
            Vehicle.transform.position += offset;
            Vehicle.Body.position = Vehicle.transform.position;
            Physics.SyncTransforms();
        }

        _joint = Vehicle.gameObject.AddComponent<ConfigurableJoint>();
        _joint.connectedBody = tow.Body;
        _joint.autoConfigureConnectedAnchor = false;
        _joint.anchor = Vehicle.transform.InverseTransformPoint(coupling.position);
        _joint.connectedAnchor = tow.transform.InverseTransformPoint(hitch.transform.position);
        _joint.enableCollision = false; // the two vehicles don't collide with each other

        // Axis = pitch (X), secondary = yaw (Y), the remaining one = roll (Z).
        _joint.axis = Vector3.right;
        _joint.secondaryAxis = Vector3.up;

        _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.angularXMotion = _joint.angularYMotion = _joint.angularZMotion = ConfigurableJointMotion.Limited;

        _joint.lowAngularXLimit = new SoftJointLimit { limit = -pitchLimit };
        _joint.highAngularXLimit = new SoftJointLimit { limit = pitchLimit };
        _joint.angularYLimit = new SoftJointLimit { limit = yawLimit };
        _joint.angularZLimit = new SoftJointLimit { limit = rollLimit };

        _joint.breakForce = breakForce > 0f ? breakForce : Mathf.Infinity;
        _joint.breakTorque = Mathf.Infinity;

        AttachedTo = hitch;
        Vehicle.ServerSetControlSource(this);
        Attached?.Invoke(hitch);
        return true;
    }

    public void ServerDetach()
    {
        if (!Vehicle.IsServer || !IsAttached) return;

        Destroy(_joint);
        Release(broke: false);
    }

    // ============================================================
    // INTERNALS
    // ============================================================

    /// <summary>Unity calls this on the joint's GameObject (the vehicle root) when it breaks.</summary>
    private void OnJointBreak(float force)
    {
        if (Vehicle == null || !Vehicle.IsServer || _joint == null) return;
        Release(broke: true); // Unity destroys the joint itself
    }

    private void Release(bool broke)
    {
        _joint = null;
        AttachedTo = null;
        Vehicle.ServerClearControlSource(this);
        Detached?.Invoke(broke);
    }

    private void OnDrawGizmosSelected()
    {
        if (coupling == null) return;
        Gizmos.color = new Color(0.2f, 0.8f, 1f);
        Gizmos.DrawWireSphere(coupling.position, 0.08f);
    }
}