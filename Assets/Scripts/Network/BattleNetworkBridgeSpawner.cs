using FishNet;
using UnityEngine;

// Place on any GameObject in BattleScene.
// Server spawns the BattleNetworkBridge prefab so FishNet syncs it to all clients.
public class BattleNetworkBridgeSpawner : MonoBehaviour
{
    [SerializeField] private GameObject bridgePrefab;

    private void Start()
    {
        if (!InstanceFinder.IsServerStarted) return;
        if (bridgePrefab == null) { Debug.LogError("[BattleBridgeSpawner] bridgePrefab not assigned!"); return; }
        var obj = Instantiate(bridgePrefab);
        InstanceFinder.ServerManager.Spawn(obj);
    }
}
