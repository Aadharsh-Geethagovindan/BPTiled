using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    [SerializeField] private Button playLocalButton;
    [SerializeField] private Button playOnlineButton;

    [Header("Scene Names")]
    [SerializeField] private string charSelectSceneName = "CharacterSelect";
    [SerializeField] private string lobbySceneName      = "LobbyScene";

    private void Awake()
    {
        playLocalButton.onClick.AddListener(OnPlayLocal);
        playOnlineButton.onClick.AddListener(OnPlayOnline);
    }

    private void OnPlayLocal()
    {
        MatchSetup.Mode = GameMode.Hotseat;
        SceneManager.LoadScene(charSelectSceneName);
    }

    private void OnPlayOnline()
    {
        MatchSetup.Mode = GameMode.Online;
        SceneManager.LoadScene(lobbySceneName);
    }
}
