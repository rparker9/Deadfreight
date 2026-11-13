using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Debug support for Vehicle (the other half of the partial class).
/// The server builds snapshots; clients that ask (VehicleDebugOverlay) get one a few times a second.
/// Nothing here runs unless someone has the overlay open.
/// </summary>
public sealed partial class Vehicle
{
    private const float DebugSendInterval = 0.2f;

    /// <summary>Client: latest snapshot received from the server (null until one arrives).</summary>
    public VehicleDebugSnapshot? LastDebugSnapshot { get; private set; }
    /// <summary>Client: Time.unscaledTime when LastDebugSnapshot arrived.</summary>
    public float LastDebugReceiveTime { get; private set; }

    // Server
    private readonly List<ulong> _debugSubscribers = new();
    private float _nextDebugSend;
    private bool _hasImpact;
    private VehicleImpact _lastImpact;
    private float _lastImpactTime;

    // ============================================================
    // OVERLAY API
    // ============================================================

    /// <summary>Overlay open/closed on this machine. Clients start/stop receiving snapshots.</summary>
    public void SetDebugSubscribed(bool subscribed)
    {
        if (!IsSpawned || IsServer) return; // the host reads BuildDebugSnapshot() directly
        SetDebugSubscribedRpc(subscribed);
    }

    /// <summary>Server: current state of everything the overlay shows.</summary>
    public VehicleDebugSnapshot BuildDebugSnapshot()
    {
        var wheels = new WheelDebugInfo[_wheels.Count];
        for (int i = 0; i < _wheels.Count; i++)
        {
            var w = _wheels[i];
            wheels[i] = new WheelDebugInfo
            {
                Name = Fs32(w.name),
                Grounded = w.IsGrounded,
                Driven = w.IsDriven,
                Braked = w.HasBrake,
                Load = w.Load,
                Surface = Fs32(w.Surface != null ? w.Surface.name : "default"),
                ContactSpeed = w.ContactSpeed,
                SlipSpeed = w.SlipSpeed,
                DriveForce = w.LastDriveForce,
                RollForce = w.LastRollForce,
                SideForce = w.LastSideForce,
                BrakeInput = w.BrakeInput
            };
        }

        var t = Telemetry;
        return new VehicleDebugSnapshot
        {
            ServerTime = Time.time,
            Mass = _rb.mass,
            Velocity = t.Velocity,
            ForwardSpeed = t.ForwardSpeed,
            TiltAngle = t.TiltAngle,
            Tipped = _tipped,
            IsStuck = t.IsStuck,
            WheelsGrounded = t.WheelsGrounded,

            ControlSource = Fs64(_controlSource != null ? _controlSource.GetType().Name : "none (parked)"),
            Controls = _lastControls,
            State = _working,

            HasImpact = _hasImpact,
            ImpactSpeed = _lastImpact.Speed,
            ImpactImpulse = _lastImpact.Impulse,
            ImpactTime = _lastImpactTime,
            ImpactWith = Fs64(_lastImpact.Other != null ? _lastImpact.Other.name : "?"),

            Wheels = wheels
        };
    }

    // ============================================================
    // SERVER INTERNALS (called from Vehicle.cs)
    // ============================================================

    private void RecordImpactForDebug(VehicleImpact impact)
    {
        _hasImpact = true;
        _lastImpact = impact;
        _lastImpactTime = Time.time;
    }

    private void UpdateDebugBroadcast()
    {
        if (_debugSubscribers.Count == 0 || Time.unscaledTime < _nextDebugSend) return;
        _nextDebugSend = Time.unscaledTime + DebugSendInterval;

        // Drop clients that disconnected.
        _debugSubscribers.RemoveAll(id => !NetworkManager.ConnectedClients.ContainsKey(id));
        if (_debugSubscribers.Count == 0) return;

        DebugSnapshotRpc(BuildDebugSnapshot(), RpcTarget.Group(_debugSubscribers, RpcTargetUse.Temp));
    }

    // Fixed strings throw if a name is too long, so trim first.
    private static FixedString32Bytes Fs32(string s) =>
        new FixedString32Bytes(s.Length > 28 ? s.Substring(0, 28) : s);

    private static FixedString64Bytes Fs64(string s) =>
        new FixedString64Bytes(s.Length > 60 ? s.Substring(0, 60) : s);

    // ============================================================
    // RPCs
    // ============================================================

    [Rpc(SendTo.Server)]
    private void SetDebugSubscribedRpc(bool subscribed, RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        _debugSubscribers.Remove(sender);
        if (subscribed) _debugSubscribers.Add(sender);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void DebugSnapshotRpc(VehicleDebugSnapshot snapshot, RpcParams rpcParams)
    {
        LastDebugSnapshot = snapshot;
        LastDebugReceiveTime = Time.unscaledTime;
    }
}