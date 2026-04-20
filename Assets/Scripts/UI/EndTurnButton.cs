using UnityEngine;
using UnityEngine.UI;

// Attach to the End Turn button GameObject.
// Enabled while a fighter is active; disabled between activations and on game over.
public class EndTurnButton : MonoBehaviour
{
    [SerializeField] private Button button;

    private void Awake()
    {
        TurnManager.OnFighterActivated  += HandleFighterActivated;
        TurnManager.OnActivationChanged += HandleActivationChanged;
        TurnManager.OnGameOver          += HandleGameOver;

        button.onClick.AddListener(() => BattleController.Instance.RequestEndTurn());
        button.interactable = false;
    }

    private void OnDestroy()
    {
        TurnManager.OnFighterActivated  -= HandleFighterActivated;
        TurnManager.OnActivationChanged -= HandleActivationChanged;
        TurnManager.OnGameOver          -= HandleGameOver;
    }

    private void HandleFighterActivated(Fighter _)  => button.interactable = true;
    private void HandleActivationChanged(int _)     => button.interactable = false;
    private void HandleGameOver(int _)              => button.interactable = false;
}
