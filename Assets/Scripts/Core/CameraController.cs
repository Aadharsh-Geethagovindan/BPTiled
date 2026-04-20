using UnityEngine;

public class CameraController : MonoBehaviour
{
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float  boardPadding = 1f;

    public void FitToBoard(Board board)
    {
        float boardWidth  = board.Width  * board.TileSize;
        float boardHeight = board.Height * board.TileSize;

        transform.position = new Vector3(
            boardWidth  / 2f - board.TileSize / 2f + 1f,
            boardHeight / 2f - board.TileSize / 2f,
            -10f
        );

        float aspectRatio  = (float)Screen.width / Screen.height;
        float sizeByHeight = (boardHeight / 2f) + boardPadding;
        float sizeByWidth  = (boardWidth  / 2f / aspectRatio) + boardPadding;

        mainCamera.orthographicSize = Mathf.Max(sizeByHeight, sizeByWidth);
    }
}
