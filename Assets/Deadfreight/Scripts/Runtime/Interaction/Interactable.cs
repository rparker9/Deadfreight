using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Base class for objects players can interact with (buttons, doors, levers, terminals, etc.).
/// The PlayerGrabController detects these and calls ServerInteract() when the player presses E.
/// 
/// Add this to a NetworkObject to make it "usable".
/// Server validates and executes the interaction.
/// </summary>
[DisallowMultipleComponent]
public abstract class Interactable : NetworkBehaviour
{
    [Header("Interaction")]
    [SerializeField] private string verb = "Use";
    [SerializeField] private int priority = 0;

    /// <summary>
    /// Display verb for UI (e.g., "Open", "Activate", "Read")
    /// </summary>
    public string Verb => verb;

    /// <summary>
    /// Priority when multiple Interactables on same object (higher = preferred)
    /// </summary>
    public int Priority => priority;

    /// <summary>
    /// CLIENT-SIDE: UI filter (NOT authoritative).
    /// Return false to hide interaction prompt.
    /// Use for context-sensitive interactions (e.g., "already open", "locked", etc.)
    /// </summary>
    public virtual bool CanInteractLocal(PlayerGrabController player) => true;

    /// <summary>
    /// SERVER-SIDE: Authorization gate (AUTHORITATIVE).
    /// Return false to reject interaction attempt.
    /// Use for validation (e.g., permissions, cooldowns, requirements)
    /// </summary>
    public virtual bool CanInteractServer(PlayerGrabController player, ulong clientId) => true;

    /// <summary>
    /// Called by PlayerGrabController when player interacts.
    /// ONLY runs on server after CanInteractServer validation.
    /// </summary>
    public void ServerInteract(PlayerGrabController player, ulong clientId)
    {
        if (!IsServer) return;
        if (!CanInteractServer(player, clientId)) return;

        OnServerInteract(player, clientId);
    }

    /// <summary>
    /// Override this to implement your interaction logic.
    /// ONLY runs on server.
    /// </summary>
    protected abstract void OnServerInteract(PlayerGrabController player, ulong clientId);
}