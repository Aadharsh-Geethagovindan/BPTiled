using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField  ipInputField;
    [SerializeField] private Button          hostButton;
    [SerializeField] private Button          joinButton;
    [SerializeField] private TextMeshProUGUI statusText;

    [Header("Settings")]
    [SerializeField] private string charSelectSceneName = "CharacterSelect";

    private NetworkManager _networkManager;

    private void Awake()
    {
        _networkManager = InstanceFinder.NetworkManager;

        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);

        _networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
        _networkManager.ServerManager.OnRemoteConnectionState  += OnRemoteConnectionState;
    }

    private void OnDestroy()
    {
        if (_networkManager != null)
        {
            _networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            _networkManager.ServerManager.OnRemoteConnectionState  -= OnRemoteConnectionState;
        }
    }

    private void OnHostClicked()
    {
        SetStatus("Starting host...");
        SetButtonsInteractable(false);

        _networkManager.ServerManager.StartConnection();
        _networkManager.ClientManager.StartConnection();
    }

    private void OnJoinClicked()
    {
        string ip = ipInputField.text.Trim();
        if (string.IsNullOrEmpty(ip))
        {
            SetStatus("Enter an IP address.");
            return;
        }

        SetStatus($"Connecting to {ip}...");
        SetButtonsInteractable(false);

        // Set the address on Tugboat before connecting
        var tugboat = _networkManager.TransportManager.Transport as FishNet.Transporting.Tugboat.Tugboat;
        if (tugboat != null) tugboat.SetClientAddress(ip);

        _networkManager.ClientManager.StartConnection();
    }

    private void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        if (args.ConnectionState == LocalConnectionState.Started)
        {
            if (_networkManager.IsServerStarted)
                SetStatus("Hosting — waiting for player to join...");
            else
                SetStatus("Connected! Waiting for host to start...");
        }
        else if (args.ConnectionState == LocalConnectionState.Stopped)
        {
            SetStatus("Disconnected.");
            SetButtonsInteractable(true);
        }
    }

    // Fires on the server when a remote client connects or disconnects
    private void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (!_networkManager.IsServerStarted) return;
        if (args.ConnectionState != RemoteConnectionState.Started) return;
        if (_networkManager.ServerManager.Clients.Count < 2) return;

        SetStatus("Player joined! Loading game...");
        var sceneLoadData = new FishNet.Managing.Scened.SceneLoadData(charSelectSceneName);
        sceneLoadData.ReplaceScenes = FishNet.Managing.Scened.ReplaceOption.All;
        _networkManager.SceneManager.LoadGlobalScenes(sceneLoadData);
    }

    private void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
    }

    private void SetButtonsInteractable(bool interactable)
    {
        hostButton.interactable = interactable;
        joinButton.interactable = interactable;
    }
}
