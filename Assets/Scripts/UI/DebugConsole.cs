using TMPro;
using UnityEngine;

// [DEBUG] Testing-only text console for the battle scene. Type a command and press Enter — it
// routes through BattleController.RequestDebugCommand, the same server-authoritative path every
// other action uses, so effects actually sync to both clients instead of only appearing locally.
// See DebugCommands.cs for the list of supported commands / how to add more.
//
// Temporary tool — safe to delete this component (and its GameObject) once no longer needed.
public class DebugConsole : MonoBehaviour
{
    [SerializeField] private TMP_InputField    inputField;
    [SerializeField] private TextMeshProUGUI   feedbackText; // optional — shows the last result

    private void Awake()
    {
        if (inputField != null)
            inputField.onSubmit.AddListener(OnSubmit);
    }

    private void OnDestroy()
    {
        if (inputField != null)
            inputField.onSubmit.RemoveListener(OnSubmit);
    }

    private void OnSubmit(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        string result = BattleController.Instance != null
            ? BattleController.Instance.RequestDebugCommand(text)
            : "BattleController not found.";

        if (feedbackText != null)
            feedbackText.text = result;

        inputField.text = string.Empty;
        inputField.ActivateInputField(); // keep focus so you can chain commands quickly
    }
}
