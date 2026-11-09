using UnityEngine;
using TMPro;

/// <summary>
/// Represents the main menu screen of the application, providing user interface controls to host a game, join a game,
/// or quit the application.
/// </summary>
/// <remarks>This screen interacts with the application's flow controller to manage game session actions. When
/// joining a game, if no address is specified, the default address '127.0.0.1' is used. This class is intended to be
/// attached to a Unity UI screen and relies on serialized input fields for user interaction.</remarks>
public sealed class MainMenuScreen : MonoBehaviour
{
    [SerializeField] private TMP_InputField addressInput;

    private AppFlow _flow;

    private void Start()
    {
        _flow = AppRoot.Instance.GetComponent<AppFlow>();
    }

    /// <summary>
    /// Initiates the hosting of a new game session when the host option is selected.
    /// </summary>
    /// <remarks>Call this method to begin hosting a game. Ensure that all necessary game settings are
    /// configured before invoking this method to avoid unexpected behavior.</remarks>
    public void OnHostPressed()
    {
        _flow.HostGame();
    }
    
    /// <summary>
    /// Initiates the process of joining a game by connecting to the specified server address.
    /// </summary>
    /// <remarks>This method uses the address entered in the input field. If no address is provided, it defaults to '127.0.0.1'.</remarks>
    public void OnJoinPressed()
    {
        string addr = addressInput != null ? addressInput.text : "127.0.0.1";
        if (string.IsNullOrWhiteSpace(addr)) addr = "127.0.0.1";
        _flow.JoinGame(addr);
    }

    /// <summary>
    /// Handles the quit action by terminating the application.
    /// </summary>
    /// <remarks>Call this method in response to a user action, such as pressing a quit or exit button, to
    /// close the application gracefully. On some platforms, this method may have no effect when running in the editor
    /// or development environment.</remarks>
    public void OnQuitPressed()
    {
#if UNITY_EDITOR
        // Application.Quit() does not work in the editor so stop playing instead
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
