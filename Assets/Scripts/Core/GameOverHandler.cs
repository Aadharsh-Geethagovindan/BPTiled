using UnityEngine;
using UnityEngine.SceneManagement;

// Listens for TurnManager.OnGameOver and hands off to VictoryScene. No UI of its own — replaces
// the old GameOverPanel now that the win/loss screen lives in its own scene rather than an
// in-battle-scene panel.
//
// Must be on an ACTIVE GameObject at battle start — Awake() has to actually run for the
// subscription to happen. (This is exactly what bit GameOverPanel: it was left inactive in the
// Hierarchy from when it used to be a toggleable panel, so Awake never ran and nothing ever
// subscribed to OnGameOver.)
public class GameOverHandler : MonoBehaviour
{
    [SerializeField] private string victorySceneName = "VictoryScene";

    private void Awake()
    {
        TurnManager.OnGameOver += HandleGameOver;
    }

    private void OnDestroy()
    {
        TurnManager.OnGameOver -= HandleGameOver;
    }

    private void HandleGameOver(int winningTeamId)
    {
        MatchSetup.WinningTeamId = winningTeamId;

        if (MatchSetup.Mode == GameMode.Online)
        {
            // Only the server actually triggers the load — see BattleNetworkBridge.ServerLoadScene.
            if (BattleNetworkBridge.IsServer)
                BattleNetworkBridge.Instance?.ServerLoadScene(victorySceneName);
        }
        else
        {
            SceneManager.LoadScene(victorySceneName);
        }
    }
}
