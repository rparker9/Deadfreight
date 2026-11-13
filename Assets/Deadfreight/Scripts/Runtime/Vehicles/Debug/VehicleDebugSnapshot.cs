using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Everything the debug overlay shows about a vehicle, built on the server
/// and sent to clients that asked for it.
/// </summary>
public struct VehicleDebugSnapshot : INetworkSerializable
{
    public float ServerTime;

    // Body
    public float Mass;
    public Vector3 Velocity;
    public float ForwardSpeed;
    public float TiltAngle;
    public bool Tipped;
    public bool IsStuck;
    public int WheelsGrounded;

    // Control
    public FixedString64Bytes ControlSource;
    public VehicleControls Controls;
    public VehicleNetState State;

    // Last impact
    public bool HasImpact;
    public float ImpactSpeed;
    public float ImpactImpulse;
    public float ImpactTime;
    public FixedString64Bytes ImpactWith;

    public WheelDebugInfo[] Wheels;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref ServerTime);

        s.SerializeValue(ref Mass);
        s.SerializeValue(ref Velocity);
        s.SerializeValue(ref ForwardSpeed);
        s.SerializeValue(ref TiltAngle);
        s.SerializeValue(ref Tipped);
        s.SerializeValue(ref IsStuck);
        s.SerializeValue(ref WheelsGrounded);

        s.SerializeValue(ref ControlSource);
        s.SerializeValue(ref Controls.Throttle);
        s.SerializeValue(ref Controls.Steer);
        s.SerializeValue(ref Controls.Brake);
        s.SerializeValue(ref State);

        s.SerializeValue(ref HasImpact);
        s.SerializeValue(ref ImpactSpeed);
        s.SerializeValue(ref ImpactImpulse);
        s.SerializeValue(ref ImpactTime);
        s.SerializeValue(ref ImpactWith);

        int count = Wheels != null ? Wheels.Length : 0;
        s.SerializeValue(ref count);
        if (s.IsReader) Wheels = new WheelDebugInfo[count];
        for (int i = 0; i < count; i++)
            s.SerializeValue(ref Wheels[i]);
    }
}

public struct WheelDebugInfo : INetworkSerializable
{
    public FixedString32Bytes Name;
    public bool Grounded;
    public bool Driven;
    public bool Braked;
    public float Load;
    public FixedString32Bytes Surface;
    public float ContactSpeed;
    public float SlipSpeed;
    public float DriveForce;
    public float RollForce;
    public float SideForce;
    public float BrakeInput;

    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Name);
        s.SerializeValue(ref Grounded);
        s.SerializeValue(ref Driven);
        s.SerializeValue(ref Braked);
        s.SerializeValue(ref Load);
        s.SerializeValue(ref Surface);
        s.SerializeValue(ref ContactSpeed);
        s.SerializeValue(ref SlipSpeed);
        s.SerializeValue(ref DriveForce);
        s.SerializeValue(ref RollForce);
        s.SerializeValue(ref SideForce);
        s.SerializeValue(ref BrakeInput);
    }
}