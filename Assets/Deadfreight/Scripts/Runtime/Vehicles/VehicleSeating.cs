using Unity.Netcode;
using UnityEngine;

/// <summary>
/// SERVER-AUTHORITATIVE seat manager for a vehicle. Goes on the vehicle root (same NetworkObject).
/// Pressing E while looking at the vehicle puts you in the nearest free seat.
///
/// The server decides who sits where, writes it to the player's PlayerState,
/// and every machine places seated players locally (PlayerSeating).
/// </summary>
public sealed class VehicleSeating : Interactable
{
    [SerializeField] private VehicleSeat[] seats;

    /// <summary>Client id in each seat (ulong.MaxValue = empty). Index matches the seats array.</summary>
    public NetworkList<ulong> Occupants { get; private set; }

    private const ulong Empty = ulong.MaxValue;
    private Vehicle _vehicle;

    public int SeatCount => seats.Length;

    protected override string DefaultVerb => "Board";
    public VehicleSeat GetSeat(int index) => index >= 0 && index < seats.Length ? seats[index] : null;

    // ============================================================
    // LIFECYCLE
    // ============================================================

    private void Awake()
    {
        Occupants = new NetworkList<ulong>();
        _vehicle = GetComponent<Vehicle>();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        Occupants.Clear();
        for (int i = 0; i < seats.Length; i++)
            Occupants.Add(Empty);

        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager != null)
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // ============================================================
    // INTERACTION (E on the vehicle)
    // ============================================================

    public override bool CanInteractLocal(PlayerInteractor player)
    {
        var state = player.GetComponent<PlayerState>();
        return state != null && !state.IsSeated && NearestFreeSeat(player.transform.position) >= 0;
    }

    protected override void OnServerInteract(PlayerInteractor player, ulong clientId)
    {
        var state = player.GetComponent<PlayerState>();
        if (state == null || state.IsSeated) return;

        int seat = NearestFreeSeat(player.transform.position);
        if (seat < 0) return;

        player.ServerDrop(); // hands must be free to ride
        Occupants[seat] = clientId;
        state.SetSeatServer(NetworkObject, seat);
    }

    // ============================================================
    // SERVER API
    // ============================================================

    public void ServerExit(PlayerState state)
    {
        if (!IsServer || state == null) return;

        int seat = state.SeatIndex.Value;
        if (seat >= 0 && seat < seats.Length && Occupants[seat] == state.OwnerClientId)
        {
            Occupants[seat] = Empty;
            if (seats[seat].Role == VehicleSeat.SeatRole.Driver)
                ReleaseControls();
        }

        state.ClearSeatServer();
    }

    /// <summary>Driver input from PlayerSeating. Ignored unless that client is in a driver seat.</summary>
    public void ServerDriverInput(ulong clientId, float throttle, float steer, bool brake)
    {
        if (!IsServer || _vehicle == null) return;
        if (!IsDriver(clientId)) return;

        _vehicle.ServerSetDriverInput(throttle, steer, brake);
    }

    // ============================================================
    // HELPERS
    // ============================================================

    private bool IsDriver(ulong clientId)
    {
        for (int i = 0; i < seats.Length; i++)
            if (Occupants[i] == clientId && seats[i].Role == VehicleSeat.SeatRole.Driver)
                return true;
        return false;
    }

    private int NearestFreeSeat(Vector3 position)
    {
        int best = -1;
        float bestDist = float.MaxValue;

        for (int i = 0; i < seats.Length && i < Occupants.Count; i++)
        {
            if (Occupants[i] != Empty || seats[i] == null) continue;

            float d = (seats[i].transform.position - position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    private void ReleaseControls()
    {
        // No driver: coast to a stop with the brake on.
        _vehicle?.ServerSetDriverInput(0f, 0f, true);
        _vehicle?.ServerClearDriver();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        for (int i = 0; i < Occupants.Count; i++)
        {
            if (Occupants[i] != clientId) continue;

            Occupants[i] = Empty;
            if (seats[i].Role == VehicleSeat.SeatRole.Driver)
                ReleaseControls();
        }
    }
}
