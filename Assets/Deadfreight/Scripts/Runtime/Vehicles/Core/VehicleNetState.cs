using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Vehicle values every machine needs for visuals, synced as ONE network variable.
/// The server builds it during the physics step; modules read it in Present().
/// Add fields here when a new module has something clients must see (e.g. engine RPM).
/// </summary>
public struct VehicleNetState : INetworkSerializable, IEquatable<VehicleNetState>
{
    /// <summary>Front axle angle in degrees.</summary>
    public float SteerAngle;

    /// <summary>Brake amount 0..1.</summary>
    public float Brake;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref SteerAngle);
        serializer.SerializeValue(ref Brake);
    }

    public bool Equals(VehicleNetState other) =>
        Mathf.Approximately(SteerAngle, other.SteerAngle) &&
        Mathf.Approximately(Brake, other.Brake);

    public override bool Equals(object obj) => obj is VehicleNetState other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(SteerAngle, Brake);
}
