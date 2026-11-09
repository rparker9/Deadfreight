using UnityEngine;
using Unity.Netcode;

public sealed class PauseMenuScreen : MonoBehaviour
{
    private AppFlow _flow;

    private void Start()
    {
        _flow = AppRoot.Instance.GetComponent<AppFlow>();
    }

    public void OnResumePressed()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.LocalClient != null && nm.LocalClient.PlayerObject != null)
        {
            var input = nm.LocalClient.PlayerObject.GetComponent <PlayerInputController>();
            if (input != null)
                input.SetPaused(false);
        }
    }

    public void OnDisconnectPressed()
    {
        // Unpause locally before leaving (same pathway)
        OnResumePressed();
        _flow.DisconnectToMenu();
    }

    public void OnQuitPressed()
    {
        // Unpause locally before quitting (same pathway)
        OnResumePressed();

#if UNITY_EDITOR
        // Application.Quit() does not work in the editor so stop playing instead
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
