using KinematicCharacterController;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Puts this player in their vehicle seat. Runs on EVERY machine:
/// each one places seated players at the seat locally, so riders never jitter.
///
/// Owner only: sends driver input and exit requests, and keeps the camera relative to the seat.
/// Runs before the camera (execution order) so the view uses this frame's seat position.
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(PlayerState))]
public sealed class PlayerSeating : NetworkBehaviour
{
    [Tooltip("Driver input sends per second")]
    [Unit("Hz")] [SerializeField] private float inputSendRate = 20f;

    private PlayerState _state;
    private KinematicCharacterMotor _motor;
    private Collider _capsule;
    private PlayerCameraController _camera;

    private VehicleSeating _vehicle;
    private Rigidbody _vehicleBody;
    private VehicleSeat _seat;

    // Owner: latest driver input, sent at inputSendRate
    private float _throttle, _steer;
    private bool _brake, _handbrake;
    private float _nextSendTime;

    public VehicleSeat CurrentSeat => _seat;
    public bool IsSeated => _seat != null;
    public bool IsDriving => _seat != null && _seat.Role == VehicleSeat.SeatRole.Driver;

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        _motor = GetComponent<KinematicCharacterMotor>();
        _capsule = GetComponent<CapsuleCollider>();
        _camera = GetComponent<PlayerCameraController>();
    }

    // ============================================================
    // OWNER INPUT API (called by PlayerInputController)
    // ============================================================

    public void SetDriverInput(float throttle, float steer, bool brake, bool handbrake)
    {
        _throttle = Mathf.Clamp(throttle, -1f, 1f);
        _steer = Mathf.Clamp(steer, -1f, 1f);
        _brake = brake;
        _handbrake = handbrake;
    }

    public void RequestExit()
    {
        if (IsOwner && IsSeated)
            ExitServerRpc();
    }

    // ============================================================
    // PER-FRAME
    // ============================================================

    private void Update()
    {
        if (!IsOwner || !IsDriving) return;

        // Sent continuously (not on change) so a lost packet can't leave the throttle stuck.
        if (Time.unscaledTime >= _nextSendTime)
        {
            _nextSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, inputSendRate);
            float steer = ComputeSteer(out float drift);
            DriverInputServerRpc(_throttle, steer, _brake, Mathf.Max(_handbrake ? 1f : 0f, drift));
        }
    }

    /// <summary>
    /// Keys: A/D as given, and no automatic drift.
    /// Camera (Halo-style): steer toward where the camera looks, reversed when backing up so the rear swings toward the camera.
    /// Swinging the camera well past full lock while driving forward also drifts (<paramref name="drift"/>, 0 to 1),
    /// until the vehicle has swung round to face it.
    /// </summary>
    private float ComputeSteer(out float drift)
    {
        drift = 0f;
        if (_seat.Steering == VehicleSeat.SteerMode.Keys || _camera == null)
            return _steer;

        float vehicleYaw = _seat.Vehicle.eulerAngles.y;
        float offset = Mathf.DeltaAngle(vehicleYaw, _camera.WorldYaw);
        float steer = Mathf.Clamp(offset / Mathf.Max(1f, _seat.CameraSteerFullAngle), -1f, 1f);

        if (_throttle > 0.01f)
            drift = Mathf.InverseLerp(_seat.CameraDriftStartAngle, _seat.CameraDriftFullAngle, Mathf.Abs(offset));

        return _throttle < -0.01f ? -steer : steer;
    }

    private void LateUpdate()
    {
        ResolveSeat();
        if (_seat == null) return;

        // Every machine: pin the player to the seat.
        transform.position = _seat.transform.position;

        if (!IsOwner || _camera == null) return;

        if (_seat.ThirdPerson)
        {
            // Halo-style: free world-space orbit around the vehicle.
            _camera.SetReference(null);
            _camera.SetThirdPerson(_seat.CameraFocusPoint, _seat.CameraDistance, _vehicleBody);
        }
        else
        {
            // First person: view is relative to the seat.
            _camera.ClearThirdPerson();
            _camera.SetReference(_seat.ViewReference, _seat.YawLimit);
        }
    }

    /// <summary>Reacts to the server's seat assignment (PlayerState) changing.</summary>
    private void ResolveSeat()
    {
        int index = _state.SeatIndex.Value;
        VehicleSeating vehicle = null;

        if (index >= 0 && _state.SeatVehicle.Value.TryGet(out NetworkObject obj) && obj != null)
            vehicle = obj.GetComponent<VehicleSeating>();

        VehicleSeat seat = vehicle != null ? vehicle.GetSeat(index) : null;
        if (seat == _seat) return;

        VehicleSeat previous = _seat;
        _vehicle = vehicle;
        _vehicleBody = vehicle != null ? vehicle.GetComponent<Rigidbody>() : null;
        _seat = seat;

        if (seat != null)
            OnEnterSeat();
        else
            OnExitSeat(previous);
    }

    private void OnEnterSeat()
    {
        // Capsule off on every machine so it doesn't collide with the vehicle it's sitting in.
        // Motor off on the owner (it only ever runs there) so it stops moving us.
        if (_capsule != null) _capsule.enabled = false;
        if (IsOwner && _motor != null) _motor.enabled = false;
        _throttle = _steer = 0f;
        _brake = _handbrake = false;

        // Third person starts behind the vehicle, looking slightly down.
        if (IsOwner && _camera != null && _seat.ThirdPerson)
            _camera.SetAngles(_seat.Vehicle.eulerAngles.y, 12f);
    }

    private void OnExitSeat(VehicleSeat previous)
    {
        if (IsOwner)
        {
            if (previous != null)
            {
                if (_motor != null) _motor.SetPosition(previous.ExitPoint.position);
                else transform.position = previous.ExitPoint.position;
            }

            if (_camera != null)
            {
                _camera.ClearThirdPerson();
                _camera.SetReference(null);
            }
        }

        if (_capsule != null) _capsule.enabled = true;
        if (IsOwner && _motor != null) _motor.enabled = true;
    }

    // ============================================================
    // RPCs
    // ============================================================

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void DriverInputServerRpc(float throttle, float steer, bool brake, float handbrake)
    {
        if (_vehicle != null)
            _vehicle.ServerDriverInput(OwnerClientId, throttle, steer, brake, handbrake);
    }

    [Rpc(SendTo.Server)]
    private void ExitServerRpc()
    {
        if (_state.SeatVehicle.Value.TryGet(out NetworkObject obj) && obj != null
            && obj.TryGetComponent(out VehicleSeating vehicle))
        {
            vehicle.ServerExit(_state);
        }
        else
        {
            _state.ClearSeatServer();
        }
    }
}