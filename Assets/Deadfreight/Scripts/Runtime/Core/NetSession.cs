using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public sealed class NetSession : MonoBehaviour
{
    public event Action Connected;
    public event Action Disconnected;

    [Header("Spawning")]
    [SerializeField] private Transform playerSpawn;

    [Header("Transport")]
    [SerializeField] private string listenAddress = "0.0.0.0";
    [SerializeField] private ushort port = 7777;

    [Header("Limits")]
    [SerializeField] private int maxPlayers = 8;

    private NetworkManager _nm;
    private UnityTransport _utp;

    private void Start()
    {
        _nm = NetworkManager.Singleton;
        if (_nm == null)
        {
            Debug.LogError("NetSession: NetworkManager.Singleton is null.");
            return;
        }

        _utp = _nm.GetComponent<UnityTransport>();
        if (_utp == null)
        {
            Debug.LogError("NetSession: UnityTransport missing on NetworkManager.");
            return;
        }

        _nm.NetworkConfig.ConnectionApproval = true;
        _nm.ConnectionApprovalCallback = ApprovalCheck;

        _nm.OnClientConnectedCallback += OnClientConnected;
        _nm.OnClientDisconnectCallback += OnClientDisconnected;
    }

    public void StartHost()
    {
        _utp.SetConnectionData(listenAddress, port);
        _nm.StartHost();
    }

    public void StartClient(string address)
    {
        _utp.SetConnectionData(address, port);
        _nm.StartClient();
    }

    public void Shutdown()
    {
        if (_nm != null)
            _nm.Shutdown();
    }

    private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
    {
        if (_nm == null)
        {
            res.Approved = false;
            res.Reason = "Server not ready";
            res.Pending = false;
            return;
        }

        int current = _nm.ConnectedClientsIds.Count;
        if (current >= maxPlayers)
        {
            res.Approved = false;
            res.Reason = "Server full";
            res.Pending = false;
            return;
        }

        // Spawn settings
        res.Approved = true;
        res.CreatePlayerObject = true;
        res.Pending = false;

        if (!TryGetSpawnPose(out Vector3 pos, out Quaternion rot))
        {
            Debug.LogWarning("ApprovalCheck: playerSpawn not set; using default spawn.");
            return;
        }

        res.Position = pos;
        res.Rotation = rot;
    }

    private void OnClientConnected(ulong clientId)
    {
        if (_nm != null && clientId == _nm.LocalClientId)
            Connected?.Invoke();
    }

    private bool TryGetSpawnPose(out Vector3 pos, out Quaternion rot)
    {
        pos = default;
        rot = default;

        if (playerSpawn == null)
            return false;

        pos = playerSpawn.position;
        rot = playerSpawn.rotation;

        // Snap to ground
        if (Physics.Raycast(pos + Vector3.up * 5f, Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore))
        {
            // Slightly above ground to avoid initial overlap
            pos = hit.point + Vector3.up * 0.05f;
        }

        return true;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_nm != null && clientId == _nm.LocalClientId)
            Disconnected?.Invoke();
    }
}
