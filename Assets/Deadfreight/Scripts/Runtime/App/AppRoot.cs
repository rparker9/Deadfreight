using UnityEngine;

/// <summary>
/// The AppRoot is a singleton MonoBehaviour that persists across scene loads and serves as the root of the application.
/// It's used to hold core components like AppFlow and UIRoot, ensuring they remain accessible throughout the app's lifecycle.
/// </summary>
public sealed class AppRoot : MonoBehaviour
{
    public static AppRoot Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Destroy is end-of-frame; deactivate immediately so child EventSystem/AudioListener
            // don't briefly enable and trigger warnings.
            gameObject.SetActive(false);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
