using TMPro;
using UnityEngine;

/// <summary>
/// Displays interaction prompts for the local player.
/// Reads state directly from PlayerGrabController each frame (no events needed).
/// </summary>
public sealed class HUDController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text promptText;

    private PlayerGrabController _grabController;

    private void Awake()
    {
        if (root == null) root = gameObject;
        SetVisible(false);
    }

    private void Update()
    {
        // Find local player if we don't have one
        if (_grabController == null)
        {
            _grabController = FindLocalPlayerGrabController();
            if (_grabController == null)
            {
                SetVisible(false);
                return;
            }
        }

        // Build prompt text from current state
        string prompt = BuildPromptText();

        if (string.IsNullOrEmpty(prompt))
        {
            SetVisible(false);
            return;
        }

        promptText.text = prompt;
        SetVisible(true);
    }

    private PlayerGrabController FindLocalPlayerGrabController()
    {
        var all = FindObjectsByType<PlayerGrabController>(FindObjectsInactive.Include);
        foreach (var controller in all)
        {
            if (controller != null && controller.IsOwner)
                return controller;
        }
        return null;
    }

    private string BuildPromptText()
    {
        // Primary action (E key)
        string primary = null;
        if (!string.IsNullOrEmpty(_grabController.ActionPrompt))
            primary = $"E: {_grabController.ActionPrompt}";

        // Secondary action (Q key) - only when holding
        string secondary = null;
        if (_grabController.CanThrow)
            secondary = "Q: Throw";

        // Combine prompts
        if (!string.IsNullOrEmpty(primary) && !string.IsNullOrEmpty(secondary))
            return $"{primary}\n{secondary}";

        if (!string.IsNullOrEmpty(primary))
            return primary;

        if (!string.IsNullOrEmpty(secondary))
            return secondary;

        return null;
    }

    private void SetVisible(bool visible)
    {
        if (root != null)
            root.SetActive(visible);
        else
            gameObject.SetActive(visible);
    }
}