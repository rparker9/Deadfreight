using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Simple interactable that invokes a UnityEvent on the server.
/// Perfect for prototyping buttons, levers, switches, etc.
/// 
/// Usage:
/// 1. Add to any NetworkObject
/// 2. Set Verb (e.g., "Press", "Pull", "Activate")
/// 3. Hook up UnityEvent in inspector (e.g., door.Open(), light.Toggle())
/// </summary>
public sealed class InteractableUnityEvent : Interactable
{
    [Header("Events")]
    [SerializeField] private UnityEvent onInteract;

    protected override void OnServerInteract(PlayerGrabController player, ulong clientId)
    {
        onInteract?.Invoke();
    }
}