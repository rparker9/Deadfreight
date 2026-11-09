using UnityEngine;
using Unity.Netcode;

public sealed class UIRoot : MonoBehaviour
{
    [Header("Scene UI (assign in inspector)")]
    [SerializeField] private GameObject _mainMenuCanvas;
    [SerializeField] private GameObject _hudCanvas;
    [SerializeField] private GameObject _pauseCanvas;
    [SerializeField] private GameObject _connectingOverlay;

    public bool IsPaused => _pauseCanvas != null && _pauseCanvas.activeSelf;
    public bool IsInGame => _hudCanvas != null && _hudCanvas.activeSelf;

    private void Awake()
    {
        // Fail fast (editor-time wiring is now required).
        if (_pauseCanvas == null)
            Debug.LogWarning("UIRoot: PauseCanvas is not assigned.");
        if (_hudCanvas == null)
            Debug.LogWarning("UIRoot: HudCanvas is not assigned.");
        if (_mainMenuCanvas == null)
            Debug.LogWarning("UIRoot: MainMenuCanvas is not assigned.");
        if (_connectingOverlay == null)
            Debug.LogWarning("UIRoot: ConnectingOverlay is not assigned.");
    }

    public void ShowMainMenu()
    {
        Set(_mainMenuCanvas, true);
        Set(_hudCanvas, false);
        Set(_pauseCanvas, false);
        // Connecting overlay controlled separately.
        Time.timeScale = 1f;
    }

    public void ShowInGame()
    {
        Set(_mainMenuCanvas, false);
        Set(_hudCanvas, true);
        Set(_pauseCanvas, false);
        Time.timeScale = 1f;
    }

    public void ShowConnecting(bool show)
    {
        Set(_connectingOverlay, show);
    }

    public void TogglePause()
    {
        if (_pauseCanvas == null)
            return;

        bool next = !_pauseCanvas.activeSelf;
        Set(_pauseCanvas, next);

        // Multiplayer-safe: do not freeze time while networking is running.
        var nm = NetworkManager.Singleton;
        bool netRunning = nm != null && nm.IsListening;
        Time.timeScale = (next && !netRunning) ? 0f : 1f;
    }

    private static void Set(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }
}
