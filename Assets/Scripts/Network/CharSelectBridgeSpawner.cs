using FishNet;
using UnityEngine;

// Place this on any GameObject in the CharSelect scene.
// The server spawns the CharSelectNetworkBridge prefab so FishNet syncs it to all clients.
public class CharSelectBridgeSpawner : MonoBehaviour
{
    [SerializeField] private GameObject bridgePrefab;

    private void Start()
    {
        if (!InstanceFinder.IsServerStarted) return;
        var obj = Instantiate(bridgePrefab);
        InstanceFinder.ServerManager.Spawn(obj);
    }
}
