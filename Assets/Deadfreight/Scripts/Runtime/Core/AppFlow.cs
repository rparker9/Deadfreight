using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages the application flow for hosting and joining games, handling network connections and user interface
/// transitions.
/// </summary>
/// <remarks>AppFlow coordinates the initialization and teardown of network sessions and updates the user
/// interface in response to connection events. It provides methods to host a new game, join an existing game by
/// address, and disconnect back to the main menu. This class is intended to be attached to a Unity GameObject and
/// relies on the presence of NetSession and UIRoot components in the scene.</remarks>
public sealed class AppFlow : MonoBehaviour
{
    private NetSession _net;
    private UIRoot _ui;

    private void Start()
    {
        // Initialize references to NetSession and UIRoot components.
        _net = GetComponent<NetSession>();
        _ui = GetComponent<UIRoot>();

        // Subscribe to network connection events.
        _net.Connected += OnConnected;
        _net.Disconnected += OnDisconnected;

        // Show the main menu at startup.
        _ui.ShowMainMenu(); 
    }

    /// <summary>
    /// Initiates the hosting process for the game and updates the user interface to indicate that a connection is being
    /// established.
    /// </summary>
    /// <remarks>Call this method to start hosting a multiplayer game session. Ensure that the network
    /// configuration is valid before invoking this method, as it will attempt to establish a host connection and update
    /// the UI accordingly.</remarks>
    public void HostGame()
    {
        // Update the UI to show the connecting state.
        _ui.ShowConnecting(true);
        _net.StartHost();
    }

    /// <summary>
    /// Initiates the process of joining a game by connecting to the specified server address.
    /// </summary>
    /// <remarks>This method updates the user interface to indicate that a connection attempt is in progress.
    /// Ensure that the address is reachable and valid before calling this method.</remarks>
    /// <param name="address">The server address to connect to for joining the game. This must be a valid network address.</param>
    public void JoinGame(string address)
    {
        // Update the UI to show the connecting state.
        _ui.ShowConnecting(true);
        _net.StartClient(address);
    }

    public void DisconnectToMenu()
    {
        _ui.ShowConnecting(true);
        _net.Shutdown();
        // OnDisconnected will finish the transition.
    }

    private void OnConnected()
    {
        _ui.ShowInGame();
        _ui.ShowConnecting(false);
    }

    private void OnDisconnected()
    {
        _ui.ShowMainMenu();
        _ui.ShowConnecting(false);
    }
}
