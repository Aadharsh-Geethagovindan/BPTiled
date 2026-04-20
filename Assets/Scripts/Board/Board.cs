using UnityEngine;

public class Board : MonoBehaviour
{
    public static Board Instance { get; private set; }

    [Header("Board Settings")]
    [SerializeField] private int width = 10;
    [SerializeField] private int height = 8;
    [SerializeField] private float tileSize = 1f;

    private Tile[,] _tiles;

    public int Width => width;
    public int Height => height;
    public float TileSize => tileSize;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void Initialize()
    {
        _tiles = new Tile[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                _tiles[x, y] = new Tile(new Vector2Int(x, y));
            }
        }
        Debug.Log($"[Board] Initialized {width}x{height} grid");
    }

    public Tile GetTile(int x, int y)
    {
        if (!IsInBounds(x, y)) return null;
        return _tiles[x, y];
    }

    public Tile GetTile(Vector2Int pos)
    {
        return GetTile(pos.x, pos.y);
    }

    public bool IsInBounds(int x, int y)
    {
        return x >= 0 && x < width && y >= 0 && y < height;
    }

    public bool IsInBounds(Vector2Int pos)
    {
        return IsInBounds(pos.x, pos.y);
    }

    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return new Vector3(gridPos.x * tileSize, gridPos.y * tileSize, 0f);
    }

    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.RoundToInt(worldPos.x / tileSize),
            Mathf.RoundToInt(worldPos.y / tileSize)
        );
    }

    public Tile[] GetNeighbors(Vector2Int pos)
    {
        Vector2Int[] directions = {
            Vector2Int.up,
            Vector2Int.down,
            Vector2Int.left,
            Vector2Int.right
        };

        var neighbors = new System.Collections.Generic.List<Tile>();
        foreach (var dir in directions)
        {
            var neighbor = GetTile(pos + dir);
            if (neighbor != null)
                neighbors.Add(neighbor);
        }
        return neighbors.ToArray();
    }
}