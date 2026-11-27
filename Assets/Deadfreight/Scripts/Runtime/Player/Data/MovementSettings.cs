using UnityEngine;
using Deadfreight.Core;

namespace Deadfreight.Player
{
    [CreateAssetMenu(menuName = "Deadfreight/Movement Settings")]
    public sealed class MovementSettings : ScriptableObject
    {
        [Header("Movement")]
        [Tooltip("Default speed, no keys held")]
        [Unit("m/s")] public float walkSpeed = 3f;
        [Tooltip("Holding Shift")]
        [Unit("m/s")] public float runSpeed = 5.5f;
        [Tooltip("Holding Shift while moving forward, after Sprint Delay")]
        [Unit("m/s")] public float sprintSpeed = 8f;
        [Unit("m/s")] public float crouchSpeed = 2.5f;

        [Header("Sprint")]
        [Tooltip("How long Shift must be held while moving forward before running becomes sprinting")]
        [Unit("s")] public float sprintDelay = 1f;
        [Tooltip("Minimum forward input to sprint. 0.5 allows forward diagonals (0.71) but not pure strafing (0).")]
        [Range(0f, 1f)] public float sprintMinForward = 0.5f;

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

        /// <summary>
        /// Speed as a gait: 0 at rest, 1 at walk speed, 2 at run speed, 3 at sprint speed (linear in between).
        /// Shared by the body animation and the first-person gun bob so they agree on what "running" is.
        /// </summary>
        public float Gait(float speed)
        {
            float walk = Mathf.Max(walkSpeed, 0.01f);
            float run = Mathf.Max(runSpeed, walk + 0.01f);
            float sprint = Mathf.Max(sprintSpeed, run + 0.01f);

            if (speed <= walk) return speed / walk;
            if (speed <= run) return 1f + (speed - walk) / (run - walk);
            return Mathf.Min(3f, 2f + (speed - run) / (sprint - run));
        }
    }
}