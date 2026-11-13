using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Base class for anything a player can use with E: pickups, buttons, doors, vehicle seats...
/// PlayerInteractor finds the Interactable you're looking at, shows its Verb, and asks the
/// server to run it.
///
/// Put on a NetworkObject (or anywhere under one: the interactor searches the parent chain).
/// Several Interactables on one object are allowed; the highest Priority that's usable wins.
/// </summary>
public abstract class Interactable : NetworkBehaviour
{
    [Header("Interaction")]
    [Tooltip("Prompt text, e.g. \"Open\", \"Press\". Empty = this type's default.")]
    [SerializeField] private string verb = "";
    [SerializeField] private int priority = 0;

    /// <summary>Prompt shown to the player.</summary>
    public string Verb => string.IsNullOrEmpty(verb) ? DefaultVerb : verb;

    /// <summary>Higher wins when several Interactables are on the same object.</summary>
    public int Priority => priority;

    /// <summary>Prompt used when Verb is left empty.</summary>
    protected virtual string DefaultVerb => "Use";

    /// <summary>
    /// CLIENT: should the prompt show? (UI only, not authoritative)
    /// e.g. hide "Pick up" for something already held.
    /// </summary>
    public virtual bool CanInteractLocal(PlayerInteractor player) => true;

    /// <summary>SERVER: authoritative check before running the interaction.</summary>
    public virtual bool CanInteractServer(PlayerInteractor player, ulong clientId) => true;

    /// <summary>SERVER: called by PlayerInteractor. Validates, then runs OnServerInteract.</summary>
    public void ServerInteract(PlayerInteractor player, ulong clientId)
    {
        if (!IsServer) return;
        if (!CanInteractServer(player, clientId)) return;

        OnServerInteract(player, clientId);
    }

    /// <summary>SERVER: what using this does.</summary>
    protected abstract void OnServerInteract(PlayerInteractor player, ulong clientId);
}
