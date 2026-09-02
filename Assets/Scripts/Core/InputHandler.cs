using System;
using UnityEngine;

public class InputHandler : MonoBehaviour
{
    // Subscribe to these from anywhere — no direct reference needed
    public static event Action<Vector2Int> OnTileHovered;
    public static event Action<Vector2Int> OnTileClicked;
    public static event Action<Vector2Int> OnTileRightClicked; // used to deselect a multi-select pick
    public static event Action             OnBoardExited;

    [SerializeField] private Board  board;
    [SerializeField] private Camera gameCamera;

    private Vector2Int _lastHovered = new Vector2Int(-1, -1);
    private bool _wasOnBoard;

    private void Awake()
    {
        //Debug.Log("[InputHandler] Awake fired");
        if (board == null)
            Debug.LogError("[InputHandler] Board reference is not assigned!");
        if (gameCamera == null)
            Debug.LogError("[InputHandler] Game Camera reference is not assigned!");
    }

    

    private void Update()
    {
        if (gameCamera == null) return;

        Vector3 worldPos = gameCamera.ScreenToWorldPoint(Input.mousePosition);
        worldPos.z = 0f;

        Vector2Int gridPos = board.WorldToGrid(worldPos);
        bool onBoard = board.IsInBounds(gridPos);

        // Log every frame so we can see what coordinates are being produced
        //Debug.Log($"[InputHandler] mouse world: {worldPos:F2}  grid: {gridPos}  onBoard: {onBoard}");

        if (onBoard)
        {
            if (gridPos != _lastHovered)
            {
                _lastHovered = gridPos;
                //Debug.Log($"[InputHandler] Firing OnTileHovered: {gridPos}");
                OnTileHovered?.Invoke(gridPos);
            }

            if (Input.GetMouseButtonDown(0))
            {
                Debug.Log($"[InputHandler] Firing OnTileClicked: {gridPos}");
                OnTileClicked?.Invoke(gridPos);
            }

            if (Input.GetMouseButtonDown(1))
            {
                Debug.Log($"[InputHandler] Firing OnTileRightClicked: {gridPos}");
                OnTileRightClicked?.Invoke(gridPos);
            }
        }
        else if (_wasOnBoard)
        {
            Debug.Log("[InputHandler] Firing OnBoardExited");
            _lastHovered = new Vector2Int(-1, -1);
            OnBoardExited?.Invoke();
        }

        _wasOnBoard = onBoard;
    }
}
