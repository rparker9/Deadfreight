using UnityEngine;

[CreateAssetMenu(menuName = "Deadfreight/Movement Settings")]
public sealed class MovementSettings : ScriptableObject
{
    [Header("Movement")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 6.0f;
    public float crouchSpeed = 2.5f;

    [Header("Acceleration (m/s^2)")]
    public float groundAccel = 55f;
    public float groundDecel = 75f;
    public float airAccel = 18f;
    public float airDecel = 6f;

    [Header("Vertical")]
    public float gravity = -18f;
    public float jumpSpeed = 6.0f;

    [Header("Aim Clamp (Server)")]
    public float pitchMin = -80f;
    public float pitchMax = 80f;
}
