using UnityEngine;

[CreateAssetMenu(menuName = "Deadfreight/Movement Settings")]
public sealed class MovementSettings : ScriptableObject
{
    [Header("Movement")]
    [Unit("m/s")] public float walkSpeed = 4.5f;
    [Unit("m/s")] public float sprintSpeed = 6.0f;
    [Unit("m/s")] public float crouchSpeed = 2.5f;

    [Header("Acceleration (m/s^2)")]
    [Unit("m/s^2")] public float groundAccel = 55f;
    [Unit("m/s^2")] public float groundDecel = 75f;
    [Unit("m/s^2")] public float airAccel = 18f;
    [Unit("m/s^2")] public float airDecel = 6f;

    [Header("Vertical")]
    [Unit("m/s^2")] public float gravity = -18f;
    [Unit("m/s")] public float jumpSpeed = 6.0f;

    [Header("Aim Clamp (Server)")]
    [Unit("°")] public float pitchMin = -80f;
    [Unit("°")] public float pitchMax = 80f;
}
