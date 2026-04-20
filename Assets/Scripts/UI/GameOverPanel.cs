using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameOverPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI resultText;
    [SerializeField] private Button          restartButton;

    private void Awake()
    {
        TurnManager.OnGameOver += HandleGameOver;
        restartButton.onClick.AddListener(() => SceneManager.LoadScene(SceneManager.GetActiveScene().name));
        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        TurnManager.OnGameOver -= HandleGameOver;
    }

    private void HandleGameOver(int winningTeamId)
    {
        resultText.text = $"Team {winningTeamId} Wins!";
        gameObject.SetActive(true);
    }
}
