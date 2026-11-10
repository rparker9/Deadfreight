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
public sealed class PlayerSeatController : NetworkBehaviour
{
    [Tooltip("Driver input sends per second")]
    [SerializeField] private float inputSendRate = 20f;

    private PlayerState _state;
    private CharacterController _cc;
    private PlayerCameraController _camera;

    private VehicleSeats _vehicle;
    private Rigidbody _vehicleBody;
    private VehicleSeat _seat;

    // Owner: latest driver input, sent at inputSendRate
    private float _throttle, _steer;
    private bool _brake;
    private float _nextSendTime;

    public VehicleSeat CurrentSeat => _seat;
    public bool IsSeated => _seat != null;
    public bool IsDriving => _seat != null && _seat.Role == VehicleSeat.SeatRole.Driver;

    private void Awake()
    {
        _state = GetComponent<PlayerState>();
        _cc = GetComponent<CharacterController>();
        _camera = GetComponent<PlayerCameraController>();
    }

    // ============================================================
    // OWNER INPUT API (called by PlayerInputController)
    // ============================================================

    public void SetDriverInput(float throttle, float steer, bool brake)
    {
        _throttle = Mathf.Clamp(throttle, -1f, 1f);
        _steer = Mathf.Clamp(steer, -1f, 1f);
        _brake = brake;
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
            DriverInputServerRpc(_throttle, ComputeSteer(), _brake);
        }
    }

    /// <summary>
    /// Keys: A/D as given. Camera (Halo-style): steer toward where the camera looks,
    /// reversed when backing up so the rear swings toward the camera.
    /// </summary>
    private float ComputeSteer()
    {
        if (_seat.Steering == VehicleSeat.SteerMode.Keys || _camera == null)
            return _steer;

        float vehicleYaw = _seat.Vehicle.eulerAngles.y;
        float offset = Mathf.DeltaAngle(vehicleYaw, _camera.WorldYaw);
        float steer = Mathf.Clamp(offset / Mathf.Max(1f, _seat.CameraSteerFullAngle), -1f, 1f);

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
        VehicleSeats vehicle = null;

        if (index >= 0 && _state.SeatVehicle.Value.TryGet(out NetworkObject obj) && obj != null)
            vehicle = obj.GetComponent<VehicleSeats>();

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
        // Disabled on every machine so the capsule doesn't collide with the vehicle it's sitting in.
        if (_cc != null) _cc.enabled = false;
        _throttle = _steer = 0f;
        _brake = false;

        // Third person starts behind the vehicle, looking slightly down.
        if (IsOwner && _camera != null && _seat.ThirdPerson)
            _camera.SetAngles(_seat.Vehicle.eulerAngles.y, 12f);
    }

    private void OnExitSeat(VehicleSeat previous)
    {
        if (IsOwner)
        {
            if (previous != null)
                transform.position = previous.ExitPoint.position;

            if (_camera != null)
            {
                _camera.ClearThirdPerson();
                _camera.SetReference(null);
            }
        }

        if (_cc != null) _cc.enabled = true;
    }

    // ============================================================
    // RPCs
    // ============================================================

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void DriverInputServerRpc(float throttle, float steer, bool brake)
    {
        if (_vehicle != null)
            _vehicle.ServerDriverInput(OwnerClientId, throttle, steer, brake);
    }

    [Rpc(SendTo.Server)]
    private void ExitServerRpc()
    {
        if (_state.SeatVehicle.Value.TryGet(out NetworkObject obj) && obj != null
            && obj.TryGetComponent(out VehicleSeats vehicle))
        {
            vehicle.ServerExit(_state);
        }
        else
        {
            _state.ClearSeatServer();
        }
    }
}