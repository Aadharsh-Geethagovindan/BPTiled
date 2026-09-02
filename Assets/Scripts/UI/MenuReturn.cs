using FishNet;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuReturn : MonoBehaviour
{
    [SerializeField] private Button menuButton;

    private void Awake()
    {
        menuButton.onClick.AddListener(OnMenuReturn);
    }

    private void OnMenuReturn()
    {
        // Tear down the connection — otherwise the port(s) stay open after leaving the match even
        // though nothing in-game is using them anymore. A plain scene load alone doesn't do this;
        // the NetworkManager is persistent (DontDestroyOnLoad) across every scene in the flow.
        if (InstanceFinder.IsServerStarted)
            InstanceFinder.ServerManager.StopConnection(true);
        if (InstanceFinder.IsClientStarted)
            InstanceFinder.ClientManager.StopConnection();

        // Clear match-scoped state (draft picks, seed, win result, etc.) so a new match never
        // accidentally starts with data left over from this one.
        MatchSetup.ResetForNewMatch();

        SceneManager.LoadScene("MainMenu");
    }
}
